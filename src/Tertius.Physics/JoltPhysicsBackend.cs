using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>
/// The physics backend on Jolt in double precision. Each reference frame that contains bodies gets its own Jolt
/// physics system, created when the first body arrives. All systems share one job system and step one after
/// another; each update spreads over the job threads. Jolt gravity is zero: before each step, every awake dynamic
/// body gets the force m · g_eff, where g_eff is the frame's own field minus the frame's proper acceleration. The force
/// leaves the body's sleep timer alone, so a body at rest falls asleep. The sleep threshold of each system scales with the
/// weakest effective gravity on its awake bodies, so a body that a small share of a weak gravity moves stays awake.
/// After each step, the backend moves bodies between frames (hand-off) and keeps ghosts of bodies that cross a
/// bubble boundary. See ARCHITECTURE.md, "Reference frames" and "Physics".
/// </summary>
/// <remarks>
/// <see cref="ApplyChanges"/> runs at the sync point and <see cref="Step"/> on the physics thread. They never run at
/// the same time. <see cref="BoundingRadius"/> is thread-safe. Both start by reconciling with the frame tree, and
/// <see cref="AssertInvariants"/> lists what every reconcile guarantees. A system that is full grows when the backend
/// adds a body to it (see <see cref="Grow"/>), so every entity with a body keeps it for its whole life.
/// </remarks>
public sealed partial class JoltPhysicsBackend : IPhysicsBackend
{
    private readonly JoltBackendOptions options;
    private readonly Action<string> log;
    private readonly JobSystemThreadPool jobs;
    private readonly ShapeCache shapes = new();
    private readonly ConcurrentDictionary<ShapeDesc, float> radii = new(ReferenceEqualityComparer.Instance);
    // The answer of ShapeProblem for each shape description that it was asked about: null when Jolt can build the shape.
    private readonly ConcurrentDictionary<ShapeDesc, string?> shapeProblems = new(ReferenceEqualityComparer.Instance);

    // Physics systems by frame index, and the same worlds as a list for iteration.
    private FrameWorld?[] worlds = new FrameWorld?[16];
    private readonly List<FrameWorld> worldList = new();

    // Body records by entity slot index.
    private BodyRecord?[] records = new BodyRecord?[256];
    private readonly List<BodyRecord> frameOwners = new();

    // Frames that a hand-off moved to a new parent during the current step. The frame tree is read-only during a step,
    // so these stand in for the tree until the step ends. The sync point then applies or refuses each hand-off, and
    // ReconcileFrames drops them all, so none survives into the next step.
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
        jobs = NativeOwnership.Owned(this.options.WorkerThreads > 0
            ? new JobSystemThreadPool(new JobSystemThreadPoolConfig { numThreads = this.options.WorkerThreads })
            : new JobSystemThreadPool());
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
    /// Physics follows the frame tree. A hand-off of a frame is only a proposal, which the sync point applies or refuses
    /// before this call, so the frame's hull goes where the tree has the frame now. A frame in FramesDestroyed is already
    /// gone from <paramref name="frames"/>: <see cref="FrameTree.Destroy"/> moved its child frames to its parent, with
    /// poses and velocities converted. The hulls of those child frames move with them and take pose and velocity from
    /// the tree. Any other body still in the destroyed frame is removed with a warning, and so is the hull of an owner
    /// that outlives its frame. A frame in FramesDestroyed that the tree still has keeps its system, with a warning,
    /// until it leaves the tree. A refused hand-off, a frame that leaves the tree without a FramesDestroyed change, a
    /// frame that the simulation reparents on its own, and a frame whose driver the game changes are handled here or at
    /// the start of the next step, whichever comes first. A hull follows its frame's driver: dynamic for the physics
    /// driver, kinematic for the Kepler and Spin drivers, static for the static driver. The simulation never gives a frame a driver
    /// that its hull cannot follow (see <see cref="BodyDesc.FrameHullProblem"/>). Such a driver comes only from a bug: it
    /// breaks a contract in a Debug build, and a Release build keeps the hull's motion and reports the bug (see
    /// <see cref="ReportBug"/>).
    /// </para>
    /// <para>
    /// The simulation refuses at spawn every body that physics cannot create, so a change that fails here comes only from
    /// a bug, for example a body that <see cref="BodyDesc.Problem"/> or <see cref="ShapeProblem"/> refuses. A failed
    /// change breaks a contract in a Debug build. A Release build skips it, leaves nothing of it behind, applies every other
    /// change of the batch, and adds a message to <see cref="PhysicsChanges.Errors"/> (see <see cref="ReportBug"/>).
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
            bugReport = changes.Errors;
            foreach (Entity entity in changes.BodiesDestroyed)
            {
                try
                {
                    DestroyRecord(entity);
                }
                catch (Exception ex) when (ex is not ContractException)
                {
                    ChangeFailed($"destroy the body of {entity}", ex);
                }
            }
            foreach (FrameHandle frame in changes.FramesDestroyed)
            {
                try
                {
                    DestroyWorld(frame);
                }
                catch (Exception ex) when (ex is not ContractException)
                {
                    ChangeFailed($"destroy the system of {frame}", ex);
                }
            }
            // Moves come before the reconcile: the tree already has each moved hull's frame under its new home, and the
            // move keeps the hull's state, which a reconcile would take from the tree.
            ApplyMoves(changes.BodiesMoved);
            ReconcileFrames();
            foreach (BodyCreate create in changes.BodiesCreated)
            {
                try
                {
                    CreateRecord(create);
                }
                catch (Exception ex) when (ex is not ContractException)
                {
                    ChangeFailed($"create the body of {create.Entity}", ex);
                }
            }
        }
    }

    /// <summary>One change of a batch failed. Only a bug sends such a change. <see cref="ApplyChanges"/> goes on with the next one.</summary>
    private void ChangeFailed(string change, Exception ex) =>
        ReportBug($"Physics could not {change}: {ex.GetType().Name}: {ex.Message} Only a bug sends such a change, so it was skipped.");

    // The list that takes the messages of ReportBug: the errors of the change batch or of the step output that the backend
    // is filling now.
    private List<string>? bugReport;

    /// <summary>
    /// Reports a state that only a bug produces, which the backend found and skipped. It breaks a contract in a Debug build.
    /// A Release build adds the message to the errors of the change batch or the step output that the backend is filling,
    /// and the simulation records it in its errors. Every state that the backend can tell comes only from a bug goes here,
    /// not to the log of warnings.
    /// </summary>
    private void ReportBug(string message)
    {
        Contract.Assert(false, message);
        bugReport?.Add(message);
    }

    /// <summary>
    /// Runs one physics step for every frame with awake bodies or characters: puts every frame hull where the frame tree
    /// says, with the motion of its frame's driver, which drops the hand-offs of the last step that the simulation refused
    /// and follows changes to the tree that no change batch announced, then applies intents, drives kinematic frame
    /// hulls, applies gravity, moves characters, updates every system, then reports contacts, hands bodies between frames,
    /// updates ghosts and writes body, character and frame states to <paramref name="output"/>.
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
    /// <item>A character keeps the desired velocity of its last CharacterMove until the next one. Only a character that
    /// physics stands moves by it and can jump. Physics stands a character when the effective gravity is at least
    /// <see cref="FrameTree.ZeroGravityThreshold"/>, there is walkable ground under it, and it does not move away from
    /// that ground. Then only the part of the desired velocity across the up direction counts. A character that does
    /// not stand, in the air, on steep ground or weightless, in any frame and any gravity, keeps its momentum, and
    /// gravity changes it. (User) CharacterMove and Jump do not move it, but physics keeps the desired velocity, which
    /// applies once the character stands. It loses its motion into each static or kinematic surface that it hits. Under
    /// gravity, its up turns against the gravity whenever it touches anything, so a character that lands on its side or
    /// its head turns upright, and a character in free fall that touches nothing does not turn.
    /// Impulses and forces change a character's velocity by their mass. KinematicTarget does nothing to a
    /// character.</item>
    /// </list>
    /// <para>
    /// Outputs: <see cref="PhysicsStepOutput.Bodies"/> holds every awake dynamic or kinematic body and every
    /// character, plus one last report for a body that fell asleep. Velocities are at the center of mass.
    /// <see cref="PhysicsStepOutput.Characters"/> holds every character once, after the hand-offs, with whether physics
    /// stood it in this step. A character that a hand-off moved has not stood in its new frame yet.
    /// <see cref="PhysicsStepOutput.HandOffs"/> holds each body that moved to another frame, with its pose there. For a
    /// character that is the rotation that it got in its new frame, which a turn to the new gravity can change.
    /// <see cref="PhysicsStepOutput.Frames"/> holds every physics-driven frame with a hull, relative to its parent
    /// after any hand-off in this step, with the velocity of the frame origin. Apply hand-offs before frame states.
    /// <see cref="PhysicsStepOutput.Contacts"/> holds each body pair that started touching, with A before B in
    /// entity order and the normal from A to B. Contacts with ghosts are not reported. A pair that separates reports again
    /// when it touches again. A pair that never separates reports once, also when a body falls asleep and wakes, when a
    /// hull becomes static and moves again, and when the system grows.
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
            bugReport = output.Errors;
            stepIndex++;
            stepDt = context.DeltaTime;
            stepTime = context.Time;
            stepEndTime = context.Time + context.DeltaTime;

            // The simulation skips ApplyChanges when a sync point has no changes, but it has still applied or refused
            // the hand-offs of the last step, and it can also have changed the tree on its own, for example to dock a ship.
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

    /// <inheritdoc/>
    /// <remarks>
    /// The first question about a shape builds its native Jolt shape, under the native gate, so it waits for a step that is
    /// running. The backend keeps the shape for the body that needs it, and keeps the answer, so a later question about the
    /// same shape description costs one dictionary lookup.
    /// </remarks>
    public string? ShapeProblem(ShapeDesc shape)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(shape);
        return shapeProblems.GetOrAdd(shape, BuildShape);
    }

    /// <summary>Builds the native shape of <paramref name="shape"/>, or returns why Jolt cannot build it.</summary>
    private string? BuildShape(ShapeDesc shape)
    {
        if (shape.Problem() is { } problem)
            return problem;
        lock (JoltRuntime.NativeGate)
        {
            try
            {
                shapes.Get(shape);
                return null;
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
        }
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

    /// <summary>The native character of <paramref name="entity"/>, or null when it has none. Tests check that the backend frees it.</summary>
    internal CharacterVirtual? CharacterOf(Entity entity) => Find(entity)?.Character;

    /// <summary>The physics system of <paramref name="frame"/>, or null when the frame has none. Tests check that the backend frees it.</summary>
    internal PhysicsSystem? SystemOf(FrameHandle frame) => ExistingWorld(frame)?.System;

    /// <summary>The most bodies that the system of <paramref name="frame"/> holds now, or 0 when the frame has no system.</summary>
    internal uint CapacityOf(FrameHandle frame) => ExistingWorld(frame)?.System.MaxBodies ?? 0;

    /// <summary>What Jolt holds for one body. Tests compare it before and after a system grows.</summary>
    internal readonly record struct BodyFacts(
        BodyID Id,
        ulong UserData,
        MotionType Motion,
        ObjectLayer Layer,
        nint Shape,
        MotionQuality Quality,
        Double3 Position,
        Quaternion Rotation,
        Vector3 LinearVelocity,
        Vector3 AngularVelocity,
        bool Active,
        float Friction,
        float Restitution,
        float InverseMass,
        Vector3 InverseInertia,
        Quaternion InertiaRotation,
        float LinearDamping,
        float AngularDamping);

    /// <summary>Every body in the system of <paramref name="frame"/>, in the order of its ID, as Jolt holds it.</summary>
    internal unsafe List<BodyFacts> BodiesIn(FrameHandle frame)
    {
        var facts = new List<BodyFacts>();
        if (ExistingWorld(frame) is not { } world)
            return facts;
        lock (JoltRuntime.NativeGate)
        {
            uint count = world.System.BodiesCount;
            var ids = new BodyID[count];
            fixed (BodyID* first = ids)
                world.System.GetBodies(first, count);
            BodyInterface bodies = world.Bodies;
            BodyLockInterface locks = world.System.BodyLockInterface;
            foreach (BodyID id in ids)
            {
                bodies.GetRPosition(id, out RVector3 position);
                bodies.GetLinearAndAngularVelocity(id, out Vector3 linear, out Vector3 angular);
                float inverseMass = 0, linearDamping = 0, angularDamping = 0;
                Vector3 inverseInertia = Vector3.Zero;
                Quaternion inertiaRotation = Quaternion.Identity;
                locks.LockRead(id, out BodyLockRead read);
                if (read.Succeeded && read.Body is { MotionProperties: { IsNotNull: true } motion })
                {
                    inverseMass = motion.InverseMassUnchecked;
                    inverseInertia = motion.InverseInertiaDiagonal;
                    inertiaRotation = motion.InertiaRotation;
                    linearDamping = motion.LinearDamping;
                    angularDamping = motion.AngularDamping;
                }
                locks.UnlockRead(read);
                facts.Add(new BodyFacts(id, bodies.GetUserData(id), bodies.GetMotionType(id), bodies.GetObjectLayer(id),
                    bodies.GetShape(id)?.Handle ?? 0, bodies.GetMotionQuality(id), position.ToDouble3(), bodies.GetRotation(id),
                    linear, angular, bodies.IsActive(id), bodies.GetFriction(id), bodies.GetRestitution(id), inverseMass,
                    inverseInertia, inertiaRotation, linearDamping, angularDamping));
            }
        }
        facts.Sort(static (a, b) => a.Id.Index.CompareTo(b.Id.Index));
        return facts;
    }

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

    /// <summary>The parent of a frame, including the hand-offs of the current step, which the frame tree does not show yet.</summary>
    private FrameHandle ParentOf(FrameHandle frame)
    {
        if (parentOverrides.TryGetValue(frame.Index, out var link) && link.Frame == frame)
            return link.Parent;
        return frames.TreeParent(frame);
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
        frames.GetAllInTree(frameScratch);
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
    /// Brings physics in line with the frame tree, which is the authority: drops the hand-offs of frames from the last
    /// step, removes the systems of frames that left the tree, gives every frame hull the motion that its frame's driver
    /// needs, puts every frame hull in the system of the parent that the tree names, and removes every ghost that the tree
    /// no longer allows. The sync point has applied or refused each hand-off before this runs, so a hull whose hand-off
    /// the tree did not take, or whose frame the simulation moved on its own, goes where the tree says, with the tree's
    /// pose and velocity. A hull whose frame got a driver keeps its body and takes the tree's pose and velocity too, also
    /// when the driver is the one that the frame had. A
    /// system that is full grows to take the hull. Afterwards <see cref="AssertInvariants"/> holds: every system belongs
    /// to a live frame, every record has the bodies that it must have, every hull is in the system of its frame's parent
    /// in the tree and has the motion of its frame's driver, and every ghost is in a frame next to its body's frame.
    /// </summary>
    private void ReconcileFrames()
    {
        // A hand-off that the tree took leaves its hull where the tree has the frame. One that the tree refused leaves
        // its hull out of place, and the tree's version does not show that, so the hulls need the check below. Every
        // override goes now, whatever happens below: the tree holds the sync point's decision, and an override that
        // outlived its step would show the next step a parent that the tree does not have.
        bool handedOffFrames = parentOverrides.Count > 0;
        parentOverrides.Clear();

        // Every structural change of the tree, removals included, and every driver change changes its version.
        // Stryker disable once Statement : the check only saves work; reconciling an unchanged tree changes nothing.
        if (frames.Version == reconciledFrameVersion && !handedOffFrames)
            return;
        // No version counts as reconciled until the reconcile ends. Anything below that throws then makes the next call
        // reconcile again, also after a step that handed a frame off, whose overrides are gone already.
        reconciledFrameVersion = -1;

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
            // After the sweep every owner's frame is alive, so this test is only a guard.
            if (!frames.IsAlive(owner.OwnedFrame))
                continue;
            // The hull's motion follows the frame's driver, and its system follows the frame's parent. A hull that moves
            // to another system has its new motion there from the start. Either way it starts from the tree.
            bool newDriver = FollowDriver(owner);
            FrameHandle parent = frames.TreeParent(owner.OwnedFrame);
            if (owner.Frame != parent)
                PlaceHullFromTree(owner, parent);
            else if (newDriver && owner.HasPrimary)
                ApplyHullMotion(owner);
        }

        // Ghosts follow the tree too. A frame that the game moved, or a hull that moved, can leave a ghost in a frame
        // that is no longer next to its body's frame, even inside the frame that the body owns. The ghost pass at the
        // end of the step adds the ghosts that the new tree wants.
        DropGhostsThatTheTreeForbids();
        reconciledFrameVersion = frames.Version;
        EnsureFollowsTree();
    }

    /// <summary>The postcondition of a reconcile. Debug builds only.</summary>
    [Conditional("TERTIUS_CONTRACTS")]
    private void EnsureFollowsTree() => AssertInvariants();

    /// <summary>
    /// Checks the invariants that hold right after every reconcile, and until the next step hands a frame off or the
    /// tree changes. Throws <see cref="ContractException"/> when one does not hold. Debug builds run it at the end of
    /// every reconcile that does work, and tests call it.
    /// <list type="bullet">
    /// <item>No hand-off override remains, so the backend reads every frame's parent from the tree.</item>
    /// <item>Every system belongs to a live frame.</item>
    /// <item>Every record that must have a body has one. A plain body or a character is a member of the system of its
    /// frame. The owner of a frame has its interior in the frame's own system, and it is in the list of frame
    /// owners.</item>
    /// <item>Every frame owner's frame is alive. The hull of a frame with a parent sits in the system of that parent,
    /// and the owner of a root frame has no hull.</item>
    /// <item>Every frame owner follows its frame's driver: the record has the frame's driver version and driver, and the
    /// motion that <see cref="HullMotion"/> gives for it, and Jolt moves the hull with that motion.</item>
    /// <item>Every ghost sits in the system of a frame that the tree puts next to its body's frame (see
    /// <see cref="GhostAllowed"/>), so never in the frame that the body owns or inside it.</item>
    /// </list>
    /// The record and ghost checks visit every entity slot, so the whole check is O(entity slots + systems).
    /// </summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(parentOverrides.Count == 0);
        foreach (FrameWorld world in worldList)
            Contract.Invariant(frames.IsAlive(world.Frame) && ExistingWorld(world.Frame) == world);
        int hulls = 0;
        foreach (BodyRecord? record in records)
        {
            if (record is null)
                continue;
            Contract.Invariant(Find(record.Entity) == record);
            if (record.IsHull)
            {
                hulls++;
                Contract.Invariant(record.InteriorBody.IsValid && record.InteriorWorld == ExistingWorld(record.OwnedFrame));
            }
            else
            {
                Contract.Invariant(record.HasPrimary && record.World == ExistingWorld(record.Frame));
            }
            if (record.World is { } home)
            {
                Contract.Invariant(record.Character is not null || record.Body.IsValid);
                Contract.Invariant(home.Members[record.MemberIndex] == record);
            }
            if (record.Ghosts is not { } ghosts)
                continue;
            foreach (GhostBody ghost in ghosts)
                Contract.Invariant(GhostAllowed(record, ghost.Frame) && ghost.World == ExistingWorld(ghost.Frame));
        }
        Contract.Invariant(hulls == frameOwners.Count);
        foreach (BodyRecord owner in frameOwners)
        {
            Contract.Invariant(frames.IsAlive(owner.OwnedFrame));
            FrameHandle parent = frames.TreeParent(owner.OwnedFrame);
            if (parent.IsNull)
                Contract.Invariant(!owner.HasPrimary);
            else
                Contract.Invariant(owner.Frame == parent && owner.HasPrimary && owner.World == ExistingWorld(parent));
            Contract.Invariant(owner.DriverVersion == frames.DriverVersion(owner.OwnedFrame) && owner.Driver == frames.Driver(owner.OwnedFrame));
            Contract.Invariant(owner.Motion == HullMotion(owner.Driver));
            if (owner.HasPrimary)
                Contract.Invariant(owner.World!.Bodies.GetMotionType(owner.Body) == ToMotionType(owner.Motion));
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
        world = new FrameWorld(frame, options);
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
                PlaceHullFromTree(member, frames.TreeParent(member.OwnedFrame));
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
    /// tree velocities. A Spin frame turns at a steady rate in place, so that extrapolation is its exact motion.
    /// </summary>
    private FrameMotion PredictMotion(FrameHandle frame, double time)
    {
        DPose pose = frames.LocalPose(frame);
        MotionDriver driver = frames.Driver(frame);
        if (driver == MotionDriver.Static)
            return new FrameMotion(pose, Double3.Zero, Double3.Zero);

        if (driver == MotionDriver.Kepler)
        {
            // The same G·M as the simulation uses: the point gravity of the parent, which also pulls the bodies in the parent.
            KeplerOrbit orbit = frames.Orbit(frame);
            double parentMu = frames.KeplerMu(frame);
            if (OrbitMatchesTree(orbit, parentMu, pose))
            {
                var (position, velocity) = orbit.StateAt(time, parentMu);
                return new FrameMotion(new DPose(position, orbit.RotationAt(time, parentMu)), velocity, orbit.AngularVelocityAt(time, parentMu));
            }
        }

        double ahead = time - stepTime;
        Double3 linear = frames.LinearVelocity(frame);
        Double3 angular = frames.AngularVelocity(frame);
        var extrapolated = new DPose(pose.Position + linear * ahead, Rotations.Integrate(pose.Rotation, angular, ahead));
        return new FrameMotion(extrapolated, linear, angular);
    }

    private bool OrbitMatchesTree(in KeplerOrbit orbit, double parentMu, in DPose treePose)
    {
        var (position, _) = orbit.StateAt(stepTime, parentMu);
        double tolerance = 1e-3 + 1e-9 * treePose.Position.Length;
        if ((position - treePose.Position).Length > tolerance)
            return false;
        DQuaternion rotation = orbit.RotationAt(stepTime, parentMu);
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
