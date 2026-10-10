using System.Collections.Concurrent;
using System.Diagnostics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// The physics backend on Jolt in double precision. Each reference frame that contains bodies gets its own Jolt
/// physics system, created when the first body arrives. All systems share one job system and step one after
/// another; each update spreads over the job threads. Jolt gravity is zero: before each step, every awake dynamic
/// body gets the force m · g_eff, where g_eff is the frame's own field minus the frame's proper acceleration.
/// After each step, the backend moves bodies between frames (hand-off) and keeps ghosts of bodies that cross a
/// bubble boundary. See ARCHITECTURE.md, "Reference frames" and "Physics".
/// </summary>
/// <remarks>
/// <see cref="ApplyChanges"/> runs at the sync point and <see cref="Step"/> on the physics thread. They never run at
/// the same time. <see cref="BoundingRadius"/> is thread-safe.
/// </remarks>
public sealed partial class JoltPhysicsBackend : IPhysicsBackend
{
    private readonly JoltBackendOptions options;
    private readonly Action<string> log;
    private readonly Layers.Tables layers;
    private readonly JobSystemThreadPool jobs;
    private readonly ShapeCache shapes = new();
    private readonly ConcurrentDictionary<ShapeDesc, float> radii = new(ReferenceEqualityComparer.Instance);

    // Physics systems by frame index, and the same worlds as a list for iteration.
    private FrameWorld?[] worlds = new FrameWorld?[16];
    private readonly List<FrameWorld> worldList = new();

    // Body records by entity slot index.
    private BodyRecord?[] records = new BodyRecord?[256];
    private readonly List<BodyRecord> frameOwners = new();

    // Frames that a hand-off moved to a new parent. The frame tree is read-only during a step, so these stand in for
    // the tree until the simulation reparents the frame and the tree agrees.
    private readonly Dictionary<int, (FrameHandle Frame, FrameHandle Parent)> parentOverrides = new();
    private List<FrameHandle>[] childLists = [];
    private readonly List<int> usedChildLists = new();
    private readonly List<FrameHandle> frameScratch = new();
    private int reconciledFrameVersion = -1;

    // Predicted end-of-step motion of frames that physics does not drive, by frame index, valid for one step.
    private FrameMotion[] predicted = new FrameMotion[16];
    // Steps count from 1, so a fresh entry of 0 never counts as cached. Only Step uses the cache.
    private long[] predictedStep = new long[16];

    private FrameTree frames = null!;
    private long stepIndex;
    private double stepTime;
    private double stepEndTime;
    private float stepDt;
    private bool disposed;

    /// <summary>Creates a backend. Initializes Jolt in double precision on first use in the process.</summary>
    public JoltPhysicsBackend(JoltBackendOptions? options = null)
    {
        this.options = options ?? new JoltBackendOptions();
        log = this.options.Log ?? (static message => Trace.TraceWarning(message));
        JoltRuntime.EnsureInitialized();
        layers = new Layers.Tables();
        jobs = this.options.WorkerThreads > 0
            ? new JobSystemThreadPool(new JobSystemThreadPoolConfig { numThreads = this.options.WorkerThreads })
            : new JobSystemThreadPool();
    }

    /// <summary>
    /// Applies the structural changes of one sync point: removes the bodies of dead entities, disposes the systems
    /// of destroyed frames and creates new bodies. An entity that owns a frame gets two bodies: its hull in the
    /// parent frame's system on the <see cref="BodyLayer.FrameHull"/> layer, and its interior as static geometry in
    /// its own frame's system. The frame must exist in <paramref name="frames"/> before its owner's body is created;
    /// the hull starts at the frame's pose and velocity in the tree, and its motion comes from the frame's driver.
    /// </summary>
    /// <remarks>
    /// A body still in a destroyed frame is removed with a warning, except the hull of a child frame, which moves
    /// to the next parent with the child. When the simulation reparents a frame on its own, the hull follows. A
    /// shape that Jolt cannot build, a mesh on a body that moves, or a character without a capsule throws
    /// <see cref="ArgumentException"/> before any state changes for that body.
    /// </remarks>
    public void ApplyChanges(PhysicsChanges changes, FrameTree frames)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(frames);
        lock (JoltRuntime.NativeGate)
        {
            this.frames = frames;
            // Frame systems are created lazily, with the first body, so FramesCreated needs no work.
            foreach (Entity entity in changes.BodiesDestroyed)
                DestroyRecord(entity);
            foreach (FrameHandle frame in changes.FramesDestroyed)
                DestroyWorld(frame);
            ReconcileFrames();
            foreach (BodyCreate create in changes.BodiesCreated)
                CreateRecord(create);
        }
    }

    /// <summary>
    /// Runs one physics step for every frame with awake bodies or characters: applies intents, drives kinematic
    /// frame hulls, applies gravity, moves characters, updates every system, then reports contacts, hands bodies
    /// between frames, updates ghosts and writes body and frame states to <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    /// <para>Intents, in the entity's frame axes. The intent of a frame's owner acts on the frame's hull.</para>
    /// <list type="bullet">
    /// <item>Teleport sets the pose and zeroes both velocities. SetVelocity and SetAngularVelocity apply after it.</item>
    /// <item>KinematicTarget moves a kinematic body to the target over one step and stops it there unless the next
    /// step gives a new target or velocity. A dynamic body gets the velocities that would carry it there.</item>
    /// <item>Force, Torque, Impulse and AngularImpulse act on dynamic bodies, for this step only.</item>
    /// <item>A character keeps the desired velocity of its last CharacterMove until the next one. Under gravity only
    /// the part across the up direction counts; near zero gravity it is a full 3D velocity. Jump applies only on
    /// the ground. Impulses and forces change a character's velocity by their mass. KinematicTarget does nothing to
    /// a character.</item>
    /// </list>
    /// <para>
    /// Outputs: <see cref="PhysicsStepOutput.Bodies"/> holds every awake dynamic or kinematic body and every
    /// character, plus one last report for a body that fell asleep. Velocities are at the center of mass.
    /// <see cref="PhysicsStepOutput.Frames"/> holds every physics-driven frame with a hull, relative to its parent
    /// after any hand-off in this step, with the velocity of the frame origin. Apply hand-offs before frame states.
    /// <see cref="PhysicsStepOutput.Contacts"/> holds each body pair that started touching, with A before B in
    /// entity order and the normal from A to B. Contacts with ghosts are not reported. A pair that separates, or
    /// falls asleep and wakes, reports again when it touches again.
    /// </para>
    /// </remarks>
    public void Step(in PhysicsStepContext context, PhysicsStepOutput output)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context.Frames);
        if (!(context.DeltaTime > 0))
            throw new ArgumentOutOfRangeException(nameof(context), context.DeltaTime, "The step needs a positive delta time.");

        lock (JoltRuntime.NativeGate)
        {
            frames = context.Frames;
            stepIndex++;
            stepDt = context.DeltaTime;
            stepTime = context.Time;
            stepEndTime = context.Time + context.DeltaTime;

            RefreshFrameStructure();
            CaptureHullVelocities();
            ApplyIntents(context);
            DriveKinematicHulls();
            ApplyGravity();
            UpdateCharacters();
            UpdateSystems();
            ComputeProperAccelerations();
            CollectContacts(output);
            HandOffBodies(output);
            UpdateGhosts();
            WriteBodies(output);
            WriteFrames(output);
        }
    }

    /// <inheritdoc/>
    public float BoundingRadius(ShapeDesc shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return radii.GetOrAdd(shape, static s => ShapeBounds.Radius(s));
    }

    /// <summary>Destroys every character, body, physics system and shape, and the job system.</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        lock (JoltRuntime.NativeGate)
        {
            // A character removes its inner body from its system, so it must go before the system.
            foreach (BodyRecord? record in records)
                record?.Character?.Dispose();
            foreach (FrameWorld world in worldList)
                world.Dispose();
            worldList.Clear();
            Array.Clear(worlds);
            Array.Clear(records);
            frameOwners.Clear();
            shapes.Dispose();
            jobs.Dispose();
            layers.Dispose();
        }
    }

    // ---- Diagnostics for tests ----

    /// <summary>The number of physics systems that exist.</summary>
    internal int SystemCount => worldList.Count;

    /// <summary>The frame whose system holds the entity's primary body or character.</summary>
    internal bool TryGetBodyFrame(Entity entity, out FrameHandle frame)
    {
        frame = Find(entity) is { HasPrimary: true } record ? record.Frame : FrameHandle.Null;
        return !frame.IsNull;
    }

    internal int GhostCount(Entity entity) => Find(entity)?.Ghosts?.Count ?? 0;

    internal bool HasGhostIn(Entity entity, FrameHandle frame)
    {
        if (Find(entity)?.Ghosts is not { } ghosts)
            return false;
        foreach (GhostBody ghost in ghosts)
            if (ghost.Frame == frame)
                return true;
        return false;
    }

    internal bool HasInterior(Entity owner) => Find(owner) is { InteriorWorld: not null };

    // ---- Frame structure ----

    private BodyRecord? Find(Entity entity)
    {
        if (entity.IsNull || entity.Index >= (uint)records.Length)
            return null;
        BodyRecord? record = records[entity.Index];
        return record is not null && record.Entity == entity ? record : null;
    }

    /// <summary>The record of the entity that owns <paramref name="frame"/>, if that entity has a body.</summary>
    private BodyRecord? OwnerRecord(FrameHandle frame) =>
        Find(frame.Entity) is { } record && record.OwnedFrame == frame ? record : null;

    /// <summary>The parent of a frame, including hand-offs that the frame tree does not show yet.</summary>
    private FrameHandle ParentOf(FrameHandle frame)
    {
        if (parentOverrides.TryGetValue(frame.Index, out var link) && link.Frame == frame)
            return link.Parent;
        return frames.Parent(frame);
    }

    private List<FrameHandle> ChildrenOf(FrameHandle frame) =>
        frame.Index < childLists.Length && childLists[frame.Index] is { } list ? list : EmptyChildren;

    private static readonly List<FrameHandle> EmptyChildren = new();

    /// <summary>Rebuilds the child lists of every frame from the tree and the pending hand-offs. Allocates nothing once warm.</summary>
    private void RefreshFrameStructure()
    {
        foreach (int index in usedChildLists)
            childLists[index].Clear();
        usedChildLists.Clear();

        frameScratch.Clear();
        frames.GetAll(frameScratch);
        foreach (FrameHandle frame in frameScratch)
        {
            FrameHandle parent = ParentOf(frame);
            if (parent.IsNull)
                continue;
            if (parent.Index >= childLists.Length)
                Array.Resize(ref childLists, Math.Max(parent.Index + 1, childLists.Length * 2));
            List<FrameHandle> list = childLists[parent.Index] ??= new List<FrameHandle>();
            if (list.Count == 0)
                usedChildLists.Add(parent.Index);
            list.Add(frame);
        }

        foreach (FrameWorld world in worldList)
        {
            if (!world.Orphaned && !frames.IsAlive(world.Frame))
            {
                world.Orphaned = true;
                log($"Physics: {world.Frame} left the frame tree without a FramesDestroyed change. Its system is no longer stepped.");
            }
        }
    }

    /// <summary>
    /// Drops hand-off overrides that the tree now agrees with, and moves frame hulls whose frame the simulation
    /// reparented on its own.
    /// </summary>
    private void ReconcileFrames()
    {
        if (parentOverrides.Count > 0)
        {
            frameScratch.Clear();
            foreach (var (_, link) in parentOverrides)
                if (!frames.IsAlive(link.Frame) || frames.Parent(link.Frame) == link.Parent)
                    frameScratch.Add(link.Frame);
            foreach (FrameHandle frame in frameScratch)
                parentOverrides.Remove(frame.Index);
        }

        if (frames.Version == reconciledFrameVersion)
            return;
        reconciledFrameVersion = frames.Version;
        for (int i = frameOwners.Count - 1; i >= 0; i--)
        {
            BodyRecord owner = frameOwners[i];
            if (!frames.IsAlive(owner.OwnedFrame) || parentOverrides.ContainsKey(owner.OwnedFrame.Index))
                continue;
            FrameHandle parent = frames.Parent(owner.OwnedFrame);
            if (owner.Frame != parent)
                PlaceHullFromTree(owner, parent);
        }
    }

    // ---- Worlds ----

    private FrameWorld WorldFor(FrameHandle frame)
    {
        if (frame.Index >= worlds.Length)
            Array.Resize(ref worlds, Math.Max(frame.Index + 1, worlds.Length * 2));
        FrameWorld? world = worlds[frame.Index];
        if (world is not null && world.Frame == frame)
            return world;
        if (world is not null)
        {
            log($"Physics: {world.Frame} was replaced by {frame} without a FramesDestroyed change. Its system is discarded.");
            DisposeWorld(world);
        }
        world = new FrameWorld(frame, layers, options);
        worlds[frame.Index] = world;
        worldList.Add(world);
        return world;
    }

    private FrameWorld? ExistingWorld(FrameHandle frame) =>
        frame.Index < worlds.Length && worlds[frame.Index] is { } world && world.Frame == frame ? world : null;

    /// <summary>Removes a destroyed frame's system. Child frame hulls move to the next parent; other bodies are removed.</summary>
    private void DestroyWorld(FrameHandle frame)
    {
        parentOverrides.Remove(frame.Index);
        if (ExistingWorld(frame) is not { } world)
            return;
        DisposeWorld(world);
    }

    private void DisposeWorld(FrameWorld world)
    {
        FrameHandle frame = world.Frame;
        bool frameAlive = frames.IsAlive(frame);

        // Ghosts that other records keep in this system go with it.
        foreach (BodyRecord? record in records)
        {
            if (record?.Ghosts is not { } ghosts)
                continue;
            for (int i = ghosts.Count - 1; i >= 0; i--)
                if (ghosts[i].World == world)
                    ghosts.RemoveAt(i);
        }

        // The interior belongs to the frame. Its owner may live on.
        foreach (BodyRecord? record in records)
        {
            if (record is not null && record.InteriorWorld == world)
            {
                record.InteriorWorld = null;
                record.InteriorBody = BodyID.Invalid;
            }
        }

        for (int i = world.Members.Count - 1; i >= 0; i--)
        {
            BodyRecord member = world.Members[i];
            if (member.IsHull && frames.IsAlive(member.OwnedFrame) && member.OwnedFrame != frame)
            {
                // A child frame survives the destruction of its parent and moves to the grandparent.
                parentOverrides.Remove(member.OwnedFrame.Index);
                RehomeHull(member, world, frameAlive);
            }
            else
            {
                log($"Physics: {member.Entity} was still in {frame} when the frame was destroyed. Its body is removed.");
                DestroyRecord(member.Entity);
            }
        }

        worlds[frame.Index] = null;
        worldList.Remove(world);
        world.Dispose();
    }

    /// <summary>Predicted motion of a frame that physics does not drive, at the end of the current step, relative to its tree parent.</summary>
    private FrameMotion PredictedMotion(FrameHandle frame)
    {
        int index = frame.Index;
        if (index >= predicted.Length)
        {
            int size = Math.Max(index + 1, predicted.Length * 2);
            Array.Resize(ref predicted, size);
            Array.Resize(ref predictedStep, size);
        }
        if (predictedStep[index] == stepIndex)
            return predicted[index];
        FrameMotion motion = PredictMotion(frame, stepEndTime);
        predicted[index] = motion;
        predictedStep[index] = stepIndex;
        return motion;
    }

    /// <summary>
    /// The motion of a frame relative to its tree parent at <paramref name="time"/>. A Kepler frame uses its orbit
    /// when the tree pose matches the orbit at the step start; anything else extrapolates the tree pose with the
    /// tree velocities.
    /// </summary>
    private FrameMotion PredictMotion(FrameHandle frame, double time)
    {
        DPose pose = frames.LocalPose(frame);
        MotionDriver driver = frames.Driver(frame);
        if (driver == MotionDriver.Static)
            return new FrameMotion(pose, Double3.Zero, Double3.Zero);

        if (driver == MotionDriver.Kepler)
        {
            KeplerOrbit orbit = frames.Orbit(frame);
            if (OrbitMatchesTree(orbit, pose))
            {
                var (position, velocity) = orbit.StateAt(time);
                return new FrameMotion(new DPose(position, orbit.RotationAt(time)), velocity, orbit.AngularVelocity());
            }
        }

        double ahead = time - stepTime;
        Double3 linear = frames.LinearVelocity(frame);
        Double3 angular = frames.AngularVelocity(frame);
        var extrapolated = new DPose(pose.Position + linear * ahead, Rotations.Integrate(pose.Rotation, angular, ahead));
        return new FrameMotion(extrapolated, linear, angular);
    }

    private bool OrbitMatchesTree(in KeplerOrbit orbit, in DPose treePose)
    {
        var (position, _) = orbit.StateAt(stepTime);
        double tolerance = 1e-3 + 1e-9 * treePose.Position.Length;
        if ((position - treePose.Position).Length > tolerance)
            return false;
        DQuaternion rotation = orbit.RotationAt(stepTime);
        DQuaternion q = treePose.Rotation;
        double dot = Math.Abs(rotation.X * q.X + rotation.Y * q.Y + rotation.Z * q.Z + rotation.W * q.W);
        return dot > 1 - 1e-6;
    }

    /// <summary>
    /// The motion of <paramref name="frame"/> relative to its current parent at the end of the step. A
    /// physics-driven frame reads its hull body; other frames use their prediction. Call it only during a step.
    /// </summary>
    private FrameMotion MotionOf(FrameHandle frame) =>
        TryHullMotion(frame, out FrameMotion motion) ? motion : PredictedMotion(frame);

    /// <summary>
    /// The motion of <paramref name="frame"/> relative to its parent at the sync point: the hull for a
    /// physics-driven frame, the tree for the others.
    /// </summary>
    private FrameMotion CurrentMotion(FrameHandle frame) =>
        TryHullMotion(frame, out FrameMotion motion)
            ? motion
            : new FrameMotion(frames.LocalPose(frame), frames.LinearVelocity(frame), frames.AngularVelocity(frame));

    private bool TryHullMotion(FrameHandle frame, out FrameMotion motion)
    {
        if (frames.Driver(frame) == MotionDriver.Physics && OwnerRecord(frame) is { HasPrimary: true, Character: null } hull)
        {
            BodySnapshot s = ReadPrimary(hull);
            // Jolt's velocity is at the center of mass. The frame origin is the body origin.
            Double3 originVelocity = (Double3)s.Linear + Double3.Cross(s.Angular, s.Pose.Position - s.CenterOfMass);
            motion = new FrameMotion(s.Pose, originVelocity, s.Angular);
            return true;
        }
        motion = default;
        return false;
    }
}
