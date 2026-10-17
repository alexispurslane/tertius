using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Orbit frames (ter-dhkf, docs/plans/numeric-frames.md, "Anchors", rule 3), on the World and the real backend. The hull of
/// a frame whose Kepler driver moves it fast or far in its semantic frame lives in a numeric frame K of its own that
/// follows the orbit, and the frame hangs at K's origin, as a ship's frame hangs under its hull's numeric frame. So the
/// hull rests in K, and the bodies near it join K, where they are slow, and where the hull is a real body that Jolt
/// sweeps them against. Each scene runs with the default numeric frames, under which a body that flies at 50 m/s or more
/// relative to its frame gets a numeric frame and a slow body 10 km or more from its frame's origin gets one too, and
/// without numeric frames, and it must give the same events and the same motion either way, apart from rounding.
/// </summary>
public partial class NumericFrameOrbitTests
{
    private const double Dt = 1.0 / 60;

    /// <summary>Membership rules under which no group of bodies and no hull ever gets a numeric frame.</summary>
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

    /// <summary>A circular low orbit of 6,800 km round earth, at about 7.66 km/s, whose frame keeps the axes of earth.</summary>
    private static readonly KeplerOrbit LowOrbit = KeplerOrbit.Circular(6.8e6, phase: 0.4);

    /// <summary>The radius of the station's bubble, in meters.</summary>
    private const float BubbleRadius = 16;

    /// <summary>
    /// The station's hull: a core, a box of 4 m at the frame's origin, and a plate of 16 m by 0.5 m by 16 m outside the
    /// bubble, across the +Y axis, whose face toward the core is 19.75 m from it. Its frame's Kepler driver moves it, so
    /// physics keeps it kinematic.
    /// </summary>
    private static readonly BodyDesc StationHull = new(new CompoundShapeDesc([
        new CompoundChild(new BoxShapeDesc(new Vector3(2)), new Pose(Vector3.Zero, Quaternion.Identity)),
        new CompoundChild(new BoxShapeDesc(new Vector3(8, 0.25f, 8)), new Pose(new Vector3(0, 20, 0), Quaternion.Identity)),
    ]), Mass: 1e5f, Restitution: 0);

    /// <summary>A crate, a box of 50 cm and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.25f)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A rock, a ball of 1 kg with a radius of 0.5 m that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc Rock = new(new SphereShapeDesc(0.5f), Mass: 1, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>Earth, the station's frame and the station's owner, whose body is the station's hull.</summary>
    private sealed record Station(FrameHandle Earth, Entity Owner, FrameHandle Frame, KeplerOrbit Orbit)
    {
        /// <summary>The place and velocity of the station on its orbit at <paramref name="time"/>, in earth.</summary>
        public (Double3 Position, Double3 Velocity) StateAt(double time) => Orbit.StateAt(time, Scenes.EarthMu);
    }

    /// <summary>Earth with its point gravity, and in it a station on <paramref name="orbit"/>, or on <see cref="LowOrbit"/>.</summary>
    private static Station KeplerStation(World world, KeplerOrbit? orbit = null)
    {
        KeplerOrbit path = orbit ?? LowOrbit;
        FrameHandle earth = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(Scenes.EarthMu)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = earth, Body = StationHull, AsFrame = new FrameSpec(FrameBounds.Sphere(BubbleRadius), MotionDriver.Kepler, Orbit: path),
        });
        return new Station(earth, owner, world.GetOwnedFrame(owner), path);
    }

    /// <summary>
    /// The kind of the physics home of <paramref name="entity"/>: "station" for the station's own system, "earth" for
    /// earth's own system, or the mode of a numeric frame.
    /// </summary>
    private static string HomeKind(World world, Station station, Entity entity)
    {
        FrameHandle home = world.PhysicsHomeOf(entity);
        return home == station.Frame ? "station" : home == station.Earth ? "earth" : world.Frames.Numeric(home).ToString();
    }

    // ---- The hull alone ------------------------------------------------------------------------------------------

    /// <summary>
    /// The station's hull, alone on its orbit at 7.66 km/s, gets an orbit frame at the first sync point and rests in it, and
    /// the station's frame hangs at its origin. Every read of the hull in earth is the orbit's state: the place to the last
    /// bit, over ten minutes, and the velocity to within the float that the row keeps of the frame's small velocity in K.
    /// Without numeric frames the hull stays in earth's own system, and the row keeps the whole velocity as a float.
    /// </summary>
    [Fact]
    public void AKeplerStationsHullAloneAtOrbitalSpeedRestsInAnOrbitFrameAndEveryReadInEarthIsTheOrbitsState()
    {
        using World world = NewWorld(numericFrames: true, threads: 1, out _);
        Station station = KeplerStation(world);
        world.Tick();
        FrameHandle home = world.PhysicsHomeOf(station.Owner);
        world.Frames.Numeric(home).Should().Be(NumericMode.Orbiting);
        world.Frames.TreeParent(home).Should().Be(station.Earth);
        world.Frames.TreeParent(station.Frame).Should().Be(home, "the frame hangs under its hull's home");
        world.Frames.Parent(station.Frame).Should().Be(station.Earth, "gameplay sees the semantic tree");
        world.GetFrame(station.Owner).Should().Be(station.Earth);

        double worstVelocity = 0;
        for (int tick = 0; tick < 36_000; tick++)
        {
            world.Tick();
            (Double3 position, Double3 velocity) = station.StateAt(world.Time);
            DPose pose = world.PoseIn(station.Owner, station.Earth);
            if (pose.Position != position)
                pose.Position.Should().Be(position, "at tick {0} the hull is where its orbit is", world.TickNumber);
            worstVelocity = Math.Max(worstVelocity, (world.VelocityIn(station.Owner, station.Earth) - velocity).Length);
        }
        world.PhysicsHomeOf(station.Owner).Should().Be(home, "the hull keeps its orbit frame");
        worstVelocity.Should().BeLessThan(1e-8, "the row keeps the frame's velocity relative to K, about 7 cm/s, as a float");
        world.PoseIn(station.Owner, station.Earth).Rotation.Should().Be(station.Orbit.RotationAt(world.Time, Scenes.EarthMu));
        world.Errors.Should().BeEmpty();
    }

    // ---- Crates that drift out of the station ----------------------------------------------------------------------

    /// <summary>
    /// A cargo pod: a frame with the Physics driver and a bubble of 1 cm, whose hull is a box of 50 cm and 2 kg that
    /// nothing slows down and that does not bounce. A hull meets only static bodies and other hulls (see ARCHITECTURE.md,
    /// "Collision groups"), so only a hull bumps the station's hull outside its bubble; a crate meets the station's
    /// interior inside the bubble instead.
    /// </summary>
    private static readonly FrameSpec PodFrame = new(FrameBounds.Sphere(0.01f), MotionDriver.Physics);

    /// <summary>
    /// What one body did: its homes in order, the ticks at which it heard a Collision with the station's hull, its place in
    /// the station at the end, its place in earth relative to the hull's place in earth at the end, and its fastest velocity
    /// relative to an orbit frame that held it.
    /// </summary>
    private sealed record Drift(List<string> Homes, List<long> Bumps, Double3 Position, Double3 OffsetInEarth, double FastestInOrbit);

    /// <summary>
    /// Two crates and a cargo pod float in the station 10 m along +Y from its core, 3 m apart, and drift along +Y at 1 m/s
    /// relative to it. They leave the bubble of 16 m and drift on toward the plate of the hull, whose face is 19.75 m from
    /// the core: the pod at x = 0, the crates at x = -3 and 3. Runs for <paramref name="ticks"/> ticks and returns the
    /// crates, then the pod.
    /// </summary>
    private static Drift[] DriftOut(bool numericFrames, int threads, int ticks)
    {
        using World world = NewWorld(numericFrames, threads, out FaultInjectingBackend faults);
        Station station = KeplerStation(world);
        var logs = new[] { new CollisionLog(), new CollisionLog(), new CollisionLog() };
        Entity[] drifters = [.. new[] { -3.0, 3.0, 0.0 }.Select((x, i) => world.Spawn(new SpawnDesc
        {
            Frame = station.Frame, Pose = At(x, 10), LinearVelocity = new Double3(0, 1, 0), Body = Crate, Components = [logs[i]],
            AsFrame = i == 2 ? PodFrame : null,
        }))];
        var homes = drifters.Select(_ => new List<string>()).ToArray();
        var fastest = new double[drifters.Length];
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            for (int i = 0; i < drifters.Length; i++)
            {
                string kind = HomeKind(world, station, drifters[i]);
                if (homes[i].Count == 0 || homes[i][^1] != kind)
                    homes[i].Add(kind);
                FrameHandle home = world.PhysicsHomeOf(drifters[i]);
                if (world.Frames.Numeric(home) == NumericMode.Orbiting)
                    fastest[i] = Math.Max(fastest[i], (world.VelocityIn(drifters[i], station.Earth) - world.Frames.LinearVelocity(home)).Length);
            }
        }
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        return [.. drifters.Select((drifter, i) => new Drift(homes[i], [.. logs[i].Seen.Where(s => s.Other == station.Owner).Select(s => s.Tick)],
            world.PoseIn(drifter, station.Frame).Position,
            world.PoseIn(drifter, station.Earth).Position - world.PoseIn(station.Owner, station.Earth).Position, fastest[i]))];
    }

    /// <summary>
    /// The crates and the pod leave the station's bubble into the station's tree parent, its orbit frame K, and stay there,
    /// linked to the hull, at local velocities near their 1 m/s. The pod bumps the hull's plate as a real body of K's
    /// system at the tick that straight-line motion relative to the station predicts: the face of the pod, 0.25 m above its
    /// center, reaches the plate's face at 19.75 m after 9.5 s, at tick 570. The crates pass the plate, which they never
    /// meet outside the bubble. Without numeric frames they all leave into earth's own system at 7.66 km/s, and the pod
    /// bumps the hull there at the same tick. A body leaves with the velocity that it had relative to the station at the end
    /// of the step, as the hand-off gives it; the scheme of the step then adds half a step of earth's pull, about 7 cm/s,
    /// to its motion relative to the hull, across the line of its drift, with numeric frames as without.
    /// </summary>
    [Fact]
    public void CratesThatDriftOutOfTheStationsBubbleJoinItsOrbitFrameAndAPodBumpsTheHullAtTheTickThatItsDriftPredicts()
    {
        Drift[] with = DriftOut(numericFrames: true, threads: 1, ticks: 700);
        Drift[] without = DriftOut(numericFrames: false, threads: 1, ticks: 700);

        for (int i = 0; i < with.Length; i++)
        {
            with[i].Homes.Should().Equal(["station", "Orbiting"], "the body leaves into the station's tree parent and stays linked to the hull");
            without[i].Homes.Should().Equal(["station", "earth"]);
            with[i].FastestInOrbit.Should().BeInRange(0.9, 1.1, "the body is slow in the orbit frame");
            with[i].Bumps.Should().Equal(without[i].Bumps, "the body hears the same Collisions with numeric frames as without");
        }
        with[0].Bumps.Should().BeEmpty("a crate never meets a hull outside the bubble");
        with[1].Bumps.Should().BeEmpty();
        for (int i = 0; i < 2; i++)
        {
            (with[i].Position - new Double3(i == 0 ? -3 : 3, 10 + 700.0 / 60, 0)).Length.Should().BeLessThan(0.01,
                "in the orbit frame the crate drifts on in a straight line relative to the station, 1 m/s for 700 ticks");
            // Without numeric frames each step's pull of 0.14 m/s rounds in a float velocity of 7.66 km/s, whose step is
            // 0.5 mm/s, for the 5 s after the crate leaves the bubble.
            (with[i].Position - without[i].Position).Length.Should().BeLessThan(0.05, "the crate moves alike");
        }
        with[2].Bumps.Should().NotBeEmpty("the pod's hull hits the station's plate");
        with[2].Bumps[0].Should().BeInRange(568, 571, "the drift of 1 m/s takes the pod's face to the plate in 9.5 s");
        // In the orbit frame the pod and the plate meet at 1 m/s, and the pod comes to rest on the plate. Without numeric
        // frames they meet at 7.66 km/s in earth's own system, where the floats of the contact step by 0.5 mm/s, and the
        // pod slides along the plate: after the bump precision is the point, and only the bump's tick is the same.
        (with[2].Position - new Double3(0, 19.5, 0)).Length.Should().BeLessThan(0.01, "the pod rests against the plate where it hit it");
        // The pod falls asleep on the plate within the 2 s after the bump. A sleeping body of an orbit frame moves with the
        // frame, so its place in earth moves with the hull's.
        (with[2].OffsetInEarth - new Double3(0, 19.5, 0)).Length.Should().BeLessThan(0.01, "a read in earth puts the pod beside the hull");
    }

    /// <summary>
    /// A frame on a circular orbit of 384,000 km round earth, at about 1 km/s, whose hull is a box of 4 m with a bubble of
    /// 1 cm, and a crate at rest relative to it, 3.5 m below its core, within the reach of the merge test. The crate joins
    /// the hull's orbit frame and falls asleep there: the tidal pull at 3.5 m is 7e-11 m/s², far below what wakes it. (In
    /// low orbit it is 1e-5 m/s², which keeps a crate awake.) A sleeping body of an orbit frame moves with the frame, so physics reports it, and a read in
    /// earth puts it beside the hull a second later, when the orbit frame has moved 1 km.
    /// </summary>
    [Fact]
    public void ACrateAtRestBesideAKeplerHullSleepsInTheOrbitFrameAndMovesWithItInEarth()
    {
        using World world = NewWorld(numericFrames: true, threads: 1, out FaultInjectingBackend faults);
        FrameHandle earth = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(Scenes.EarthMu)));
        KeplerOrbit far = KeplerOrbit.Circular(3.84e8, phase: 0.4);
        Entity hull = world.Spawn(new SpawnDesc
        {
            Frame = earth, Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1e4f), AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f), MotionDriver.Kepler, Orbit: far),
        });
        (Double3 place, Double3 velocity) = far.StateAt(world.Time, Scenes.EarthMu);
        Entity crate = world.Spawn(new SpawnDesc { Frame = earth, Pose = new DPose(place + new Double3(0, -3.5, 0), DQuaternion.Identity), LinearVelocity = velocity, Body = Crate });
        world.Tick();
        FrameHandle home = world.PhysicsHomeOf(crate);
        world.Frames.Numeric(home).Should().Be(NumericMode.Orbiting);
        home.Should().Be(world.PhysicsHomeOf(hull), "the crate shares the hull's orbit frame");
        TickUntil(world, () => !IsAwake(faults.Jolt, home, crate), 300);

        IsAwake(faults.Jolt, home, crate).Should().BeFalse("the crate rests in the orbit frame");
        Tick(world, 60);
        IsAwake(faults.Jolt, home, crate).Should().BeFalse();
        (world.PoseIn(crate, earth).Position - world.PoseIn(hull, earth).Position - new Double3(0, -3.5, 0)).Length
            .Should().BeLessThan(1e-3, "the sleeping crate moves with its orbit frame, 17 m a tick");
        world.Errors.Should().BeEmpty();
    }

    // ---- A fast rock that passes the station --------------------------------------------------------------------

    /// <summary>What the rock did: its homes in order, with the tick and its distance from the hull at each change, its Collisions, and its place and velocity in the station at the end.</summary>
    private sealed record Pass(List<(long Tick, string Home, double Distance)> Homes, int Collisions, Double3 Position, Double3 Velocity);

    /// <summary>
    /// A rock flies past the station at 60 m/s relative to it, along +Y, 40 m to the side of its core, outside the bubble
    /// and clear of the plate. It starts 300 m before the core.
    /// </summary>
    private static Pass PassTheStation(bool numericFrames, int threads)
    {
        using World world = NewWorld(numericFrames, threads, out FaultInjectingBackend faults);
        Station station = KeplerStation(world);
        (Double3 place, Double3 velocity) = station.StateAt(world.Time);
        var log = new CollisionLog();
        Entity rock = world.Spawn(new SpawnDesc
        {
            Frame = station.Earth, Pose = new DPose(place + new Double3(40, -300, 0), DQuaternion.Identity),
            LinearVelocity = velocity + new Double3(0, 60, 0), Body = Rock, Components = [log],
        });
        var homes = new List<(long, string, double)>();
        for (int tick = 0; tick < 600; tick++)
        {
            world.Tick();
            string kind = HomeKind(world, station, rock);
            if (homes.Count == 0 || homes[^1].Item2 != kind)
                homes.Add((world.TickNumber, kind, world.PoseIn(rock, station.Frame).Position.Length));
        }
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        return new Pass(homes, log.Seen.Count, world.PoseIn(rock, station.Frame).Position, world.VelocityIn(rock, station.Frame));
    }

    /// <summary>
    /// A fast rock that passes the station falls in a numeric frame of its own until the merge test links it to the hull,
    /// 2 s ahead: then it joins the hull's orbit frame for the encounter, where the hull is a real body, and falls in a
    /// frame of its own again when the merge test lets go. It moves alike with numeric frames and without.
    /// </summary>
    [Fact]
    public void AFastRockThatPassesTheStationLinksItsHullAndRunsInItsOrbitFrameForTheEncounter()
    {
        Pass with = PassTheStation(numericFrames: true, threads: 1);
        Pass without = PassTheStation(numericFrames: false, threads: 1);

        with.Homes.Select(change => change.Home).Should().Equal(["Falling", "Orbiting", "Falling"]);
        with.Homes[1].Distance.Should().BeLessThan(150, "the merge test reaches the hull 2 s ahead at 60 m/s");
        with.Homes[2].Distance.Should().BeGreaterThan(130, "the rock lets go beyond the reach and the split margin");
        without.Homes.Select(change => change.Home).Should().Equal(["earth"]);
        with.Collisions.Should().Be(0);
        without.Collisions.Should().Be(0);
        (with.Position - without.Position).Length.Should().BeLessThan(0.05, "the rock moves alike");
        (with.Velocity - without.Velocity).Length.Should().BeLessThan(0.01);
    }

    // ---- Inside the station ------------------------------------------------------------------------------------

    /// <summary>
    /// A crate that drifts inside the station at 0.5 m/s, and another at rest that sticks out of its bubble at the far side,
    /// stay in the station's own system for ten seconds, with the same motion with numeric frames as without. The crate
    /// that sticks out has a ghost in the station's tree parent, which is the orbit frame with numeric frames and earth
    /// without, and with numeric frames one in earth too, whose own system holds the bodies that the merge test does not
    /// link to the hull.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheBodiesInTheStationsOwnFrameMoveAsBeforeAndTheOnesThatStickOutHaveGhostsInItsTreeParentAndInEarth(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, threads: 1, out FaultInjectingBackend faults);
        Station station = KeplerStation(world);
        Entity drifter = world.Spawn(new SpawnDesc { Frame = station.Frame, Pose = At(-5, 0, 5), LinearVelocity = new Double3(0.5, 0, 0), Body = Crate });
        Entity stickOut = world.Spawn(new SpawnDesc { Frame = station.Frame, Pose = At(0, -15.9), Body = Crate });
        Tick(world, 2);

        FrameHandle parent = world.Frames.TreeParent(station.Frame);
        world.Frames.Numeric(parent).Should().Be(numericFrames ? NumericMode.Orbiting : NumericMode.None);
        faults.Jolt.HasGhostIn(stickOut, parent).Should().BeTrue("a body that sticks out of a bubble is partly in the frame's tree parent");
        faults.Jolt.HasGhostIn(stickOut, station.Earth).Should().BeTrue();

        Tick(world, 598);
        world.PhysicsHomeOf(drifter).Should().Be(station.Frame);
        world.PhysicsHomeOf(stickOut).Should().Be(station.Frame);
        (world.PoseIn(drifter, station.Frame).Position - new Double3(0, 0, 5)).Length.Should().BeLessThan(1e-3, "the crate drifts 5 m in 10 s");
        (world.PoseIn(stickOut, station.Frame).Position - new Double3(0, -15.9, 0)).Length.Should().BeLessThan(1e-3, "nothing pulls the crate in the station");
        world.Errors.Should().BeEmpty();
    }

    // ---- A Kepler hull and a static rock ---------------------------------------------------------------------------

    /// <summary>The space of <see cref="HullAndRock"/>, the hull's owner, a static rock and a cargo pod.</summary>
    private sealed record HullScene(FrameHandle Space, Entity Hull, Entity Rock, Entity Pod);

    /// <summary>
    /// An <see cref="Scenes.AsteroidSpace"/>, and in it a frame on a circular orbit of 5 km (see
    /// <see cref="Scenes.FastKeplerHull"/>), whose hull is a box of 4 m, which passes along -Z at 63 m/s. 100 m ahead on its
    /// path a cargo pod (see <see cref="PodFrame"/>) floats, and 8 m to its outer side a static rock of radius 1 m, which
    /// the hull misses.
    /// </summary>
    private static HullScene HullAndRock(World world, CollisionLog log)
    {
        FrameHandle space = Scenes.AsteroidSpace(world);
        Entity hull = Scenes.FastKeplerHull(world, space, new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1e4f, Restitution: 0));
        Entity rock = world.Spawn(new SpawnDesc { Frame = space, Pose = At(5_008, 0, -100), Body = new BodyDesc(new SphereShapeDesc(1), BodyMotion.Static) });
        Entity pod = world.Spawn(new SpawnDesc { Frame = space, Pose = At(5_000, 0, -100), Body = Crate, AsFrame = PodFrame, Components = [log] });
        return new HullScene(space, hull, rock, pod);
    }

    /// <summary>
    /// The hull on its orbit at 63 m/s and a static rock that it passes are anchors that move differently, so no home holds
    /// both at rest. The anchor at rest wins: the group that links them lives in the rock's home, space's own system, and
    /// the hull stays in its orbit frame and keeps a ghost there, which meets only what the hull meets: static bodies and
    /// other hulls. The ghost hits the pod's hull in space's own system as the hull does without numeric frames, and the
    /// pod, which then flies with the hull, joins the hull's orbit frame once the merge test lets go of the rock.
    /// </summary>
    [Fact]
    public void AGroupThatLinksAKeplerHullAndAStaticRockLivesInTheRocksHomeAndKeepsAGhostOfTheHull()
    {
        var withLog = new CollisionLog();
        using World world = NewWorld(numericFrames: true, threads: 1, out FaultInjectingBackend faults);
        HullScene scene = HullAndRock(world, withLog);
        world.Tick();
        FrameHandle orbit = world.PhysicsHomeOf(scene.Hull);
        world.Frames.Numeric(orbit).Should().Be(NumericMode.Orbiting, "the hull is fast in space");
        world.PhysicsHomeOf(scene.Rock).Should().Be(scene.Space, "the rock rests 5 km from the origin");
        world.PhysicsHomeOf(scene.Pod).Should().Be(scene.Space, "the group of the hull, the rock and the pod lives in the rock's home");
        world.Frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(scene.Hull, scene.Space));
        Tick(world, 1);
        faults.Jolt.HasGhostIn(scene.Hull, scene.Space).Should().BeTrue();

        var homes = new List<string> { "space" };
        for (int tick = 0; tick < 400; tick++)
        {
            world.Tick();
            FrameHandle home = world.PhysicsHomeOf(scene.Pod);
            string kind = home == scene.Space ? "space" : home == orbit ? "orbit" : world.Frames.Numeric(home).ToString();
            if (homes[^1] != kind)
                homes.Add(kind);
        }
        homes.Should().Equal(["space", "orbit"], "the pod flies with the hull after the hit, and joins its orbit frame");
        world.Frames.AnchorGhosts.IsEmpty.Should().BeTrue("no group links the hull and the rock any more");
        faults.Jolt.HasGhostIn(scene.Hull, scene.Space).Should().BeFalse();
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();

        var withoutLog = new CollisionLog();
        using World plain = NewWorld(numericFrames: false, threads: 1, out _);
        HullScene other = HullAndRock(plain, withoutLog);
        Tick(plain, 402);
        withLog.Seen.Select(s => (s.Tick, s.Other.Index)).Should().Equal(withoutLog.Seen.Select(s => (s.Tick, s.Other.Index)),
            "the ghost hits the pod as the hull does without numeric frames");
        withLog.Seen.Should().NotBeEmpty();
        // A ghost moves with the chord of its body's last step, so during the push it lags the hull by one step of the
        // orbit's 0.8 m/s², 1.3 cm/s, and the pod leaves the hit that much slower: 7 cm in the 5 s after it.
        (world.VelocityIn(scene.Pod, scene.Space) - plain.VelocityIn(other.Pod, other.Space)).Length.Should().BeLessThan(0.02);
        (world.PoseIn(scene.Pod, scene.Space).Position - plain.PoseIn(other.Pod, other.Space).Position).Length.Should().BeLessThan(0.1);
    }

    // ---- Driver changes ------------------------------------------------------------------------------------------

    /// <summary>
    /// A station in low orbit, with a crate beside its hull in the orbit frame, gets the Static driver and then the Kepler
    /// driver again. With the Static driver the frame stops where it is in earth, and its hull lives in earth's own system,
    /// a fixed hull that the crate's group joins. A second crate comes to rest beside the stopped hull. With the Kepler
    /// driver again, the frame takes its place on the orbit at the world's time, and its hull rests in a new orbit frame,
    /// while the second crate stays where it was, as without numeric frames: the numeric update links the hull to the
    /// bodies at the place to which its driver moved it, here the crate 640 m behind it for the encounter. Every read in
    /// earth is the orbit's state again.
    /// </summary>
    [Fact]
    public void AFrameWhoseDriverChangesFromKeplerToStaticAndBackLeavesItsOrbitFrameAndGetsANewOne()
    {
        using World world = NewWorld(numericFrames: true, threads: 1, out FaultInjectingBackend faults);
        Station station = KeplerStation(world);
        (Double3 place, Double3 velocity) = station.StateAt(world.Time);
        // 20 m below the core, outside the bubble, and within the 23 m that the merge test reaches from the hull.
        Entity crate = world.Spawn(new SpawnDesc { Frame = station.Earth, Pose = new DPose(place + new Double3(0, -20, 0), DQuaternion.Identity), LinearVelocity = velocity, Body = Crate });
        Tick(world, 2);
        FrameHandle orbit = world.PhysicsHomeOf(station.Owner);
        world.Frames.Numeric(orbit).Should().Be(NumericMode.Orbiting);
        world.PhysicsHomeOf(crate).Should().Be(orbit);

        DPose stopped = world.PoseIn(station.Owner, station.Earth);
        world.SetFrameDriver(station.Frame, MotionDriver.Static);
        world.Frames.TreeParent(station.Frame).Should().Be(station.Earth, "a frame that physics does not drive and that no orbit moves hangs in its semantic parent");
        world.PhysicsHomeOf(station.Owner).Should().Be(station.Earth);
        world.PhysicsHomeOf(crate).Should().Be(station.Earth, "the crate's group joins the fixed hull's home");
        world.Frames.LinearVelocity(station.Frame).Should().Be(Double3.Zero, "a frame that gets the Static driver stops where it is");
        world.PoseIn(station.Owner, station.Earth).Position.Should().Be(stopped.Position);
        Tick(world, 3);
        world.Frames.IsAlive(orbit).Should().BeFalse("the orbit frame lost its frame and its bodies");
        world.PoseIn(station.Owner, station.Earth).Position.Should().Be(stopped.Position);
        Entity resting = world.Spawn(new SpawnDesc { Frame = station.Earth, Pose = new DPose(stopped.Position + new Double3(0, -20, 0), DQuaternion.Identity), Body = Crate });
        Tick(world, 1);
        Double3 restingPlace = world.PoseIn(resting, station.Earth).Position;

        world.SetFrameDriver(station.Frame, MotionDriver.Kepler);
        FrameHandle again = world.PhysicsHomeOf(station.Owner);
        world.Frames.Numeric(again).Should().Be(NumericMode.Orbiting);
        world.Frames.TreeParent(station.Frame).Should().Be(again);
        world.PoseIn(station.Owner, station.Earth).Position.Should().Be(station.StateAt(world.Time).Position);
        world.PoseIn(resting, station.Earth).Position.Should().Be(restingPlace, "the crate beside the stopped hull does not jump with the hull");
        world.Tick();
        (world.PoseIn(resting, station.Earth).Position - restingPlace).Length.Should().BeLessThan(0.01, "nor in the step after");
        Tick(world, 60);
        world.PoseIn(station.Owner, station.Earth).Position.Should().Be(station.StateAt(world.Time).Position);
        world.PhysicsHomeOf(station.Owner).Should().Be(again);
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>
    /// What a run of <see cref="NewOrbit"/> saw: the crate's place and velocity in earth just before and just after the new
    /// orbit, and at the end; the crate's home at the end; the hull's place in its home at the end, and its frame's pose
    /// there; and the cargo pod's place in earth just before and just after the new orbit.
    /// </summary>
    private sealed record OrbitChange(Double3 PlaceBefore, Double3 VelocityBefore, Double3 PlaceAfter, Double3 VelocityAfter, Double3 PlaceAtEnd,
        Double3 VelocityAtEnd, string CrateHome, Double3 HullInHome, DPose FrameInHome, Double3 PodBefore, Double3 PodAfter);

    /// <summary>
    /// A station in low orbit, with a crate 20 m below its core and a cargo pod (see <see cref="PodFrame"/>) 20 m to the
    /// side of it, outside the bubble, at the station's velocity, so that with numeric frames both share the station's orbit
    /// frame, and the pod's frame hangs in it. After two ticks the game gives the station a new orbit 0.01 rad ahead on the
    /// same circle, 68 km away, and the world runs on for a second.
    /// </summary>
    private static OrbitChange NewOrbit(bool numericFrames)
    {
        using World world = NewWorld(numericFrames, threads: 1, out FaultInjectingBackend faults);
        Station station = KeplerStation(world);
        (Double3 place, Double3 velocity) = station.StateAt(world.Time);
        Entity crate = world.Spawn(new SpawnDesc { Frame = station.Earth, Pose = new DPose(place + new Double3(0, -20, 0), DQuaternion.Identity), LinearVelocity = velocity, Body = Crate });
        Entity pod = world.Spawn(new SpawnDesc
        {
            Frame = station.Earth, Pose = new DPose(place + new Double3(20, 0, 0), DQuaternion.Identity), LinearVelocity = velocity, Body = Crate, AsFrame = PodFrame,
        });
        Tick(world, 2);
        if (numericFrames)
        {
            world.PhysicsHomeOf(crate).Should().Be(world.PhysicsHomeOf(station.Owner), "the crate shares the hull's orbit frame");
            world.Frames.TreeParent(world.GetOwnedFrame(pod)).Should().Be(world.PhysicsHomeOf(station.Owner), "the pod's frame hangs in the orbit frame");
        }
        Double3 placeBefore = world.PoseIn(crate, station.Earth).Position, velocityBefore = world.VelocityIn(crate, station.Earth);
        Double3 podBefore = world.PoseIn(pod, station.Earth).Position;

        KeplerOrbit ahead = LowOrbit with { MeanAnomalyAtEpoch = LowOrbit.MeanAnomalyAtEpoch + 0.01 };
        world.SetFrameOrbit(station.Frame, ahead);
        world.PoseIn(station.Owner, station.Earth).Position.Should().Be(ahead.StateAt(world.Time, Scenes.EarthMu).Position, "the frame jumps to its new orbit");
        Double3 placeAfter = world.PoseIn(crate, station.Earth).Position, velocityAfter = world.VelocityIn(crate, station.Earth);
        Double3 podAfter = world.PoseIn(pod, station.Earth).Position;

        Tick(world, 60);
        world.PoseIn(station.Owner, station.Earth).Position.Should().Be(ahead.StateAt(world.Time, Scenes.EarthMu).Position);
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        // Jolt does not report a kinematic hull that rests, so the test reads it from Jolt.
        Double3 hull = faults.Jolt.BodiesIn(world.PhysicsHomeOf(station.Owner))
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, station.Owner)).Position;
        FrameHandle home = world.PhysicsHomeOf(crate);
        return new OrbitChange(placeBefore, velocityBefore, placeAfter, velocityAfter, world.PoseIn(crate, station.Earth).Position,
            world.VelocityIn(crate, station.Earth), home == station.Earth ? "earth" : world.Frames.Numeric(home).ToString(), hull,
            world.Frames.TreeLocalPose(station.Frame), podBefore, podAfter);
    }

    /// <summary>
    /// A new orbit moves the station's frame to its place on that orbit at once, and the crate beside its hull stays where
    /// it was, at its velocity, as without numeric frames, where the crate lies in earth's own system. So the station's
    /// orbit frame moves to the new place, and the crate, which it held, loses that move. The hull follows its frame there
    /// at the start of the next step, as for any new orbit. The crate falls on in a frame of its own, far from the hull.
    /// </summary>
    [Fact]
    public void ANewOrbitMovesTheStationAndLeavesTheCrateInItsOrbitFrameWhereItWasAsWithoutNumericFrames()
    {
        OrbitChange with = NewOrbit(numericFrames: true);
        OrbitChange without = NewOrbit(numericFrames: false);

        (with.PlaceAfter - with.PlaceBefore).Length.Should().BeLessThan(1e-6, "the crate stays where it was");
        (with.PodAfter - with.PodBefore).Length.Should().BeLessThan(1e-6, "the pod's frame stays where it was");
        (without.PodAfter - without.PodBefore).Length.Should().BeLessThan(1e-6);
        (with.VelocityAfter - with.VelocityBefore).Length.Should().BeLessThan(1e-4, "the crate keeps its velocity, to the float of its new local velocity");
        (without.PlaceAfter - without.PlaceBefore).Length.Should().BeLessThan(1e-6);
        with.CrateHome.Should().Be("Falling", "the crate is 68 km from the hull");
        without.CrateHome.Should().Be("earth");
        // Without numeric frames each step's pull rounds in a float velocity of 7.66 km/s.
        (with.PlaceAtEnd - without.PlaceAtEnd).Length.Should().BeLessThan(0.05, "the crate moves alike");
        (with.VelocityAtEnd - without.VelocityAtEnd).Length.Should().BeLessThan(0.05);
        (with.HullInHome - with.FrameInHome.Position).Length.Should().BeLessThan(1e-3, "the hull follows its frame to the new place");
        with.FrameInHome.Position.Should().Be(Double3.Zero, "the hull rests at its orbit frame's origin");
    }

    // ---- Determinism ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The homes and the bumps of the crates of <see cref="DriftOut"/> and the homes of the rock of
    /// <see cref="PassTheStation"/>, as text, with numeric frames, on <paramref name="threads"/> gameplay threads and as
    /// many Jolt threads. Each line holds the tick and the bits of what it names.
    /// </summary>
    private static List<string> TraceOfTheOrbitScenes(int threads)
    {
        var lines = new List<string>();
        foreach (Drift drift in DriftOut(numericFrames: true, threads, ticks: 600))
        {
            lines.Add(string.Join(" ", drift.Homes) + " " + string.Join(" ", drift.Bumps));
            lines.Add($"end {BitConverter.DoubleToInt64Bits(drift.Position.X)} {BitConverter.DoubleToInt64Bits(drift.Position.Y)}");
        }
        Pass pass = PassTheStation(numericFrames: true, threads);
        lines.AddRange(pass.Homes.Select(h => $"{h.Tick} {h.Home} {BitConverter.DoubleToInt64Bits(h.Distance)}"));
        lines.Add($"end {BitConverter.DoubleToInt64Bits(pass.Position.X)} {BitConverter.DoubleToInt64Bits(pass.Velocity.Y)}");
        return lines;
    }

    [Fact]
    public void TheOrbitScenesGiveTheSameEventsAndHomesInEveryRunWithOneOrFourThreads()
    {
        List<string> first = TraceOfTheOrbitScenes(1);
        List<string> second = TraceOfTheOrbitScenes(1);
        List<string> parallel = TraceOfTheOrbitScenes(4);

        first.Count(line => line.Contains("Orbiting")).Should().Be(4, "each crate, the pod and the rock join the orbit frame");
        second.Should().Equal(first);
        parallel.Should().Equal(first);
    }
}
