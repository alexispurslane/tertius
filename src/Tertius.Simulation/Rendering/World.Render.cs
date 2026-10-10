using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Simulation;

public sealed partial class World
{
    private const int RenderChunkSlots = 1024;
    private const int ParallelRenderThreshold = 8192;
    private static readonly Double3 LightForward = new(0, 0, -1);

    private readonly Action<int> renderChunk;

    // Frame data by dense index, rebuilt at every build.
    private DPose[] relCurrent = new DPose[16];
    private DPose[] relPrevious = new DPose[16];
    private DPose[] localCurrent = new DPose[16];
    private DPose[] localPrevious = new DPose[16];
    private int[] parentDense = new int[16];
    private byte[] onPath = new byte[16];
    private FrameHandle[] denseFrames = new FrameHandle[16];

    // Frame data by frame index.
    private int[] denseOfFrame = new int[16];
    private uint[] genOfFrame = new uint[16];
    private long[] frameSeen = new long[16];
    private DPose[] lastFrameLocal = new DPose[16];
    private FrameHandle[] lastFrameHandle = new FrameHandle[16];
    private FrameHandle[] lastFrameParent = new FrameHandle[16];
    private long[] lastFrameStamp = new long[16];

    // Entity data by slot: the pose that the previous build used.
    private DPose[] lastEntityPose = [];
    private FrameHandle[] lastEntityFrame = [];
    private uint[] lastEntityGen = [];
    private long[] lastEntityStamp = [];

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

    /// <summary>
    /// Builds a render snapshot of the current state and publishes it. The sync point does this every tick; call it
    /// yourself to show the world before the first tick. Outside a tick only.
    /// </summary>
    public void PublishRenderSnapshot()
    {
        ThrowIfInTick(nameof(PublishRenderSnapshot));
        BuildRenderSnapshot();
    }

    /// <summary>
    /// Writes every visible entity, light, occluder and the camera relative to the render origin: the camera position
    /// in the camera's frame. Frame poses relative to that frame are computed in doubles along the tree, so an object
    /// far away in a sibling frame is as exact as the float result allows, and one near the camera is exact.
    /// </summary>
    private void BuildRenderSnapshot()
    {
        long stamp = ++buildStamp;
        RenderSnapshot snapshot = RenderSnapshots.WriteBuffer;
        snapshot.Clear();
        snapshot.Tick = TickNumber;
        snapshot.Time = Time;
        snapshot.Ambient = Ambient;
        snapshot.Camera = new RenderCamera { Rotation = Quaternion.Identity, PreviousRotation = Quaternion.Identity, VerticalFov = VerticalFov };
        snapshot.Sun = new RenderSun { Direction = Vector3.UnitY, Color = SunColor, Intensity = SunIntensity };

        if (TryGetCameraPose(out FrameHandle cameraFrame, out DPose cameraPose))
        {
            // Interpolate only when the same camera is in the same frame as at the previous build.
            bool interpolate = lastCamera == Camera && lastCameraFrame == cameraFrame && lastCameraStamp == stamp - 1;
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
                VerticalFov = VerticalFov,
            };

            BuildInstances(snapshot);
            BuildSun(snapshot);
            BuildOccluders(snapshot, frameCount);

            lastCamera = Camera;
            lastCameraFrame = cameraFrame;
            lastCameraPose = cameraPose;
            lastCameraStamp = stamp;
        }
        else
        {
            lastCamera = Entity.Null;
        }

        snapshot.PublishedAt = Stopwatch.GetTimestamp();
        RenderSnapshots.Publish();
    }

    private bool TryGetCameraPose(out FrameHandle frame, out DPose pose)
    {
        Entity camera = Camera;
        frame = default;
        pose = default;
        if (!IsAlive(camera))
            return false;
        ref readonly TransformRow row = ref frontTransform[camera.Index];
        frame = row.Frame;
        DPose entityPose = row.Pose;
        if (frame.IsNull)
        {
            // The owner of a root frame looks out from its own frame's origin.
            frame = records[camera.Index].OwnedFrame;
            entityPose = DPose.Identity;
            if (frame.IsNull)
                return false;
        }
        pose = DPose.Compose(entityPose, SafePose(frontRender[camera.Index].ViewOffset).ToDPose());
        return true;
    }

    /// <summary>
    /// Fills the current and previous pose of every frame relative to the camera frame, by dense index, and records
    /// this build's local poses for the next one. Returns the frame count.
    /// </summary>
    private int PrepareFramePoses(FrameHandle cameraFrame, long stamp, bool interpolate)
    {
        int n = Frames.Count;
        EnsureDenseFrameArrays(n);
        Frames.ComputeRelativeTo(cameraFrame, relCurrent.AsSpan(0, n));

        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = Frames.AtDense(i);
            EnsureFrameIndexArrays(frame.Index);
            denseFrames[i] = frame;
            denseOfFrame[frame.Index] = i;
            genOfFrame[frame.Index] = frame.Generation;
            frameSeen[frame.Index] = stamp;
        }

        bool anyMoved = false;
        for (int i = 0; i < n; i++)
        {
            FrameHandle frame = denseFrames[i];
            FrameHandle parent = Frames.Parent(frame);
            parentDense[i] = parent.IsNull ? -1 : denseOfFrame[parent.Index];
            DPose local = Frames.LocalPose(frame);
            localCurrent[i] = local;
            // A frame that is new, or that changed parent, has no usable previous local pose.
            bool known = lastFrameStamp[frame.Index] == stamp - 1 && lastFrameHandle[frame.Index] == frame && lastFrameParent[frame.Index] == parent;
            localPrevious[i] = known ? lastFrameLocal[frame.Index] : local;
            anyMoved |= !BitEquals(localPrevious[i], local);

            lastFrameLocal[frame.Index] = local;
            lastFrameHandle[frame.Index] = frame;
            lastFrameParent[frame.Index] = parent;
            lastFrameStamp[frame.Index] = stamp;
        }

        if (interpolate && anyMoved)
            ComputeRelative(denseOfFrame[cameraFrame.Index], localPrevious, relPrevious, n);
        else
            relCurrent.AsSpan(0, n).CopyTo(relPrevious);
        return n;
    }

    /// <summary>
    /// The pose of every frame relative to frame <paramref name="reference"/>, from the given local poses. This is the
    /// same computation, in the same order, as <see cref="PoseTree{TPose}.ComputeRelativeTo"/>, so equal local poses
    /// give bit-identical results.
    /// </summary>
    private void ComputeRelative(int reference, ReadOnlySpan<DPose> local, Span<DPose> result, int n)
    {
        Span<byte> path = onPath.AsSpan(0, n);
        path.Clear();
        result[reference] = DPose.Identity;
        path[reference] = 1;
        int x = reference;
        while (parentDense[x] >= 0)
        {
            int p = parentDense[x];
            result[p] = DPose.Compose(result[x], local[x].Inverse());
            path[p] = 1;
            x = p;
        }
        DPose virtualRoot = DPose.Compose(result[x], local[x].Inverse());
        for (int i = 0; i < n; i++)
        {
            if (path[i] != 0)
                continue;
            int p = parentDense[i];
            result[i] = p < 0 ? DPose.Compose(virtualRoot, local[i]) : DPose.Compose(result[p], local[i]);
        }
    }

    private void BuildInstances(RenderSnapshot snapshot)
    {
        renderSlotCount = Math.Min(slotCount, capacity);
        EnsureRenderSlotArrays(capacity);
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

        if (renderSlotCount >= ParallelRenderThreshold && options.MaxDegreeOfParallelism != 1)
            Parallel.For(0, chunkCount, parallelOptions, renderChunk);
        else
            for (int c = 0; c < chunkCount; c++)
                BuildRenderChunk(c);

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

    private void BuildRenderChunk(int chunk)
    {
        RefList<RenderInstance> instances = instanceChunks[chunk];
        RefList<RenderLight> lights = lightChunks[chunk];
        int start = chunk * RenderChunkSlots;
        int end = Math.Min(start + RenderChunkSlots, renderSlotCount);
        Double3 origin = renderOrigin;
        for (int slot = start; slot < end; slot++)
        {
            if (slotState[slot] != SlotState.Live)
                continue;
            ref RenderData render = ref frontRender[slot];
            bool visible = (render.Flags & RenderFlags.Visible) != 0 && !render.Mesh.IsNone;
            if (!visible && !render.Light.Enabled)
                continue;
            if (!TryGetRenderPoses(slot, out DPose current, out DPose previous))
                continue;

            if (visible)
            {
                ref RenderInstance instance = ref instances.AddRef();
                instance = new RenderInstance
                {
                    EntityIndex = (uint)slot,
                    Mesh = render.Mesh,
                    Material = render.Material,
                    Flags = render.Flags,
                    Position = (current.Position - origin).ToVector3(),
                    Rotation = current.Rotation.ToQuaternion(),
                    Scale = render.Scale,
                    PreviousPosition = (previous.Position - origin).ToVector3(),
                    PreviousRotation = previous.Rotation.ToQuaternion(),
                    BoundingRadius = render.BoundingRadius,
                };
            }
            if (render.Light.Enabled)
            {
                ref readonly LightData light = ref render.Light;
                DPose pose = DPose.Compose(current, SafePose(light.Offset).ToDPose());
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
    /// so a hand-off interpolates smoothly.
    /// </summary>
    private bool TryGetRenderPoses(int slot, out DPose current, out DPose previous)
    {
        FrameHandle own = records[slot].OwnedFrame;
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

        ref readonly TransformRow row = ref frontTransform[slot];
        if (!FrameSeen(row.Frame))
        {
            current = previous = default;
            return false;
        }
        current = DPose.Compose(relCurrent[denseOfFrame[row.Frame.Index]], row.Pose);
        previous = current;
        uint gen = generation[slot];
        if (renderInterpolate && lastEntityStamp[slot] == buildStamp - 1 && lastEntityGen[slot] == gen)
        {
            FrameHandle lastFrame = lastEntityFrame[slot];
            if (FrameSeen(lastFrame))
                previous = DPose.Compose(relPrevious[denseOfFrame[lastFrame.Index]], lastEntityPose[slot]);
        }
        lastEntityPose[slot] = row.Pose;
        lastEntityFrame[slot] = row.Frame;
        lastEntityGen[slot] = gen;
        lastEntityStamp[slot] = buildStamp;
        return true;
    }

    /// <summary>True for a live frame that this build has placed.</summary>
    private bool FrameSeen(FrameHandle frame) =>
        !frame.IsNull && (uint)frame.Index < (uint)frameSeen.Length && frameSeen[frame.Index] == buildStamp && genOfFrame[frame.Index] == frame.Generation;

    private void BuildSun(RenderSnapshot snapshot)
    {
        FrameHandle sun = SunFrame;
        if (!Frames.IsAlive(sun) || !FrameSeen(sun))
            return;
        Double3 toSun = relCurrent[denseOfFrame[sun.Index]].Position - renderOrigin;
        double distance = toSun.Length;
        if (!(distance > 0))
            return;
        float radius = IsAlive(sun.Entity) ? frontRender[sun.Entity.Index].BoundingRadius : 0;
        snapshot.Sun.Direction = (toSun / distance).ToVector3();
        snapshot.Sun.AngularRadius = radius > 0 ? (float)Math.Atan(radius / distance) : 0;
    }

    /// <summary>Every planet frame with a render radius casts eclipses and planet shadows.</summary>
    private void BuildOccluders(RenderSnapshot snapshot, int frameCount)
    {
        for (int i = 0; i < frameCount; i++)
        {
            FrameHandle frame = denseFrames[i];
            if (Frames.Gravity(frame).Kind != GravityKind.Point || !IsAlive(frame.Entity))
                continue;
            float radius = frontRender[frame.Entity.Index].BoundingRadius;
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
        Array.Resize(ref localCurrent, size);
        Array.Resize(ref localPrevious, size);
        Array.Resize(ref parentDense, size);
        Array.Resize(ref onPath, size);
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

    private void EnsureRenderSlotArrays(int size)
    {
        if (lastEntityPose.Length >= size)
            return;
        Array.Resize(ref lastEntityPose, size);
        Array.Resize(ref lastEntityFrame, size);
        Array.Resize(ref lastEntityGen, size);
        Array.Resize(ref lastEntityStamp, size);
    }
}
