using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Anchors in the merge test of numeric frames (ter-dhkf, docs/plans/numeric-frames.md, "Anchors"), on the World and the
/// real backend. An anchor is a static body or the hull of a frame that physics does not drive. The merge test links an
/// anchor to every group, also to one that falls, and the group takes the anchor's home: an anchor counts as infinitely
/// heavy. So the anchor is a real body of the group's Jolt system, and Jolt sweeps a fast body against it. Before, a fast
/// body fell in a numeric frame of its own, slow there, and the frame kept a kinematic ghost of the anchor, which came at
/// the body fast. Jolt gives a kinematic body no linear cast, so the body passed through a thin wall (ter-ykqs). Each scene
/// runs with the default numeric frames and without numeric frames, and it must give the same events and the same motion
/// either way. Under the default rules, a group with a body that thrusts gets a falling numeric frame from 50 m/s relative
/// to its frame, a group of free bodies only from 1,000 m/s, and a slow group 2^23 m (8,389 km) or more from its frame's
/// origin gets a resting one. So the body that flies in each scene thrusts gently from its spawn on (see
/// <see cref="Along"/>
/// and <see cref="Across"/>): it counts as a body that thrusts from its thirtieth tick on, and from then on it falls when no
/// anchor holds it. Each scene gives the same thruster to its body in the run without numeric frames.
/// </summary>
public sealed class NumericFrameAnchorTests
{
    /// <summary>
    /// How many ticks of thrust at 60 Hz a body needs before it thrusts (see <see cref="NumericFrameOptions.ThrustOnset"/>):
    /// the first count of ticks of 1/60 s that reaches 0.5 s. A body with a thruster from its spawn on thrusts from the sync
    /// point of this tick on.
    /// </summary>
    private const int OnsetTicks = 30;

    // ---- A group that links two static anchors -------------------------------------------------------------------

    /// <summary>
    /// The distance from its frame's origin, in meters, from which a slow group gets a resting frame under the default rules
    /// (see <see cref="NumericFrameOptions.CreateDistance"/>): 2^23 m, about 8,389 km. The scenes of resting frames put their
    /// bodies a few meters on either side of it.
    /// </summary>
    private const double RestingDistance = 1 << 23;

    /// <summary>A static wall 10 cm thick, 10 m by 10 m, across the X axis.</summary>
    private static readonly BodyDesc ThinWall = new(Shape: new BoxShapeDesc(new(0.05f, 5, 5)),
        Motion: BodyMotion.Static);

    /// <summary>A static mesh wall without thickness, 10 m by 10 m, across the X axis, whose two triangles face -X.</summary>
    private static readonly BodyDesc ThinMeshWall = new(Shape: new MeshShapeDesc(
            [new(0, -5, -5), new(0, -5, 5), new(0, 5, 5), new(0, 5, -5)], [0, 1, 2, 0, 2, 3]),
        Motion: BodyMotion.Static,
        Mass: 0);

    /// <summary>A crate of 2 kg, a box of 20 cm that nothing slows down on its own and that does not bounce.</summary>
    private static readonly BodyDesc SmallCrate = new(Shape: new BoxShapeDesc(new(0.1f)), Mass: 2, Restitution: 0,
        LinearDamping: 0, AngularDamping: 0);

    /// <summary>A ball of 1 kg with a radius of 0.5 m that nothing slows down on its own and that does not bounce.</summary>
    private static readonly BodyDesc FlyingBall = new(Shape: new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0,
        LinearDamping: 0, AngularDamping: 0);

    /// <summary>A static rock, a sphere with a radius of 1 m.</summary>
    private static readonly BodyDesc Rock = new(Shape: new SphereShapeDesc(1), Motion: BodyMotion.Static);

    // ---- A fast crate thrown at a kinematic door ------------------------------------------------------------------

    /// <summary>A crate of 25 kg, a box of 50 cm that nothing slows down on its own and that does not bounce.</summary>
    private static readonly BodyDesc HeavyCrate = new(Shape: new BoxShapeDesc(new(0.25f)), Mass: 25, Restitution: 0,
        LinearDamping: 0, AngularDamping: 0);

    /// <summary>A kinematic door of 1 kg, 10 cm thick, 10 m by 10 m, across the X axis.</summary>
    private static readonly BodyDesc ThinDoor = new(Shape: new BoxShapeDesc(new(0.05f, 5, 5)),
        Motion: BodyMotion.Kinematic, Mass: 1);

    /// <summary>
    /// A gentle thruster (see <see cref="Thruster"/>) for a body of <paramref name="mass"/> kg that pushes it along +X, the
    /// flight of a crate thrown at a wall here, so that the body thrusts and presses the crate against the wall once it
    /// stops there. In a trial with a push across the flight instead, the crate did not stay at the mesh wall, which it hits
    /// where the wall's two triangles meet, but left it at 3 m/s. The thrust adds 1.7 µm/s to the velocity in a step, which
    /// a velocity of 60 m/s in the station's own system rounds away as a float, while a falling frame, where the crate is
    /// slow, keeps it. So the crate reaches the wall less than a millimeter farther with numeric frames, in the same step.
    /// </summary>
    private static Thruster Along(float mass) => new(mass);

    /// <summary>
    /// A gentle thruster (see <see cref="Thruster"/>) for a body of <paramref name="mass"/> kg that pushes it along +Z,
    /// across the flight of a ball that passes an anchor here, so that the body thrusts. Across the flight, the runs with
    /// and without numeric frames both keep the 1.7 µm/s that the thrust adds to the velocity in a step, so the ball moves
    /// alike to well within a millimeter. Along the flight, the station's own system would round it away (see
    /// <see cref="Along"/>).
    /// </summary>
    private static Thruster Across(float mass) => new(mass) { Direction = Vector3.UnitZ };

    /// <summary>
    /// A world on the wrapper with the default numeric frames or, when <paramref name="numericFrames"/> is false, with none.
    /// It runs on <paramref name="threads"/> gameplay threads, and its backend on as many Jolt threads.
    /// </summary>
    private static World NewWorld(bool numericFrames, int threads, out FaultInjectingBackend faults)
    {
        faults = new(new(new() { WorkerThreads = threads }));
        var options = new WorldOptions
        {
            MaxDegreeOfParallelism = threads, InitialCapacity = TestCapacity, DebugChecks = true, DisposePhysics = true
        };
        if (!numericFrames)
            options.NumericFrames = WithoutNumericFrames;
        return new(options, faults);
    }

    /// <summary>
    /// Space without gravity, and at its origin a station-like frame: a frame with no body and no
    /// gravity, with a bubble of radius <paramref name="radius"/> m.
    /// </summary>
    private static FrameHandle Station(World world, float radius = 1000)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        return world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = space, Pose = At(0), AsFrame = new(FrameBounds.Sphere(radius))
        }));
    }

    /// <summary>The bodies of a scene: the station, the body that flies, and the anchor that it meets.</summary>
    private sealed record Scene(FrameHandle Station, Entity Mover, Entity Anchor);

    /// <summary>
    /// What the body that flew did: its place and velocity in the station at the end, every Collision and CollisionEnded
    /// that it heard, and each change of its physics home: the tick, the kind of the new home (the station's own system,
    /// or the mode of a numeric frame), and its distance from the anchor's center then, in meters.
    /// </summary>
    private sealed record Flight(
        string Name,
        Double3 Position,
        Double3 Velocity,
        List<(long Tick, Entity Other, float Impulse)> Seen,
        List<(long Tick, Entity Other)> Ended,
        List<(long Tick, string Home, double Distance)> Homes)
    {
        /// <summary>The kinds of the homes, in order.</summary>
        public List<string> HomeKinds => [.. Homes.Select(change => change.Home)];
    }

    /// <summary>
    /// Builds a scene with <paramref name="build"/>, runs it for <paramref name="ticks"/> ticks, and calls
    /// <paramref name="watch"/> after each tick, when it is given. The mover carries <c>log</c>.
    /// </summary>
    private static Flight Fly(bool numericFrames, int threads, Func<World, CollisionLog, Scene> build, int ticks,
        Action<World, Scene>? watch = null)
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
                double distance = (world.PoseIn(scene.Mover, scene.Station).Position -
                                   world.PoseIn(scene.Anchor, scene.Station).Position).Length;
                homes.Add((world.TickNumber, home == scene.Station ? "station" : world.Frames.Numeric(home).ToString(),
                    distance));
                last = home;
            }

            watch?.Invoke(world, scene);
        }

        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        return new(Name: numericFrames ? "with numeric frames" : "without numeric frames",
            Position: world.PoseIn(scene.Mover, scene.Station).Position,
            Velocity: world.VelocityIn(scene.Mover, scene.Station), Seen: log.Seen, Ended: log.Ended, Homes: homes);
    }

    /// <summary>Checks that two runs of one scene, with and without numeric frames, heard the same events and moved alike.</summary>
    private static void ShouldMatch(Flight with, Flight without, double tolerance)
    {
        with.Seen.Select(s => (s.Tick, s.Other)).Should().Equal(without.Seen.Select(s => (s.Tick, s.Other)),
            "the body hears the same Collisions");
        with.Ended.Should().Equal(without.Ended, "the body hears the same CollisionEnded events");
        for (int i = 0; i < with.Seen.Count; i++)
            with.Seen[i].Impulse.Should()
                .BeApproximately(without.Seen[i].Impulse, Math.Abs(without.Seen[i].Impulse) * 0.02f + 1e-3f);
        (with.Position - without.Position).Length.Should().BeLessThan(tolerance, "the body moves alike");
        (with.Velocity - without.Velocity).Length.Should().BeLessThan(tolerance, "the body moves alike");
        without.HomeKinds.Should()
            .Equal(["station"], "without numeric frames the body stays in the station's own system");
    }

    // ---- A crate thrown at a thin wall ---------------------------------------------------------------------------

    /// <summary>
    /// A crate of 20 cm with a gentle thruster (see <see cref="Along"/>) flies along +X at 60 m/s, 1 m per step, at a static
    /// wall across the X axis at the station's origin, whose face toward the crate lies <c>face</c> m before the
    /// origin. It starts 200.5 m before the wall, so at the start of step 200 its center is 0.5 m before the wall, and at the
    /// end of it 0.5 m behind: no step ends with the crate touching the wall, and only a linear cast stops it. The crate
    /// thrusts from tick 30 on, 170 m before the wall, farther than the merge test reaches.
    /// </summary>
    private static Scene ThrowAtAWall(World world, CollisionLog log, BodyDesc wall)
    {
        FrameHandle station = Station(world);
        Entity anchor = world.Spawn(new() { Frame = station, Pose = At(0), Body = wall });
        Entity crate = world.Spawn(new()
        {
            Frame = station, Pose = At(-200.5), LinearVelocity = new Vector3(60, 0, 0), Body = SmallCrate,
            Components = [log, Along(SmallCrate.Mass)]
        });
        return new(station, crate, anchor);
    }

    /// <summary>
    /// A crate thrown at 60 m/s inside a station flies at a static wall that is thinner than one step of its motion, and
    /// stops at it, with numeric frames as without. With numeric frames the crate stays in the station's own system until
    /// it thrusts, from tick 30 on, and then falls in a numeric frame of its own until the merge test reaches the wall, 2 s
    /// ahead. Then it joins the wall's home, the station's own system, so that Jolt sweeps it against the real wall. Before
    /// ter-dhkf, the crate stayed in its falling frame, the wall's kinematic ghost came at it at 60 m/s, and the crate passed
    /// through the wall.
    /// </summary>
    [Theory]
    [InlineData(false, 0.05)]
    [InlineData(true, 0.0)]
    public void ACrateThrownAtAThinStaticWallJoinsTheWallsHomeAndStopsAtItAsItDoesWithoutNumericFrames(bool mesh,
        double face)
    {
        BodyDesc wall = mesh ? ThinMeshWall : ThinWall;
        Flight without = Fly(numericFrames: false, threads: 1, build: (world, log) => ThrowAtAWall(world, log, wall),
            ticks: 230);
        Flight with = Fly(numericFrames: true, threads: 1, build: (world, log) => ThrowAtAWall(world, log, wall),
            ticks: 230);

        foreach (Flight run in new[] { without, with })
        {
            run.Position.X.Should().BeApproximately(-face - 0.1, 0.03,
                "the crate stops with its front at the wall's face {0}", run.Name);
            run.Velocity.Length.Should().BeLessThan(0.05, "the crate stopped at the wall {0}", run.Name);
            run.Seen.Should().ContainSingle("the crate hears one Collision with the wall {0}", run.Name);
        }

        with.HomeKinds.Should().Equal(["station", "Falling", "station"],
            "the crate falls once it thrusts, until the merge test reaches the wall, then joins its home");
        with.Homes[1].Tick.Should()
            .Be(OnsetTicks, "the crate thrusts from the sync point of its thirtieth tick of thrust");
        with.Homes[2].Distance.Should().BeInRange(126, 128,
            "the merge test reaches the wall 2 s ahead: 120 m plus the bounding radii");
        with.Homes[2].Tick.Should()
            .BeLessThan(with.Seen[0].Tick, "the crate joins the wall's home before it hits the wall");
        ShouldMatch(with, without, tolerance: 0.01);
    }

    /// <summary>
    /// The crate of <see cref="ThrowAtAWall"/>, thrown at a wall of the station's interior instead of a body of its own:
    /// the station's owner has a static hull of 2 m in space and the thin wall as its interior shape, which physics keeps
    /// as a static body in the station's own system, across the X axis at the station's origin. No other body is in the
    /// station.
    /// </summary>
    private static Scene ThrowAtAnInteriorWall(World world, CollisionLog log)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        Entity owner = world.Spawn(new()
        {
            Frame = space,
            Pose = At(0),
            Body = new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Static, InteriorShape: ThinWall.Shape),
            AsFrame = new(FrameBounds.Sphere(1000))
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        Entity crate = world.Spawn(new()
        {
            Frame = station, Pose = At(-200.5), LinearVelocity = new Vector3(60, 0, 0), Body = SmallCrate,
            Components = [log, Along(SmallCrate.Mass)]
        });
        return new(station, crate, owner);
    }

    /// <summary>
    /// The interior of a frame owner is a static body of the frame's own system, so it is an anchor of that system, as a
    /// static wall of its own is. A crate thrown at 60 m/s at a thin wall of the interior falls in a numeric frame of its
    /// own once it thrusts, from tick 30 on, until the merge test reaches the interior's bounding sphere, 2 s ahead, and
    /// then joins the station's own system, where Jolt sweeps it against the wall. Before, the update had a body only for
    /// the hull of a frame owner, in the frame's parent, so the crate fell on in its own frame, whose system holds no copy
    /// of the interior, and passed through every wall of the cabin.
    /// </summary>
    [Fact]
    public void
        ACrateThrownAtAThinWallOfItsFramesInteriorJoinsTheFramesOwnSystemAndStopsAtItAsItDoesWithoutNumericFrames()
    {
        Flight without = Fly(numericFrames: false, threads: 1, build: ThrowAtAnInteriorWall, ticks: 230);
        Flight with = Fly(numericFrames: true, threads: 1, build: ThrowAtAnInteriorWall, ticks: 230);

        foreach (Flight run in new[] { without, with })
        {
            run.Position.X.Should().BeApproximately(-0.15, 0.03,
                "the crate stops with its front at the wall's face {0}", run.Name);
            run.Velocity.Length.Should().BeLessThan(0.05, "the crate stopped at the wall {0}", run.Name);
            run.Seen.Should().ContainSingle("the crate hears one Collision with the station's owner {0}", run.Name);
        }

        with.HomeKinds.Should().Equal(["station", "Falling", "station"],
            "the crate falls once it thrusts, until the merge test reaches the interior, then joins its home");
        with.Homes[1].Tick.Should()
            .Be(OnsetTicks, "the crate thrusts from the sync point of its thirtieth tick of thrust");
        with.Homes[2].Distance.Should().BeInRange(126, 128,
            "the merge test reaches the interior 2 s ahead: 120 m plus the bounding radii");
        with.Homes[2].Tick.Should()
            .BeLessThan(with.Seen[0].Tick, "the crate joins the interior's home before it hits the wall");
        ShouldMatch(with, without, tolerance: 0.01);
    }

    /// <summary>
    /// The crate of 25 kg with a gentle thruster (see <see cref="Along"/>) flies along +X at 60 m/s, 1 m per step, at a shut
    /// kinematic door that holds still at the station's origin. It starts 200.5 m before the door, so the steps end with its
    /// center 0.5 m before the door and 0.5 m behind it, and neither touches the door: only a linear cast stops it.
    /// </summary>
    private static Scene ThrowAtADoor(World world, CollisionLog log)
    {
        FrameHandle station = Station(world);
        Entity door = world.Spawn(new() { Frame = station, Pose = At(0), Body = ThinDoor });
        Entity crate = world.Spawn(new()
        {
            Frame = station, Pose = At(-200.5), LinearVelocity = new Vector3(60, 0, 0), Body = HeavyCrate,
            Components = [log, Along(HeavyCrate.Mass)]
        });
        return new(station, crate, door);
    }

    /// <summary>
    /// Jolt treats a kinematic body as infinitely heavy in a contact, so a door stops a crate as a static wall does. In the
    /// merge test it is infinitely heavy too: no contact changes its velocity, so the group of the crate and the door takes
    /// the door's velocity, zero, and lives in the station's own system, where Jolt sweeps the crate against the door.
    /// Before, the group weighed the door by its 1 kg and moved at 57.7 m/s, so it fell in a numeric frame where the door
    /// came at the crate at 57.7 m/s. Jolt gives a kinematic body no linear cast, so the door passed through the crate.
    /// The crate thrusts from tick 30 on, so it falls from then until the merge test reaches the door.
    /// </summary>
    [Fact]
    public void ACrateThrownAtAStillKinematicDoorRunsInTheDoorsSystemAndStopsAtItAsItDoesWithoutNumericFrames()
    {
        Flight without = Fly(numericFrames: false, threads: 1, build: ThrowAtADoor, ticks: 230);
        Flight with = Fly(numericFrames: true, threads: 1, build: ThrowAtADoor, ticks: 230);

        foreach (Flight run in new[] { without, with })
        {
            run.Position.X.Should().BeApproximately(-0.3, 0.03, "the crate stops with its front at the door's face {0}",
                run.Name);
            run.Velocity.Length.Should().BeLessThan(0.05, "the crate stopped at the door {0}", run.Name);
            run.Seen.Should().ContainSingle("the crate hears one Collision with the door {0}", run.Name);
        }

        with.HomeKinds.Should().Equal(["station", "Falling", "station"],
            "the crate falls once it thrusts, until the merge test reaches the door, then joins its system");
        with.Homes[1].Tick.Should()
            .Be(OnsetTicks, "the crate thrusts from the sync point of its thirtieth tick of thrust");
        with.Homes[2].Distance.Should().BeInRange(126, 129,
            "the merge test reaches the door 2 s ahead: 120 m plus the bounding radii");
        ShouldMatch(with, without, tolerance: 0.01);
    }

    // ---- A fast body that passes an anchor -----------------------------------------------------------------------

    /// <summary>
    /// A ball of radius 0.5 m with a gentle thruster (see <see cref="Across"/>) flies along +X at 80 m/s from 300 m before a
    /// static rock of radius 1 m at the station's origin, 1.4 m to the side of its center, so that it grazes the rock and
    /// flies on at about 70 m/s. It thrusts from tick 30 on, 260 m before the rock.
    /// </summary>
    private static Scene GrazeARock(World world, CollisionLog log)
    {
        FrameHandle station = Station(world);
        Entity rock = world.Spawn(new() { Frame = station, Pose = At(0), Body = Rock });
        Entity ball = world.Spawn(new()
        {
            Frame = station, Pose = At(-300, 1.4), LinearVelocity = new Vector3(80, 0, 0), Body = FlyingBall,
            Components = [log, Across(FlyingBall.Mass)]
        });
        return new(station, ball, rock);
    }

    /// <summary>
    /// A fast ball that thrusts falls in a numeric frame of its own from tick 30 on, and joins the home of a static rock that
    /// it grazes, the station's own system, as soon as the merge test reaches the rock, 2 s ahead: its bounding radius, the
    /// rock's, and 80 m/s for 2 s, 161.5 m. It runs there for the encounter, grazes the rock, and flies on. When the merge
    /// test no longer links them, with the split margin of 10 m of two bodies in one system, it lets go and falls in a
    /// numeric frame again. It hears the same events and moves alike, with numeric frames as without.
    /// </summary>
    [Fact]
    public void AFastBallThatGrazesAStaticRockRunsInTheRocksHomeForTheEncounterAndThenFallsAgain()
    {
        Flight without = Fly(numericFrames: false, threads: 1, build: GrazeARock, ticks: 420);
        Flight with = Fly(numericFrames: true, threads: 1, build: GrazeARock, ticks: 420);

        with.Seen.Should().ContainSingle("the ball grazes the rock once");
        with.Ended.Should().ContainSingle();
        with.HomeKinds.Should().Equal("station", "Falling", "station", "Falling");
        with.Homes[1].Tick.Should()
            .Be(OnsetTicks, "the ball thrusts from the sync point of its thirtieth tick of thrust");
        with.Homes[2].Distance.Should().BeInRange(160, 162, "the merge test reaches the rock 2 s ahead");
        with.Homes[2].Tick.Should().BeLessThan(with.Seen[0].Tick);
        with.Homes[3].Tick.Should().BeGreaterThan(with.Ended[0].Tick);
        with.Velocity.Length.Should().BeGreaterThan(50, "the ball flies on fast enough to fall again");
        // After the graze only the gentle thrust changes the ball's velocity, by 0.5 mm/s in the 5 s that remain, so the
        // reach that lets go is the one at the end.
        double reach = 1.5 + with.Velocity.Length * 2 + 10;
        with.Homes[3].Distance.Should().BeInRange(reach, reach + with.Velocity.Length / 60 + 0.1,
            "the ball lets go at the first sync point beyond the reach of the merge test and the split margin");
        ShouldMatch(with, without, tolerance: 1e-3);
    }

    /// <summary>
    /// A frame that turns in place at the station's origin with a <see cref="SpinMotion"/>: a kinematic box hull of 10 m by
    /// 6 m by 30 m that turns at 0.5 rad/s about Y, inside a bubble of radius 20 m. A ball of radius 0.5 m with a gentle thruster (see
    /// <see cref="Across"/>) flies past
    /// along +X at 80 m/s, 30 m to the side of the hull's center, outside the bubble, so that it neither enters the frame
    /// nor touches the hull. It thrusts from tick 30 on, 260 m before the hull.
    /// </summary>
    private static Scene PassATurntable(World world, CollisionLog log)
    {
        FrameHandle station = Station(world);
        Entity turntable = world.Spawn(new SpawnDesc
        {
            Frame = station,
            Pose = At(0),
            Body = new(Shape: new BoxShapeDesc(new(5, 3, 15)), Motion: BodyMotion.Kinematic),
            AsFrame = new(FrameBounds.Sphere(20))
        }.Spinning(world, new(Tilt: DQuaternion.Identity, Rate: 0.5)));
        Entity ball = world.Spawn(new()
        {
            Frame = station, Pose = At(-300, 30), LinearVelocity = new Vector3(80, 0, 0), Body = FlyingBall,
            Components = [log, Across(FlyingBall.Mass)]
        });
        return new(station, ball, turntable);
    }

    /// <summary>
    /// The hull of a Spin frame is an anchor whose center rests in the system of the frame's semantic parent. A fast ball
    /// that thrusts falls from tick 30 on, joins that system for the encounter, where the turning hull is a real body, and
    /// falls again after, with the same motion as without numeric frames.
    /// </summary>
    [Fact]
    public void AFastBallThatPassesTheHullOfASpinFrameRunsInTheHullsHomeForTheEncounterAndThenFallsAgain()
    {
        Flight without = Fly(numericFrames: false, threads: 1, build: PassATurntable, ticks: 480);
        Flight with = Fly(numericFrames: true, threads: 1, build: PassATurntable, ticks: 480);

        with.HomeKinds.Should().Equal("station", "Falling", "station", "Falling");
        with.Homes[1].Tick.Should()
            .Be(OnsetTicks, "the ball thrusts from the sync point of its thirtieth tick of thrust");
        with.Seen.Should().BeEmpty("the ball passes outside the turntable's bubble");
        ShouldMatch(with, without, tolerance: 1e-3);
    }

    /// <summary>The rocks of <see cref="FlyBetweenTwoRocks"/>.</summary>
    private sealed record TwoRocks(Entity Near, Entity Far);

    /// <summary>
    /// A station with a bubble of radius 9,000 km. Two static rocks of radius 1 m lie on the X axis, 40 m apart: the near one
    /// 30 m nearer to the station's origin than the <see cref="RestingDistance"/> at which a slow body gets a resting frame,
    /// so it stays in the station's own system, and the far one 10 m farther, in a resting frame of its own. A ball with a
    /// gentle thruster (see <see cref="Across"/>) flies between them along +Y at 80 m/s, from 300 m before their line, 20 m
    /// from each. Its path lies 10 m nearer than the resting distance, so it stays in the station's own system until it
    /// thrusts, from tick 30 on, 260 m before the rocks.
    /// </summary>
    private static Scene FlyBetweenTwoRocks(World world, CollisionLog log, out TwoRocks rocks)
    {
        FrameHandle station = Station(world, radius: 9e6f);
        Entity near = world.Spawn(new() { Frame = station, Pose = At(RestingDistance - 30), Body = Rock });
        Entity far = world.Spawn(new() { Frame = station, Pose = At(RestingDistance + 10), Body = Rock });
        Entity ball = world.Spawn(new()
        {
            Frame = station, Pose = At(RestingDistance - 10, -300), LinearVelocity = new Vector3(0, 80, 0),
            Body = FlyingBall,
            Components = [log, Across(FlyingBall.Mass)]
        });
        rocks = new(near, far);
        return new(station, ball, far);
    }

    /// <summary>
    /// A fast ball that links two static rocks whose homes differ takes one home for all three: the far rock's resting
    /// frame, the frame that holds a static body of the group. The near rock moves into it for the encounter, so both rocks
    /// are real bodies of the ball's system, and no rock has a ghost. Before and after, the ball thrusts and falls in a
    /// numeric frame of its own, with the same motion as without numeric frames.
    /// </summary>
    [Fact]
    public void AFastBallThatLinksTwoStaticRocksInDifferentHomesTakesTheRestingFrameOfTheFarOneForBoth()
    {
        TwoRocks rocks = null!;
        FrameHandle farHome = FrameHandle.Null;
        int sharedTicks = 0;
        Flight without = Fly(numericFrames: false, threads: 1,
            build: (world, log) => FlyBetweenTwoRocks(world, log, out _),
            ticks: 480);
        Flight with = Fly(numericFrames: true, threads: 1,
            build: (world, log) => FlyBetweenTwoRocks(world, log, out rocks),
            ticks: 480, watch: (world, scene) =>
            {
                if (farHome.IsNull)
                {
                    farHome = world.PhysicsHomeOf(rocks.Far);
                    world.Frames.Numeric(farHome).Should().Be(NumericMode.Resting,
                        "the far rock lies 10 m beyond the resting distance");
                    world.PhysicsHomeOf(rocks.Near).Should().Be(scene.Station,
                        "the near rock lies 30 m within the resting distance");
                }

                FrameHandle ball = world.PhysicsHomeOf(scene.Mover);
                if (ball == world.PhysicsHomeOf(rocks.Far))
                {
                    ball.Should().Be(farHome, "the group takes the resting frame that holds a static body");
                    world.PhysicsHomeOf(rocks.Near).Should().Be(farHome, "the near rock joins its group's home");
                    sharedTicks++;
                }
            });

        with.HomeKinds.Should().Equal("station", "Falling", "Resting", "Falling");
        with.Homes[1].Tick.Should()
            .Be(OnsetTicks, "the ball thrusts from the sync point of its thirtieth tick of thrust");
        sharedTicks.Should()
            .BeGreaterThan(200, "the ball shares the rocks' home while it is within the reach of either");
        with.Seen.Should().BeEmpty();
        ShouldMatch(with, without, tolerance: 1e-3);
    }

    /// <summary>The rock and the owner of the planetoid of <see cref="FlyBetweenTheInteriorAndARock"/>.</summary>
    private sealed record Planetoid(Entity Owner, Entity Rock);

    /// <summary>
    /// A planetoid: a frame whose owner has a static hull of 2 m in space and as its interior a
    /// static sphere whose surface lies 10 m within the <see cref="RestingDistance"/>, at the frame's origin, with a bubble
    /// of radius 9,000 km. A static rock of radius 1 m lies on the X axis 20 m beyond the resting distance, 30 m above the
    /// interior's surface, in a resting frame of its own. A ball with a gentle thruster (see <see cref="Across"/>) flies
    /// between them along +Y at 80 m/s, from 300 m before their line, 15 m from the surface and from the rock's center.
    /// </summary>
    private static Scene FlyBetweenTheInteriorAndARock(World world, CollisionLog log, out Planetoid planetoid)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        Entity owner = world.Spawn(new()
        {
            Frame = space,
            Pose = At(0),
            Body = new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Static,
                InteriorShape: new SphereShapeDesc((float)(RestingDistance - 10))),
            AsFrame = new(FrameBounds.Sphere(9e6f))
        });
        FrameHandle frame = world.GetOwnedFrame(owner);
        Entity rock = world.Spawn(new() { Frame = frame, Pose = At(RestingDistance + 20), Body = Rock });
        Entity ball = world.Spawn(new()
        {
            Frame = frame, Pose = At(RestingDistance + 5, -300), LinearVelocity = new Vector3(0, 80, 0),
            Body = FlyingBall,
            Components = [log, Across(FlyingBall.Mass)]
        });
        planetoid = new(owner, rock);
        return new(frame, ball, rock);
    }

    /// <summary>
    /// The interior of a frame owner never leaves the frame's own system, because physics keeps it there, so it fixes its
    /// group there, as the hull of a Static frame does. The ball links the interior all along its flight, so it stays in
    /// the frame's own system, although it lies beyond the resting distance and thrusts at 80 m/s from tick 30 on, and the
    /// static rock that it passes joins that system for the encounter, although it lies in a resting frame. Two static
    /// rocks would take the resting frame of the far one (see
    /// <see cref="AFastBallThatLinksTwoStaticRocksInDifferentHomesTakesTheRestingFrameOfTheFarOneForBoth"/>).
    /// </summary>
    [Fact]
    public void AFastBallThatLinksTheInteriorOfAFrameAndAStaticRockInAFarRestingFrameTakesTheInteriorsHomeForBoth()
    {
        Planetoid planetoid = null!;
        FrameHandle rockHome = FrameHandle.Null;
        int sharedTicks = 0;
        Flight without = Fly(numericFrames: false, threads: 1,
            build: (world, log) => FlyBetweenTheInteriorAndARock(world, log, out _), ticks: 480);
        Flight with = Fly(numericFrames: true, threads: 1,
            build: (world, log) => FlyBetweenTheInteriorAndARock(world, log, out planetoid), ticks: 480,
            watch: (world, scene) =>
            {
                if (rockHome.IsNull)
                {
                    rockHome = world.PhysicsHomeOf(planetoid.Rock);
                    world.Frames.Numeric(rockHome).Should().Be(NumericMode.Resting,
                        "the rock lies 20 m beyond the resting distance");
                }

                world.PhysicsHomeOf(planetoid.Owner).Should()
                    .Be(world.Frames.TreeParent(scene.Station), "the hull stays in space");
                if (world.PhysicsHomeOf(planetoid.Rock) == scene.Station)
                    sharedTicks++;
            });

        with.HomeKinds.Should().Equal(["station"], "the interior keeps the ball in the frame's own system");
        sharedTicks.Should()
            .BeGreaterThan(200, "the rock joins the interior's home while the ball is within its reach");
        with.Seen.Should().BeEmpty();
        ShouldMatch(with, without, tolerance: 1e-3);
    }

    /// <summary>
    /// A frame owner has two bodies in the numeric update: its hull in the frame's parent and its interior in the frame's
    /// own system. A ship's hull 5 m beyond the <see cref="RestingDistance"/>, slow, needs a resting frame by itself, but a
    /// static rock 10 m within it, which stays in the system of space, keeps it there, so the hull gets a leave clock. The
    /// tree's invariants find the hull's clock on the hull, not on the interior, which is an anchor and has none. The clock
    /// stays at zero while the rock keeps the hull. The rock goes at the sync point of tick 4. From then on the hull's group
    /// would rest in a new frame, so the clock counts, and it holds the hull in the system of space until it reaches the
    /// split cooldown of 1 s, at the sixtieth sync point: the hull takes the resting frame that it needs at tick 63.
    /// </summary>
    [Fact]
    public void AShipsHullThatAStaticRockKeepsInTheSystemOfSpaceHasALeaveClockThatTheInvariantsFindOnTheHull()
    {
        using World world = NewWorld(numericFrames: true, threads: 1, out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));
        Entity rock = world.Spawn(new() { Frame = space, Pose = At(RestingDistance - 10), Body = Rock });
        Scenes.ShipScene ship =
            Scenes.ShipWithInterior(world: world, space: space, position: new(RestingDistance + 5, 0, 0));

        Tick(world, 3);

        world.PhysicsHomeOf(ship.Hull).Should().Be(space, "the rock keeps the ship's hull in the system of space");
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        world.Destroy(rock);
        TickUntil(world, () => world.PhysicsHomeOf(ship.Hull) != space, 120);

        world.Frames.Numeric(world.PhysicsHomeOf(ship.Hull)).Should().Be(NumericMode.Resting,
            "without the rock the hull rests in a numeric frame");
        world.TickNumber.Should().Be(63,
            "the hull's clock holds it in the system of space for 60 sync points after the rock went");
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
    }

    // ---- A move keeps the ghosts of the moved body -----------------------------------------------------------------

    /// <summary>
    /// A move between numeric frames keeps the moved body's ghosts in other systems, but not one in its new home. Only the
    /// ghost of an anchor lies in a home of its own semantic frame: here the hull of a Kepler frame that flies at 2 km/s in
    /// a following frame of its own past a static rock 8 m to the side of its path (see <see cref="Scenes.FastKeplerHull"/>),
    /// so that the system of space, the rock's home, keeps a ghost of the hull. Then the game parks the hull: in one tick
    /// its owner removes its <see cref="KeplerMotion"/> and makes the hull static. At the sync point of that tick the hull
    /// is a fixed anchor in the system of space, and the numeric update moves it there, with its frame, in the same batch
    /// that withdraws the place of its old ghost there. The move drops that ghost, and so does the withdrawal later in the
    /// batch, so no step runs with the hull and its ghost in one system. Before ter-dhkf rule 3, the scene was a still
    /// Kepler hull whose ghost a falling crate's frame kept, and which joined that frame when the game made its hull
    /// dynamic.
    /// </summary>
    [Fact]
    public void AKeplerHullThatTheGameParksJoinsTheSystemThatKeptItsGhostWithoutTheGhost()
    {
        using World world = NewWorld(numericFrames: true, threads: 1, out FaultInjectingBackend faults);
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity hull = Scenes.FastKeplerHull(world, space,
            new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Kinematic, Mass: 1, LinearDamping: 0,
                AngularDamping: 0));
        world.Spawn(new() { Frame = space, Pose = At(5_008, 0, -100), Body = Rock });
        Tick(world, 2);
        FrameHandle orbit = world.PhysicsHomeOf(hull);
        world.Frames.Numeric(orbit).Should().Be(NumericMode.Following);
        faults.Jolt.HasGhostIn(hull, space).Should().BeTrue("the rock's home keeps a ghost of the Kepler hull");

        world.SetMotionNextTick(owner: hull, motion: BodyMotion.Static, remove: world.GetComponent<KeplerMotion>(hull));
        world.Tick();

        world.PhysicsHomeOf(hull).Should().Be(space, "a static hull lives in its semantic frame's own system");
        world.Frames.IsAlive(orbit).Should().BeFalse("the following frame lost its frame and its hull");
        world.Tick();
        faults.Jolt.HasGhostIn(hull, space).Should().BeFalse("the hull is a real body of the system");
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
        foreach (Func<World, CollisionLog, Scene> build in new[]
                     { (world, log) => ThrowAtAWall(world, log, ThinWall), GrazeARock })
        {
            Flight run = Fly(numericFrames: true, threads: threads, build: build, ticks: 420);
            lines.AddRange(run.Seen.Select(s => $"{s.Tick} collision {s.Other} {s.Impulse:R}"));
            lines.AddRange(run.Ended.Select(e => $"{e.Tick} ended {e.Other}"));
            lines.AddRange(
                run.Homes.Select(h => $"{h.Tick} home {h.Home} {BitConverter.DoubleToInt64Bits(h.Distance)}"));
            lines.Add(
                $"end {BitConverter.DoubleToInt64Bits(run.Position.X)} {BitConverter.DoubleToInt64Bits(run.Position.Y)} {BitConverter.DoubleToInt64Bits(run.Velocity.X)}");
        }

        return lines;
    }

    [Fact]
    public void TheAnchorScenesGiveTheSameEventsAndHomesInEveryRunWithOneOrFourThreads()
    {
        List<string> first = TraceOfTheAnchorScenes(1);
        List<string> second = TraceOfTheAnchorScenes(1);
        List<string> parallel = TraceOfTheAnchorScenes(4);

        first.Count(line => line.Contains(" home ")).Should().Be(7,
            "the crate has three homes, the station, its falling frame and the station, and the ball four, the station and then a falling frame twice");
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }
}
