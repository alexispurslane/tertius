using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Simulation;

/// <summary>
/// What a render snapshot shows, apart from the entities: the tick, the camera, the lights of the scene, and whether it
/// shows the outlines of the frames (<see cref="World.ShowFrameOutlines"/>).
/// </summary>
internal readonly record struct RenderView(
    long Tick,
    double Time,
    Entity Camera,
    float VerticalFov,
    FrameHandle SunFrame,
    Vector3 SunColor,
    float SunIntensity,
    AmbientLight Ambient,
    bool ShowFrameOutlines
);

/// <summary>
/// Builds render snapshots from the front tables. Every frame's pose relative to the camera's frame is computed in
/// doubles along the frame tree, so an object far away in a sibling frame is as exact as the float result allows, and
/// one near the camera is exact. Instances, lights and occluders are then written relative to the render origin, the
/// camera position at this tick.
/// </summary>
/// <remarks>
/// The builder remembers the frame and entity poses of the previous build, so that each instance also carries its
/// previous transform relative to the same origin. It interpolates only between consecutive builds of the same camera
/// in the same frame. Runs at the sync point, or on the ticking thread outside a tick. A build of at least
/// <see cref="ParallelThreshold"/> slots runs on the workers, one chunk of the entity table for each work item, and the
/// chunks join in slot order.
/// <para>
/// <see cref="BuildPuffs"/> (RenderSnapshotBuilder.Puffs.cs) places the puffs of the puff table after a build, through
/// the frame poses of that build. A puff keeps no memory here: its previous place is its place one chord back.
/// </para>
/// <para>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>An entity's remembered pose comes from a build that has happened, and names the frame it was relative
/// to.</item>
/// <item>The memory is a column of the entity table, so a released entity's memory is cleared, and a new entity in a
/// reused slot never interpolates from the old one.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed partial class RenderSnapshotBuilder
{
    private const int RenderChunkSlots = 1024;

    /// <summary>
    /// The default of <see cref="ParallelThreshold"/>, in slots of the entity table. It comes from the first version of the
    /// builder. Unlike the thresholds of the row copy, the cell search and the routing, ter-sn28 did not measure it again.
    /// </summary>
    internal const int DefaultParallelThreshold = 8192;

    private static readonly Double3 LightForward = new(0, 0, -1);

    private readonly EntityTable entities;
    private readonly FrameTree frames;
    private readonly ParallelOptions? workers;
    private readonly Action<int> renderChunk;

    // Frame data by dense index, rebuilt at every build.
    private DPose[] relCurrent = new DPose[16];
    private DPose[] relPrevious = new DPose[16];
    private DPose[] localPrevious = new DPose[16];
    private FrameHandle[] denseFrames = new FrameHandle[16];
    private FrameHemisphere[] frameHemisphere = new FrameHemisphere[16];

    // Frame data by frame index.
    private int[] denseOfFrame = new int[16];
    private uint[] genOfFrame = new uint[16];
    private long[] frameSeen = new long[16];
    private DPose[] lastFrameLocal = new DPose[16];
    private FrameHandle[] lastFrameHandle = new FrameHandle[16];
    private FrameHandle[] lastFrameParent = new FrameHandle[16];
    private long[] lastFrameStamp = new long[16];

    private readonly EntityTable.Column<PoseMemory> lastEntityPose;

    private long buildStamp;
    private Entity lastCamera;

    private FrameHandle lastCameraFrame;

    // The camera's physics home at the last build, and its eye pose in that home.
    private FrameHandle lastCameraHome;

    private DPose lastCameraPose;

    // The camera's eye pose in its semantic frame at the previous build, for when its physics home is gone since.
    private DPose lastCameraInFrame;

    private long lastCameraStamp;

    // The tick of the last build with a camera.
    private long lastCameraTick;

    // Inputs of the per-chunk workers for the build that is running.
    private Double3 renderOrigin;
    private bool renderInterpolate;
    private int renderSlotCount;
    private RefList<RenderInstance>[] instanceChunks = [];
    private RefList<RenderLight>[] lightChunks = [];

    // What the placement of the puffs knows of the build that is running: whether the build placed a camera, and whether
    // it interpolates from a build of the tick before, so that a puff's place one chord back is its previous place.
    private bool renderHasCamera;
    private bool renderFollowsTick;

    // What the outline build knows of each frame, by dense index, during one build with outlines.
    private OutlineFrame[] outlineFrames = [];

    /// <param name="entities">The entity table whose front rows the builds read.</param>
    /// <param name="frames">The frame tree whose poses the builds compose.</param>
    /// <param name="workers">The workers on which a large build runs, or null to run every build on the calling thread.</param>
    public RenderSnapshotBuilder(EntityTable entities, FrameTree frames, ParallelOptions? workers = null)
    {
        this.entities = entities;
        this.frames = frames;
        this.workers = workers;
        renderChunk = BuildChunk;
        lastEntityPose = entities.AddColumn<PoseMemory>();
    }

    /// <summary>
    /// The fewest slots of the entity table for which a build runs on the workers. 1 or more. Both paths fill the same
    /// snapshot, so only tests change it: at 1, every build takes the workers.
    /// </summary>
    internal int ParallelThreshold
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = DefaultParallelThreshold;

    /// <summary>
    /// What a frame's gravity field means for the hemisphere ambient light, at this build. <see cref="Up"/> is the up of
    /// every entity in the frame, but a point field's center depends on the entity, and a uniform field holds only inside
    /// <see cref="Region"/> when <see cref="HasRegion"/> is set. The snapshot build reads it once per instance, so it
    /// holds no nullable box to copy.
    /// </summary>
    private struct FrameHemisphere
    {
        public HemisphereUp Up;
        public bool HasRegion;
        public BoundingBox Region;
    }

    /// <summary>The pose of one entity at the last build that placed it, relative to its physics home then.</summary>
    private struct PoseMemory
    {
        public DPose Pose;
        public FrameHandle Frame;

        /// <summary>The build that wrote it, or 0 for none.</summary>
        public long Stamp;
    }

    /// <summary>What the outline build knows of one frame, by dense index.</summary>
    private struct OutlineFrame
    {
        /// <summary>How the frame moves as a numeric frame: <see cref="NumericMode.None"/> for a semantic frame.</summary>
        public NumericMode Mode;

        /// <summary>The dense index of the frame's parent in the tree as it is stored, or -1 for a root frame.</summary>
        public int Parent;

        /// <summary>
        /// The radius of the frame's outline sphere so far, in meters. For a semantic frame, it is the largest radius of the
        /// bubble from the start. For a numeric frame, it is the radius of the contents sphere: it starts at 0 and grows to
        /// the farthest reach of what the frame holds.
        /// </summary>
        public double Sphere;
    }

    /// <summary>
    /// Fills <paramref name="snapshot"/> with the current state: the instances, the sun, the frame outlines when the view
    /// shows them, and the puffs of <paramref name="puffs"/>, placed after the instances through the same frame poses.
    /// Without a live camera, the snapshot has the view settings but nothing in it, and the next build does not interpolate.
    /// </summary>
    public void Build(RenderSnapshot snapshot, in RenderView view, in PuffView puffs)
    {
        long stamp = ++buildStamp;
        snapshot.Clear();
        snapshot.Tick = view.Tick;
        snapshot.Time = view.Time;
        snapshot.Ambient = view.Ambient;
        snapshot.Camera = new()
        {
            Rotation = Quaternion.Identity,
            PreviousRotation = Quaternion.Identity,
            VerticalFov = view.VerticalFov,
        };
        snapshot.Sun = new()
        {
            Direction = Vector3.UnitY,
            Color = view.SunColor,
            Intensity = view.SunIntensity,
        };
        snapshot.RootRotation = snapshot.PreviousRootRotation = Quaternion.Identity;
        snapshot.OriginInFrame = Double3.Zero;
        snapshot.OriginFrameId = 0;
        renderHasCamera = renderFollowsTick = false;

        if (CameraPoseOf(view.Camera) is not (var cameraFrame, var cameraHome, var eyeInFrame, var cameraPose))
        {
            lastCamera = Entity.Null;
            return;
        }

        // Interpolate only when the same camera is in the same frame as at the previous build. Everything is placed relative
        // to the camera's physics home, which has the axes of the camera's frame: far from that frame's origin, the home is
        // near the camera, so positions near the camera stay exact.
        bool interpolate = lastCamera == view.Camera && lastCameraFrame == cameraFrame && lastCameraStamp == stamp - 1;
        PrepareFramePoses(cameraHome, stamp, interpolate);
        renderInterpolate = interpolate;
        renderOrigin = cameraPose.Position;
        renderHasCamera = true;
        renderFollowsTick = interpolate && view.Tick == lastCameraTick + 1;

        // The previous pose from the previous home, which stays exact near the camera. When a numeric update collected
        // that home, the camera's semantic frame, which is the same, places it, rounded only far from that frame's origin.
        DPose previousCamera;
        if (!interpolate)
            previousCamera = cameraPose;
        else if (lastCameraHome == cameraHome)
            previousCamera = lastCameraPose;
        else if (FrameSeen(lastCameraHome))
            previousCamera = DPose.ComposeUnitParent(relPrevious[denseOfFrame[lastCameraHome.Index]], lastCameraPose);
        else
            previousCamera = DPose.ComposeUnitParent(relPrevious[denseOfFrame[cameraFrame.Index]], lastCameraInFrame);
        snapshot.Camera = new()
        {
            Position = Vector3.Zero,
            Rotation = cameraPose.Rotation.ToQuaternion(),
            PreviousPosition = (previousCamera.Position - renderOrigin).ToVector3(),
            PreviousRotation = previousCamera.Rotation.ToQuaternion(),
            VerticalFov = view.VerticalFov,
        };
        int root = denseOfFrame[RootOf(cameraFrame).Index];
        snapshot.RootRotation = relCurrent[root].Rotation.ToQuaternion();
        snapshot.PreviousRootRotation = interpolate ? relPrevious[root].Rotation.ToQuaternion() : snapshot.RootRotation;
        snapshot.OriginInFrame = eyeInFrame.Position;
        // Stable for the life of the frame, and new when a frame reuses the index: the generation changes.
        snapshot.OriginFrameId = ((long)cameraFrame.Generation << 32) | (uint)cameraFrame.Index;

        BuildInstances(snapshot);
        BuildSun(snapshot, view);
        if (view.ShowFrameOutlines)
            BuildOutlines(snapshot);
        BuildPuffs(snapshot, puffs);

        lastCamera = view.Camera;
        lastCameraFrame = cameraFrame;
        lastCameraHome = cameraHome;
        lastCameraPose = cameraPose;
        lastCameraInFrame = eyeInFrame;
        lastCameraStamp = stamp;
        lastCameraTick = view.Tick;
    }

    /// <summary>
    /// The camera's frame and physics home, and its eye pose in each: the entity pose composed with its view offset.
    /// Null if the camera is not live or owns no frame.
    /// </summary>
    private (FrameHandle Frame, FrameHandle Home, DPose InFrame, DPose InHome)? CameraPoseOf(Entity camera)
    {
        if (!entities.IsAlive(camera))
            return null;
        ref readonly TransformRow row = ref entities.FrontRow(camera);
        FrameHandle frame = row.Frame,
            home = row.PhysicsHome;
        DPose entityPose = row.Pose,
            homePose = row.HomePose;
        if (frame.IsNull)
        {
            // The owner of a root frame looks out from its own frame's origin.
            frame = home = entities.OwnedFrame(camera);
            entityPose = homePose = DPose.Identity;
            if (frame.IsNull)
                return null;
        }

        var view = Poses.Safe(entities.FrontRender(camera).ViewOffset).ToDPose();
        var inFrame = DPose.Compose(entityPose, view);
        return (frame, home, inFrame, home == frame ? inFrame : DPose.Compose(homePose, view));
    }

    /// <summary>
    /// Fills the current and previous pose of every frame relative to <paramref name="reference"/>, the camera's physics
    /// home, by dense index, and records this build's local poses for the next one. The poses compose along the tree from
    /// the reference, so frames near it are exact however far they are from the root.
    /// </summary>
    private void PrepareFramePoses(FrameHandle reference, long stamp, bool interpolate)
    {
        int n = frames.Count;
        EnsureDenseFrameArrays(n);
        frames.ComputeRelativeTo(reference, relCurrent.AsSpan(0, n));

        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = frames.AtDense(i);
            EnsureFrameIndexArrays(frame.Index);
            denseFrames[i] = frame;
            denseOfFrame[frame.Index] = i;
            genOfFrame[frame.Index] = frame.Generation;
            frameSeen[frame.Index] = stamp;
        }

        ReadOnlySpan<DPose> locals = frames.LocalPosesDense;
        bool anyMoved = false;
        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = denseFrames[i];
            // The local poses are relative to the parent in the tree as it is stored, numeric frames included.
            FrameHandle parent = frames.TreeParent(frame);
            DPose local = locals[i];
            // A frame that is new, or that changed parent, has no usable previous local pose.
            bool known =
                lastFrameStamp[frame.Index] == stamp - 1
                && lastFrameHandle[frame.Index] == frame
                && lastFrameParent[frame.Index] == parent;
            localPrevious[i] = known ? lastFrameLocal[frame.Index] : local;
            anyMoved |= !Poses.BitEquals(localPrevious[i], local);

            lastFrameLocal[frame.Index] = local;
            lastFrameHandle[frame.Index] = frame;
            lastFrameParent[frame.Index] = parent;
            lastFrameStamp[frame.Index] = stamp;
            frameHemisphere[i] = HemisphereOfFrame(frame, relCurrent[i].Rotation);
        }

        // The previous poses use this build's tree with last build's local poses: a frame that moved between the builds
        // carries its contents along, and equal local poses give bit-identical results.
        if (interpolate && anyMoved)
            frames.ComputeRelativeTo(reference, localPrevious.AsSpan(0, n), relPrevious.AsSpan(0, n));
        else
            relCurrent.AsSpan(0, n).CopyTo(relPrevious);
    }

    private void BuildInstances(RenderSnapshot snapshot)
    {
        renderSlotCount = entities.SlotCount;
        int chunkCount = (renderSlotCount + RenderChunkSlots - 1) / RenderChunkSlots;
        if (instanceChunks.Length < chunkCount)
        {
            int old = instanceChunks.Length;
            Array.Resize(ref instanceChunks, chunkCount);
            Array.Resize(ref lightChunks, chunkCount);
            for (int i = old; i < chunkCount; i++)
            {
                instanceChunks[i] = new(64);
                lightChunks[i] = new(4);
            }
        }

        if (workers != null && renderSlotCount >= ParallelThreshold)
            Parallel.For(fromInclusive: 0, toExclusive: chunkCount, parallelOptions: workers, body: renderChunk);
        else
            for (int c = 0; c < chunkCount; c++)
                BuildChunk(c);

        // Join the chunks in order, so that the instance order does not depend on threads.
        int instances = 0,
            lights = 0;
        for (int c = 0; c < chunkCount; c++)
        {
            instances += instanceChunks[c].Count;
            lights += lightChunks[c].Count;
        }

        CollectionsMarshal.SetCount(snapshot.Instances, instances);
        CollectionsMarshal.SetCount(snapshot.Lights, lights);
        Span<RenderInstance> instanceSpan = CollectionsMarshal.AsSpan(snapshot.Instances);
        Span<RenderLight> lightSpan = CollectionsMarshal.AsSpan(snapshot.Lights);
        for (int c = 0; c < chunkCount; c++)
        {
            instanceChunks[c].AsSpan().CopyTo(instanceSpan);
            instanceSpan = instanceSpan[instanceChunks[c].Count..];
            lightChunks[c].AsSpan().CopyTo(lightSpan);
            lightSpan = lightSpan[lightChunks[c].Count..];
            instanceChunks[c].Clear();
            lightChunks[c].Clear();
        }
    }

    private void BuildChunk(int chunk)
    {
        RefList<RenderInstance> instances = instanceChunks[chunk];
        RefList<RenderLight> lights = lightChunks[chunk];
        int start = chunk * RenderChunkSlots;
        int end = Math.Min(start + RenderChunkSlots, renderSlotCount);
        Double3 origin = renderOrigin;
        foreach (Entity entity in entities.LiveEntitiesIn(start, end - start))
        {
            ref readonly RenderData render = ref entities.FrontRender(entity);
            // A default mesh is none, so a render row that never set a mesh draws nothing even with Visible set.
            bool visible = (render.Flags & RenderFlags.Visible) != 0 && !render.Mesh.IsNone;
            if (!visible && !render.Light.HasValue)
                continue;
            if (
                !TryGetRenderPoses(
                    entity: entity,
                    current: out DPose current,
                    previous: out DPose previous,
                    dense: out int dense,
                    place: out Vector3 place
                )
            )
                continue;

            if (visible)
            {
                // One render row per entity today, so every instance is part 0.
                ref RenderInstance instance = ref instances.AddRef();
                instance = RenderInstance.Create(
                    mesh: render.Mesh,
                    material: render.Material,
                    position: (current.Position - origin).ToVector3(),
                    rotation: current.Rotation.ToQuaternion(),
                    scale: render.Scale,
                    boundingRadius: render.BoundingRadius,
                    hemisphere: HemisphereOf(dense, place, current.Position),
                    flags: render.Flags,
                    entityIndex: entity.Index,
                    part: 0
                );
                instance.PreviousPosition = (previous.Position - origin).ToVector3();
                instance.PreviousRotation = previous.Rotation.ToQuaternion();
            }

            // The default light holds no case, so the entity adds no light.
            switch (render.Light)
            {
                case PointLightData
                {
                    Offset: var offset,
                    Color: var color,
                    Intensity: var intensity,
                    Range: var range
                }:
                {
                    DPose pose = LightPose(current, offset);
                    lights.AddRef() = new PointRenderLight(
                        Position: (pose.Position - origin).ToVector3(),
                        Color: color,
                        Intensity: intensity,
                        Range: range
                    );
                    break;
                }
                case SpotLightData
                {
                    Offset: var offset,
                    Color: var color,
                    Intensity: var intensity,
                    Range: var range,
                    InnerConeAngle: var innerConeAngle,
                    OuterConeAngle: var outerConeAngle
                }:
                {
                    DPose pose = LightPose(current, offset);
                    lights.AddRef() = new SpotRenderLight(
                        Position: (pose.Position - origin).ToVector3(),
                        Direction: pose.Rotation.Rotate(LightForward).ToVector3(),
                        Color: color,
                        Intensity: intensity,
                        Range: range,
                        InnerConeAngle: innerConeAngle,
                        OuterConeAngle: outerConeAngle
                    );
                    break;
                }
                case null:
                    break;
                default:
                    throw new UnreachableException(
                        $"The light {render.Light} has a case that the snapshot does not draw."
                    );
            }
        }
    }

    /// <summary>
    /// The pose of a light in the camera's frame: the pose of the entity that carries it, composed with the light's offset.
    /// A zeroed rotation in the offset, as in a defaulted pose, counts as the identity.
    /// </summary>
    private static DPose LightPose(in DPose carrier, Pose offset) =>
        // The rotation of the carrier is unit length, because TryGetRenderPoses gives the pose of a frame or the result of
        // ComposeUnitParent.
        DPose.ComposeUnitParent(carrier, Poses.Safe(offset).ToDPose());

    /// <summary>
    /// The entity's pose relative to the camera frame now and at the previous build. A frame owner uses its frame's
    /// pose. Any other entity composes its frame's pose with its own; its previous pose uses the frame it was in then,
    /// so a hand-off interpolates smoothly. False for an entity whose frame this build did not place. After a sync point
    /// every live entity is in a live frame, so that only guards against a broken table.
    /// </summary>
    /// <param name="dense">The dense index of the entity's own frame: the frame that it owns, or else the frame that it
    /// is in.</param>
    /// <param name="place">The entity's position in that frame: zero for a frame owner, which is its frame's origin.</param>
    // Kept as a Try method: RenderSnapshotBenchmarks.Build (Release, short job, in-process) took 914,002 ns (flat) and
    // 950,777 ns (frames1000) with this out form, and 1,099,087 ns and 1,142,552 ns when the method returned a nullable
    // tuple of about 140 bytes.
    private bool TryGetRenderPoses(
        Entity entity,
        out DPose current,
        out DPose previous,
        out int dense,
        out Vector3 place
    )
    {
        FrameHandle own = entities.OwnedFrame(entity);
        if (!own.IsNull)
        {
            if (!FrameSeen(own))
            {
                current = previous = default;
                (dense, place) = (0, Vector3.Zero);
                return false;
            }

            dense = denseOfFrame[own.Index];
            place = Vector3.Zero;
            current = relCurrent[dense];
            previous = renderInterpolate ? relPrevious[dense] : current;
            return true;
        }

        ref readonly TransformRow row = ref entities.FrontRow(entity);
        if (!FrameSeen(row.Frame))
        {
            current = previous = default;
            (dense, place) = (0, Vector3.Zero);
            return false;
        }

        dense = denseOfFrame[row.Frame.Index];
        place = row.Pose.Position.ToVector3();
        // The entity is placed from its physics home, which is in the tree below its frame, so a position near the camera
        // stays exact far from the frame's origin. Numeric frames do not rotate, so the pose in the home has the row's rotation.
        FrameHandle home = row.PhysicsHome;
        DPose inHome = row.HomePose;
        // The parent of each composition is the pose of a frame relative to the camera's home. ComputeRelativeTo gives the
        // identity or a result of DPose.Compose for it, and Compose normalizes the rotation of its result, so the parent is
        // unit length. The rotation of the entity's own pose is not always, because a program wrote it or Jolt gave it, and
        // the result is normalized in any case. ComposeUnitParent skips the division that DPose.Compose spends on every
        // entity to normalize the parent. In a build with contracts, it checks that the parent is unit length.
        current = DPose.ComposeUnitParent(relCurrent[denseOfFrame[home.Index]], inHome);
        previous = current;
        ref PoseMemory last = ref lastEntityPose[entity];
        if (renderInterpolate && last.Stamp == buildStamp - 1 && FrameSeen(last.Frame))
            previous = DPose.ComposeUnitParent(relPrevious[denseOfFrame[last.Frame.Index]], last.Pose);
        last = new()
        {
            Pose = inHome,
            Frame = home,
            Stamp = buildStamp,
        };
        return true;
    }

    /// <summary>
    /// What a frame's gravity field means for the hemisphere ambient light: a uniform field points up against its
    /// acceleration, turned by <paramref name="rotation"/> into the axes of the camera's frame; a point field leads up
    /// away from the frame's origin; no field, or one that pulls nowhere, mixes evenly. The frame's proper acceleration
    /// does not count, so thrust never turns the up. (User)
    /// </summary>
    private FrameHemisphere HemisphereOfFrame(FrameHandle frame, DQuaternion rotation) =>
        frames.Gravity(frame) switch
        {
            UniformGravity { Acceleration: var acceleration, Region: var region } when acceleration != Vector3.Zero =>
                new()
                {
                    Up = HemisphereUp.Along(rotation.Rotate((Double3)(-acceleration)).Normalized().ToVector3()),
                    HasRegion = region.HasValue,
                    Region = region.GetValueOrDefault(),
                },
            PointGravity { Mu: > 0 } => new() { Up = HemisphereUp.AwayFrom(Vector3.Zero) },
            // A field without a pull, and no field, give the even mix.
            UniformGravity or PointGravity or null => default,
        };

    /// <summary>
    /// Which way is up for the hemisphere ambient light of a placed entity: its own frame's, at its place. A frame owner
    /// is its frame, so it takes its frame's up at the frame's origin; any other entity takes the up of the frame that it
    /// is in. A uniform field holds only inside its region.
    /// </summary>
    /// <param name="dense">The dense index of the entity's own frame.</param>
    /// <param name="place">The entity's position in that frame.</param>
    /// <param name="position">The entity's position relative to the camera's physics home, which a point field's center
    /// is given relative to.</param>
    private HemisphereUp HemisphereOf(int dense, Vector3 place, Double3 position)
    {
        ref readonly FrameHemisphere frame = ref frameHemisphere[dense];
        if (frame.Up is UpAwayFrom)
            return HemisphereUp.AwayFrom((relCurrent[dense].Position - position).ToVector3());
        if (frame.HasRegion && !frame.Region.Contains(place))
            return HemisphereUp.Even;
        return frame.Up;
    }

    /// <summary>The root frame of the tree that <paramref name="frame"/> is in.</summary>
    private FrameHandle RootOf(FrameHandle frame)
    {
        for (FrameHandle parent = frames.Parent(frame); !parent.IsNull; parent = frames.Parent(frame))
            frame = parent;
        return frame;
    }

    /// <summary>True for a live frame that this build has placed.</summary>
    private bool FrameSeen(FrameHandle frame) =>
        !frame.IsNull
        && (uint)frame.Index < (uint)frameSeen.Length
        && frameSeen[frame.Index] == buildStamp
        && genOfFrame[frame.Index] == frame.Generation;

    private void BuildSun(RenderSnapshot snapshot, in RenderView view)
    {
        FrameHandle sun = view.SunFrame;
        if (!frames.IsAlive(sun) || !FrameSeen(sun))
            return;
        Double3 toSun = relCurrent[denseOfFrame[sun.Index]].Position - renderOrigin;
        double distance = toSun.Length;
        if (!(distance > 0))
            return;
        float radius = entities.IsAlive(sun.Entity) ? entities.FrontRender(sun.Entity).BoundingRadius : 0;
        snapshot.Sun.Direction = (toSun / distance).ToVector3();
        snapshot.Sun.AngularRadius = radius > 0 ? (float)Math.Atan(radius / distance) : 0;
    }

    /// <summary>
    /// Adds the outline of each frame that has a parent to <paramref name="snapshot"/>, in the dense order of the tree, on
    /// the calling thread: a semantic frame's bubble, and a numeric frame's contents sphere. A root frame has no outline,
    /// because nothing enters or leaves it. Each center and rotation is the frame's pose relative to the camera's physics
    /// home, which also places the instances, less the render origin.
    /// </summary>
    /// <remarks>
    /// The contents sphere of a numeric frame is the smallest sphere centred on its origin that holds the bounding sphere of
    /// each body whose physics home it is and the outline sphere of each frame whose parent in the tree, as the tree stores
    /// it, is this frame. The outline sphere of a frame is the smallest sphere centred on its origin that holds its outline:
    /// the largest radius of the bubble for a semantic frame, and the contents sphere for a numeric frame. A largest value
    /// does not depend on the order of the bodies, but the list does, so it keeps the dense order, which every run shares.
    /// The radii are doubles until each narrows to a float in its outline.
    /// </remarks>
    private void BuildOutlines(RenderSnapshot snapshot)
    {
        int n = frames.Count;
        if (outlineFrames.Length < n)
            outlineFrames = new OutlineFrame[Math.Max(n, outlineFrames.Length * 2)];
        Span<OutlineFrame> outline = outlineFrames.AsSpan(0, n);
        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = denseFrames[i];
            FrameHandle parent = frames.TreeParent(frame);
            NumericMode mode = frames.Numeric(frame);
            outline[i] = new()
            {
                Mode = mode,
                Parent = parent.IsNull ? -1 : denseOfFrame[parent.Index],
                Sphere = mode == NumericMode.None ? frames.Bounds(frame).MaxRadius : 0,
            };
        }

        // Each body grows its physics home, when that is numeric, to the far side of its bounding sphere: its position in the
        // home plus the radius that the entity table kept at its spawn.
        foreach (Entity entity in entities.BodyEntities)
        {
            ref readonly TransformRow row = ref entities.FrontRow(entity);
            FrameHandle home = row.PhysicsHome;
            if (!FrameSeen(home))
                continue;
            ref OutlineFrame frame = ref outline[denseOfFrame[home.Index]];
            if (frame.Mode != NumericMode.None)
                frame.Sphere = Math.Max(frame.Sphere, row.HomePosition.Length + entities.BodyBoundingRadius(entity));
        }

        // Each frame grows its numeric parent to the far side of its outline sphere. A semantic frame's sphere stays its
        // bubble, so what reaches out of a bubble grows nothing. A parent comes before its children in the dense order, so
        // the walk from the last frame to the first finishes each frame's sphere before its parent reads it.
        ReadOnlySpan<DPose> locals = frames.LocalPosesDense;
        for (int i = n - 1; i >= 0; i--)
        {
            ref readonly OutlineFrame frame = ref outline[i];
            if (frame.Parent < 0)
                continue;
            ref OutlineFrame parent = ref outline[frame.Parent];
            if (parent.Mode != NumericMode.None)
                parent.Sphere = Math.Max(parent.Sphere, locals[i].Position.Length + frame.Sphere);
        }

        Double3 origin = renderOrigin;
        for (int i = 0; i < n; i++)
        {
            ref readonly OutlineFrame frame = ref outline[i];
            if (frame.Parent < 0)
                continue;
            ref readonly DPose pose = ref relCurrent[i];
            var center = (pose.Position - origin).ToVector3();
            float radius = (float)frame.Sphere;
            snapshot.Outlines.Add(
                frame.Mode switch
                {
                    NumericMode.None => new SemanticOutline(
                        center,
                        pose.Rotation.ToQuaternion(),
                        frames.Bounds(denseFrames[i]).Radii
                    ),
                    NumericMode.Falling => new FallingOutline(center, radius),
                    NumericMode.Resting => new RestingOutline(center, radius),
                    NumericMode.Nesting => new NestingOutline(center, radius),
                    NumericMode.Following => new FollowingOutline(center, radius),
                }
            );
        }
    }

    private void EnsureDenseFrameArrays(int n)
    {
        if (relCurrent.Length >= n)
            return;
        int size = Math.Max(n, relCurrent.Length * 2);
        Array.Resize(ref relCurrent, size);
        Array.Resize(ref relPrevious, size);
        Array.Resize(ref localPrevious, size);
        Array.Resize(ref denseFrames, size);
        Array.Resize(ref frameHemisphere, size);
    }

    private void EnsureFrameIndexArrays(int index)
    {
        if (index < denseOfFrame.Length)
            return;
        int size = Math.Max(index + 1, denseOfFrame.Length * 2);
        Array.Resize(ref denseOfFrame, size);
        Array.Resize(ref genOfFrame, size);
        Array.Resize(ref frameSeen, size);
        Array.Resize(ref lastFrameLocal, size);
        Array.Resize(ref lastFrameHandle, size);
        Array.Resize(ref lastFrameParent, size);
        Array.Resize(ref lastFrameStamp, size);
    }

    /// <summary>Checks the invariant in the class comment. O(capacity): a pass over the whole memory column.</summary>
    internal void AssertInvariants()
    {
        foreach (ref readonly PoseMemory last in lastEntityPose.AsSpan())
            Contract.Invariant(last.Stamp >= 0 && last.Stamp <= buildStamp && (last.Stamp == 0 || !last.Frame.IsNull));
    }
}
