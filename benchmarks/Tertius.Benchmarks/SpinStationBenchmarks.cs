using System.Numerics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Tertius.Frames.Rails;
using Tertius.Physics;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of the rotation terms on awake dynamic bodies (ter-1bnq, docs/plans/spin-gravity.md, "Cost to measure"): the
/// two-turn update, the angular form and the velocity shadow, per body per step. One tick of a station, a frame without a
/// field that turns in place about its +Y at <see cref="Rate"/> in a root frame without a field, and 1,000 crates of
/// 0.5 m half extent on a grid of 10 by 10 by 10, 3 m apart, 30 m to 57 m from the axis. The station's spawn carries no
/// first motion intent, so it holds still in its first step, and each crate spawns at rest in inertial space except for
/// 5 cm/s along the axis. That drift keeps every crate awake at the rate 0 too, and it is the same for every crate, so no
/// two crates ever touch. In the turning station each crate circles in the station's axes at 9 to 18 m/s and stays at its
/// distance from the axis. The crates have no damping, so they keep that path, but they take the same path through the
/// update as damped crates: the shadow takes Jolt's damping factor, 1 here. At the rate 0 the station does not turn, so
/// the bodies take the path of a frame that does not turn, which is the path from before the rotation terms.
/// </summary>
[MemoryDiagnoser]
public class SpinStationCrateBenchmarks
{
    [Params(0, 0.31321)]
    public double Rate;

    private World world = null!;

    [GlobalSetup]
    public void Setup()
    {
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
        world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var spin = new FrameSpin(DQuaternion.Identity, Rate);
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            AsFrame = new FrameSpec(FrameBounds.Sphere(300)),
            Components = [new SpinMotion { Spin = spin }],
        }));
        var crate = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);
        for (int i = 0; i < 1000; i++)
            world.Spawn(new SpawnDesc
            {
                Frame = station,
                Pose = new DPose(new Double3(30 + i % 10 * 3, i / 10 % 10 * 3 - 13.5, i / 100 * 3 - 13.5), DQuaternion.Identity),
                LinearVelocity = new Double3(0, 0.05, 0),
                Body = crate,
            });
        // Two seconds: past the station's first step, which holds still, and past the half second after which Jolt would
        // put a body to sleep.
        for (int i = 0; i < 120; i++)
            world.Tick();

        int awake = backend.BodiesIn(station).Count(body => body.Active && body.Motion == JoltPhysicsSharp.MotionType.Dynamic);
        if (awake != 1000)
            throw new InvalidOperationException($"Expected 1000 awake crates, found {awake}.");
        Console.WriteLine($"// {awake} awake crates; VelocityShadow {Unsafe.SizeOf<VelocityShadow>()} bytes, " +
                          $"AppliedFrameAcceleration {Unsafe.SizeOf<AppliedFrameAcceleration>()} bytes per body.");
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();
}

/// <summary>
/// The cost of the wake test's reach in a frame whose turn changes in every step (ter-1bnq, docs/plans/spin-gravity.md, "The
/// wake"): one tick of a ship, a frame with a dynamic hull, a ball of 5 m and 100 t, that spins at 0.3 rad/s about its +Y in
/// a root frame without a field, and 1,000 crates in its bubble on the grid of <see cref="SpinStationCrateBenchmarks"/>.
/// The crates spawn at rest in inertial space, so they circle in the ship's axes, all awake, and stay in its bubble. With <see cref="AngularDamping"/> 0 the hull keeps
/// its rate, so the turn does not change and the wake test reads no reach. With a damping of 0.05 per second the hull's
/// rate falls in every step, so in every step in which the change of the push alone does not wake the system, the test
/// reads the place of every crate for the reach.
/// </summary>
[MemoryDiagnoser]
public class DampedSpinningShipBenchmarks
{
    [Params(0f, 0.05f)]
    public float AngularDamping;

    private World world = null!;

    [GlobalSetup]
    public void Setup()
    {
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
        world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            AngularVelocity = new Vector3(0, 0.3f, 0),
            Body = new BodyDesc(new SphereShapeDesc(5), Mass: 100_000, LinearDamping: 0, AngularDamping: AngularDamping,
                InteriorShape: new SphereShapeDesc(2)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(300)),
        }));
        var crate = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);
        for (int i = 0; i < 1000; i++)
        {
            var place = new Double3(30 + i % 10 * 3, i / 10 % 10 * 3 - 13.5, i / 100 * 3 - 13.5);
            world.Spawn(new SpawnDesc
            {
                Frame = ship,
                Pose = new DPose(place, DQuaternion.Identity),
                // At rest in inertial space: −ω × r relative to the ship's axes.
                LinearVelocity = -Double3.Cross(new Double3(0, 0.3, 0), place),
                Body = crate,
            });
        }
        for (int i = 0; i < 120; i++)
            world.Tick();

        int awake = backend.BodiesIn(ship).Count(body => body.Active && body.Motion == JoltPhysicsSharp.MotionType.Dynamic);
        Console.WriteLine($"// {awake} awake crates; the ship turns at {world.Frames.AngularVelocity(ship).Length():G4} rad/s.");
    }

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();
}

/// <summary>
/// The cost of the character update in a turning frame (ter-1bnq, docs/plans/spin-gravity.md, "Cost to measure"): one tick
/// of 100 people who stand on a static floor, on a grid of 10 by 10, 3 m apart. The floor's face lies 100 m from the
/// station's axis along its +X. With <see cref="Turning"/>, the station turns at 0.31321 rad/s, which gives 9.81 m/s² at
/// 100 m, and its spawn carries its first motion intent, so the people turn with it from their first step. Without it the
/// station does not turn and has a uniform field of the same 9.81 m/s² along +X: the same people on the same floor in a
/// frame that does not turn.
/// <para>
/// The people spawn 5 cm above the floor. Since ter-1b21 a walking character in a turning frame feels the rotation terms, so
/// in both cases every person falls onto the floor and stands, and the two cases do the same work: the setup prints how
/// many stand and how far their feet are from the floor. Before ter-1b21 the people of the turning station floated where
/// they spawned, and its tick cost 100 µs against 167 µs, which did not compare like with like.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SpinStationCharacterBenchmarks
{
    private const double StationRate = 0.31321;

    [Params(false, true)]
    public bool Turning;

    private World world = null!;
    private readonly Entity[] people = new Entity[100];

    [GlobalSetup]
    public void Setup()
    {
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = 2 });
        world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true }, backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        var spin = new FrameSpin(DQuaternion.Identity, StationRate);
        GravityField field = Turning ? GravityField.None : GravityField.Uniform(new Vector3((float)(StationRate * StationRate * 100), 0, 0));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(Turning
            ? new SpawnDesc
            {
                Frame = space,
                AsFrame = new FrameSpec(FrameBounds.Sphere(150)),
                FirstMove = new MotionIntent(spin.Sample(Double3.Zero, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
                Components = [new SpinMotion { Spin = spin }],
            }
            : new SpawnDesc { Frame = space, AsFrame = new FrameSpec(FrameBounds.Sphere(150), field) }));
        world.Spawn(new SpawnDesc
        {
            Frame = station,
            Pose = new DPose(new Double3(100.5, 0, 0), DQuaternion.Identity),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 20, 20)), BodyMotion.Static),
        });
        var person = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);
        for (int i = 0; i < 100; i++)
            people[i] = world.Spawn(new SpawnDesc
            {
                Frame = station,
                Pose = new DPose(new Double3(100 - 0.95, i % 10 * 3 - 13.5, i / 10 * 3 - 13.5), DQuaternion.Identity),
                Body = person,
            });
        for (int i = 0; i < 120; i++)
            world.Tick();
        int standing = people.Count(world.IsStanding);
        double lowest = people.Min(p => FeetAboveFloor(world.PoseIn(p, station)));
        double highest = people.Max(p => FeetAboveFloor(world.PoseIn(p, station)));
        Console.WriteLine($"// {standing} of 100 people stand; their feet are {lowest:G3} m to {highest:G3} m above the floor.");
    }

    /// <summary>
    /// How far the lowest point of a person's capsule at <paramref name="pose"/> in the station lies above the floor's face
    /// at x = 100, in meters. The capsule's half height is 0.6 m along its up and its radius 0.3 m. In the turning station
    /// up is against the gravity, which points away from the station's axis, so it leans from the flat floor's normal by
    /// up to 7.8 degrees at the grid's edge, 13.5 m from the face's middle, and the tilted capsule's center sits lower.
    /// </summary>
    private static double FeetAboveFloor(in DPose pose) =>
        100 - pose.Position.X - 0.6 * Math.Abs(pose.Rotation.Rotate(Double3.UnitY).X) - 0.3;

    [Benchmark]
    public void Tick() => world.Tick();

    [GlobalCleanup]
    public void Cleanup() => world.Dispose();
}

/// <summary>
/// The cost of <see cref="FrameTree.BeginStep"/> (ter-1bnq, docs/plans/spin-gravity.md, "Cost to measure"): one pass over
/// 1,000 frames, a root and 333 chains of 3 levels below it. Each frame lies 10 m from its parent's origin. With
/// <see cref="Turning"/>, the motion intent of each frame's owner is the sample of a spin at 0.1 rad/s about its parent's
/// +Y, so every level turns and every frame below the root composes its turn from its parent's. Without it no frame has a
/// motion intent, so every frame holds still and takes exactly the identity turn. Beside
/// <see cref="NumericFrameUpdateBenchmarks"/>, and against the 44 µs that ter-sn28 measured for a pass over 1,000 frames.
/// </summary>
[MemoryDiagnoser]
public class BeginStepBenchmarks
{
    private const double Step = 1.0 / 60;
    private const int Chains = 333;

    [Params(false, true)]
    public bool Turning;

    private readonly FrameTree frames = new();
    private PhysicsIntent[] intents = [];

    [GlobalSetup]
    public void Setup()
    {
        uint next = 1;
        FrameHandle root = frames.Create(new Entity(next++, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));
        var spin = new FrameSpin(DQuaternion.Identity, 0.1);
        var moves = new List<(uint Slot, FrameHandle Parent)>();
        for (int c = 0; c < Chains; c++)
        {
            FrameHandle parent = root;
            for (int level = 0; level < 3; level++)
            {
                uint slot = next++;
                parent = frames.Create(new Entity(slot, 1), parent, new DPose(new Double3(10, 0, 0), DQuaternion.Identity), FrameBounds.Sphere(1));
                moves.Add((slot, frames.Parent(parent)));
            }
        }
        intents = new PhysicsIntent[next];
        if (Turning)
            foreach ((uint slot, FrameHandle parent) in moves)
                intents[slot] = new PhysicsIntent { Frame = parent, Move = new MotionIntent(spin.Sample(new Double3(10, 0, 0), Step, Step)) };
        frames.Update();
        BeginStep();
        BeginStep();
        Console.WriteLine($"// {frames.Count} frames; FrameTurns {Unsafe.SizeOf<FrameTurns>()} bytes per frame.");
    }

    [Benchmark]
    public void BeginStep() => frames.BeginStep(intents, Step, lastStepFailed: false);
}
