using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// Drives <see cref="JoltPhysicsBackend"/> the way the sync point does, without the simulation: it applies body
/// states, hand-offs and frame states from the physics output, advances Kepler frames, and feeds intents. Like the sync
/// point in a Debug build, it refuses output that only a bug in the backend produces: it runs the same
/// <see cref="PhysicsOutputCheck"/>, and every broken output throws out of <see cref="Step"/>, so a test whose backend
/// has such a bug fails loudly. A test that wants a refused hand-off on purpose breaks it in <see cref="BeforeApply"/>
/// and names it in <see cref="RefusesHandOff"/>.
/// </summary>
internal sealed class PhysicsHarness : IDisposable
{
    public const float Dt = 1f / 60f;

    private uint nextEntity = 1;
    private PhysicsIntent[] intents = new PhysicsIntent[64];
    private readonly List<Entity> intentEntities = new();
    private readonly List<FrameHandle> frameScratch = new();
    private readonly PhysicsOutputCheck check = new();
    // Entities whose body a test destroyed and did not create again.
    private readonly HashSet<Entity> destroyed = new();

    public PhysicsHarness(JoltBackendOptions? options = null)
    {
        Backend = new JoltPhysicsBackend((options ?? new JoltBackendOptions()) with { WorkerThreads = 2, Log = Warnings.Add });
    }

    public List<string> Warnings { get; } = new();
    public FrameTree Frames { get; } = new();
    public JoltPhysicsBackend Backend { get; }
    public PhysicsChanges Changes { get; } = new();
    public PhysicsStepOutput Output { get; } = new();
    public double Time { get; private set; }
    public long Tick { get; private set; }

    /// <summary>The latest state of every body, as the transform table would hold it.</summary>
    public Dictionary<Entity, BodyState> States { get; } = new();
    public List<HandOff> HandOffs { get; } = new();
    public List<ContactStarted> Contacts { get; } = new();

    /// <summary>Runs after each step, before the outputs are cleared.</summary>
    public Action<PhysicsStepOutput>? AfterStep { get; set; }

    /// <summary>
    /// Runs after the backend's step and before the harness applies its output. A test adds an output there that a
    /// correct backend never reports, to check that the harness refuses it.
    /// </summary>
    public Action<PhysicsStepOutput>? BeforeApply { get; set; }

    /// <summary>
    /// Opts a test in to refused hand-offs, and names the hand-offs that the test expects the harness to refuse. Null by
    /// default: every broken output throws. Each hand-off that it names must be broken (a test breaks the backend's own
    /// hand-off in <see cref="BeforeApply"/>), and the harness treats it as a Release sync point does: it skips the
    /// entity's whole update in that step, its body state and the state of the frame that it owns included, so the entity
    /// and its frame keep the state they had before the step. A hand-off that it names but that passes every check throws
    /// out of <see cref="Step"/>, so that a test cannot hide a hand-off that the frame tree would take.
    /// </summary>
    public Func<HandOff, bool>? RefusesHandOff { get; set; }

    /// <summary>The hand-offs that <see cref="RefusesHandOff"/> refused, in the order of the steps.</summary>
    public List<HandOff> RefusedHandOffs { get; } = new();

    public Entity NewEntity() => new(nextEntity++, 1);

    /// <summary>
    /// Replaces each hand-off of <paramref name="entity"/> in <paramref name="output"/> with what <paramref name="replace"/>
    /// makes of it. A test calls it from <see cref="BeforeApply"/> to give the backend's own hand-off the shape of a bug's
    /// output, so that the harness refuses it as the sync point would.
    /// </summary>
    public static void ReplaceHandOffs(PhysicsStepOutput output, Entity entity, Func<HandOff, HandOff> replace)
    {
        for (int i = 0; i < output.HandOffs.Count; i++)
            if (output.HandOffs[i].Entity == entity)
                output.HandOffs[i] = replace(output.HandOffs[i]);
    }

    public FrameHandle AddFrame(
        FrameHandle parent,
        DPose pose,
        FrameBounds bounds,
        MotionDriver driver,
        GravityField gravity = default,
        KeplerOrbit orbit = default,
        Entity owner = default)
    {
        Entity entity = owner.IsNull ? NewEntity() : owner;
        bool orbiting = driver == MotionDriver.Kepler && orbit.SemiMajorAxis > 0;
        if (orbiting)
            pose = new DPose(orbit.StateAt(Time).Position, orbit.RotationAt(Time));
        FrameHandle frame = Frames.Create(entity, parent, pose, bounds, driver, gravity, orbit);
        if (orbiting)
        {
            Frames.LinearVelocity(frame) = orbit.StateAt(Time).Velocity;
            Frames.AngularVelocity(frame) = orbit.AngularVelocityAt(Time).ToVector3();
        }
        Changes.FramesCreated.Add(frame);
        return frame;
    }

    public Entity AddBody(FrameHandle frame, BodyDesc desc, DPose pose, Vector3 velocity = default, Vector3 angularVelocity = default, Entity entity = default)
    {
        Entity e = entity.IsNull ? NewEntity() : entity;
        Changes.BodiesCreated.Add(new BodyCreate(e, frame, desc, pose, velocity, angularVelocity));
        States[e] = new BodyState(e, frame, pose, velocity, angularVelocity);
        destroyed.Remove(e);
        return e;
    }

    /// <summary>Adds a frame whose owner entity has a body: the hull in the parent and the interior in the frame.</summary>
    public FrameHandle AddFrameWithBody(
        FrameHandle parent,
        DPose pose,
        FrameBounds bounds,
        MotionDriver driver,
        BodyDesc desc,
        GravityField gravity = default,
        KeplerOrbit orbit = default,
        Vector3 velocity = default,
        Vector3 angularVelocity = default)
    {
        FrameHandle frame = AddFrame(parent, pose, bounds, driver, gravity, orbit);
        if (driver != MotionDriver.Kepler)
        {
            Frames.LinearVelocity(frame) = velocity;
            Frames.AngularVelocity(frame) = angularVelocity;
        }
        AddBody(parent, desc, Frames.LocalPose(frame), Frames.LinearVelocity(frame).ToVector3(), Frames.AngularVelocity(frame), frame.Entity);
        return frame;
    }

    /// <summary>Destroys the entity's body. Physics may name the entity again only after a test creates its body again.</summary>
    public void Destroy(Entity entity)
    {
        Changes.BodiesDestroyed.Add(entity);
        States.Remove(entity);
        destroyed.Add(entity);
    }

    /// <summary>The intent slot of an entity for the next step.</summary>
    public ref PhysicsIntent Intent(Entity entity)
    {
        if (entity.Index >= intents.Length)
            Array.Resize(ref intents, (int)entity.Index * 2 + 1);
        if (!intentEntities.Contains(entity))
            intentEntities.Add(entity);
        return ref intents[entity.Index];
    }

    public BodyState this[Entity entity] => States[entity];

    /// <summary>Applies pending changes, as the sync point does before a step. Like the simulation, it skips an empty batch.</summary>
    public void Sync()
    {
        if (Changes.FramesCreated.Count > 0 || Changes.BodiesDestroyed.Count > 0 ||
            Changes.FramesDestroyed.Count > 0 || Changes.BodiesCreated.Count > 0)
            Backend.ApplyChanges(Changes, Frames);
        Changes.Clear();
        Frames.Update();
    }

    /// <summary>
    /// Applies pending changes even when there are none, so that the backend reconciles with the frame tree now, as it
    /// does at the start of a step. A test then sees the backend's state between the reconcile and the step.
    /// </summary>
    public void Reconcile()
    {
        Backend.ApplyChanges(Changes, Frames);
        Changes.Clear();
        Frames.Update();
    }

    public void Step(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            StepOnce();
    }

    /// <summary>Steps until <paramref name="done"/> is true, at most <paramref name="maxTicks"/> times. Returns the ticks run.</summary>
    public int StepUntil(Func<bool> done, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            if (done())
                return i;
            StepOnce();
        }
        return maxTicks;
    }

    public void Dispose() => Backend.Dispose();

    private void StepOnce()
    {
        Sync();
        Output.Clear();
        var context = new PhysicsStepContext(Tick, Dt, Time, Frames, intents, intentEntities.ToArray());
        Backend.Step(context, Output);
        BeforeApply?.Invoke(Output);
        CheckOutput();

        // The order of World.ApplyPhysicsOutput: the reparents of the hand-offs, then body states, then the states of the
        // hand-offs, so that an entity with both keeps its hand-off's state, then frame states, then contacts.
        foreach (HandOff handOff in Output.HandOffs)
        {
            HandOffs.Add(handOff);
            if (!check.IsSkipped(handOff.Entity) && Frames.TryGetFrame(handOff.Entity, out FrameHandle frame))
                Frames.Reparent(frame, handOff.To);
        }
        foreach (BodyState body in Output.Bodies)
        {
            if (!check.IsSkipped(body.Entity))
                States[body.Entity] = body;
        }
        foreach (HandOff handOff in Output.HandOffs)
        {
            if (!check.IsSkipped(handOff.Entity))
                States[handOff.Entity] = new BodyState(handOff.Entity, handOff.To, handOff.Pose, handOff.LinearVelocity, handOff.AngularVelocity);
        }
        foreach (FrameState state in Output.Frames)
        {
            if (check.IsSkipped(state.Frame.Entity))
                continue;
            Frames.SetLocalPose(state.Frame, state.LocalPose);
            Frames.LinearVelocity(state.Frame) = state.LinearVelocity;
            Frames.AngularVelocity(state.Frame) = state.AngularVelocity;
            Frames.ProperAcceleration(state.Frame) = state.ProperAcceleration;
        }
        foreach (ContactStarted contact in Output.Contacts)
        {
            if (!check.IsSkipped(contact.A) && !check.IsSkipped(contact.B))
                Contacts.Add(contact);
        }
        AfterStep?.Invoke(Output);

        foreach (Entity entity in intentEntities)
            intents[entity.Index] = default;
        intentEntities.Clear();

        Time += Dt;
        Tick++;
        AdvanceKeplerFrames();
        Frames.Update();
    }

    /// <summary>
    /// Runs the sync point's check over the output. Throws for every broken output, except a broken hand-off that the test
    /// refuses, and throws for a hand-off that the test refuses although it passes every check.
    /// </summary>
    private void CheckOutput()
    {
        check.Run(Output, Frames, new Facts(this));
        var brokenHandOffs = new HashSet<int>();
        foreach (PhysicsOutputFault fault in check.Faults)
        {
            if (fault.Kind == PhysicsOutputKind.HandOff && RefusesHandOff is { } refuses && refuses(Output.HandOffs[fault.Index]))
            {
                brokenHandOffs.Add(fault.Index);
                continue;
            }
            throw new InvalidOperationException($"Physics reported output that only a bug produces: {fault.Message}.");
        }
        if (RefusesHandOff is not { } refusesAny)
            return;
        for (int i = 0; i < Output.HandOffs.Count; i++)
        {
            HandOff handOff = Output.HandOffs[i];
            if (!refusesAny(handOff))
                continue;
            if (!brokenHandOffs.Contains(i))
                throw new InvalidOperationException(
                    $"The test refuses the hand-off of {handOff.Entity} from {handOff.From} to {handOff.To}, but the frame tree accepts it.");
            RefusedHandOffs.Add(handOff);
        }
    }

    /// <summary>What the harness knows about an entity: like the sync point, it knows which bodies exist and where.</summary>
    private readonly struct Facts(PhysicsHarness harness) : IPhysicsOutputFacts
    {
        public bool IsAlive(Entity entity) => !harness.destroyed.Contains(entity);

        public bool HasBody(Entity entity) => harness.States.ContainsKey(entity);

        public FrameHandle FrameOf(Entity entity) => harness.States[entity].Frame;
    }

    private void AdvanceKeplerFrames()
    {
        frameScratch.Clear();
        Frames.GetAll(frameScratch);
        foreach (FrameHandle frame in frameScratch)
        {
            if (Frames.Driver(frame) != MotionDriver.Kepler)
                continue;
            KeplerOrbit orbit = Frames.Orbit(frame);
            if (orbit.SemiMajorAxis <= 0)
                continue;
            var (position, velocity) = orbit.StateAt(Time);
            Frames.SetLocalPose(frame, new DPose(position, orbit.RotationAt(Time)));
            Frames.LinearVelocity(frame) = velocity;
            Frames.AngularVelocity(frame) = orbit.AngularVelocityAt(Time).ToVector3();
        }
    }
}

/// <summary>Shapes and descriptions that the tests share.</summary>
internal static class Shapes
{
    public static BodyDesc Box(float half, float mass = 1f, BodyMotion motion = BodyMotion.Dynamic, float restitution = 0f, float damping = 0f) =>
        new(new BoxShapeDesc(new Vector3(half)), motion, Mass: mass, Restitution: restitution, LinearDamping: damping, AngularDamping: damping);

    public static BodyDesc Ball(float radius, float mass = 1f, float restitution = 0f) =>
        new(new SphereShapeDesc(radius), BodyMotion.Dynamic, Mass: mass, Restitution: restitution, LinearDamping: 0, AngularDamping: 0);

    public static BodyDesc Floor(float halfSize, float halfThickness = 0.5f) =>
        new(new BoxShapeDesc(new Vector3(halfSize, halfThickness, halfSize)), BodyMotion.Static);

    public static DPose At(double x, double y, double z) => new(new Double3(x, y, z), DQuaternion.Identity);
}
