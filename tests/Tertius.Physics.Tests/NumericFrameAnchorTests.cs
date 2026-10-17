using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Anchors in the merge test of numeric frames (ter-dhkf, docs/plans/numeric-frames.md, "Anchors"), on the World and the
/// real backend. An anchor is a static body or the hull of a frame that physics does not drive. The merge test links an
/// anchor to every group, also to one that falls, and the group takes the anchor's home: an anchor counts as infinitely
/// heavy. So the anchor is a real body of the group's Jolt system, and Jolt sweeps a fast body against it. Before, a fast
/// body fell in a numeric frame of its own, slow there, and the frame kept a kinematic ghost of the anchor, which came at
/// the body fast. Jolt gives a kinematic body no linear cast, so the body passed through a thin wall (ter-ykqs). Each scene
/// runs with the default numeric frames, under which a body that flies at 50 m/s or more relative to its frame gets a
/// falling numeric frame and a slow body 10 km or more from its frame's origin gets a resting one, and without numeric
/// frames, and it must give the same events and the same motion either way.
/// </summary>
public class NumericFrameAnchorTests
{
    /// <summary>Membership rules under which no group of bodies ever gets a numeric frame.</summary>
    private static readonly NumericFrameOptions WithoutNumericFrames = new()
    {
        CreateSpeed = double.PositiveInfinity,
        LeaveSpeed = double.PositiveInfinity,
        CreateDistance = double.PositiveInfinity,
        LeaveDistance = double.PositiveInfinity,
    };

    /// <summary>
    /// A world on the wrapper with the default numeric frames or, when <paramref name="numericFrames"/> is false, with none.
    /// It runs on <paramref name="threads"/> gameplay threads, and its backend on as many Jolt threads.
    /// </summary>
    private static World NewWorld(bool numericFrames, int threads, out FaultInjectingBackend faults)
    {
        faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { WorkerThreads = threads }));
        var options = new WorldOptions { MaxDegreeOfParallelism = threads, DebugChecks = true, DisposePhysics = true };
        if (!numericFrames)
            options.NumericFrames = WithoutNumericFrames;
        return new World(options, faults);
    }

    /// <summary>
    /// Space without gravity, and at its origin a station-like frame: a frame with the Static driver, no body and no
    /// gravity, with a bubble of radius <paramref name="radius"/> m.
    /// </summary>
    private static FrameHandle Station(World world, float radius = 1000)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
        return world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(0), AsFrame = new FrameSpec(FrameBounds.Sphere(radius), MotionDriver.Static),
        }));
    }

    /// <summary>A static wall 10 cm thick, 10 m by 10 m, across the X axis.</summary>
    private static readonly BodyDesc ThinWall = new(new BoxShapeDesc(new Vector3(0.05f, 5, 5)), BodyMotion.Static);

    /// <summary>A static mesh wall without thickness, 10 m by 10 m, across the X axis, whose two triangles face -X.</summary>
    private static readonly BodyDesc ThinMeshWall = new(new MeshShapeDesc(
        [new Vector3(0, -5, -5), new Vector3(0, -5, 5), new Vector3(0, 5, 5), new Vector3(0, 5, -5)], [0, 1, 2, 0, 2, 3]), BodyMotion.Static, Mass: 0);

    /// <summary>A crate of 2 kg, a box of 20 cm that nothing slows down on its own and that does not bounce.</summary>
    private static readonly BodyDesc SmallCrate = new(new BoxShapeDesc(new Vector3(0.1f)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A ball of 1 kg with a radius of 0.5 m that nothing slows down on its own and that does not bounce.</summary>
    private static readonly BodyDesc FlyingBall = new(new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A static rock, a sphere with a radius of 1 m.</summary>
    private static readonly BodyDesc Rock = new(new SphereShapeDesc(1), BodyMotion.Static);

    /// <summary>The bodies of a scene: the station, the body that flies, and the anchor that it meets.</summary>
    private sealed record Scene(FrameHandle Station, Entity Mover, Entity Anchor);

    /// <summary>
    /// What the body that flew did: its place and velocity in the station at the end, every Collision and CollisionEnded
    /// that it heard, and each change of its physics home: the tick, the kind of the new home (the station's own system,
    /// or the mode of a numeric frame), and its distance from the anchor's center then, in meters.
    /// </summary>
    private sealed record Flight(string Name, Double3 Position, Double3 Velocity, List<(long Tick, Entity Other, float Impulse)> Seen,
        List<(long Tick, Entity Other)> Ended, List<(long Tick, string Home, double Distance)> Homes)
    {
        /// <summary>The kinds of the homes, in order.</summary>
        public List<string> HomeKinds => Homes.Select(change => change.Home).ToList();
    }

    /// <summary>
    /// Builds a scene with <paramref name="build"/>, runs it for <paramref name="ticks"/> ticks, and calls
    /// <paramref name="watch"/> after each tick, when it is given. The mover carries <paramref name="log"/>.
    /// </summary>
    private static Flight Fly(bool numericFrames, int threads, Func<World, CollisionLog, Scene> build, int ticks, Action<World, Scene>? watch = null)
    {
        using World world = NewWorld(numericFrames, threads, out FaultInjectingBackend faults);
        var log = new CollisionLog();
        Scene scene = build(world, log);
        var homes = new List<(long Tick, string Home, double Distance)>();
        FrameHandle last = FrameHandle.Null;
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            FrameHandle home = world.PhysicsHomeOf(scene.Mover);
            if (home != last)
            {
                double distance = (world.PoseIn(scene.Mover, scene.Station).Position - world.PoseIn(scene.Anchor, scene.Station).Position).Length;
                homes.Add((world.TickNumber, home == scene.Station ? "station" : world.Frames.Numeric(home).ToString(), distance));
                last = home;
            }
            watch?.Invoke(world, scene);
        }
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        return new Flight(numericFrames ? "with numeric frames" : "without numeric frames", world.PoseIn(scene.Mover, scene.Station).Position,
            world.VelocityIn(scene.Mover, scene.Station), log.Seen, log.Ended, homes);
    }

    /// <summary>Checks that two runs of one scene, with and without numeric frames, heard the same events and moved alike.</summary>
    private static void ShouldMatch(Flight with, Flight without, double tolerance)
    {
        with.Seen.Select(s => (s.Tick, s.Other)).Should().Equal(without.Seen.Select(s => (s.Tick, s.Other)), "the body hears the same Collisions");
        with.Ended.Should().Equal(without.Ended, "the body hears the same CollisionEnded events");
        for (int i = 0; i < with.Seen.Count; i++)
            with.Seen[i].Impulse.Should().BeApproximately(without.Seen[i].Impulse, Math.Abs(without.Seen[i].Impulse) * 0.02f + 1e-3f);
        (with.Position - without.Position).Length.Should().BeLessThan(tolerance, "the body moves alike");
        (with.Velocity - without.Velocity).Length.Should().BeLessThan(tolerance, "the body moves alike");
        without.HomeKinds.Should().Equal(["station"], "without numeric frames the body stays in the station's own system");
    }

    // ---- A crate thrown at a thin wall ---------------------------------------------------------------------------

    /// <summary>
    /// A crate of 20 cm flies along +X at 60 m/s, 1 m per step, at a static wall across the X axis at the station's origin,
    /// whose face toward the crate lies <paramref name="face"/> m before the origin. It starts 200.5 m before the wall, so
    /// at the start of step 200 its center is 0.5 m before the wall, and at the end of it 0.5 m behind: no step ends with
    /// the crate touching the wall, and only a linear cast stops it.
    /// </summary>
    private static Scene ThrowAtAWall(World world, CollisionLog log, BodyDesc wall)
    {
        FrameHandle station = Station(world);
        Entity anchor = world.Spawn(new SpawnDesc { Frame = station, Pose = At(0), Body = wall });
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = station, Pose = At(-200.5), LinearVelocity = new Vector3(60, 0, 0), Body = SmallCrate, Components = [log],
        });
        return new Scene(station, crate, anchor);
    }

    /// <summary>
    /// A crate thrown at 60 m/s inside a station flies at a static wall that is thinner than one step of its motion, and
    /// stops at it, with numeric frames as without. With numeric frames the crate falls in a numeric frame of its own until
    /// the merge test reaches the wall, 2 s ahead, and then joins the wall's home, the station's own system, so that Jolt
    /// sweeps it against the real wall. Before ter-dhkf, the crate stayed in its falling frame, the wall's kinematic ghost
    /// came at it at 60 m/s, and the crate passed through the wall.
    /// </summary>
    [Theory]
    [InlineData(false, 0.05)]
    [InlineData(true, 0.0)]
    public void ACrateThrownAtAThinStaticWallJoinsTheWallsHomeAndStopsAtItAsItDoesWithoutNumericFrames(bool mesh, double face)
    {
        BodyDesc wall = mesh ? ThinMeshWall : ThinWall;
        Flight without = Fly(numericFrames: false, threads: 1, (world, log) => ThrowAtAWall(world, log, wall), ticks: 230);
        Flight with = Fly(numericFrames: true, threads: 1, (world, log) => ThrowAtAWall(world, log, wall), ticks: 230);

        foreach (Flight run in new[] { without, with })
        {
            run.Position.X.Should().BeApproximately(-face - 0.1, 0.03, "the crate stops with its front at the wall's face {0}", run.Name);
            run.Velocity.Length.Should().BeLessThan(0.05, "the crate stopped at the wall {0}", run.Name);
            run.Seen.Should().ContainSingle("the crate hears one Collision with the wall {0}", run.Name);
        }
        with.HomeKinds.Should().Equal(["Falling", "station"], "the crate falls until the merge test reaches the wall, then joins its home");
        with.Homes[1].Distance.Should().BeInRange(126, 128, "the merge test reaches the wall 2 s ahead: 120 m plus the bounding radii");
        with.Homes[1].Tick.Should().BeLessThan(with.Seen[0].Tick, "the crate joins the wall's home before it hits the wall");
        ShouldMatch(with, without, tolerance: 0.01);
    }

    // ---- A fast body that passes an anchor -----------------------------------------------------------------------

    /// <summary>
    /// A ball of radius 0.5 m flies along +X at 80 m/s from 300 m before a static rock of radius 1 m at the station's
    /// origin, 1.4 m to the side of its center, so that it grazes the rock and flies on at about 70 m/s.
    /// </summary>
    private static Scene GrazeARock(World world, CollisionLog log)
    {
        FrameHandle station = Station(world);
        Entity rock = world.Spawn(new SpawnDesc { Frame = station, Pose = At(0), Body = Rock });
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = station, Pose = At(-300, 1.4), LinearVelocity = new Vector3(80, 0, 0), Body = FlyingBall, Components = [log],
        });
        return new Scene(station, ball, rock);
    }

    /// <summary>
    /// A fast ball that grazes a static rock joins the rock's home, the station's own system, as soon as the merge test
    /// reaches the rock, 2 s ahead: its bounding radius, the rock's, and 80 m/s for 2 s, 161.5 m. It runs there for the
    /// encounter, grazes the rock, and flies on. When the merge test no longer links them, with the split margin of 10 m of
    /// two bodies in one system, it lets go and falls in a numeric frame again. It hears the same events and moves alike,
    /// with numeric frames as without.
    /// </summary>
    [Fact]
    public void AFastBallThatGrazesAStaticRockRunsInTheRocksHomeForTheEncounterAndThenFallsAgain()
    {
        Flight without = Fly(numericFrames: false, threads: 1, GrazeARock, ticks: 420);
        Flight with = Fly(numericFrames: true, threads: 1, GrazeARock, ticks: 420);

        with.Seen.Should().ContainSingle("the ball grazes the rock once");
        with.Ended.Should().ContainSingle();
        with.HomeKinds.Should().Equal(["Falling", "station", "Falling"]);
        with.Homes[1].Distance.Should().BeInRange(160, 162, "the merge test reaches the rock 2 s ahead");
        with.Homes[1].Tick.Should().BeLessThan(with.Seen[0].Tick);
        with.Homes[2].Tick.Should().BeGreaterThan(with.Ended[0].Tick);
        with.Velocity.Length.Should().BeGreaterThan(50, "the ball flies on fast enough to fall again");
        // After the graze nothing changes the ball's velocity, so the reach that lets go is the one at the end.
        double reach = 1.5 + with.Velocity.Length * 2 + 10;
        with.Homes[2].Distance.Should().BeInRange(reach, reach + with.Velocity.Length / 60 + 0.1,
            "the ball lets go at the first sync point beyond the reach of the merge test and the split margin");
        ShouldMatch(with, without, tolerance: 1e-3);
    }

    /// <summary>
    /// A frame with the Spin driver at the station's origin: a box hull of 10 m by 6 m by 30 m that turns at 0.5 rad/s about
    /// Y, inside a bubble of radius 20 m. A ball of radius 0.5 m flies past along +X at 80 m/s, 30 m to the side of the
    /// hull's center, outside the bubble, so that it neither enters the frame nor touches the hull.
    /// </summary>
    private static Scene PassATurntable(World world, CollisionLog log)
    {
        FrameHandle station = Station(world);
        Entity turntable = world.Spawn(new SpawnDesc
        {
            Frame = station,
            Pose = At(0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5, 3, 15)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(20), MotionDriver.Spin, Spin: new FrameSpin(DQuaternion.Identity, 0.5)),
        });
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = station, Pose = At(-300, 30), LinearVelocity = new Vector3(80, 0, 0), Body = FlyingBall, Components = [log],
        });
        return new Scene(station, ball, turntable);
    }

    /// <summary>
    /// The hull of a Spin frame is an anchor whose center rests in the system of the frame's semantic parent. A fast ball
    /// that passes it joins that system for the encounter, where the turning hull is a real body, and falls again after,
    /// with the same motion as without numeric frames.
    /// </summary>
    [Fact]
    public void AFastBallThatPassesTheHullOfASpinFrameRunsInTheHullsHomeForTheEncounterAndThenFallsAgain()
    {
        Flight without = Fly(numericFrames: false, threads: 1, PassATurntable, ticks: 480);
        Flight with = Fly(numericFrames: true, threads: 1, PassATurntable, ticks: 480);

        with.HomeKinds.Should().Equal(["Falling", "station", "Falling"]);
        with.Seen.Should().BeEmpty("the ball passes outside the turntable's bubble");
        ShouldMatch(with, without, tolerance: 1e-3);
    }

    // ---- A group that links two static anchors -------------------------------------------------------------------

    /// <summary>The rocks of <see cref="FlyBetweenTwoRocks"/>.</summary>
    private sealed record TwoRocks(Entity Near, Entity Far);

    /// <summary>
    /// A station with a bubble of radius 20 km. Two static rocks of radius 1 m lie on the X axis, 40 m apart: the near one
    /// 9,980 m from the station's origin, nearer than the 10 km at which a slow body gets a resting frame, so it stays in the
    /// station's own system, and the far one 10,020 m out, in a resting frame of its own. A ball flies between them along
    /// +Y at 80 m/s, from 300 m before their line, 20 m from each.
    /// </summary>
    private static Scene FlyBetweenTwoRocks(World world, CollisionLog log, out TwoRocks rocks)
    {
        FrameHandle station = Station(world, radius: 20_000);
        Entity near = world.Spawn(new SpawnDesc { Frame = station, Pose = At(9_980), Body = Rock });
        Entity far = world.Spawn(new SpawnDesc { Frame = station, Pose = At(10_020), Body = Rock });
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = station, Pose = At(10_000, -300), LinearVelocity = new Vector3(0, 80, 0), Body = FlyingBall, Components = [log],
        });
        rocks = new TwoRocks(near, far);
        return new Scene(station, ball, far);
    }

    /// <summary>
    /// A fast ball that links two static rocks whose homes differ takes one home for all three: the far rock's resting
    /// frame, the frame that holds a static body of the group. The near rock moves into it for the encounter, so both rocks
    /// are real bodies of the ball's system, and no rock has a ghost. Then the ball lets go and falls again, with the same
    /// motion as without numeric frames.
    /// </summary>
    [Fact]
    public void AFastBallThatLinksTwoStaticRocksInDifferentHomesTakesTheRestingFrameOfTheFarOneForBoth()
    {
        TwoRocks rocks = null!;
        FrameHandle farHome = FrameHandle.Null;
        int sharedTicks = 0;
        Flight without = Fly(numericFrames: false, threads: 1, (world, log) => FlyBetweenTwoRocks(world, log, out _), ticks: 480);
        Flight with = Fly(numericFrames: true, threads: 1, (world, log) => FlyBetweenTwoRocks(world, log, out rocks), ticks: 480, (world, scene) =>
        {
            if (farHome.IsNull)
            {
                farHome = world.PhysicsHomeOf(rocks.Far);
                world.Frames.Numeric(farHome).Should().Be(NumericMode.Resting, "the far rock lies 10,020 m out");
                world.PhysicsHomeOf(rocks.Near).Should().Be(scene.Station, "the near rock lies within 10 km of the origin");
            }
            FrameHandle ball = world.PhysicsHomeOf(scene.Mover);
            if (ball == world.PhysicsHomeOf(rocks.Far))
            {
                ball.Should().Be(farHome, "the group takes the resting frame that holds a static body");
                world.PhysicsHomeOf(rocks.Near).Should().Be(farHome, "the near rock joins its group's home");
                sharedTicks++;
            }
        });

        with.HomeKinds.Should().Equal(["Falling", "Resting", "Falling"]);
        sharedTicks.Should().BeGreaterThan(200, "the ball shares the rocks' home while it is within the reach of either");
        with.Seen.Should().BeEmpty();
        ShouldMatch(with, without, tolerance: 1e-3);
    }

    // ---- A move keeps the ghosts of the moved body -----------------------------------------------------------------

    /// <summary>
    /// A move between numeric frames keeps the moved body's ghosts in other systems, but not one in its new home. Only the
    /// ghost of an anchor lies in a home of its own semantic frame: here the hull of a still Kepler frame (see
    /// <see cref="Scenes.KeplerHull"/>), which a crate at 60 m/s passes 3 m from its center, so that the crate's falling
    /// frame keeps a ghost of the hull. Then the game hands the frame to the Physics driver. The hull is no anchor any more,
    /// the merge test links it to the crate, and the group keeps falling in the crate's frame, which takes the hull in,
    /// in the same batch that withdraws the place of its old ghost there. The move drops that ghost, and so does the
    /// withdrawal later in the batch, so no step runs with the hull and its ghost in one system.
    /// </summary>
    [Fact]
    public void AKeplerHullThatTheGameHandsToThePhysicsDriverJoinsTheFallingFrameThatKeptItsGhostWithoutTheGhost()
    {
        using World world = NewWorld(numericFrames: true, threads: 1, out FaultInjectingBackend faults);
        FrameHandle space = Scenes.StillSpace(world);
        Entity hull = Scenes.KeplerHull(world, space, 1000, new BodyDesc(new BoxShapeDesc(new Vector3(1)), Mass: 1, LinearDamping: 0, AngularDamping: 0));
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = space, Pose = At(950, 3), LinearVelocity = new Vector3(60, 0, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
        });
        Tick(world, 2);
        FrameHandle falling = world.PhysicsHomeOf(crate);
        world.Frames.Numeric(falling).Should().Be(NumericMode.Falling);
        faults.Jolt.HasGhostIn(hull, falling).Should().BeTrue("the crate's falling frame keeps a ghost of the Kepler hull");

        world.SetFrameDriver(world.GetOwnedFrame(hull), MotionDriver.Physics);
        TickUntil(world, () => world.PhysicsHomeOf(hull) == falling, 5);

        world.PhysicsHomeOf(hull).Should().Be(falling, "the merge test links the hull, which physics drives now, to the crate");
        faults.Jolt.HasGhostIn(hull, falling).Should().BeFalse("the hull is a real body of the frame's system");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        world.Tick();
        world.Errors.Should().BeEmpty();
    }

    // ---- Determinism ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The events and the homes of the crate of <see cref="ThrowAtAWall"/> and the ball of <see cref="GrazeARock"/>, as
    /// text, with numeric frames, on <paramref name="threads"/> gameplay threads and as many Jolt threads. Each line holds the
    /// tick and the bits of what it names.
    /// </summary>
    private static List<string> TraceOfTheAnchorScenes(int threads)
    {
        var lines = new List<string>();
        foreach (Func<World, CollisionLog, Scene> build in new Func<World, CollisionLog, Scene>[] { (world, log) => ThrowAtAWall(world, log, ThinWall), GrazeARock })
        {
            Flight run = Fly(numericFrames: true, threads, build, ticks: 420);
            lines.AddRange(run.Seen.Select(s => $"{s.Tick} collision {s.Other} {s.Impulse:R}"));
            lines.AddRange(run.Ended.Select(e => $"{e.Tick} ended {e.Other}"));
            lines.AddRange(run.Homes.Select(h => $"{h.Tick} home {h.Home} {BitConverter.DoubleToInt64Bits(h.Distance)}"));
            lines.Add($"end {BitConverter.DoubleToInt64Bits(run.Position.X)} {BitConverter.DoubleToInt64Bits(run.Position.Y)} {BitConverter.DoubleToInt64Bits(run.Velocity.X)}");
        }
        return lines;
    }

    [Fact]
    public void TheAnchorScenesGiveTheSameEventsAndHomesInEveryRunWithOneOrFourThreads()
    {
        List<string> first = TraceOfTheAnchorScenes(1);
        List<string> second = TraceOfTheAnchorScenes(1);
        List<string> parallel = TraceOfTheAnchorScenes(4);

        first.Count(line => line.Contains(" home ")).Should().Be(5, "the crate changes its home twice and the ball three times");
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }
}
