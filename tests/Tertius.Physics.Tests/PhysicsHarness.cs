using System.Numerics;

namespace Tertius.Physics.Tests;

/// <summary>
/// Drives <see cref="JoltPhysicsBackend"/> the way the sync point does, without the simulation: it applies body
/// states, hand-offs and frame states from the physics output, advances Kepler frames, and feeds intents.
/// </summary>
internal sealed class PhysicsHarness : IDisposable
{
    public const float Dt = 1f / 60f;

    private uint nextEntity = 1;
    private PhysicsIntent[] intents = new PhysicsIntent[64];
    private readonly List<Entity> intentEntities = new();
    private readonly List<FrameHandle> frameScratch = new();

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

    public Entity NewEntity() => new(nextEntity++, 1);

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
            Frames.AngularVelocity(frame) = orbit.AngularVelocity();
        }
        Changes.FramesCreated.Add(frame);
        return frame;
    }

    public Entity AddBody(FrameHandle frame, BodyDesc desc, DPose pose, Vector3 velocity = default, Vector3 angularVelocity = default, Entity entity = default)
    {
        Entity e = entity.IsNull ? NewEntity() : entity;
        Changes.BodiesCreated.Add(new BodyCreate(e, frame, desc, pose, velocity, angularVelocity));
        States[e] = new BodyState(e, frame, pose, velocity, angularVelocity);
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

    public void Destroy(Entity entity)
    {
        Changes.BodiesDestroyed.Add(entity);
        States.Remove(entity);
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

    /// <summary>Applies pending changes, as the sync point does before a step.</summary>
    public void Sync()
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

        foreach (HandOff handOff in Output.HandOffs)
        {
            HandOffs.Add(handOff);
            States[handOff.Entity] = new BodyState(handOff.Entity, handOff.To, handOff.Pose, handOff.LinearVelocity, handOff.AngularVelocity);
            if (Frames.TryGetFrame(handOff.Entity, out FrameHandle frame))
                Frames.Reparent(frame, handOff.To);
        }
        foreach (BodyState body in Output.Bodies)
            States[body.Entity] = body;
        foreach (FrameState state in Output.Frames)
        {
            Frames.SetLocalPose(state.Frame, state.LocalPose);
            Frames.LinearVelocity(state.Frame) = state.LinearVelocity;
            Frames.AngularVelocity(state.Frame) = state.AngularVelocity;
            Frames.ProperAcceleration(state.Frame) = state.ProperAcceleration;
        }
        Contacts.AddRange(Output.Contacts);
        AfterStep?.Invoke(Output);

        foreach (Entity entity in intentEntities)
            intents[entity.Index] = default;
        intentEntities.Clear();

        Time += Dt;
        Tick++;
        AdvanceKeplerFrames();
        Frames.Update();
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
            Frames.AngularVelocity(frame) = orbit.AngularVelocity();
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
