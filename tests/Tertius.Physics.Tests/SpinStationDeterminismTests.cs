using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The rotation terms keep a run deterministic, on the World and the real Jolt backend (docs/plans/spin-gravity.md,
/// "Acceptance tests"): a scene in a turning station gives the same events and the same state, to the bit, in every run,
/// with one gameplay thread and one Jolt thread or with four of each.
/// </summary>
public class SpinStationDeterminismTests
{
    private const double StationRate = 0.31321;
    private const double FloorRadius = 100;

    /// <summary>
    /// A station that turns from its first step, with a floor 100 m from its axis, and in it: a stack of three crates on the
    /// floor, a ship on the floor, a crate thrown across the station, a crate at rest in inertial space 50 m from the axis,
    /// and a ball that spins. A group of free bodies gets a falling frame from 10 m/s, so the thrown crate and the crate at
    /// rest in inertial space move into falling frames below the station, and the update runs on their semantic places.
    /// Returns, for each of 300 ticks, one line with the tick, each body's frame and physics home, and a hash of the bits of
    /// each body's place and velocity in the station, and then one line for each collision that a crate heard.
    /// </summary>
    private static List<string> Trace(int threads)
    {
        var backend = new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = threads });
        var options = new WorldOptions { MaxDegreeOfParallelism = threads, DebugChecks = true, DisposePhysics = true };
        options.NumericFrames = NumericFrameTesting.EveryGroupFalls with { CreateSpeed = 10, LeaveSpeed = 4 };
        using var world = new World(options, backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = space, AsFrame = new FrameSpec(FrameBounds.Sphere(150)) }
            .Spinning(world, new FrameSpin(DQuaternion.Identity, StationRate), firstMove: true)));
        world.Spawn(new SpawnDesc
        {
            Frame = station,
            Pose = At(FloorRadius + 0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 20, 20)), BodyMotion.Static),
        });
        var crate = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10);
        var logs = new List<CollisionLog>();
        var bodies = new List<Entity>();
        // Spawns the description that the function gives for a new collision log.
        void Add(Func<IComponent[], SpawnDesc> desc)
        {
            var log = new CollisionLog();
            logs.Add(log);
            bodies.Add(world.Spawn(desc([log])));
        }
        for (int i = 0; i < 3; i++)
            Add(log => new SpawnDesc { Frame = station, Pose = At(FloorRadius - 0.5 - i), Body = crate, Components = log });
        Add(log => new SpawnDesc
        {
            Frame = station,
            Components = log,
            Pose = At(FloorRadius - 1, 0, 8),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1, 2, 2)), Mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.9f)),
        });
        Add(log => new SpawnDesc { Frame = station, Pose = At(FloorRadius - 3, 0, -8), LinearVelocity = new Double3(-12, 0, 4), Body = crate, Components = log });
        var restPlace = new Double3(50, 0, 10);
        Add(log => new SpawnDesc
        {
            Frame = station,
            Components = log,
            Pose = new DPose(restPlace, DQuaternion.Identity),
            LinearVelocity = -Double3.Cross(new Double3(0, StationRate, 0), restPlace),
            Body = crate,
        });
        Add(log => new SpawnDesc
        {
            Frame = station,
            Components = log,
            Pose = At(60, 5, -10),
            AngularVelocity = new Vector3(1, 2, 0),
            Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 10),
        });

        var lines = new List<string>();
        for (int tick = 0; tick < 300; tick++)
        {
            world.Tick();
            var hash = new HashCode();
            var homes = new List<string>();
            foreach (Entity body in bodies)
            {
                FrameHandle home = world.PhysicsHomeOf(body);
                homes.Add($"{world.GetFrame(body)}/{(home == station ? "station" : world.Frames.Numeric(home).ToString())}");
                Double3 position = world.PoseIn(body, station).Position;
                Double3 velocity = world.VelocityIn(body, station);
                foreach (double value in new[] { position.X, position.Y, position.Z, velocity.X, velocity.Y, velocity.Z })
                    hash.Add(BitConverter.DoubleToInt64Bits(value));
            }
            lines.Add($"{tick} {string.Join(" ", homes)} {hash.ToHashCode()}");
        }
        for (int i = 0; i < logs.Count; i++)
            lines.AddRange(logs[i].Seen.Select(seen => $"body {i} hit {seen.Other} at {seen.Tick} with {BitConverter.SingleToInt32Bits(seen.Impulse)}"));
        world.Errors.Should().BeEmpty();
        return lines;
    }

    [Fact]
    public void AStationSceneGivesTheSameEventsAndStateInEveryRunWithOneOrFourThreads()
    {
        List<string> first = Trace(1);
        List<string> second = Trace(1);
        List<string> parallel = Trace(4);

        first.Should().Contain(line => line.Contains("Falling"), "some bodies fall in numeric frames below the station");
        first.Should().Contain(line => line.StartsWith("body "), "the crates hit each other and the floor");
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }
}
