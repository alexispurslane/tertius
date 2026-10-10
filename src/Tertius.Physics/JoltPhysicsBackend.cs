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
    // Systems whose frames left the tree. They are collected first, because disposing them changes the world list.
    private readonly List<FrameWorld> lostWorlds = new();
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
    /// Applies the structural changes of one sync point in the order that <see cref="PhysicsChanges"/> documents:
    /// removes the bodies in <see cref="PhysicsChanges.BodiesDestroyed"/>, then the systems of the frames in
    /// <see cref="PhysicsChanges.FramesDestroyed"/>, then creates the bodies in <see cref="PhysicsChanges.BodiesCreated"/>.
    /// <see cref="PhysicsChanges.FramesCreated"/> needs no work, because a frame's system is created with its first
    /// body. An entity that owns a frame gets two bodies: its hull in the parent frame's system on the
    /// <see cref="BodyLayer.FrameHull"/> layer, and its interior as static geometry in its own frame's system. The
    /// frame must exist in <paramref name="frames"/> before its owner's body is created; the hull starts at the
    /// frame's pose and velocity in the tree, and its motion comes from the frame's driver.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Physics follows the frame tree. A frame in FramesDestroyed is already gone from <paramref name="frames"/>:
    /// <see cref="FrameTree.Destroy"/> moved its child frames to its parent, with poses and velocities converted. The
    /// hulls of those child frames move with them and take pose and velocity from the tree. Any other body still in
    /// the destroyed frame is removed with a warning, and so is the hull of an owner that outlives its frame. A frame
    /// in FramesDestroyed that the tree still has keeps its system, with a warning, until it leaves the tree. A frame
    /// that leaves the tree without a FramesDestroyed change, and a frame that the simulation reparents on its own,
    /// are handled here or at the start of the next step, whichever comes first.
    /// </para>
    /// <para>
    /// A shape that Jolt cannot build, a mesh on a body that moves, or a character without a capsule throws
    /// <see cref="ArgumentException"/> before any state changes for that body.
    /// </para>
    /// </remarks>
    public void ApplyChanges(PhysicsChanges changes, FrameTree frames)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(frames);
        lock (JoltRuntime.NativeGate)
        {
            this.frames = frames;
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
    /// Runs one physics step for every frame with awake bodies or characters: catches up with changes to the frame
    /// tree that no change batch announced, applies intents, drives kinematic frame hulls, applies gravity, moves
    /// characters, updates every system, then reports contacts, hands bodies between frames, updates ghosts and
    /// writes body and frame states to <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    /// <para>Intents, in the entity's frame axes. The intent of a frame's owner acts on the frame's hull.</para>
    /// <list type="bullet">
    /// <item>Teleport sets the pose to <see cref="PhysicsIntent.TeleportPose"/> and zeroes both velocities.
    /// SetVelocity, SetAngularVelocity and KinematicTarget apply after it, so a target in the same intent moves the
    /// body on from the teleport pose.</item>
    /// <item>KinematicTarget moves a kinematic body to <see cref="PhysicsIntent.Target"/> over one step and stops it
    /// there unless the next step gives a new target or velocity. A dynamic body gets the velocities that would carry
    /// it there.</item>
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

            // The simulation skips ApplyChanges when a sync point has no changes, but it may still have changed the
            // tree, for example to dock a ship.
            ReconcileFrames();
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
    }

    /// <summary>
    /// Brings physics in line with the frame tree: drops hand-off overrides that the tree now agrees with, removes
    /// the systems of frames that left the tree, and moves frame hulls whose frame the simulation reparented on its
    /// own. Afterwards every system belongs to a live frame.
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

        // Every structural change of the tree, removals included, changes its version.
        // Stryker disable once Statement : the version check only saves work; reconciling an unchanged tree changes nothing.
        if (frames.Version == reconciledFrameVersion)
            return;
        reconciledFrameVersion = frames.Version;

        // A frame that left the tree without a FramesDestroyed change, or after one that came too early, loses its
        // system the same way as a destroyed frame.
        // Stryker disable once Statement : without the clear, the guard below skips the stale entries; only memory grows.
        lostWorlds.Clear();
        foreach (FrameWorld world in worldList)
            if (!frames.IsAlive(world.Frame))
                lostWorlds.Add(world);
        foreach (FrameWorld world in lostWorlds)
        {
            // Re-homing a hull out of an earlier system can already have replaced this one at the same index.
            if (ExistingWorld(world.Frame) != world)
                continue;
            log($"Physics: {world.Frame} left the frame tree, but no FramesDestroyed change came with it. Its system is removed.");
            DisposeWorld(world);
        }

        for (int i = frameOwners.Count - 1; i >= 0; i--)
        {
            BodyRecord owner = frameOwners[i];
            // After the sweep every owner's frame is alive, so the first test is only a guard. A frame with an override
            // keeps its hull where the hand-off put it until the tree agrees.
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

    /// <summary>
    /// Applies one FramesDestroyed change. The tree must be rid of the frame already; a frame that it still has keeps
    /// its system until it leaves the tree, because physics follows the tree.
    /// </summary>
    private void DestroyWorld(FrameHandle frame)
    {
        if (frames.IsAlive(frame))
        {
            log($"Physics: {frame} is in FramesDestroyed but still in the frame tree. Its system stays until the frame leaves the tree.");
            return;
        }
        if (ExistingWorld(frame) is { } world)
            DisposeWorld(world);
    }

    /// <summary>
    /// Disposes the system of a frame that left the tree. The hulls of child frames follow the tree; every other
    /// body in the system is removed with a warning, and so is the owner's hull, which no longer stands for a frame.
    /// </summary>
    private void DisposeWorld(FrameWorld world)
    {
        FrameHandle frame = world.Frame;

        // The owner's hull and its interior in this system stand for the frame. Without the frame, they go.
        if (OwnerRecord(frame) is { } owner)
        {
            log($"Physics: {owner.Entity} still had a body when its frame {frame} was destroyed. Its hull is removed.");
            DestroyRecord(owner.Entity);
        }

        // Ghosts that other records keep in this system go with it.
        foreach (BodyRecord? record in records)
        {
            if (record?.Ghosts is not { } ghosts)
                continue;
            for (int i = ghosts.Count - 1; i >= 0; i--)
                if (ghosts[i].World == world)
                    ghosts.RemoveAt(i);
        }

        for (int i = world.Members.Count - 1; i >= 0; i--)
        {
            BodyRecord member = world.Members[i];
            if (member.IsHull && frames.IsAlive(member.OwnedFrame))
            {
                // A child frame outlives its parent. FrameTree.Destroy has already moved it to the grandparent (or
                // made it a root) and converted its pose and velocities, so the hull takes them from the tree as they
                // are. Physics converts nothing itself: the tree's conversion is the only one, so the destroyed
                // frame's motion counts once and the hull and the tree agree.
                parentOverrides.Remove(member.OwnedFrame.Index);
                PlaceHullFromTree(member, frames.Parent(member.OwnedFrame));
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
