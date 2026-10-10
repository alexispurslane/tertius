using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Simulation;

/// <summary>What a render snapshot shows, apart from the entities: the tick, the camera and the lights of the scene.</summary>
internal readonly record struct RenderView(
    long Tick,
    double Time,
    Entity Camera,
    float VerticalFov,
    FrameHandle SunFrame,
    Vector3 SunColor,
    float SunIntensity,
    AmbientLight Ambient);

/// <summary>
/// Builds render snapshots from the front tables. Every frame's pose relative to the camera's frame is computed in
/// doubles along the frame tree, so an object far away in a sibling frame is as exact as the float result allows, and
/// one near the camera is exact. Instances, lights and occluders are then written relative to the render origin, the
/// camera position at this tick.
/// </summary>
/// <remarks>
/// The builder remembers the frame and entity poses of the previous build, so that each instance also carries its
/// previous transform relative to the same origin. It interpolates only between consecutive builds of the same camera
/// in the same frame. Runs at the sync point, or on the ticking thread outside a tick; large builds fan out over the
/// thread pool, one slice of the entity table per worker, and join in slot order.
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
internal sealed class RenderSnapshotBuilder
{
    private const int RenderChunkSlots = 1024;
    private const int ParallelRenderThreshold = 8192;
    private static readonly Double3 LightForward = new(0, 0, -1);

    private readonly EntityTable entities;
    private readonly FrameTree frames;
    private readonly ParallelOptions parallelOptions;
    private readonly bool parallel;
    private readonly Action<int> renderChunk;

    // Frame data by dense index, rebuilt at every build.
    private DPose[] relCurrent = new DPose[16];
    private DPose[] relPrevious = new DPose[16];
    private DPose[] localPrevious = new DPose[16];
    private FrameHandle[] denseFrames = new FrameHandle[16];

    // Frame data by frame index.
    private int[] denseOfFrame = new int[16];
    private uint[] genOfFrame = new uint[16];
    private long[] frameSeen = new long[16];
    private DPose[] lastFrameLocal = new DPose[16];
    private FrameHandle[] lastFrameHandle = new FrameHandle[16];
    private FrameHandle[] lastFrameParent = new FrameHandle[16];
    private long[] lastFrameStamp = new long[16];

    /// <summary>The pose of one entity at the last build that placed it, relative to its frame then.</summary>
    private struct PoseMemory
    {
        public DPose Pose;
        public FrameHandle Frame;
        /// <summary>The build that wrote it, or 0 for none.</summary>
        public long Stamp;
    }

    private readonly EntityTable.Column<PoseMemory> lastEntityPose;

    private long buildStamp;
    private Entity lastCamera;
    private FrameHandle lastCameraFrame;
    private DPose lastCameraPose;
    private long lastCameraStamp;

    // Inputs of the per-chunk workers for the build that is running.
    private Double3 renderOrigin;
    private bool renderInterpolate;
    private int renderSlotCount;
    private RefList<RenderInstance>[] instanceChunks = [];
    private RefList<RenderLight>[] lightChunks = [];

    public RenderSnapshotBuilder(EntityTable entities, FrameTree frames, ParallelOptions parallelOptions)
    {
        this.entities = entities;
        this.frames = frames;
        this.parallelOptions = parallelOptions;
        parallel = parallelOptions.MaxDegreeOfParallelism != 1;
        renderChunk = BuildChunk;
        lastEntityPose = entities.AddColumn<PoseMemory>();
    }

    /// <summary>
    /// Fills <paramref name="snapshot"/> with the current state. Without a live camera, the snapshot has the view
    /// settings but nothing in it, and the next build does not interpolate.
    /// </summary>
    public void Build(RenderSnapshot snapshot, in RenderView view)
    {
        long stamp = ++buildStamp;
        snapshot.Clear();
        snapshot.Tick = view.Tick;
        snapshot.Time = view.Time;
        snapshot.Ambient = view.Ambient;
        snapshot.Camera = new RenderCamera { Rotation = Quaternion.Identity, PreviousRotation = Quaternion.Identity, VerticalFov = view.VerticalFov };
        snapshot.Sun = new RenderSun { Direction = Vector3.UnitY, Color = view.SunColor, Intensity = view.SunIntensity };
        snapshot.OriginInFrame = Double3.Zero;
        snapshot.OriginFrameId = 0;

        if (!TryGetCameraPose(view.Camera, out FrameHandle cameraFrame, out DPose cameraPose))
        {
            lastCamera = Entity.Null;
            return;
        }

        // Interpolate only when the same camera is in the same frame as at the previous build.
        bool interpolate = lastCamera == view.Camera && lastCameraFrame == cameraFrame && lastCameraStamp == stamp - 1;
        int frameCount = PrepareFramePoses(cameraFrame, stamp, interpolate);
        renderInterpolate = interpolate;
        renderOrigin = cameraPose.Position;

        DPose previousCamera = interpolate ? lastCameraPose : cameraPose;
        snapshot.Camera = new RenderCamera
        {
            Position = Vector3.Zero,
            Rotation = cameraPose.Rotation.ToQuaternion(),
            PreviousPosition = (previousCamera.Position - renderOrigin).ToVector3(),
            PreviousRotation = previousCamera.Rotation.ToQuaternion(),
            VerticalFov = view.VerticalFov,
        };
        snapshot.OriginInFrame = renderOrigin;
        // Stable for the life of the frame, and new when a frame reuses the index: the generation changes.
        snapshot.OriginFrameId = ((long)cameraFrame.Generation << 32) | (uint)cameraFrame.Index;

        BuildInstances(snapshot);
        BuildSun(snapshot, view);
        BuildOccluders(snapshot, frameCount);

        lastCamera = view.Camera;
        lastCameraFrame = cameraFrame;
        lastCameraPose = cameraPose;
        lastCameraStamp = stamp;
    }

    /// <summary>The camera's frame, and its eye pose in that frame: the entity pose composed with its view offset.</summary>
    private bool TryGetCameraPose(Entity camera, out FrameHandle frame, out DPose pose)
    {
        frame = default;
        pose = default;
        if (!entities.IsAlive(camera))
            return false;
        ref readonly TransformRow row = ref entities.FrontRow(camera);
        frame = row.Frame;
        DPose entityPose = row.Pose;
        if (frame.IsNull)
        {
            // The owner of a root frame looks out from its own frame's origin.
            frame = entities.OwnedFrame(camera);
            entityPose = DPose.Identity;
            if (frame.IsNull)
                return false;
        }
        pose = DPose.Compose(entityPose, Poses.Safe(entities.FrontRender(camera).ViewOffset).ToDPose());
        return true;
    }

    /// <summary>
    /// Fills the current and previous pose of every frame relative to the camera frame, by dense index, and records
    /// this build's local poses for the next one. Returns the frame count.
    /// </summary>
    private int PrepareFramePoses(FrameHandle cameraFrame, long stamp, bool interpolate)
    {
        int n = frames.Count;
        EnsureDenseFrameArrays(n);
        frames.ComputeRelativeTo(cameraFrame, relCurrent.AsSpan(0, n));

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
            FrameHandle parent = frames.Parent(frame);
            DPose local = locals[i];
            // A frame that is new, or that changed parent, has no usable previous local pose.
            bool known = lastFrameStamp[frame.Index] == stamp - 1 && lastFrameHandle[frame.Index] == frame && lastFrameParent[frame.Index] == parent;
            localPrevious[i] = known ? lastFrameLocal[frame.Index] : local;
            anyMoved |= !Poses.BitEquals(localPrevious[i], local);

            lastFrameLocal[frame.Index] = local;
            lastFrameHandle[frame.Index] = frame;
            lastFrameParent[frame.Index] = parent;
            lastFrameStamp[frame.Index] = stamp;
        }

        // The previous poses use this build's tree with last build's local poses: a frame that moved between the builds
        // carries its contents along, and equal local poses give bit-identical results.
        if (interpolate && anyMoved)
            frames.ComputeRelativeTo(cameraFrame, localPrevious.AsSpan(0, n), relPrevious.AsSpan(0, n));
        else
            relCurrent.AsSpan(0, n).CopyTo(relPrevious);
        return n;
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
                instanceChunks[i] = new RefList<RenderInstance>(64);
                lightChunks[i] = new RefList<RenderLight>(4);
            }
        }

        if (renderSlotCount >= ParallelRenderThreshold && parallel)
            Parallel.For(0, chunkCount, parallelOptions, renderChunk);
        else
            for (int c = 0; c < chunkCount; c++)
                BuildChunk(c);

        // Join the chunks in order, so that the instance order does not depend on threads.
        int instances = 0, lights = 0;
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
            if (!visible && !render.Light.Enabled)
                continue;
            if (!TryGetRenderPoses(entity, out DPose current, out DPose previous))
                continue;

            if (visible)
            {
                // One render row per entity today, so every instance is part 0.
                ref RenderInstance instance = ref instances.AddRef();
                instance = RenderInstance.Create(
                    render.Mesh,
                    render.Material,
                    (current.Position - origin).ToVector3(),
                    current.Rotation.ToQuaternion(),
                    render.Scale,
                    render.BoundingRadius,
                    render.Flags,
                    entityIndex: entity.Index,
                    part: 0);
                instance.PreviousPosition = (previous.Position - origin).ToVector3();
                instance.PreviousRotation = previous.Rotation.ToQuaternion();
            }
            if (render.Light.Enabled)
            {
                ref readonly LightData light = ref render.Light;
                DPose pose = DPose.Compose(current, Poses.Safe(light.Offset).ToDPose());
                ref RenderLight output = ref lights.AddRef();
                output = new RenderLight
                {
                    Kind = light.Kind,
                    Position = (pose.Position - origin).ToVector3(),
                    Direction = pose.Rotation.Rotate(LightForward).ToVector3(),
                    Color = light.Color,
                    Intensity = light.Intensity,
                    Range = light.Range,
                    InnerConeAngle = light.InnerConeAngle,
                    OuterConeAngle = light.OuterConeAngle,
                };
            }
        }
    }

    /// <summary>
    /// The entity's pose relative to the camera frame now and at the previous build. A frame owner uses its frame's
    /// pose. Any other entity composes its frame's pose with its own; its previous pose uses the frame it was in then,
    /// so a hand-off interpolates smoothly. False for an entity whose frame this build did not place. After a sync point
    /// every live entity is in a live frame, so that only guards against a broken table.
    /// </summary>
    private bool TryGetRenderPoses(Entity entity, out DPose current, out DPose previous)
    {
        FrameHandle own = entities.OwnedFrame(entity);
        if (!own.IsNull)
        {
            if (!FrameSeen(own))
            {
                current = previous = default;
                return false;
            }
            int d = denseOfFrame[own.Index];
            current = relCurrent[d];
            previous = renderInterpolate ? relPrevious[d] : current;
            return true;
        }

        ref readonly TransformRow row = ref entities.FrontRow(entity);
        if (!FrameSeen(row.Frame))
        {
            current = previous = default;
            return false;
        }
        current = DPose.Compose(relCurrent[denseOfFrame[row.Frame.Index]], row.Pose);
        previous = current;
        ref PoseMemory last = ref lastEntityPose[entity];
        if (renderInterpolate && last.Stamp == buildStamp - 1 && FrameSeen(last.Frame))
            previous = DPose.Compose(relPrevious[denseOfFrame[last.Frame.Index]], last.Pose);
        last = new PoseMemory { Pose = row.Pose, Frame = row.Frame, Stamp = buildStamp };
        return true;
    }

    /// <summary>True for a live frame that this build has placed.</summary>
    private bool FrameSeen(FrameHandle frame) =>
        !frame.IsNull && (uint)frame.Index < (uint)frameSeen.Length && frameSeen[frame.Index] == buildStamp && genOfFrame[frame.Index] == frame.Generation;

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

    /// <summary>Every planet frame with a render radius casts eclipses and planet shadows.</summary>
    private void BuildOccluders(RenderSnapshot snapshot, int frameCount)
    {
        for (int i = 0; i < frameCount; i++)
        {
            FrameHandle frame = denseFrames[i];
            if (frames.Gravity(frame).Kind != GravityKind.Point || !entities.IsAlive(frame.Entity))
                continue;
            float radius = entities.FrontRender(frame.Entity).BoundingRadius;
            if (radius <= 0)
                continue;
            snapshot.Occluders.Add(new RenderOccluder { Center = (relCurrent[i].Position - renderOrigin).ToVector3(), Radius = radius });
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
