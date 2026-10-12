using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Physics.Tests;

/// <summary>
/// Helpers for tests that run the simulation's <see cref="World"/> on the real <see cref="JoltPhysicsBackend"/>. The real physics
/// in every test work (docs/plans/real-physics-in-tests.md) moves them into a shared test project.
/// </summary>
internal static class WorldTesting
{
    public static DPose At(double x, double y = 0, double z = 0) => new(new Double3(x, y, z), DQuaternion.Identity);

    /// <summary>
    /// A world with one gameplay thread, which owns a new backend with two Jolt threads. The backend's warnings go to
    /// <paramref name="warnings"/> when it is given. With <paramref name="debugChecks"/>, the world checks all of its
    /// invariants at the end of every tick, which costs about 0.5 ms a tick in a Debug build.
    /// </summary>
    public static World NewWorld(out JoltPhysicsBackend backend, List<string>? warnings = null, bool debugChecks = true)
    {
        Action<string>? log = warnings is null ? null : message => { lock (warnings) warnings.Add(message); };
        backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2, Log = log });
        return new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = debugChecks, DisposePhysics = true }, backend);
    }

    /// <summary>A root frame with uniform gravity of <paramref name="g"/> m/s² along -Y and a large bubble.</summary>
    public static FrameHandle Ground(World world, float g) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -g, 0))));

    /// <summary>A static floor of 100 m by 100 m whose top is at <paramref name="top"/>.</summary>
    public static Entity Floor(World world, FrameHandle frame, double top = 0) => world.Spawn(new SpawnDesc
    {
        Frame = frame,
        Pose = At(0, top - 0.5),
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
    });

    /// <summary>A dynamic cube with sides of 1 m.</summary>
    public static BodyDesc Cube(float mass = 10) => new(new BoxShapeDesc(new Vector3(0.5f)), Mass: mass);

    /// <summary>True while Jolt simulates the primary body of <paramref name="entity"/> in the system of <paramref name="frame"/>.</summary>
    public static bool IsAwake(JoltPhysicsBackend backend, FrameHandle frame, Entity entity) =>
        backend.BodiesIn(frame).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, entity)).Active;

    /// <summary>Ticks until <paramref name="done"/> holds, at most <paramref name="limit"/> times. Returns the ticks it ran.</summary>
    public static int TickUntil(World world, Func<bool> done, int limit)
    {
        int ticks = 0;
        while (ticks < limit && !done())
        {
            world.Tick();
            ticks++;
        }
        return ticks;
    }

    public static void Tick(World world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            world.Tick();
    }
}

/// <summary>Records the tick and the other entity of every Collision that its entity hears.</summary>
internal sealed class CollisionLog : IComponent
{
    public List<(long Tick, Entity Other)> Seen { get; } = new();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Collision>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].Is<Collision>())
                Seen.Add((self.Tick, events[i].As<Collision>().Other));
    }

    public int CountWith(Entity other) => Seen.Count(seen => seen.Other == other);
}

/// <summary>What a <see cref="Pilot"/> does to its entity's body during a tick.</summary>
internal delegate void BodyCommand(BodyHandle body);

/// <summary>
/// Lets a test steer its entity's body. A command written during one tick acts in the physics step of the next tick.
/// </summary>
internal sealed class Pilot : IComponent
{
    /// <summary>Runs on every tick until the test changes it. Null does nothing.</summary>
    public BodyCommand? EveryTick { get; set; }

    /// <summary>Runs on the next tick only. Null does nothing.</summary>
    public BodyCommand? Once { get; set; }

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        EveryTick?.Invoke(self.Body);
        if (Once is { } once)
        {
            Once = null;
            once(self.Body);
        }
    }
}

/// <summary>
/// The real backend plus one edit of the input that the World sends it, or of the output that it gives back. It stands in
/// for a bug, for the tests of what the World and the backend do with a state that only a bug produces. The real physics
/// in every test work grows it into the only test double for physics.
/// </summary>
internal sealed class FaultInjectingBackend(JoltPhysicsBackend jolt) : IPhysicsBackend
{
    public JoltPhysicsBackend Jolt { get; } = jolt;

    /// <summary>Edits the next change batch before Jolt applies it, once. Null edits nothing.</summary>
    public Action<PhysicsChanges>? EditNextChanges { get; set; }

    public void ApplyChanges(PhysicsChanges changes, FrameTree frames)
    {
        if (EditNextChanges is { } edit)
        {
            EditNextChanges = null;
            edit(changes);
        }
        Jolt.ApplyChanges(changes, frames);
    }

    /// <summary>Edits the output of the next step after Jolt wrote it, once. Null edits nothing.</summary>
    public Action<PhysicsStepOutput>? EditNextOutput { get; set; }

    public void Step(in PhysicsStepContext context, PhysicsStepOutput output)
    {
        Jolt.Step(context, output);
        if (EditNextOutput is { } edit)
        {
            EditNextOutput = null;
            edit(output);
        }
    }

    public float BoundingRadius(ShapeDesc shape) => Jolt.BoundingRadius(shape);

    public string? ShapeProblem(ShapeDesc shape) => Jolt.ShapeProblem(shape);

    public void Dispose() => Jolt.Dispose();
}
