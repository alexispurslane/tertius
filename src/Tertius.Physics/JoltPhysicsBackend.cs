using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;

namespace Tertius.Physics;

/// <summary>
/// The physics backend on Jolt in double precision. Each reference frame that contains bodies gets its own Jolt
/// physics system, created when the first body arrives. All systems share one job system and step one after
/// another; each update spreads over the job threads. Jolt gravity is zero: before each step, every awake dynamic
/// body gets the force m · g, where g is the linear gravity: the frame's own field minus the frame's proper acceleration.
/// The force leaves the body's sleep timer alone, so a body at rest falls asleep. The sleep threshold of each system scales
/// with the weakest effective gravity on its awake bodies, the linear gravity plus the centrifugal and the Euler term of a
/// turning frame, so a body that a small share of a weak gravity moves stays awake.
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

    // The step record at the end of the step of each frame without a dynamic hull, by frame index, valid for one step (see
    // StepRecordOf).
    private MotionSample[] predicted = new MotionSample[16];
    // Steps count from 1, so a fresh entry of 0 never counts as cached. Only Step uses the cache.
    private long[] predictedStep = new long[16];

    private FrameTree frames = null!;
    // The intents that the current step applies, by entity slot (see PhysicsStepContext.Intents). Valid during a step only.
    private ReadOnlyMemory<PhysicsIntent> stepIntents;
    private long stepIndex;
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
    /// <see cref="BodyLayer.FrameHull"/> layer, and its interior at rest in its own frame's system, static while the hull is
    /// static and kinematic while it can move (see <see cref="InteriorMotion"/>). The
    /// frame must exist in <paramref name="frames"/> before its owner's body is created; the hull starts at the
    /// frame's pose and velocity in the tree, with the motion of its description, which is the motion that the hull has in
    /// the next step (see <see cref="BodyCreate"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Physics follows the frame tree. A hand-off of a frame is only a proposal, which the sync point applies or refuses
    /// before this call, so the frame's hull goes where the tree has the frame now. A frame in FramesDestroyed is already
    /// gone from <paramref name="frames"/>: <see cref="FrameTree.Destroy"/> moved its child frames to its parent, with
    /// poses and velocities converted. The hulls of those child frames move with them and take pose and velocity from
    /// the tree. Any other body still in the destroyed frame is removed with a warning, and so is the hull of an owner
    /// that outlives its frame. A frame in FramesDestroyed that the tree still has keeps its system, with a warning,
    /// until it leaves the tree. A refused hand-off, a frame that leaves the tree without a FramesDestroyed change, and a
    /// frame that the simulation reparents on its own are handled here or at the start of the next step, whichever comes
    /// first. A hull has the motion of its description at spawn. A change of motion that its owner wrote in a tick is in the
    /// intents of the next step, and the hull takes it at the start of that step; any step takes the motion that the hull
    /// has in it, from the change in the step's intents or else from the stored motion of the last finished step (see
    /// <see cref="FollowHullMotions"/> and <see cref="FrameTree.HullMotionInStep"/>). The simulation never gives a hull
    /// a motion that it cannot take. Such a motion comes only from a bug: it breaks a contract in a Debug build, and a
    /// Release build keeps the hull's motion and reports the bug (see <see cref="ReportBug"/>).
    /// </para>
    /// <para>
    /// After the moves and the reconcile, the backend drops each ghost whose place the numeric update of the sync point
    /// withdrew: a home that the update no longer names to keep a ghost of an anchor (see
    /// <see cref="DropWithdrawnAnchorGhosts"/>). The World hands physics the last batch of every numeric update, also an
    /// empty one, so this happens at the sync point of the update, also with no other change.
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
            ApplyShifts(changes.FramesShifted);
            // Moves come before the reconcile: the tree already has each moved hull's frame under its new home, and the
            // move keeps the hull's state, which a reconcile would take from the tree.
            ApplyMoves(changes.BodiesMoved);
            ReconcileFrames();
            DropWithdrawnAnchorGhosts();
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
    /// says, which drops the hand-offs of the last step that the simulation refused and follows changes to the tree that no
    /// change batch announced, then gives each hull the motion that it has in this step (see
    /// <see cref="FollowHullMotions"/>), then gives every character the form that the gravity at it asks for, applies
    /// intents, which aim each kinematic body at its end pose or full sample and steer each dynamic body to its steer target,
    /// applies gravity, moves the characters in the walking form, updates every system, puts each aimed kinematic body
    /// exactly on its aim, then reports contacts, hands bodies between frames, updates ghosts and writes body, character and
    /// frame states to <paramref name="output"/>.
    /// </summary>
    /// <remarks>
    /// <para>Intents, in the entity's frame axes. The intent of a frame's owner acts on the frame's hull.</para>
    /// <list type="bullet">
    /// <item>Teleport sets the pose to <see cref="PhysicsIntent.TeleportPose"/> and zeroes both velocities.
    /// SetVelocity, SetAngularVelocity and the motion intent apply after it, so an end pose or a steer target in the same
    /// intent moves the body on from the teleport pose.</item>
    /// <item>A velocity that SetVelocity, SetAngularVelocity, a teleport or a jump gives, like a velocity that a spawn
    /// gives, is an instant velocity: from the start of the step the body moves as it says, with Jolt's acceleration
    /// (ARCHITECTURE.md, "Instant velocities and chords").</item>
    /// <item>An end pose or a full sample in <see cref="PhysicsIntent.Move"/> moves a kinematic body to its pose over one
    /// step, puts it exactly there, and stops it there unless the next step gives a new motion intent. A plain kinematic
    /// body reports the step record of that motion intent, and a hand-off carries the record's instant velocity. A steer
    /// target gives a dynamic body the velocities that would carry it there. Only a bug gives a body a case that its motion
    /// refuses, which the step ignores and reports.</item>
    /// <item>Force, Torque, Impulse and AngularImpulse act on dynamic bodies, for this step only.</item>
    /// <item>SetCollision gives every body of the entity new collision groups and a new mask, before anything else moves,
    /// and they stay. It works for every entity with a body, a static body and the owner of a root frame too. A frame's
    /// hull ignores them (see <see cref="Layers"/>).</item>
    /// <item>A character keeps the desired velocity of its last CharacterMove until the next one. Only a character that
    /// physics stands moves by it and can jump. Physics stands a character when the effective gravity is at least
    /// <see cref="FrameTree.ZeroGravityThreshold"/>, there is walkable ground under it, and it does not move away from
    /// that ground. Then only the part of the desired velocity across the up direction counts. A character that does
    /// not stand, in the air, on steep ground or weightless, in any frame and any gravity, keeps its momentum, and
    /// gravity changes it. (User) CharacterMove and Jump do not move it, but physics keeps the desired velocity, which
    /// applies once the character stands. Impulses and forces change a character's velocity by their mass.
    /// A character refuses every motion intent.</item>
    /// <item>A character has two forms (see <see cref="NextCharacterMotion"/>). Below the threshold it is weightless, and
    /// physics makes it a rigid body with its rotation locked, the rigid form, whose contacts Jolt solves as it solves
    /// those of any rigid body: it pushes a dynamic body as hard as its momentum says, takes the reaction, and slides on a
    /// surface with friction. It keeps its up and its rotation. From twice the threshold on, or from the threshold on
    /// while it touches something, it is a Jolt character again, the walking form. A walking character that does not
    /// stand loses its motion into each static or kinematic surface that it hits. Under gravity, the walking form's up
    /// turns against the semantic gravity: at once when the character touches anything, so a character that lands on its
    /// side or its head turns upright, and at most at <see cref="JoltBackendOptions.CharacterAirUpTurnRate"/> when it
    /// touches nothing. Below the zero-gravity threshold it keeps its last up. A switch of form keeps the character's
    /// pose, velocity, up and contacts.</item>
    /// </list>
    /// <para>
    /// Outputs: <see cref="PhysicsStepOutput.Bodies"/> holds every awake dynamic or kinematic body and every
    /// character in either form, also one in the rigid form that sleeps, plus one last report for a body that fell asleep.
    /// Velocities are at the center of mass, and each is Jolt's: the chord of the last step, its displacement divided by
    /// the step, which is the velocity half a step earlier. Both forms of a character store that chord.
    /// <see cref="PhysicsStepOutput.Characters"/> holds every character once, in either form, after the hand-offs, with
    /// whether physics stood it in this step. A character that a hand-off moved has not stood in its new frame yet, and a
    /// character in the rigid form never stands.
    /// <see cref="PhysicsStepOutput.HandOffs"/> holds each body that moved to another frame, with its pose there. For a
    /// character that is the rotation that it got in its new frame, which a turn to the new gravity can change.
    /// <see cref="PhysicsStepOutput.Frames"/> holds the step record of each frame whose hull moved in the step, relative to
    /// its parent after any hand-off in this step, with the velocity of the frame origin. Apply hand-offs before the step records.
    /// <see cref="PhysicsStepOutput.Contacts"/> holds each pair of entities whose contact started, with A before B in
    /// entity order and the normal from A to B. A contact through a ghost is a contact of the two entities, between the
    /// body of one entity and the ghost of another entity's body, and between the ghosts of two entities, which Jolt makes
    /// only when one of them is a sensor. A body pair starts at a real
    /// touch: the first step in which its bodies are at most <see cref="FrameWorld.TouchTolerance"/> apart or approach
    /// fast enough to close their gap, and not when Jolt finds them within its speculative contact distance. The contact of
    /// two entities starts when the first system starts a body pair of theirs, and the system of the frame with the lowest
    /// index gives the point and the normal when several start one in the same step. A contact that separates reports
    /// again when it touches again. A contact that never separates reports once, also when a body falls asleep and
    /// wakes, when a hull becomes static and moves again, when the system grows, and while its bodies touch in several
    /// systems through ghosts.
    /// <see cref="PhysicsStepOutput.ContactsEnded"/> holds each contact that started and then ended, once, in entity
    /// order, when the last system drops it. A system drops a body pair when Jolt no longer finds it in a step that
    /// simulates one of its bodies (for a character, in either form, its own contact list no longer has the other
    /// body), or when one of
    /// its bodies was destroyed or left the system, a ghost too. A body pair that leaves goes on in every system that
    /// holds a body of each entity that can touch the other one there, after the hand-offs and the ghosts of the step. So
    /// a contact goes on when two touching bodies move to another system together, by a hand-off or a move between
    /// numeric frames, and when one of them leaves while the other touches its ghost. A contact that no system takes over
    /// after a hand-off of one of its bodies, while a body of it or a ghost of one is in a numeric frame, waits one more
    /// step, for the numeric update of the sync point to link the bodies, or to name the home of their group for the ghost
    /// of the anchor of a following frame that lives apart from the group (see <see cref="ResolveLooseContacts"/>).
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
            stepIntents = context.Intents;

            // The World hands physics a batch at every sync point, which reconciles, but the game can change the tree
            // between two ticks with no batch, for example to dock a ship, and a batch that failed reconciled nothing.
            ReconcileFrames();
            RefreshFrameStructure();
            // Before anything reads a record's motion: a hull that becomes dynamic joins the spawned rigid bodies, whose
            // velocities the next pass converts into chords.
            FollowHullMotions(context.Intents.Span);
            // Before the intents and the gravity, which act on the form that a character has for the whole step, and before
            // the spawn velocities, which a spawned character that turns rigid needs as chords too.
            SwitchCharacterForms();
            // Before the hull velocities, so that a new hull's accelerometer starts from its chord.
            ConvertSpawnVelocities();
            // Before the intents, so that it never moves a static hull back from a teleport, which the tree learns only at
            // the next sync point.
            FollowStaticHulls();
            CaptureHullVelocities();
            ApplyIntents(context);
            CaptureHullVelocitiesAfterIntents();
            ApplyGravity();
            UpdateCharacters();
            UpdateSystems();
            // Before anything reads the end of the step: the proper accelerations, the contacts, the hand-offs and the ghosts.
            SnapKinematicBodies();
            ComputeProperAccelerations();
            CollectContacts(output);
            HandOffBodies(output);
            // After the ghosts, so that the new ghost of a body that left its system can carry the body's contacts.
            UpdateGhosts();
            WriteEndedContacts(output);
            WriteBodies(output);
            WriteFrames(output);
        }
    }

    /// <summary>
    /// The <see cref="ShapeDesc.BoundingRadius"/> of a shape, which the backend keeps for each shape description, because it
    /// costs a pass over the shape.
    /// </summary>
    private float BoundingRadius(ShapeDesc shape) => radii.GetOrAdd(shape, static s => s.BoundingRadius());

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
            queryBodyFilter?.Dispose();
            queryShapes.Dispose();
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

    /// <summary>
    /// True when <paramref name="entity"/> is a character in its rigid form: its body is a dynamic Jolt body, not a Jolt
    /// character. Tests check which form a character has.
    /// </summary>
    internal bool HasRigidForm(Entity entity) => Find(entity) is { Desc.Motion: BodyMotion.Character, Character: null, HasPrimary: true };

    /// <summary>The physics system of <paramref name="frame"/>, or null when the frame has none. Tests check that the backend frees it.</summary>
    internal PhysicsSystem? SystemOf(FrameHandle frame) => ExistingWorld(frame)?.System;

    /// <summary>The most bodies that the system of <paramref name="frame"/> holds now, or 0 when the frame has no system.</summary>
    internal uint CapacityOf(FrameHandle frame) => ExistingWorld(frame)?.System.MaxBodies ?? 0;

    /// <summary>
    /// The number of body pairs in the system of <paramref name="frame"/> that Jolt found within its speculative contact
    /// distance and that wait for their first real touch, or 0 when the frame has no system.
    /// </summary>
    internal int PendingPairCount(FrameHandle frame) => ExistingWorld(frame)?.PendingPairCount ?? 0;

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
        float AngularDamping,
        bool EnhancedInternalEdgeRemoval);

    /// <summary>
    /// The velocity shadow of the body of <paramref name="entity"/> after the last step (see <see cref="BodyRecord.Shadow"/>),
    /// or none for an entity without a body.
    /// </summary>
    internal VelocityShadow ShadowOf(Entity entity)
    {
        lock (JoltRuntime.NativeGate)
            return Find(entity)?.Shadow ?? default;
    }

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
                bool edgeRemoval = false;
                locks.LockRead(id, out BodyLockRead read);
                if (read.Succeeded && read.Body is { } body)
                {
                    edgeRemoval = body.EnhancedInternalEdgeRemoval;
                    if (body.MotionProperties is { IsNotNull: true } motion)
                    {
                        inverseMass = motion.InverseMassUnchecked;
                        inverseInertia = motion.InverseInertiaDiagonal;
                        inertiaRotation = motion.InertiaRotation;
                        linearDamping = motion.LinearDamping;
                        angularDamping = motion.AngularDamping;
                    }
                }
                locks.UnlockRead(read);
                facts.Add(new BodyFacts(id, bodies.GetUserData(id), bodies.GetMotionType(id), bodies.GetObjectLayer(id),
                    bodies.GetShape(id)?.Handle ?? 0, bodies.GetMotionQuality(id), position.ToDouble3(), bodies.GetRotation(id),
                    linear, angular, bodies.IsActive(id), bodies.GetFriction(id), bodies.GetRestitution(id), inverseMass,
                    inverseInertia, inertiaRotation, linearDamping, angularDamping, edgeRemoval));
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
    /// step, removes the systems of frames that left the tree, puts every frame hull in the system of the parent that the
    /// tree names, and removes every ghost that the tree no longer allows. The sync point has applied or refused each
    /// hand-off before this runs, so a hull whose hand-off the tree did not take, or whose frame the simulation moved on its
    /// own, goes where the tree says, with the tree's pose and velocity and its own motion. A system that is full grows to
    /// take the hull. A change of a hull's motion is no change of the tree's structure, so it is not handled here (see
    /// <see cref="FollowHullMotions"/>). Afterwards <see cref="AssertInvariants"/> holds: every system belongs to a live
    /// frame, every record has the bodies that it must have, every hull is in the system of its frame's parent in the tree,
    /// and every ghost is in a frame next to its body's frame.
    /// </summary>
    private void ReconcileFrames()
    {
        // A hand-off that the tree took leaves its hull where the tree has the frame. One that the tree refused leaves
        // its hull out of place, and the tree's version does not show that, so the hulls need the check below. Every
        // override goes now, whatever happens below: the tree holds the sync point's decision, and an override that
        // outlived its step would show the next step a parent that the tree does not have.
        bool handedOffFrames = parentOverrides.Count > 0;
        parentOverrides.Clear();

        // Every structural change of the tree, removals included, changes its version.
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
            // The hull's system follows the frame's parent. A hull that moves to another system keeps its motion and
            // starts from the tree's pose and velocity.
            FrameHandle parent = frames.TreeParent(owner.OwnedFrame);
            if (owner.Frame != parent)
                PlaceHullFromTree(owner, parent);
        }

        // Ghosts follow the tree too. A frame that the game moved, or a hull that the tree moved, can leave a ghost in a
        // frame that is no longer next to its body's frame, even inside the frame that the body owns. The ghost pass at
        // the end of the step adds the ghosts that the new tree wants. A hull that a numeric update moved took the ghosts
        // of the bodies that stick out of its frame along already (see ApplyMoves).
        DropGhostsThatTheTreeForbids();
        reconciledFrameVersion = frames.Version;
        EnsureFollowsTree();
    }

    /// <summary>The postcondition of a reconcile. Debug builds only.</summary>
    [Conditional("TERTIUS_CONTRACTS")]
    private void EnsureFollowsTree() => AssertInvariants();

    /// <summary>
    /// Checks the invariants that hold right after every reconcile, and until the next step hands a frame off or the
    /// tree changes. A numeric update changes where the tree allows a ghost without changing the tree's version, and the
    /// batch of its sync point drops each ghost whose place it withdrew (see <see cref="DropWithdrawnAnchorGhosts"/>), so
    /// the invariants hold after it too. Throws <see cref="ContractException"/> when one does not hold. Debug builds run it at
    /// the end of every reconcile that does work, and tests call it.
    /// <list type="bullet">
    /// <item>No hand-off override remains, so the backend reads every frame's parent from the tree.</item>
    /// <item>Every system belongs to a live frame.</item>
    /// <item>Every record that must have a body has one. A plain body or a character is a member of the system of its
    /// frame. The owner of a frame has its interior in the frame's own system, and it is in the list of frame
    /// owners.</item>
    /// <item>A character has one of its two forms (see <see cref="NextCharacterMotion"/>): the walking form, a Jolt
    /// character, or the rigid form, a dynamic Jolt body that cannot turn and does not stand. No other record has a Jolt
    /// character. Each system counts its members that are characters, and those in the walking form.</item>
    /// <item>Every frame owner's frame is alive. The hull of a frame with a parent sits in the system of that parent,
    /// and the owner of a root frame has no hull.</item>
    /// <item>Jolt moves every hull with the motion of its record. The record takes the motion of each step at the start of
    /// that step (see <see cref="FollowHullMotions"/>). A record that the simulation creates between two steps, at a spawn
    /// or a rehome, starts with the motion of the next step, which it runs first. Every interior has the Jolt motion that
    /// its record's motion gives it, static while the hull is static and kinematic otherwise (see
    /// <see cref="InteriorMotion"/>).</item>
    /// <item>Every ghost sits in the system of a frame that the tree puts next to its body's frame among the semantic
    /// frames, or in the home of a group that keeps a ghost of an anchor, the body's own or the hull of its home's semantic
    /// frame (see <see cref="GhostAllowed"/>), so never in the frame that the body owns or inside it.</item>
    /// <item>The ghosts of each system (see <see cref="FrameWorld.AddGhost"/>) are the ghosts that the records keep in
    /// it, no more and no fewer, and each has the contact motion of its record (see <see cref="GhostContactMotion"/> and
    /// <see cref="FrameWorld.GhostContactMotion"/>). Every ghost of a body that can move is kinematic. A ghost of a static
    /// body is static while it holds still (see <see cref="GhostMotion"/>), and Jolt can move it as a kinematic body, with
    /// a finite mass (see <see cref="FrameWorld.CanMove"/>), so the ghost pass and the shifts can turn it kinematic when
    /// it starts to move.</item>
    /// <item>Every Jolt body of a record has the layer that the record's collision groups and mask give it: the primary
    /// body or the character's inner body, the interior and every ghost (see <see cref="Layers"/>). Each ghost has the
    /// layer of its role (see <see cref="GhostBody.Role"/>).</item>
    /// <item>The contact ledger counts, for each pair of entities, the body pairs that hold their contact as started in
    /// every system, and its loose and waiting contacts (see <see cref="AssertContactInvariants"/>). This one holds at
    /// every sync point.</item>
    /// </list>
    /// The record and ghost checks visit every entity slot, so the whole check is O(entity slots + systems + started pairs).
    /// </summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(parentOverrides.Count == 0);
        foreach (FrameWorld world in worldList)
        {
            Contract.Invariant(frames.IsAlive(world.Frame) && ExistingWorld(world.Frame) == world);
            AssertCharacterCounts(world);
        }
        int hulls = 0;
        int recordGhosts = 0;
        foreach (BodyRecord? record in records)
        {
            if (record is null)
                continue;
            Contract.Invariant(Find(record.Entity) == record);
            AssertFormInvariants(record);
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
                BodyID primary = record.Character?.InnerBodyID ?? record.Body;
                Contract.Invariant(home.Bodies.GetObjectLayer(primary) == PrimaryLayer(record));
            }
            if (record.InteriorWorld is { } interior)
            {
                Contract.Invariant(interior.Bodies.GetObjectLayer(record.InteriorBody) == InteriorLayer(record));
                Contract.Invariant(interior.Bodies.GetMotionType(record.InteriorBody) == InteriorMotion(record));
            }
            if (record.Ghosts is not { } ghosts)
                continue;
            foreach (GhostBody ghost in ghosts)
            {
                Contract.Invariant(GhostAllowed(record, ghost.Frame) && ghost.World == ExistingWorld(ghost.Frame));
                Contract.Invariant(ghost.World.HasGhost(ghost.Body));
                Contract.Invariant(ghost.World.Bodies.GetObjectLayer(ghost.Body) == GhostLayer(record, ghost.Role));
                Contract.Invariant(ghost.World.GhostContactMotion(ghost.Body) == GhostContactMotion(record));
                Contract.Invariant(record.IsMovable
                    ? ghost.World.Bodies.GetMotionType(ghost.Body) == MotionType.Kinematic
                    : ghost.World.CanMove(ghost.Body));
            }
            recordGhosts += ghosts.Count;
        }
        Contract.Invariant(hulls == frameOwners.Count);
        AssertContactInvariants();
        // Every ghost of a record is in its system's ghosts, and each is a body of its own, so the systems hold as many
        // ghosts as the records only when they hold no other, and so no contact motion of another body.
        int systemGhosts = 0;
        foreach (FrameWorld world in worldList)
            systemGhosts += world.GhostCount;
        Contract.Invariant(systemGhosts == recordGhosts);
        foreach (BodyRecord owner in frameOwners)
        {
            Contract.Invariant(frames.IsAlive(owner.OwnedFrame));
            FrameHandle parent = frames.TreeParent(owner.OwnedFrame);
            if (parent.IsNull)
                Contract.Invariant(!owner.HasPrimary);
            else
                Contract.Invariant(owner.Frame == parent && owner.HasPrimary && owner.World == ExistingWorld(parent));
            // A hull has the Jolt motion type of its record's motion. The record takes the motion of each step at the start of
            // that step (see FollowHullMotions).
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
        world = new FrameWorld(frame, options, ReportBug);
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

        // Ghosts that other records keep in this system go with it, and so do the system's own ghosts. Each one's contacts
        // loosen first, as when the ghost pass removes a ghost: a contact of two ghosts here has no member whose removal
        // below loosens it. The carry of the end of the next step keeps each one that another system holds then (see
        // ResolveLooseContacts).
        foreach (BodyRecord? record in records)
        {
            if (record?.Ghosts is not { } ghosts)
                continue;
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                if (ghosts[i].World != world)
                    continue;
                LoosenContacts(world, ghosts[i].Body);
                ghosts.RemoveAt(i);
            }
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
        queryBodyFilter?.ForgetBodies();
    }

    /// <summary>
    /// The step record of <paramref name="frame"/> at the end of the current step, relative to its tree parent now, after any
    /// hand-off of the step (see "The step record of a step in flight" in docs/plans/rails-components.md). Physics predicts
    /// every frame from it: ghosts take its chord (see <see cref="MotionOf"/>), hand-offs its instant velocity (see
    /// <see cref="InstantMotionOf"/>). The owner's kind decides it:
    /// <list type="bullet">
    /// <item>A dynamic hull: Jolt's state after the update (see <see cref="TryHullRecord"/>). Its instant velocity is relative
    /// to the instant velocity of a numeric home, as every velocity that a hand-off carries. The record that physics
    /// releases for the tree differs there (see <see cref="WriteFrames"/>).</item>
    /// <item>A kinematic hull, or no body, whose owner has an end pose or a full sample: the record of that motion intent, in
    /// the tree parent, from the frame's tree pose where the step starts, as the sync point derives it for a frame without a
    /// hull (see <see cref="FrameTree.StepRecordInStep"/>), so that the two agree to the last bit. A hull at the origin of its
    /// following frame rests there, with the record's rotation and no velocity (see <see cref="KinematicTarget"/>): the following
    /// frame's own chord and instant velocity are the step plan's (see <see cref="FrameTree.HomeInstantVelocityOverChord"/>).
    /// The record that physics releases for the tree differs there (see <see cref="WriteFrames"/>).</item>
    /// <item>A static hull, or a kinematic hull without a motion intent, that teleported in the step: the teleport's pose in
    /// the hull's home, the tree parent, with no velocity. The record that physics releases for the tree differs in a numeric
    /// home (see <see cref="WriteFrames"/>).</item>
    /// <item>Anything else: the tree pose, with no velocity. The frame holds still.</item>
    /// </list>
    /// The records of the frames without a dynamic hull do not change during the step, so each is made once a step. Call it
    /// only after <see cref="ComputeProperAccelerations"/>.
    /// </summary>
    private MotionSample StepRecordOf(FrameHandle frame)
    {
        if (TryHullRecord(frame, out MotionSample record))
            return record;
        int index = frame.Index;
        if (index >= predicted.Length)
        {
            int size = Math.Max(index + 1, predicted.Length * 2);
            Array.Resize(ref predicted, size);
            Array.Resize(ref predictedStep, size);
        }
        if (predictedStep[index] == stepIndex)
            return predicted[index];
        record = PredictRecord(frame);
        predicted[index] = record;
        predictedStep[index] = stepIndex;
        return record;
    }

    /// <summary>The step record of a frame without a dynamic hull at the end of the current step (see <see cref="StepRecordOf"/>).</summary>
    private MotionSample PredictRecord(FrameHandle frame)
    {
        BodyRecord? hull = OwnerRecord(frame);
        if ((hull is null || hull.Motion == BodyMotion.Kinematic) && frames.StepRecordInStep(frame, stepIntents.Span, stepDt, out MotionSample move))
        {
            if (hull is { HasPrimary: true } && frames.InFollowingFrame(frame))
            {
                DPose target = KinematicTarget(hull, move.Pose.Position, move.Pose.Rotation);
                return move with
                {
                    Pose = new DPose(target.Position, move.Pose.Rotation),
                    ChordVelocity = Double3.Zero,
                    Velocity = Double3.Zero,
                    Acceleration = Double3.Zero,
                };
            }
            // A frame with a kinematic hull or without a body hangs in its semantic parent, which its motion intent is
            // relative to, unless it hangs at the origin of its following frame (see FrameTree). The assertion takes no message
            // of its own, which a Debug build would build in every step.
            Contract.Assert(frames.TreeParent(frame) == frames.Parent(frame));
            return move;
        }
        // A static hull, or a kinematic hull without a motion intent, that teleported in the step stands at the teleport's
        // pose in its home, the frame's tree parent, at rest.
        if (hull is { HasPrimary: true } && hull.TeleportStep == stepIndex)
            return new MotionSample(hull.TeleportPose, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero);
        return new MotionSample(frames.TreeLocalPose(frame), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero);
    }

    /// <summary>
    /// The motion of <paramref name="frame"/> relative to its current parent at the end of the step, with the chord of its
    /// step record as its velocity, which a ghost follows, and the record's acceleration and angular acceleration (see
    /// <see cref="StepRecordOf"/>). Call it only during a step.
    /// </summary>
    private FrameMotion MotionOf(FrameHandle frame)
    {
        MotionSample record = StepRecordOf(frame);
        return new FrameMotion(record.Pose, record.ChordVelocity, record.AngularVelocity, record.Acceleration, record.AngularAcceleration);
    }

    /// <summary>
    /// The step record of the frame of a dynamic hull at the end of the step, from Jolt's state after the update, relative
    /// to the hull's system: the hull's pose; the velocity of the frame's origin, the chord of the step, as Jolt keeps it;
    /// the instant velocity at the end of the step, half a step of the hull's acceleration further on, and in a turning home
    /// the exact inverse of the chord rule for the turn's part of it (see <see cref="InstantVelocity"/>), relative to the
    /// instant velocity of the home that every hand-off adds (see <see cref="HomeSteppedVelocityOverPlan"/>); that acceleration in
    /// the update, the change of Jolt's velocity without its float noise and without what the intents set before the
    /// update (see <see cref="ComputeProperAccelerations"/>); and Jolt's angular velocity and the change of it in the
    /// update. Physics releases the proper acceleration that it measured beside the record (see <see cref="WriteFrames"/>).
    /// A hull that handed off in the step is in its new home: Jolt's velocity is the chord of its first step there, so its
    /// acceleration is the one that the hand-off made that chord with, in the new home's axes (see
    /// <see cref="HandedOffVelocities"/>), the instant velocity is the one that the hand-off carried, and the angular
    /// acceleration is the one of the update, converted into the new home by the full rule (see
    /// <see cref="FrameMotion.AngularAccelerationToParent"/>). False for any other frame.
    /// </summary>
    private bool TryHullRecord(FrameHandle frame, out MotionSample record)
    {
        if (OwnerRecord(frame) is { Motion: BodyMotion.Dynamic, HasPrimary: true, Character: null } hull)
        {
            BodySnapshot s = ReadPrimary(hull);
            // Jolt's velocity is at the center of mass. The frame origin is the body origin.
            Double3 chord = (Double3)s.Linear + Double3.Cross(s.Angular, s.Pose.Position - s.CenterOfMass);
            Double3 acceleration = hull.StepAcceleration;
            // An awake hull took its home's linear gravity, so its chord plus half a step of its acceleration is relative
            // to the home's instant velocity in Jolt's stepping, and the record is relative to the one of every hand-off (see
            // HomeSteppedVelocityOverPlan). A hull that handed off in the step is awake in its new home.
            double half = 0.5 * stepDt;
            Double3 velocity = chord + acceleration * half;
            if (hull.World!.Bodies.IsActive(hull.Body))
            {
                velocity += HomeSteppedVelocityOverPlan(hull.Frame);
                // In a turning home the chord of an awake hull that stayed there spans the home's turn of the step, so the
                // exact inverse of the chord rule gives the instant velocity of its origin, as for every body (see
                // InstantVelocity), in place of half a step of the rotation terms that the update gave it. The acceleration
                // of a hand-off holds that turn's part already (see HandedOffVelocities).
                if (hull.HandOffStep != stepIndex && StepEnd(hull.Frame) is { } moment)
                    velocity += moment.InstantAtEndChange(s.Pose.Position, chord, stepDt) - (Double3)hull.FrameAcceleration.Turning * half;
            }
            record = new MotionSample(s.Pose, chord, velocity, acceleration, s.Angular, hull.AngularAcceleration);
            return true;
        }
        record = default;
        return false;
    }
}
