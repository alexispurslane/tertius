using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The chord rule and the conversion of chords in a turning frame, on the World and the real Jolt backend
/// (docs/plans/spin-gravity.md, "The chord rule and the two conversion rules"). A velocity that gameplay gives a body in a
/// turning frame, by a spawn, a <c>SetVelocity</c>, a <c>SetAngularVelocity</c> or the rest after a teleport, is an instant
/// velocity relative to the frame, and Jolt gets the chord of the inertial line through the body's state at that instant.
/// A hand-off carries the body's instant velocity at the end of the step, which the chord of that step gives by the exact
/// inverse of the rule, and a ghost follows its real body's chord, which converts by the frame's poses at both ends of the
/// step. The station of the tests is a frame without a body and without a field, which turns in place in a root frame
/// without a field about its +Y, whose axes are inertial space. Unless a test says otherwise, it spins at the spike's
/// rate, sqrt(9.81 / 100) rad/s, which gives 9.81 m/s² at 100 m, and it spawns with its first motion intent, so that it
/// turns from its first step. Every expected value comes from the straight line of a free body in inertial space and the
/// station's poses, never from the turns that the engine derives.
/// </summary>
public class SpinStationChordTests
{
    /// <summary>The plane that the center of a crate of 1 m reaches when it lands on the floor face x = 100 m, in m.</summary>
    private const double LandingPlane = 99.5;

    /// <summary>The angular acceleration of the outer frame of a <see cref="SpinningPair"/>, in rad/s².</summary>
    private const double OuterSpinUp = 0.02;

    /// <summary>The angular acceleration of the station that spins up in the tests of a spinning ship, in rad/s².</summary>
    private const double StationSpinUp = 0.2;

    private static readonly double SpikeRate = Math.Sqrt(9.81 / 100);

    private static readonly BodyDesc UndampedCrate =
        new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 20, LinearDamping: 0, AngularDamping: 0);

    private static readonly BodyDesc Crate = new(Shape: new BoxShapeDesc(new(0.5f)), Mass: 20);

    // A ball of 10 kg and radius 0.5 m has the moment of inertia 2/5 m r² = 1 kg m² about every axis.
    private static readonly BodyDesc UndampedBall =
        new(Shape: new SphereShapeDesc(0.5f), Mass: 10, LinearDamping: 0, AngularDamping: 0);

    private sealed record Station(FrameHandle Space, FrameHandle Frame)
    {
        /// <summary>The station's pose in inertial space, at the end of the last step.</summary>
        public DPose Pose(World world) => world.Frames.Relative(Frame, Space);
    }

    /// <summary>
    /// A root frame without a field, and in it a station with a bubble of radius 150 m that turns in place about its +Y at
    /// <paramref name="rate"/> rad/s from its first step.
    /// </summary>
    private static Station SpawnStation(World world, double rate)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(new SpawnDesc { Frame = space, AsFrame = new(FrameBounds.Sphere(150)) }
            .Spinning(world, new(Tilt: DQuaternion.Identity, Rate: rate), firstMove: true));
        return new(space, world.GetOwnedFrame(owner));
    }

    /// <summary>A static floor in the station whose face toward the axis is the plane x = 100 m, 10 m by 10 m about the X axis.</summary>
    private static void SpawnFloor(World world, Station station) => world.Spawn(new()
    {
        Frame = station.Frame,
        Pose = At(100.5),
        Body = new(Shape: new BoxShapeDesc(new(0.5f, 5, 5)), Motion: BodyMotion.Static)
    });

    private static Entity SpawnIn(World world, FrameHandle frame, BodyDesc body, Double3 place,
        Double3 velocity = default,
        Vector3 angularVelocity = default, IComponent? component = null) =>
        world.Spawn(new()
        {
            Frame = frame,
            Pose = new(place, DQuaternion.Identity),
            LinearVelocity = velocity,
            AngularVelocity = angularVelocity,
            Body = body,
            Components = component is null ? [] : [component]
        });

    /// <summary>The angle between two rotations, in radians.</summary>
    private static double AngleBetween(DQuaternion a, DQuaternion b) =>
        2 * DQuaternion.Identity.AngularVelocityTo(a * b.Conjugate(), 2).Length;

    /// <summary>The place of a body in inertial space at the end of a tick, and the station's pose there.</summary>
    private readonly record struct Sample(double Time, Double3 Place, DPose Station);

    /// <summary>
    /// The place of <paramref name="body"/> in inertial space now and after each of <paramref name="ticks"/> ticks, with the
    /// station's pose at the same moments.
    /// </summary>
    private static List<Sample> Record(World world, Station station, Entity body, int ticks)
    {
        var samples = new List<Sample>
            { new(world.Time, world.PoseIn(body, station.Space).Position, station.Pose(world)) };
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Tick();
            samples.Add(new(world.Time, world.PoseIn(body, station.Space).Position, station.Pose(world)));
        }

        return samples;
    }

    /// <summary>
    /// The first sample from the third on whose step left the straight line in inertial space through the two samples
    /// before it by more than 0.1 mm: the step in which something touched the body. −1 when there is none.
    /// </summary>
    private static int FirstStepOffTheLine(List<Sample> samples)
    {
        for (int n = 2; n < samples.Count; n++)
            if ((samples[n].Place - samples[n - 1].Place * 2 + samples[n - 2].Place).Length > 1e-4)
                return n;
        return -1;
    }

    /// <summary>
    /// Where the straight line through <paramref name="place"/> at <paramref name="time"/> with the inertial velocity
    /// <paramref name="velocity"/> first brings a point to the station's plane x = <see cref="LandingPlane"/>, between
    /// <paramref name="from"/> and <paramref name="until"/> seconds after <paramref name="time"/>, while the station turns
    /// about +Y at <paramref name="rate"/> from <paramref name="station"/> at <paramref name="time"/>, with its origin at the
    /// origin of inertial space: the time and the point in the station's axes.
    /// </summary>
    private static (double Time, Double3 InStation) Landing(Double3 place, Double3 velocity, double time,
        in DPose station, double rate,
        double from, double until)
    {
        DQuaternion rotation = station.Rotation;

        Double3 InStation(double s) =>
            (DQuaternion.FromAxisAngle(Double3.UnitY, rate * s) * rotation).Conjugate().Rotate(place + velocity * s);

        double lo = from, hi = from + 1e-4;
        while (hi < until && !(InStation(lo).X < LandingPlane && InStation(hi).X >= LandingPlane))
        {
            lo = hi;
            hi += 1e-4;
        }

        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2;
            if (InStation(mid).X < LandingPlane)
                lo = mid;
            else
                hi = mid;
        }

        double at = (lo + hi) / 2;
        return (time + at, InStation(at));
    }

    // ---- The chord rule at a spawn ----

    [Fact]
    public void AnUndampedCrateReleasedAtRestAboveTheFloorLandsWhereItsInertialLineSays()
    {
        // The spike's drop. The crate is released at rest in the station with its center 98 m from the axis, so it moves on
        // the straight line through its place at the inertial velocity ω × r, 30.7 m/s, until its center reaches the plane
        // x = 99.5 m. Worked out from that line, it lands 0.5607786 s after its release, 0.176458 m antispinward (+Z) of the
        // point under it. The landing that the flight shows is the crossing of that plane by the straight line through its
        // last two free places, as the spike measured it. Without the chord rule of a turning frame, physics took the spawn
        // velocity, zero, as the chord of the step before, and the crate left on the step convention's line, about 0.08 m/s
        // faster outward.
        using World world = NewWorld(backend: out _, debugChecks: false);
        Station station = SpawnStation(world, SpikeRate);
        SpawnFloor(world, station);
        Tick(world, 30);

        Entity crate = SpawnIn(world: world, frame: station.Frame, body: UndampedCrate, place: new(98, 0, 0));
        List<Sample> flight = Record(world: world, station: station, body: crate, ticks: 60);
        Sample release = flight[0];
        Double3 velocity = Double3.Cross(new(0, SpikeRate, 0), release.Place - release.Station.Position);

        int touch = FirstStepOffTheLine(flight);
        touch.Should().BeGreaterThan(2, "the crate flies free until it meets the floor");
        double worst = 0;
        for (int n = 0; n < touch; n++)
            worst = Math.Max(worst,
                Double3.Distance(flight[n].Place, release.Place + velocity * (flight[n].Time - release.Time)));
        worst.Should().BeLessThan(1e-5, "the crate flies on the straight line through its place at ω × r");

        Sample last = flight[touch - 1];
        (double time, Double3 inStation) = Landing(place: last.Place, velocity: (last.Place - flight[touch - 2].Place) / world.DeltaTime,
            time: last.Time,
            station: last.Station, rate: SpikeRate, from: -world.DeltaTime, until: 3 * world.DeltaTime);
        // To the last digit of the spike's numbers.
        (time - release.Time).Should().BeApproximately(0.5607786, 5e-8);
        inStation.Z.Should().BeApproximately(0.176458, 5e-7);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACrateWithTheDefaultDampingReleasedAtRestAboveTheFloorFollowsItsExpectedPath()
    {
        // The drop of the test before with Jolt's damping, which takes the crate's motion relative to the station's axes
        // away, like air that turns with the station. Its expected path starts on the straight line through its place at
        // ω × r: at rest relative to the station, the chord rule adds no half step of the damping.
        using World world = NewWorld(backend: out _, debugChecks: false);
        Station station = SpawnStation(world, SpikeRate);
        SpawnFloor(world, station);
        Tick(world, 30);

        Entity crate = SpawnIn(world: world, frame: station.Frame, body: Crate, place: new(98, 0, 0));
        // The crate lands after about 34 ticks.
        List<Sample> flight = Record(world: world, station: station, body: crate, ticks: 30);
        Sample release = flight[0];
        Double3 velocity = Double3.Cross(new(0, SpikeRate, 0), release.Place - release.Station.Position);
        var path = new SpinStationBodyTests.ExpectedPath(place: release.Place, damping: Crate.LinearDamping, dt: world.DeltaTime,
            lastMove: velocity * world.DeltaTime);

        double worst = 0;
        for (int n = 1; n < flight.Count; n++)
        {
            path.Step(flight[n - 1].Station, flight[n].Station);
            worst = Math.Max(worst, Double3.Distance(flight[n].Place, path.Place));
        }

        worst.Should().BeLessThan(1e-5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACrateReleasedWhileTheStationSpinsUpMovesOnTheInertialLineOfItsRelease()
    {
        // The station spins up at 0.1 rad/s² from its spawn, so 3 s later it turns at 0.3 rad/s, and its rate changes by
        // 1.7e-3 rad/s in each step. The chord rule at the spawn takes the turn of the step that ended at the release and the
        // rate at that instant, so the crate leaves at ω × r at the rate of its release. The rate at the end of the step in
        // flight would make it 0.08 m/s faster at 50 m.
        using World world = NewWorld(backend: out _, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        double spinStart = world.Time;
        Entity owner = world.Spawn(SpinUp.Spawning(new() { Frame = space, AsFrame = new(FrameBounds.Sphere(150)) },
            world, 0.1));
        var station = new Station(space, world.GetOwnedFrame(owner));
        Tick(world, 3 * world.Options.TickRate);

        Entity crate = SpawnIn(world: world, frame: station.Frame, body: UndampedCrate, place: new(50, 0, 0));
        List<Sample> flight = Record(world: world, station: station, body: crate, ticks: 30);
        Sample release = flight[0];
        var rate = new Double3(0, 0.1 * (release.Time - spinStart), 0);
        Double3 velocity = Double3.Cross(rate, release.Place - release.Station.Position);

        double worst = 0;
        foreach (Sample sample in flight)
            worst = Math.Max(worst,
                Double3.Distance(sample.Place, release.Place + velocity * (sample.Time - release.Time)));

        worst.Should().BeLessThan(1e-5);
        world.Errors.Should().BeEmpty();
    }

    // ---- The chord rule of a velocity that the game sets ----

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    public void ACrateThrownAlongTheFloorFollowsTheExpectedPathOfItsInstantVelocity(int tickRate)
    {
        // The spike's throw: 3 m/s spinward (-Z) relative to the station, with the crate's center 1 m above the floor. The
        // velocity acts at the start of its step, at the crate's place and the station's pose where the tick that wrote it
        // ended, and Jolt gets the chord behind it: the chord of the step that ended of the inertial line through that state,
        // plus half a step of the damping. The expected path continues that line with Jolt's damping. Taken as the chord of
        // the step before, the throw left 0.1 m/s off its line at 60 Hz.
        using World world = NewWorld(backend: out _, debugChecks: false, configure: options => options.TickRate = tickRate);
        Station station = SpawnStation(world, SpikeRate);
        SpawnFloor(world, station);
        Tick(world, tickRate / 2);

        var pilot = new Pilot();
        Entity crate = SpawnIn(world: world, frame: station.Frame, body: Crate, place: new(99, 0, 0), component: pilot);
        var throwVelocity = new Double3(0, 0, -3);
        pilot.Once = body => body.SetVelocity(throwVelocity);
        DPose beforeThrow = station.Pose(world);
        // The crate lands after about 0.3 s.
        List<Sample> flight = Record(world: world, station: station, body: crate, ticks: tickRate / 4);
        Sample atThrow = flight[1];
        double dt = world.DeltaTime;
        Double3 inStation = atThrow.Station.InverseTransformPoint(atThrow.Place);
        Double3 velocity =
            atThrow.Station.Rotation.Rotate(throwVelocity + Double3.Cross(new(0, SpikeRate, 0), inStation));
        // The chord behind the throw is faster by half a step of the damping, in the station's axes where the step that
        // ended started.
        Double3 halfStep = beforeThrow.Rotation.Rotate(throwVelocity * (Crate.LinearDamping * 0.5 * dt));
        var path = new SpinStationBodyTests.ExpectedPath(place: atThrow.Place, damping: Crate.LinearDamping, dt: world.DeltaTime,
            lastMove: (velocity + halfStep) * dt);

        double worst = 0;
        for (int n = 2; n < flight.Count; n++)
        {
            path.Step(flight[n - 1].Station, flight[n].Station);
            worst = Math.Max(worst, Double3.Distance(flight[n].Place, path.Place));
        }

        flight[^1].Station.InverseTransformPoint(flight[^1].Place).X.Should()
            .BeLessThan(LandingPlane, "the crate is still in the air");
        worst.Should().BeLessThan(1e-5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnUndampedCrateThrownInAFallingFrameInsideTheStationMovesOnTheInertialLineOfItsInstantVelocity()
    {
        // Under these rules every group falls in a numeric frame, so the crate lives in a falling frame below the station
        // when the throw acts, and the frame's origin is near the crate, on the inertial line of a free body there. The chord
        // rule acts on the crate's semantic place and velocity, its home's offset and frame velocity included, and the
        // crate's local chord loses the home's frame velocity again. Without damping, the crate's expected path is the
        // inertial line.
        using World world = NewWorld(backend: out _, debugChecks: false,
            configure: options => options.NumericFrames = NumericFrameTesting.EveryGroupFalls);
        Station station = SpawnStation(world, SpikeRate);
        Tick(world, 30);
        var pilot = new Pilot();
        Entity crate = SpawnIn(world: world, frame: station.Frame, body: UndampedCrate, place: new(99, 0, 0), component: pilot);
        world.Tick();
        world.Frames.IsNumeric(world.PhysicsHomeOf(crate)).Should()
            .BeTrue("the crate fell in a numeric frame below the station");

        var throwVelocity = new Double3(0, 0, -3);
        pilot.Once = body => body.SetVelocity(throwVelocity);
        world.Tick();
        double throwTime = world.Time;
        DPose atThrow = station.Pose(world);
        Double3 start = world.PoseIn(crate, station.Space).Position;
        Double3 inStation = atThrow.InverseTransformPoint(start);
        Double3 velocity = atThrow.Rotation.Rotate(throwVelocity + Double3.Cross(new(0, SpikeRate, 0), inStation));

        double worst = 0;
        for (int tick = 0; tick < 30; tick++)
        {
            world.Tick();
            worst = Math.Max(worst,
                Double3.Distance(world.PoseIn(crate, station.Space).Position,
                    start + velocity * (world.Time - throwTime)));
        }

        world.Frames.IsNumeric(world.PhysicsHomeOf(crate)).Should().BeTrue();
        worst.Should().BeLessThan(1e-5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACrateThatLeavesTheBubbleOfAShipThatFallsInANumericFrameInsideTheStationMovesOnItsInertialLine()
    {
        // Under these rules every group falls in a numeric frame. The ship, a dynamic hull at rest in inertial space 60 m
        // from the axis with no spin there, falls in a numeric frame below the station, whose origin moves on the inertial
        // line of a free body at its place. The crate drifts out of the ship's bubble at 2 m/s and hands off into that
        // falling frame, with the turn's part of its chord on its semantic place and instant velocity there, which holds the
        // home's instant velocity past its chord: half a step of the frame's step acceleration, the rotation terms at the
        // origin, 5.9 m/s² there (docs/plans/spin-gravity.md, "The origin of a falling frame"). Without that term the turn's
        // part misses by ω dt times it, 2.6e-4 m/s. Without damping, the crate's path is a straight line in inertial space.
        using World world = NewWorld(backend: out _, debugChecks: false,
            configure: options => options.NumericFrames = NumericFrameTesting.EveryGroupFalls);
        Station station = SpawnStation(world, SpikeRate);
        Tick(world, 30);
        var omega = new Double3(0, SpikeRate, 0);
        var place = new Double3(60, 0, 0);
        Entity ship = world.Spawn(new()
        {
            Frame = station.Frame,
            Pose = new(place, DQuaternion.Identity),
            LinearVelocity = -Double3.Cross(omega, place),
            AngularVelocity = (-omega).ToVector3(),
            Body = new(Shape: new BoxShapeDesc(new(1)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(3))
        });
        FrameHandle shipFrame = world.GetOwnedFrame(ship);
        Tick(world, 2);
        FrameHandle falling = world.PhysicsHomeOf(ship);
        world.Frames.Numeric(falling).Should()
            .Be(NumericMode.Falling, "the ship falls in a numeric frame below the station");

        Entity crate = SpawnIn(world: world, frame: shipFrame, body: UndampedCrate, place: new(0, 2.4, 0), velocity: new(0, 2, 0));
        Double3 velocity = world.Frames.Relative(shipFrame, station.Space).Rotation.Rotate(new Double3(0, 2, 0));
        // It is fully outside the bubble of 3 m, plus its bounding radius of 0.87 m and the exit margin, as much again, after
        // about 70 ticks.
        List<Sample> flight = Record(world: world, station: station, body: crate, ticks: 100);

        world.GetFrame(crate).Should().Be(station.Frame, "the crate leaves the ship's bubble");
        world.PhysicsHomeOf(crate).Should().Be(world.PhysicsHomeOf(ship),
            "the crate lives in the ship's falling frame after the hand-off");
        world.Frames.Numeric(world.PhysicsHomeOf(crate)).Should().Be(NumericMode.Falling);
        double worst = 0;
        foreach (Sample sample in flight)
            worst = Math.Max(worst,
                Double3.Distance(sample.Place, flight[0].Place + velocity * (sample.Time - flight[0].Time)));
        worst.Should().BeLessThan(1e-6, "the crate moves on its inertial line through the hand-off");
        world.Errors.Should().BeEmpty();
        TestContext.Current.TestOutputHelper?.WriteLine($"worst distance from the inertial line {worst:E2} m");
    }

    [Fact]
    public void ACrateTeleportedToRestInTheStationMovesOnTheInertialLineOfItsRelease()
    {
        // The crate rests on the axis, where the station's turn does not move it, until a teleport puts it at rest 98 m out.
        // The rest after a teleport is an instant velocity of zero relative to the station, so from the teleport on the crate
        // moves at ω × r in inertial space.
        using World world = NewWorld(backend: out _, debugChecks: false);
        Station station = SpawnStation(world, SpikeRate);
        Tick(world, 30);
        var pilot = new Pilot();
        Entity crate = SpawnIn(world: world, frame: station.Frame, body: UndampedCrate, place: Double3.Zero, component: pilot);
        world.Tick();

        var place = new Double3(98, 0, 0);
        pilot.Once = body => body.Teleport(new(place, DQuaternion.Identity));
        world.Tick();
        double teleportTime = world.Time;
        Double3 start = station.Pose(world).TransformPoint(place);
        Double3 velocity = Double3.Cross(new(0, SpikeRate, 0), start - station.Pose(world).Position);

        double worst = 0;
        for (int tick = 0; tick < 30; tick++)
        {
            world.Tick();
            worst = Math.Max(worst,
                Double3.Distance(world.PoseIn(crate, station.Space).Position,
                    start + velocity * (world.Time - teleportTime)));
        }

        worst.Should().BeLessThan(1e-5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AKinematicCrateTeleportedInTheStationRestsWhereItWasPutInTheStationsAxes()
    {
        // A kinematic body moves in the frame's coordinates, by its motion intents and teleports alone, so the chord rule of
        // a turning frame does not act on it (docs/plans/spin-gravity.md, "The flow per kind of entity"). The chord of a point
        // at rest in the station's axes would carry it off at ω × r, 18.8 m/s here.
        using World world = NewWorld(backend: out _, debugChecks: false);
        Station station = SpawnStation(world, SpikeRate);
        Tick(world, 30);
        var pilot = new Pilot();
        Entity crate = SpawnIn(world: world, frame: station.Frame, body: new(Shape: new BoxShapeDesc(new(0.5f)), Motion: BodyMotion.Kinematic),
            place: new(20, 0, 0), component: pilot);
        world.Tick();

        var place = new Double3(60, 0, 0);
        pilot.Once = body => body.Teleport(new(place, DQuaternion.Identity));
        world.Tick();
        double worst = 0;
        for (int tick = 0; tick < 30; tick++)
        {
            world.Tick();
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(crate, station.Frame).Position, place));
        }

        worst.Should().BeLessThan(1e-9);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABallGivenASpinInTheStationKeepsTheInertialAngularVelocityOfItsInstantSpin()
    {
        // The ball rests on the axis and turns with the station, until it gets 2 rad/s about the station's +X relative to the
        // station. At that instant its inertial angular velocity is that spin plus the station's, which it keeps. Taken as
        // the angular chord of the step before, the spin was off by half the station's turn of a step, 5 mrad/s.
        using World world = NewWorld(backend: out _, debugChecks: false);
        Station station = SpawnStation(world, SpikeRate);
        Tick(world, 30);
        var pilot = new Pilot();
        Entity ball = SpawnIn(world: world, frame: station.Frame, body: UndampedBall, place: Double3.Zero, component: pilot);
        world.Tick();

        var spin = new Vector3(2, 0, 0);
        pilot.Once = body => body.SetAngularVelocity(spin);
        world.Tick();
        double spinTime = world.Time;
        DQuaternion atSpin = world.PoseIn(ball, station.Space).Rotation;
        Double3 inertialSpin = station.Pose(world).Rotation.Rotate(spin + new Double3(0, SpikeRate, 0));

        double worst = 0;
        for (int tick = 0; tick < 300; tick++)
        {
            world.Tick();
            DQuaternion expected =
                DQuaternion.FromAxisAngle(inertialSpin, inertialSpin.Length * (world.Time - spinTime)) * atSpin;
            worst = Math.Max(worst, AngleBetween(world.PoseIn(ball, station.Space).Rotation, expected));
        }

        worst.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    // ---- Hand-offs and ghosts ----

    /// <summary>The inertial state of a ball that a test spawns in a station: its place, velocity, angular velocity and rotation in inertial space.</summary>
    private readonly record struct InertialState(
        double Time,
        Double3 Place,
        Double3 Velocity,
        Double3 AngularVelocity,
        DQuaternion Rotation)
    {
        public Double3 PlaceAt(double time) => Place + Velocity * (time - Time);

        public DQuaternion RotationAt(double time) =>
            DQuaternion.FromAxisAngle(AngularVelocity, AngularVelocity.Length * (time - Time)) * Rotation;
    }

    /// <summary>
    /// Spawns an undamped ball in <paramref name="frame"/> at 140 m on its X axis, with the spawn velocities that give it
    /// 10 m/s along that axis and 3 rad/s about its +Z in inertial space, when the frame's pose in inertial space is
    /// <paramref name="pose"/> and its inertial angular velocity in its own axes is <paramref name="frameSpin"/>.
    /// </summary>
    private static Entity SpawnOutwardBall(World world, FrameHandle frame, in DPose pose, Double3 frameSpin,
        out InertialState state)
    {
        var place = new Double3(140, 0, 0);
        var inertialVelocity = new Double3(10, 0, 0);
        var relativeSpin = (new Double3(0, 0, 3) - frameSpin).ToVector3();
        Entity ball = SpawnIn(world: world, frame: frame, body: UndampedBall, place: place, velocity: inertialVelocity - Double3.Cross(frameSpin, place),
            angularVelocity: relativeSpin);
        state = new(Time: world.Time, Place: pose.TransformPoint(place), Velocity: pose.Rotation.Rotate(inertialVelocity),
            AngularVelocity: pose.Rotation.Rotate(relativeSpin + frameSpin), Rotation: pose.Rotation);
        return ball;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABallThatLeavesTheStationHasAGhostOnItsInertialLineAndHandsOffWithTheVelocitiesOfThatLine(
        bool numericFrames)
    {
        // The ball moves out at 10 m/s in inertial space and spins at 3 rad/s about a direction fixed there. While it sticks
        // out of the station's bubble, its ghost in space moves by its chord in space, which is its inertial velocity, and
        // turns at its inertial angular velocity. The instant rule at the end of the step turned the chord in the station
        // into a velocity off by 0.1 m/s. Once the ball is fully out, it hands off into space with its instant velocities at
        // the end of the step, which the chord rule's inverse gives from its chords in the station, and keeps its line. With
        // numeric frames, under rules by which every group falls, the ball lives in a falling frame below the station, and
        // the ghost and the hand-off add the home's offset and frame velocity at the end of the step.
        using World world = NewWorld(backend: out JoltPhysicsBackend jolt, debugChecks: false,
            configure: options =>
                options.NumericFrames = numericFrames ? NumericFrameTesting.EveryGroupFalls : options.NumericFrames);
        Station station = SpawnStation(world, SpikeRate);
        Tick(world, 30);
        Entity ball = SpawnOutwardBall(world: world, frame: station.Frame, pose: station.Pose(world), frameSpin: new(0, SpikeRate, 0),
            state: out InertialState state);
        bool fell = false;

        double worstPlace = 0, worstGhost = 0, worstGhostSpin = 0;
        int ghostTicks = 0;
        int ticks = TickUntil(world, () =>
        {
            worstPlace = Math.Max(worstPlace,
                Double3.Distance(world.PoseIn(ball, station.Space).Position, state.PlaceAt(world.Time)));
            fell |= world.Frames.IsNumeric(world.PhysicsHomeOf(ball)) && world.GetFrame(ball) == station.Frame;
            if (jolt.HasGhostIn(ball, station.Space))
            {
                JoltPhysicsBackend.BodyFacts ghost = jolt.BodiesIn(station.Space)
                    .Single(facts =>
                        BodyUserData.Kind(facts.UserData) == BodyKind.Ghost &&
                        BodyUserData.Names(facts.UserData, ball));
                worstGhost = Math.Max(worstGhost, Double3.Distance(ghost.LinearVelocity, state.Velocity));
                worstGhostSpin = Math.Max(worstGhostSpin,
                    Double3.Distance(ghost.AngularVelocity, state.AngularVelocity));
                ghostTicks++;
            }

            return world.GetFrame(ball) == station.Space;
        }, 200);

        ticks.Should().BeLessThan(200, "the ball leaves the bubble of 150 m within 2 s");
        fell.Should().Be(numericFrames,
            "under the rules of the numeric case the ball falls in a numeric frame below the station");
        ghostTicks.Should().BeGreaterThan(0, "the ball sticks out of the bubble before it leaves");
        worstGhost.Should().BeLessThan(1e-4);
        worstGhostSpin.Should().BeLessThan(1e-4);
        Double3.Distance(world.VelocityIn(ball, station.Space), state.Velocity).Should().BeLessThan(1e-4);
        Double3.Distance(world.AngularVelocityIn(ball, station.Space), state.AngularVelocity).Should().BeLessThan(1e-4);

        double worstAngle = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            worstPlace = Math.Max(worstPlace,
                Double3.Distance(world.PoseIn(ball, station.Space).Position, state.PlaceAt(world.Time)));
            worstAngle = Math.Max(worstAngle,
                AngleBetween(world.PoseIn(ball, station.Space).Rotation, state.RotationAt(world.Time)));
        }

        worstPlace.Should().BeLessThan(5e-5);
        worstAngle.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A station that turns at the spike's rate about the +X of a frame, the outer frame, that spins up about its +Y at
    /// <see cref="OuterSpinUp"/> from its spawn, at the origin of space, after both have turned for 1 s. The station's
    /// inertial angular velocity changes by the outer frame's spin-up and by the turn of its own axis with the outer frame.
    /// </summary>
    private sealed record SpinningPair(Station Station, FrameHandle Outer, double SpinStart, FrameSpin StationSpin)
    {
        /// <summary>The outer frame's angular velocity at <paramref name="time"/>, in its axes and in those of space.</summary>
        public Double3 OuterSpin(double time) => new(0, OuterSpinUp * (time - SpinStart), 0);

        /// <summary>The station's inertial angular velocity in its own axes, at the end of the last step.</summary>
        public Double3 StationSpinNow(World world)
        {
            Double3 inSpace = OuterSpin(world.Time) +
                              world.Frames.Relative(Outer, Station.Space).Rotation.Rotate(StationSpin.AngularVelocity);
            return Station.Pose(world).Rotation.Conjugate().Rotate(inSpace);
        }
    }

    private static SpinningPair SpawnSpinningPair(World world)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        double spinStart = world.Time;
        Entity outerOwner = world.Spawn(SpinUp.Spawning(
            new() { Frame = space, AsFrame = new(FrameBounds.Sphere(1e4f)) },
            world, OuterSpinUp));
        FrameHandle outer = world.GetOwnedFrame(outerOwner);
        // The tilt takes the spin's pole, +Y, to +X.
        var spin = new FrameSpin(Tilt: DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI / 2), Rate: SpikeRate);
        Entity stationOwner = world.Spawn(new SpawnDesc { Frame = outer, AsFrame = new(FrameBounds.Sphere(150)) }
            .Spinning(world, spin, firstMove: true));
        Tick(world, world.Options.TickRate);
        return new(Station: new(space, world.GetOwnedFrame(stationOwner)), Outer: outer, SpinStart: spinStart, StationSpin: spin);
    }

    [Fact]
    public void ABallThatLeavesTheStationIntoAParentThatSpinsUpKeepsItsInertialLine()
    {
        // Both frames turn, and their rates change in every step, each in its own way, so the chord rule at the hand-off
        // must take the turn of the step that just ended in the new frame, and the instant velocity in the station the turn
        // of the same step there. The ball's new body in the outer frame starts without the station's velocity shadow, whose
        // chord and place are relative to the station.
        using World world = NewWorld(backend: out _, debugChecks: false);
        SpinningPair pair = SpawnSpinningPair(world);
        Station station = pair.Station;
        FrameHandle space = station.Space, outer = pair.Outer;
        Entity ball = SpawnOutwardBall(world: world, frame: station.Frame, pose: station.Pose(world), frameSpin: pair.StationSpinNow(world),
            state: out InertialState state);

        double worstPlace = 0;
        int ticks = TickUntil(world, () =>
        {
            worstPlace = Math.Max(worstPlace,
                Double3.Distance(world.PoseIn(ball, space).Position, state.PlaceAt(world.Time)));
            return world.GetFrame(ball) == outer;
        }, 200);
        ticks.Should().BeLessThan(200, "the ball leaves the bubble of 150 m within 2 s");

        double worstAngle = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            worstPlace = Math.Max(worstPlace,
                Double3.Distance(world.PoseIn(ball, space).Position, state.PlaceAt(world.Time)));
            worstAngle = Math.Max(worstAngle,
                AngleBetween(world.PoseIn(ball, space).Rotation, state.RotationAt(world.Time)));
        }

        world.GetFrame(ball).Should().Be(outer);
        worstPlace.Should().BeLessThan(5e-5);
        worstAngle.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShipThatLeavesTheStationIntoAParentThatSpinsUpGivesItsFrameTheInstantVelocityOfItsInertialLineThere()
    {
        // The ship's hull hands off as the ball of the test before does. Jolt's velocity of the hull is then the chord behind
        // its instant velocity in the outer frame, which holds that frame's turn of the step, so the step record of the ship's
        // frame takes back the same turn with its acceleration over half a step, and the frame's velocity in the tree is the
        // instant velocity of the hull's inertial line relative to the turning outer frame.
        using World world = NewWorld(backend: out _, debugChecks: false);
        SpinningPair pair = SpawnSpinningPair(world);
        Station station = pair.Station;
        DPose atSpawn = station.Pose(world);
        Double3 stationSpin = pair.StationSpinNow(world);
        var place = new Double3(140, 0, 0);
        var inertialVelocity = new Double3(10, 0, 0);
        double spawnTime = world.Time;
        Entity ship = world.Spawn(new()
        {
            Frame = station.Frame,
            Pose = new(place, DQuaternion.Identity),
            LinearVelocity = inertialVelocity - Double3.Cross(stationSpin, place),
            AngularVelocity = (-stationSpin).ToVector3(),
            Body = new(Shape: new BoxShapeDesc(new(1)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(5))
        });
        FrameHandle shipFrame = world.GetOwnedFrame(ship);
        Double3 start = atSpawn.TransformPoint(place), velocity = atSpawn.Rotation.Rotate(inertialVelocity);

        int ticks = TickUntil(world, () => world.GetFrame(ship) == pair.Outer, 200);

        ticks.Should().BeLessThan(200, "the ship leaves the bubble of 150 m within 2 s");
        DPose outer = world.Frames.Relative(pair.Outer, station.Space);
        Double3 inOuterAxes = velocity - Double3.Cross(pair.OuterSpin(world.Time),
            start + velocity * (world.Time - spawnTime) - outer.Position);
        Double3.Distance(world.Frames.LinearVelocity(shipFrame), outer.Rotation.Conjugate().Rotate(inOuterAxes))
            .Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFreeShipAtRestInInertialSpaceInsideTheStationGivesItsFrameTheInstantVelocityOfItsInertialLine()
    {
        // The ship is at rest in inertial space, 100 m from the station's axis, with no spin there, so in the station it
        // moves on a circle at ω × r, 31.3 m/s, and its instant velocity relative to the station at every moment is -ω × r
        // at its place then. The step record of its frame holds the instant velocity at the end of each step, which a
        // hand-off out of the ship carries. By the chord rule, that is the exact inverse of the rule on Jolt's chord, as for
        // every other body (docs/plans/spin-gravity.md, "The chord rule"). Jolt's chord plus half a step of the change of
        // Jolt's velocity misses it by the jerk of the circle times dt² / 3, ω³ r dt² / 3 = 2.9e-4 m/s. A float velocity at
        // 31 m/s has steps of 1.9e-6 m/s.
        using World world = NewWorld(backend: out _, debugChecks: false);
        Station station = SpawnStation(world, SpikeRate);
        Tick(world, 30);
        var omega = new Double3(0, SpikeRate, 0);
        var place = new Double3(100, 0, 0);
        Entity ship = world.Spawn(SpinningShip(frame: station.Frame, place: place, velocity: -Double3.Cross(omega, place), angularVelocity: -omega));
        FrameHandle shipFrame = world.GetOwnedFrame(ship);

        double worst = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            Double3 now = world.Frames.Relative(shipFrame, station.Frame).Position;
            worst = Math.Max(worst,
                Double3.Distance(world.Frames.LinearVelocity(shipFrame), -Double3.Cross(omega, now)));
        }

        world.GetFrame(ship).Should().Be(station.Frame);
        worst.Should().BeLessThan(1e-5, "the frame's velocity is the instant velocity of the ship's inertial line");
        world.Errors.Should().BeEmpty();
        TestContext.Current.TestOutputHelper?.WriteLine($"worst velocity of the ship's frame {worst:E2} m/s");
    }

    /// <summary>
    /// An undamped ship of 1 m, a cube, so that its inertia is the same about every axis and it keeps its inertial angular
    /// velocity, with a bubble of 5 m inside it that nothing enters.
    /// </summary>
    private static SpawnDesc
        SpinningShip(FrameHandle frame, Double3 place, Double3 velocity, Double3 angularVelocity) => new()
    {
        Frame = frame,
        Pose = new(place, DQuaternion.Identity),
        LinearVelocity = velocity,
        AngularVelocity = angularVelocity.ToVector3(),
        Body = new(Shape: new BoxShapeDesc(new(1)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
        AsFrame = new(FrameBounds.Sphere(0.5f))
    };

    /// <summary>
    /// A root frame without a field, and in it a station with a bubble of radius 150 m that spins up about its +Y at
    /// <see cref="StationSpinUp"/> from its spawn, after 1 s of that, when it turns at 0.2 rad/s.
    /// </summary>
    private static Station SpawnStationThatSpinsUp(World world)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(SpinUp.Spawning(new() { Frame = space, AsFrame = new(FrameBounds.Sphere(150)) },
            world, StationSpinUp));
        Tick(world, world.Options.TickRate);
        return new(space, world.GetOwnedFrame(owner));
    }

    [Fact]
    public void AShipThatSpinsAcrossTheAxisOfAStationThatSpinsUpGivesItsFrameNoAngularAccelerationInSpaceWhenItLeaves()
    {
        // The ship spins at 1 rad/s about an axis at right angles to the station's, and moves outward at 10 m/s, both in
        // inertial space. In the station's axes its spin turns backward and the station's spin-up adds to its angular
        // velocity relative to the station, which changes at −ω × Ω_s − α, about 0.3 rad/s², and its step record holds that
        // as its angular acceleration. The hand-off converts it by the full rule, α' = R α_b + α + ω × (R ω_b), with the
        // station's ω and α from its step record, so the ship's frame in space reads the zero angular acceleration of a free
        // cube. Without the cross term the frame kept |ω| |Ω|, and without the station's α it kept 0.2 rad/s². The record's
        // angular acceleration is the change of Jolt's angular chords, centered a step before the instant angular velocity
        // that the cross term takes, so the frame keeps about one step of the change of α_b, (α + ω²) |Ω| dt = 6e-3 rad/s²
        // at a rate of 0.4 rad/s (measured 5.3e-3 rad/s²).
        using World world = NewWorld(backend: out _, debugChecks: false);
        Station station = SpawnStationThatSpinsUp(world);
        Double3 omega = world.Frames.AngularVelocity(station.Frame);
        var spin = new Double3(1, 0, 0);
        var place = new Double3(140, 0, 0);
        Entity ship = world.Spawn(SpinningShip(frame: station.Frame, place: place,
            velocity: new Double3(10, 0, 0) - Double3.Cross(omega, place), angularVelocity: spin - omega));
        FrameHandle shipFrame = world.GetOwnedFrame(ship);
        world.Tick();
        Double3 inStation = world.Frames.AngularAcceleration(shipFrame);

        int ticks = TickUntil(world, () => world.GetFrame(ship) == station.Space, 200);

        ticks.Should().BeLessThan(200, "the ship leaves the bubble of 150 m within 2 s");
        inStation.Length.Should().BeGreaterThan(0.25,
            "in the station's axes the ship's spin turns and the station spins up under it");
        world.Frames.AngularAcceleration(shipFrame).Length.Should()
            .BeLessThan(1e-2, "a free cube keeps its spin in inertial space");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShipThatSpinsAcrossTheAxisOfAStationThatSpinsUpGivesItsFrameTheChangeOfItsSpinThereWhenItEnters()
    {
        // The reverse of the test before: the ship keeps a spin of 1 rad/s in inertial space and flies into the station. Its
        // angular velocity relative to the station, in the station's axes, changes at −α − ω × Ω_s, with Ω_s its spin in the
        // station's axes and ω the station's rate when it enters. The hand-off converts its zero angular acceleration in
        // space by the full rule back, α_b = R⁻¹ (α' − α − ω × Ω'), which gives that. The rule that only turns the vector
        // gave zero.
        using World world = NewWorld(backend: out _, debugChecks: false);
        Station station = SpawnStationThatSpinsUp(world);
        var spin = new Double3(1, 0, 0);
        Entity ship = world.Spawn(SpinningShip(frame: station.Space, place: new(158, 0, 0), velocity: new(-10, 0, 0), angularVelocity: spin));
        FrameHandle shipFrame = world.GetOwnedFrame(ship);

        int ticks = TickUntil(world, () => world.GetFrame(ship) == station.Frame, 200);

        ticks.Should().BeLessThan(200, "the ship enters the bubble of 150 m within 2 s");
        // The station turns about +Y, so its rate and its spin-up have the same components in its axes and in space's.
        Double3 omega = world.Frames.AngularVelocity(station.Frame);
        Double3 spinInStation = station.Pose(world).Rotation.Conjugate().Rotate(spin);
        Double3 expected = -new Double3(0, StationSpinUp, 0) - Double3.Cross(omega, spinInStation);
        Double3.Distance(world.Frames.AngularAcceleration(shipFrame), expected).Should().BeLessThan(1e-6,
            $"the ship's angular velocity in the station changes at {expected}, and it read {world.Frames.AngularAcceleration(shipFrame)}");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AStaticPillarThatReachesIntoTheStationHasAGhostThereThatMovesByItsChordInTheStationsAxes()
    {
        // The pillar stands in space and reaches 5 m into the station's bubble, so it has a ghost in the station, which a
        // static body never enters. In the station's axes its center goes round the axis, and its ghost moves by the chord
        // of that motion over the step that ended. The instant rule at the end of the step gave −ω × r, 0.13 m/s off.
        using World world = NewWorld(backend: out JoltPhysicsBackend jolt, debugChecks: false);
        Station station = SpawnStation(world, SpikeRate);
        var center = new Double3(160, 0, 0);
        Entity pillar = world.Spawn(new()
        {
            Frame = station.Space,
            Pose = new(center, DQuaternion.Identity),
            Body = new(Shape: new BoxShapeDesc(new(15, 1, 1)), Motion: BodyMotion.Static)
        });
        Tick(world, 10);

        DPose start = station.Pose(world);
        world.Tick();
        DPose end = station.Pose(world);

        jolt.HasGhostIn(pillar, station.Frame).Should().BeTrue();
        JoltPhysicsBackend.BodyFacts ghost = jolt.BodiesIn(station.Frame)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Ghost &&
                             BodyUserData.Names(facts.UserData, pillar));
        Double3 chord = (end.InverseTransformPoint(center) - start.InverseTransformPoint(center)) / world.DeltaTime;
        Double3.Distance(ghost.LinearVelocity, chord).Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Writes the full sample of <see cref="Spin"/> for its entity in every tick, at its parent's origin, and in the tick
    /// <paramref name="jumpTick"/> switches to <paramref name="ahead"/> and teleports its entity to that spin's pose where the
    /// step that applies the tick's intents starts, so that its frame's axes jump.
    /// </summary>
    private sealed class JumpingSpin(FrameSpin spin, FrameSpin ahead, long jumpTick) : IComponent
    {
        public FrameSpin Spin { get; private set; } = spin;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick == jumpTick)
            {
                Spin = ahead;
                self.Body.Teleport(new(Double3.Zero, ahead.RotationAt(self.TimeAt(self.Tick + 1))));
            }

            self.MoveOnRails(Spin.Sample(Double3.Zero, self.TimeAt(self.Tick + 2), self.DeltaTime));
        }
    }

    /// <summary>
    /// The owner of a station that turns at 0.05 rad/s teleports it ahead by 0.5 rad, in the tick after which the step
    /// applies it, while a ball at rest in the station sticks out of its bubble. The ghost of the ball in space follows the
    /// ball's chord, converted by the station's poses at both ends of the step (docs/plans/spin-gravity.md, "The two
    /// conversion rules"), and the start of the step is the teleport's pose, where the station's level starts the step
    /// (<c>JoltPhysicsBackend.StartPoseOf</c>). So the ghost moves at the speed of the ball, about 7.5 m/s, and not by the
    /// jump of the station's axes, 75 m in a step. The station has a kinematic hull, which physics teleports. A frame without
    /// a body cannot teleport: its owner has no body to write a teleport for.
    /// </summary>
    [Fact]
    public void TheGhostOfABodyInAStationThatTeleportsFollowsItsChordFromTheTeleportsPose()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend jolt, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        var spin = new FrameSpin(Tilt: DQuaternion.Identity, Rate: 0.05);
        FrameSpin ahead = spin with { PhaseAtEpoch = 0.5 };
        const long jumpTick = 20;
        var jumping = new JumpingSpin(spin, ahead, jumpTick);
        Entity owner = world.Spawn(new()
        {
            Frame = space,
            Body = new(Shape: new BoxShapeDesc(new(1)), Motion: BodyMotion.Kinematic, InteriorShape: new SphereShapeDesc(0.5f)),
            AsFrame = new(FrameBounds.Sphere(150)),
            FirstMove = new(spin.Sample(Double3.Zero, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
            Components = [jumping]
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        Entity ball = SpawnIn(world: world, frame: station, body: UndampedBall, place: new(149.8, 0, 0));
        while (world.TickNumber <= jumpTick)
            world.Tick();
        jolt.HasGhostIn(ball, space).Should().BeTrue("the ball sticks out of the bubble");

        world.Tick();

        DPose start = new(Double3.Zero, ahead.RotationAt(world.TimeAt(jumpTick + 1)));
        DPose end = world.Frames.Relative(station, space);
        AngleBetween(end.Rotation, ahead.RotationAt(world.Time)).Should()
            .BeLessThan(1e-9, "the step applied the teleport and the new spin");
        JoltPhysicsBackend.BodyFacts real = jolt.BodiesIn(station)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary &&
                             BodyUserData.Names(facts.UserData, ball));
        JoltPhysicsBackend.BodyFacts ghost = jolt.BodiesIn(space)
            .Single(facts =>
                BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, ball));
        Double3 chord =
            (end.TransformPoint(real.Position) -
             start.TransformPoint(real.Position - (Double3)real.LinearVelocity * world.DeltaTime)) / world.DeltaTime;
        chord.Length.Should().BeLessThan(10, "the ball moves at about 7.5 m/s in space");
        Double3.Distance(ghost.LinearVelocity, chord).Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship that spins at 0.5 rad/s in inertial space flies out of the station at 10 m/s, with a free ball that starts at
    /// rest in its axes and sticks out of its bubble. The ball flies on its inertial line, at the ship's velocity plus the
    /// ship's turn at its place at the spawn, 2.4 m/s. In the step in which the ship hands off into space, the ball's ghost
    /// moves to space, the ship's new parent, but the ship's pose where the step started is in the station, its old parent,
    /// for which the conversion by the poses at both ends of the step has no rule. So for that one step the ghost converts by
    /// the ship's motion at the end of the step (<c>JoltPhysicsBackend.PosesInStep</c>), and moves with the ball, to the
    /// instant rule's error of a few cm/s.
    /// </summary>
    [Fact]
    public void TheGhostOfABodyInAShipThatHandsOffOutOfTheStationMovesWithTheBodyInTheStepOfTheHandOff()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend jolt, debugChecks: false);
        Station station = SpawnStation(world, SpikeRate);
        Tick(world, 30);
        DPose atSpawn = station.Pose(world);
        var stationSpin = new Double3(0, SpikeRate, 0);
        var place = new Double3(140, 0, 0);
        var inertialVelocity = new Double3(10, 0, 0);
        var shipSpin = new Double3(0, 0.5, 0);
        Entity ship = world.Spawn(new()
        {
            Frame = station.Frame,
            Pose = new(place, DQuaternion.Identity),
            LinearVelocity = inertialVelocity - Double3.Cross(stationSpin, place),
            AngularVelocity = (shipSpin - stationSpin).ToVector3(),
            Body = new(Shape: new BoxShapeDesc(new(1)), Mass: 1000, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(5))
        });
        FrameHandle shipFrame = world.GetOwnedFrame(ship);
        Entity ball = SpawnIn(world: world, frame: shipFrame, body: UndampedBall, place: new(0, 0, 4.8));

        int ticks = TickUntil(world, () => world.GetFrame(ship) == station.Space, 200);

        ticks.Should().BeLessThan(200, "the ship leaves the bubble of 150 m within 2 s");
        world.GetFrame(ball).Should().Be(shipFrame);
        jolt.HasGhostIn(ball, station.Space).Should()
            .BeTrue("the ball sticks out of the ship's bubble, whose parent is space now");
        JoltPhysicsBackend.BodyFacts ghost = jolt.BodiesIn(station.Space)
            .Single(facts =>
                BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, ball));
        Double3 expected = atSpawn.Rotation.Rotate(inertialVelocity) + Double3.Cross(atSpawn.Rotation.Rotate(shipSpin),
            atSpawn.Rotation.Rotate(new Double3(0, 0, 4.8)));
        expected.Length.Should().BeGreaterThan(10.1, "the ship's turn moves the ball");
        // The read in space keeps the instant rule on the chord in the turning ship, off by about ω dt |u| / 2 for a velocity u
        // relative to the ship (see ARCHITECTURE.md, "Turning frames").
        Double3.Distance(world.VelocityIn(ball, station.Space), expected).Should()
            .BeLessThan(0.02, "the ball flies on its inertial line");
        Double3.Distance(ghost.LinearVelocity, expected).Should().BeLessThan(0.05);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A room that holds still in space beside the station, with bubbles that meet at 150 m from the station's axis, and a
    /// ball in the room that sticks out of the room's bubble into the station's bubble and drifts at 1 m/s along +Y. The
    /// ball stays in the room, and has a ghost in the station, its room's sibling. That ghost moves by the chord of the
    /// ball's motion in the station's axes over the step that ended, which converts by the station's poses at both ends of
    /// the step (docs/plans/spin-gravity.md, "The two conversion rules"). The instant rule at the end of the step gave a
    /// velocity about 0.12 m/s off.
    /// </summary>
    [Fact]
    public void ABodyThatSticksOutOfItsRoomIntoTheSiblingStationHasAGhostThereThatMovesByItsChordInTheStationsAxes()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend jolt, debugChecks: false);
        Station station = SpawnStation(world, SpikeRate);
        var roomPlace = new Double3(300, 0, 0);
        FrameHandle room = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = station.Space,
            Pose = new(roomPlace, DQuaternion.Identity),
            AsFrame = new(FrameBounds.Sphere(150))
        }));
        Entity ball = SpawnIn(world: world, frame: room, body: UndampedBall, place: new(-149.8, 0, 0), velocity: new(0, 1, 0));
        Tick(world, 10);

        DPose start = station.Pose(world);
        world.Tick();
        DPose end = station.Pose(world);

        world.GetFrame(ball).Should().Be(room);
        jolt.HasGhostIn(ball, station.Frame).Should().BeTrue("the ball reaches into the station's bubble");
        JoltPhysicsBackend.BodyFacts real = jolt.BodiesIn(room)
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary &&
                             BodyUserData.Names(facts.UserData, ball));
        JoltPhysicsBackend.BodyFacts ghost = jolt.BodiesIn(station.Frame)
            .Single(facts =>
                BodyUserData.Kind(facts.UserData) == BodyKind.Ghost && BodyUserData.Names(facts.UserData, ball));
        Double3 placeEnd = roomPlace + real.Position;
        Double3 placeStart = placeEnd - (Double3)real.LinearVelocity * world.DeltaTime;
        Double3 chord = (end.InverseTransformPoint(placeEnd) - start.InverseTransformPoint(placeStart)) /
                        world.DeltaTime;
        Double3.Distance(ghost.LinearVelocity, chord).Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }
}
