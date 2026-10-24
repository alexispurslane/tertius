using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// Walker: a character walks its path of waypoints with the character controls, each waypoint a position in a frame of
/// its own, on the World and the real Jolt backend, through the wrapper that records the intents. The person is the
/// capsule of the demo scenes, 1.8 m tall and 0.3 m in radius, whose center rests 0.9 m above the floor. In every scene
/// the frames' +Y is up, so the distances across the person's up are the distances in X and Z.
/// </summary>
public sealed class WalkerTests : IDisposable
{
    private static readonly CapsuleShapeDesc Capsule = new(0.6f, 0.3f);

    private readonly FaultInjectingBackend faults;
    private readonly World world;

    public WalkerTests()
    {
        world = NewWorldWithFaults(faults: out faults);
    }

    public void Dispose() => world.Dispose();

    private Entity Person(FrameHandle frame, Double3 position, params Waypoint[] path) => world.Spawn(new()
    {
        Frame = frame,
        Pose = new(position, DQuaternion.Identity),
        Body = new(Shape: Capsule, Motion: BodyMotion.Character, Mass: 70),
        Components = [new Walker(path)]
    });

    /// <summary>The distance from <paramref name="a"/> to <paramref name="b"/> across +Y, in meters.</summary>
    private static double Across(Double3 a, Double3 b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>
    /// Runs <paramref name="ticks"/> ticks, and returns for each waypoint of <paramref name="path"/> the person's closest
    /// approach to it across +Y, in meters, read in the waypoint's frame, and the tick of that approach.
    /// </summary>
    private (double[] Closest, long[] Tick) Walk(Entity person, Waypoint[] path, int ticks)
    {
        double[] closest = [.. path.Select(_ => double.PositiveInfinity)];
        long[] tick = new long[path.Length];
        for (int t = 0; t < ticks; t++)
        {
            world.Tick();
            for (int i = 0; i < path.Length; i++)
            {
                double distance = Across(world.PoseIn(person, path[i].Frame).Position, path[i].Position);
                if (distance < closest[i])
                    (closest[i], tick[i]) = (distance, world.TickNumber);
            }
        }

        return (closest, tick);
    }

    /// <summary>The walks that <paramref name="person"/> asked for, in m/s, in the order of the steps that received them.</summary>
    private List<Vector3> Walks(Entity person) =>
    [
        .. faults.Intents
            .Where(record => record.Entity == person && (record.Intent.Flags & IntentFlags.CharacterMove) != 0)
            .Select(record => record.Intent.CharacterVelocity)
    ];

    /// <summary>An L of three waypoints 4 m apart on a floor in a root frame with 9.81 m/s² of gravity.</summary>
    private (Entity Person, Waypoint[] Path, FrameHandle Ground) OnTheGround()
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Waypoint[] path = [new(ground, new(4, 0.9, 0)), new(ground, new(4, 0.9, -4)), new(ground, new(0, 0.9, -4))];
        return (Person(ground, new(0, 0.95, 0), path), path, ground);
    }

    [Fact]
    public void TheWalkerReachesEachWaypointInOrder()
    {
        (Entity person, Waypoint[] path, _) = OnTheGround();

        (double[] closest, long[] tick) = Walk(person, path, 600);

        closest.Should().AllSatisfy(distance => distance.Should().BeLessThanOrEqualTo(Walker.ArrivalRadius));
        tick.Should().BeInAscendingOrder();
    }

    [Fact]
    public void TheWalkerStopsAtTheLastWaypoint()
    {
        (Entity person, Waypoint[] path, FrameHandle ground) = OnTheGround();

        Tick(world, 600);
        Double3 end = world.PoseIn(person, ground).Position;
        Tick(world, 60);

        Across(end, path[^1].Position).Should().BeLessThanOrEqualTo(Walker.StopRadius + 0.01);
        Across(world.PoseIn(person, ground).Position, end).Should()
            .BeLessThan(0.005, "the walker stays where it stopped");
    }

    /// <summary>
    /// Physics keeps the last walk that a character asked for, so the walker asks for no walk once when it stops, and for
    /// nothing after that: of all the walks it asked for, only the last is no walk.
    /// </summary>
    [Fact]
    public void TheWalkerAsksForNoWalkOnceWhenItStopsAtTheLastWaypoint()
    {
        (Entity person, _, _) = OnTheGround();

        Tick(world, 660);

        List<Vector3> walks = Walks(person);
        walks.Should().NotBeEmpty();
        walks[^1].Should().Be(Vector3.Zero, "the walker asked for no walk when it stopped");
        walks.Count(walk => walk == Vector3.Zero).Should().Be(1, "the walker asked for nothing after it stopped");
    }

    /// <summary>
    /// In the last meter before the last waypoint the walker slows, so that it does not stop from full speed at once: half
    /// a meter before it, the walk is about half as fast as the 3 m/s of the rest of the path.
    /// </summary>
    [Fact]
    public void TheWalkerSlowsDownBeforeTheLastWaypoint()
    {
        (Entity person, Waypoint[] path, FrameHandle ground) = OnTheGround();

        TickUntil(world, () => Across(world.PoseIn(person, ground).Position, path[^1].Position) < 0.5, 600);
        Double3 velocity = world.VelocityIn(person, ground);

        Across(velocity, Double3.Zero).Should().BeInRange(0.5, 0.75 * PersonControl.WalkSpeed);
    }

    /// <summary>
    /// The walk starts from rest: the first walk that the walker asks for is one tick of
    /// <see cref="Walker.StartAcceleration"/>, 3 m/s² for 1/60 s, so 0.05 m/s. Each later walk is at most that much faster
    /// than the one before it, so the walk reaches the 3 m/s of the path only after a second, and not before 0.95 s.
    /// </summary>
    [Fact]
    public void TheWalkSpeedsUpFromRestOverASecondInsteadOfAtOnce()
    {
        (Entity person, _, _) = OnTheGround();

        Tick(world, 90);

        List<double> speeds = [.. Walks(person).Select(walk => (double)walk.Length())];
        speeds[0].Should().BeApproximately(0.05, 1e-4, "the walk starts from rest");
        for (int i = 1; i < speeds.Count; i++)
            (speeds[i] - speeds[i - 1]).Should().BeLessThanOrEqualTo(0.05 + 1e-4,
                $"walk {i} speeds up by one tick of the start's acceleration at most");
        speeds.FindIndex(speed => speed >= PersonControl.WalkSpeed - 1e-4).Should()
            .BeInRange(56, 61, "the walk reaches its full speed after about a second");
    }

    /// <summary>
    /// The start's acceleration limits only a rise of the speed. The slowdown before the last waypoint applies at once: it
    /// takes 5 percent of the speed in each tick, more than 0.25 m/s in 4 ticks, where the start's acceleration would take
    /// 0.2 m/s. So the person still stops on it, and the speed of the walk never rises again before the stop.
    /// </summary>
    [Fact]
    public void TheWalkSlowsDownAtOnceBeforeTheLastWaypoint()
    {
        (Entity person, _, _) = OnTheGround();

        Tick(world, 660);

        List<double> speeds = [.. Walks(person).Select(walk => (double)walk.Length())];
        int top = speeds.FindLastIndex(speed => speed >= PersonControl.WalkSpeed - 1e-4);
        speeds.Skip(top + 1).Should().BeInDescendingOrder("past the last tick at full speed, the walk only slows");
        speeds.Skip(top + 1).Take(4).Should().Contain(speed => speed < PersonControl.WalkSpeed - 0.25,
            "the slowdown is faster than the start");
    }

    /// <summary>
    /// The person walks on a floor 30 m from the axis of a turntable, a frame that turns at 0.1 rad/s in place in a root
    /// frame with 9.81 m/s² of gravity. The turntable holds its place, so the engine gives it that gravity, reversed, as
    /// its proper acceleration, and its contents feel their weight (ARCHITECTURE.md, "How a frame moves"). The floor moves
    /// at 3 m/s in the root frame, as fast as the person walks, and it turns 1 rad in the 10 s of the walk.
    /// </summary>
    [Fact]
    public void TheWalkerFollowsItsWaypointsInAFrameThatMoves()
    {
        FrameHandle space = Ground(world, 9.81f);
        Entity turntable = world.Spawn(new SpawnDesc { Frame = space, AsFrame = new(FrameBounds.Sphere(100)) }
            .Spinning(world, new(Tilt: DQuaternion.Identity, Rate: 0.1)));
        FrameHandle table = world.GetOwnedFrame(turntable);
        world.Spawn(new()
            { Frame = table, Pose = At(30, -0.5), Body = new(Shape: new BoxShapeDesc(new(8, 0.5f, 8)), Motion: BodyMotion.Static) });
        Waypoint[] path = [new(table, new(34, 0.9, 0)), new(table, new(34, 0.9, -4)), new(table, new(30, 0.9, -4))];
        Entity person = Person(table, new(30, 0.95, 0), path);
        Double3 start = world.PoseIn(person, space).Position;

        (double[] closest, long[] tick) = Walk(person, path, 600);

        Across(world.PoseIn(person, space).Position, start).Should()
            .BeGreaterThan(10, "the turntable carried the person round");
        closest.Should().AllSatisfy(distance => distance.Should().BeLessThanOrEqualTo(Walker.ArrivalRadius));
        tick.Should().BeInAscendingOrder();
        closest[^1].Should().BeLessThanOrEqualTo(Walker.StopRadius + 0.01);
    }

    /// <summary>
    /// The person walks on still ground, and its waypoints lie in a small frame above the floor that turns at 0.05 rad/s
    /// and starts a half turn round, so that its axes point against the ground's. The waypoints form the L of
    /// <see cref="OnTheGround"/> at the start, and the turn carries them 0.14 m/s across the ground, 2.8 m from the
    /// frame's axis. The frame's bubble lies 4 m above the floor, out of the person's reach.
    /// </summary>
    [Fact]
    public void TheWalkerFollowsWaypointsWhoseFrameTurns()
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity marker = world.Spawn(new SpawnDesc
                { Frame = ground, Pose = At(2, 5, -2), AsFrame = new(FrameBounds.Sphere(0.5f)) }
            .Spinning(world, new(Tilt: DQuaternion.Identity, Rate: 0.05, PhaseAtEpoch: Math.PI)));
        FrameHandle turning = world.GetOwnedFrame(marker);
        // A half turn about +Y takes (x, y, z) in the marker's axes to (-x, y, -z) in the ground's.
        Waypoint[] path =
            [new(turning, new(-2, -4.1, -2)), new(turning, new(-2, -4.1, 2)), new(turning, new(2, -4.1, 2))];
        Entity person = Person(ground, new(0, 0.95, 0), path);

        (double[] closest, long[] tick) = Walk(person, path, 600);

        closest.Should().AllSatisfy(distance => distance.Should().BeLessThanOrEqualTo(Walker.ArrivalRadius));
        tick.Should().BeInAscendingOrder();
    }

    /// <summary>
    /// The person starts on the ground 12 m from a parked shuttle, turned a quarter turn, so that its hatch faces the
    /// ground's -Z, and walks into it: to a waypoint 3 m outside the hatch, one just inside it, and the place at the seat,
    /// all in the shuttle's frame. The hatch's sill is 0.3 m above the ground, which the person climbs as a stair.
    /// </summary>
    [Fact]
    public void TheWalkerHandsOffIntoTheShuttlesFrameWhenItWalksIn()
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        FrameHandle cabin = world.GetOwnedFrame(Shuttle.Park(world: world, frame: ground,
            pose: new(new(0, Shuttle.Hull.Y, 0), DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2))));
        // The shuttle's +X, the hatch's side, is the ground's -Z, and its +Z is the ground's +X.
        Entity person = Person(ground, new(Shuttle.Hatch.Center.Z, 0.95, -15),
            new Waypoint(cabin, Shuttle.OutsideHatch), new Waypoint(cabin, Shuttle.InsideHatch),
            new Waypoint(cabin, Shuttle.SeatStand));

        Tick(world, 900);

        world.Snapshot.FrameOf(person).Should().Be(cabin);
        Across(world.PoseIn(person, cabin).Position, Shuttle.SeatStand).Should()
            .BeLessThanOrEqualTo(Walker.StopRadius + 0.01);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The person walks toward a waypoint in a small frame 8 m away, whose owner has no body. A second after the start the
    /// frame dies with its owner, and its waypoint with it.
    /// </summary>
    [Fact]
    public void TheWalkerStopsWhereItIsWhenTheFrameOfItsNextWaypointDies()
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity marker = world.Spawn(new()
            { Frame = ground, Pose = At(10, 5), AsFrame = new(FrameBounds.Sphere(0.5f)) });
        Entity person = Person(ground, new(0, 0.95, 0), new Waypoint(world.GetOwnedFrame(marker), new(-2, -4.1, 0)));
        Tick(world, 60);
        double walked = world.PoseIn(person, ground).Position.X;

        world.Destroy(marker);
        Tick(world, 10);
        Double3 stopped = world.PoseIn(person, ground).Position;
        Tick(world, 60);

        walked.Should().BeGreaterThan(1, "the person walked toward the waypoint while its frame lived");
        stopped.X.Should().BeLessThan(walked + 0.25, "the person stopped within a few steps");
        Across(world.PoseIn(person, ground).Position, stopped).Should()
            .BeLessThan(0.005, "the walker stays where it stopped");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The scene of <see cref="TheWalkerStopsWhereItIsWhenTheFrameOfItsNextWaypointDies"/>. When the frame of its next
    /// waypoint dies, the walker asks for no walk once, and for nothing after that, as when it reaches the last waypoint.
    /// </summary>
    [Fact]
    public void TheWalkerAsksForNoWalkOnceWhenTheFrameOfItsNextWaypointDies()
    {
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity marker = world.Spawn(new()
            { Frame = ground, Pose = At(10, 5), AsFrame = new(FrameBounds.Sphere(0.5f)) });
        Entity person = Person(ground, new(0, 0.95, 0), new Waypoint(world.GetOwnedFrame(marker), new(-2, -4.1, 0)));
        Tick(world, 60);

        world.Destroy(marker);
        Tick(world, 60);

        List<Vector3> walks = Walks(person);
        walks[^1].Should().Be(Vector3.Zero, "the walker asked for no walk when it stopped");
        walks.Count(walk => walk == Vector3.Zero).Should().Be(1, "the walker asked for nothing after it stopped");
    }
}
