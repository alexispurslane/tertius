using System.Numerics;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Helpers for tests that run the simulation's <see cref="World"/> on the real <see cref="JoltPhysicsBackend"/>, which
/// every test of physics does (docs/plans/real-physics-in-tests.md). The scenes that several tests share are in <see cref="Scenes"/>.
/// </summary>
public static class WorldTesting
{
    public static DPose At(double x, double y = 0, double z = 0) => new(new Double3(x, y, z), DQuaternion.Identity);

    /// <summary>
    /// A world with one gameplay thread, which owns a new backend with two Jolt threads. The backend's warnings go to
    /// <paramref name="warnings"/> when it is given. With <paramref name="debugChecks"/>, the world checks all of its
    /// invariants at the end of every tick, which costs about 0.5 ms a tick in a Debug build. <paramref name="configure"/>
    /// changes any other option before the world is created, for example the membership rules of numeric frames.
    /// </summary>
    public static World NewWorld(out JoltPhysicsBackend backend, List<string>? warnings = null, bool debugChecks = true,
        Action<WorldOptions>? configure = null)
    {
        backend = NewBackend(warnings);
        return new World(NewOptions(debugChecks, configure), backend);
    }

    /// <summary>
    /// Like <see cref="NewWorld"/>, but the world runs on a <see cref="FaultInjectingBackend"/> around a new real backend.
    /// With no edit and no exception set, it behaves as the world of <see cref="NewWorld"/>.
    /// </summary>
    public static World NewWorldWithFaults(out FaultInjectingBackend faults, List<string>? warnings = null, bool debugChecks = true,
        Action<WorldOptions>? configure = null)
    {
        faults = new FaultInjectingBackend(NewBackend(warnings));
        return new World(NewOptions(debugChecks, configure), faults);
    }

    private static JoltPhysicsBackend NewBackend(List<string>? warnings)
    {
        Action<string>? log = warnings is null ? null : message => { lock (warnings) warnings.Add(message); };
        return new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2, Log = log });
    }

    private static WorldOptions NewOptions(bool debugChecks, Action<WorldOptions>? configure)
    {
        var options = new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = debugChecks, DisposePhysics = true };
        configure?.Invoke(options);
        return options;
    }

    /// <summary>A root frame with uniform gravity of <paramref name="g"/> m/s² along -Y and a large bubble.</summary>
    public static FrameHandle Ground(World world, float g) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), GravityField.Uniform(new Vector3(0, -g, 0))));

    /// <summary>A static floor of 100 m by 100 m whose top is at <paramref name="top"/>.</summary>
    public static Entity Floor(World world, FrameHandle frame, double top = 0) => world.Spawn(new SpawnDesc
    {
        Frame = frame,
        Pose = At(0, top - 0.5),
        Body = new BodyDesc(new BoxShapeDesc(new Vector3(50, 0.5f, 50)), BodyMotion.Static),
    });

    /// <summary>A dynamic cube with sides of 1 m.</summary>
    public static BodyDesc Cube(float mass = 10) => new(new BoxShapeDesc(new Vector3(0.5f)), Mass: mass);

    /// <summary>A closed cube of triangles with <paramref name="half"/> m from its center to each face, every face toward the outside.</summary>
    public static MeshShapeDesc MeshCube(float half)
    {
        Vector3[] v =
        [
            new(-half, -half, -half), new(half, -half, -half), new(half, half, -half), new(-half, half, -half),
            new(-half, -half, half), new(half, -half, half), new(half, half, half), new(-half, half, half),
        ];
        int[] faces = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2];
        return new MeshShapeDesc(v, faces);
    }

    /// <summary>A dynamic ball that nothing slows down on its own, with a radius of <paramref name="radius"/> m.</summary>
    public static BodyDesc Ball(float radius = 0.5f, float mass = 1) =>
        new(new SphereShapeDesc(radius), Mass: mass, LinearDamping: 0, AngularDamping: 0);

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
