using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Characters in a turning frame, on the World and the real Jolt backend (docs/plans/spin-gravity.md, "Characters"). The
/// station of the tests is a frame without a body and without a field, which turns in place in a root frame without a field
/// about its +Y at 0.31321 rad/s, the rate that gives 9.81 m/s² at 100 m, from its first step: its spawn carries its first
/// motion intent. The root frame's axes are inertial space. Its floor is a static slab whose face toward the axis lies at
/// 100 m along the station's +X, so the effective gravity on a person at rest there points along +X, and up along −X.
/// Every expected value comes from the person's straight line in inertial space and the station's poses, never from the
/// turns that the engine derives.
/// </summary>
public class SpinStationCharacterTests
{
    private const double StationRate = 0.31321;
    private const double FloorRadius = 100;
    private const float PersonMass = 70;
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: PersonMass);

    // A standing capsule rests on the floor, so its center is 0.9 m above it.
    private const double StandingRadius = FloorRadius - 0.9;

    private sealed record Station(FrameHandle Space, FrameHandle Frame)
    {
        /// <summary>The station's pose in inertial space, at the end of the last step.</summary>
        public DPose Pose(World world) => world.Frames.LocalPose(Frame);
    }

    private static Station SpawnStation(World world)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(new SpawnDesc { Frame = space, AsFrame = new FrameSpec(FrameBounds.Sphere(150)) }
            .Spinning(world, new FrameSpin(DQuaternion.Identity, StationRate), firstMove: true));
        FrameHandle frame = world.GetOwnedFrame(owner);
        world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = At(FloorRadius + 0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 10, 10)), BodyMotion.Static),
        });
        return new Station(space, frame);
    }

    private static Entity SpawnPerson(World world, Station station, Double3 place, Pilot pilot) =>
        world.Spawn(new SpawnDesc { Frame = station.Frame, Pose = new DPose(place, DQuaternion.Identity), Body = Person, Components = [pilot] });

    /// <summary>One tick's end: the time, the station's pose in inertial space, and the person's place in inertial space.</summary>
    private readonly record struct Sample(double Time, DPose Station, Double3 Inertial, bool Standing)
    {
        /// <summary>The person's place in the station's coordinates.</summary>
        public Double3 InStation => Station.InverseTransformPoint(Inertial);
    }

    private static Sample Take(World world, Station station, Entity person) =>
        new(world.Time, station.Pose(world), world.PoseIn(person, station.Space).Position, world.IsStanding(person));

    /// <summary>The station's angular velocity in inertial space, from the poses of two samples one step apart.</summary>
    private static Double3 AngularVelocity(Sample a, Sample b) => a.Station.Rotation.AngularVelocityTo(b.Station.Rotation, b.Time - a.Time);

    /// <summary>
    /// When the inertial line X(s) = <paramref name="start"/> + <paramref name="velocity"/> s, which starts at the time of
    /// <paramref name="at"/>, first brings a point back to the station plane x = <paramref name="planeX"/> from below, after
    /// <paramref name="from"/> s and before <paramref name="until"/> s, while the station turns steadily at
    /// <paramref name="omega"/>, in inertial axes, from its pose at <paramref name="at"/>: the time after the start and the
    /// place in the station's coordinates.
    /// </summary>
    private static (double Time, Double3 Place) Landing(Sample at, Double3 omega, Double3 start, Double3 velocity, double planeX,
        double from, double until)
    {
        Double3 InStation(double s)
        {
            DQuaternion rotation = DQuaternion.FromAxisAngle(omega, omega.Length * s) * at.Station.Rotation;
            return new DPose(at.Station.Position, rotation).InverseTransformPoint(start + velocity * s);
        }
        double Gap(double s) => InStation(s).X - planeX;
        const double step = 1e-4;
        double lo = from, hi = from + step;
        while (hi < until && !(Gap(lo) < 0 && Gap(hi) >= 0))
        {
            lo = hi;
            hi += step;
        }
        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2;
            if (Gap(mid) < 0)
                lo = mid;
            else
                hi = mid;
        }
        double time = (lo + hi) / 2;
        return (time, InStation(time));
    }

    /// <summary>
    /// The first sample n at or after <paramref name="from"/> whose step left the inertial straight line through samples
    /// n − 2 and n − 1 by more than 0.1 mm: the step in which something touched the person. −1 if there is none.
    /// </summary>
    private static int FirstConstrainedStep(IReadOnlyList<Sample> samples, int from)
    {
        for (int n = Math.Max(from, 2); n < samples.Count; n++)
            if ((samples[n].Inertial - samples[n - 1].Inertial * 2 + samples[n - 2].Inertial).Length > 1e-4)
                return n;
        return -1;
    }

    [Fact]
    public void APersonJustAboveTheFloorOfTheStationFallsOntoItAndStandsFor600Of600Ticks()
    {
        // The station has no field, so only the station's turn brings the person down: in inertial space it keeps the
        // velocity of its spawn place on the station, and the floor turns up under it. It then stands, and the ground holds it.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Entity person = SpawnPerson(world, station, new Double3(StandingRadius - 0.03, 0, 0), new Pilot());
        Tick(world, 60);

        int standing = 0;
        double worstHeight = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            world.Tick();
            if (world.IsStanding(person))
                standing++;
            worstHeight = Math.Max(worstHeight, Math.Abs(world.PoseIn(person, station.Frame).Position.X - StandingRadius));
        }

        standing.Should().Be(600);
        worstHeight.Should().BeLessThan(1e-3, "the person rests on the floor");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APersonWhoJumpsInTheStationLandsWhereTheInertialLineOfItsTakeoffSays()
    {
        // The jump is an instant velocity of 3 m/s along up, relative to the ground, where the step of the jump starts. In
        // inertial space the person then moves on the straight line through its place there with that velocity plus the
        // velocity of the floor under it, ω × x, until the floor's plane meets it again. The spike measured 7 µm, the design's
        // bound, and the person here lands within 4e-8 m of the place.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        var pilot = new Pilot();
        Entity person = SpawnPerson(world, station, new Double3(StandingRadius - 0.01, 0, 0), pilot);
        Tick(world, 60);
        var samples = new List<Sample>();
        world.Tick();
        samples.Add(Take(world, station, person));
        pilot.Once = body => body.Jump(3);
        world.Tick();
        // The step of the next tick applies the jump, so this sample is where it starts.
        Sample start = Take(world, station, person);
        samples.Add(start);
        for (int tick = 0; tick < 90; tick++)
        {
            world.Tick();
            samples.Add(Take(world, station, person));
        }

        start.Standing.Should().BeTrue("the person stands where it jumps");
        Double3 x0 = start.InStation;
        Double3 up0 = -new Double3(x0.X, 0, x0.Z).Normalized();
        Double3 omega = AngularVelocity(samples[0], start);
        Double3 velocity = start.Station.Rotation.Rotate(up0 * 3) + Double3.Cross(omega, start.Inertial - start.Station.Position);
        (double time, Double3 place) = Landing(start, omega, start.Inertial, velocity, x0.X, 1e-3, 1);

        int landed = FirstConstrainedStep(samples, 3);
        landed.Should().BeGreaterThan(3, "the person flew and landed");
        Sample last = samples[landed - 1], before = samples[landed - 2];
        double dt = world.DeltaTime;
        (double measuredTime, Double3 measured) = Landing(last, omega, last.Inertial, (last.Inertial - before.Inertial) / dt, x0.X, -dt, 3 * dt);

        Double3.Distance(measured, place).Should().BeLessThan(7e-6);
        (last.Time - start.Time + measuredTime).Should().BeApproximately(time, 1e-4);
        samples[^1].Standing.Should().BeTrue("it stands again after it lands");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APersonInTheAirOfTheStationThatTheGameGivesAVelocityMovesOnTheInertialLineOfThatInstantVelocity()
    {
        // The velocity that the game gives is an instant velocity relative to the station, where the step that applies it
        // starts. In inertial space the person then moves on the straight line through its place there with that velocity,
        // turned into inertial axes, plus the velocity of the station's point there, ω × x.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        var pilot = new Pilot();
        Entity person = SpawnPerson(world, station, new Double3(50, 0, 0), pilot);
        Tick(world, 10);
        Sample before = Take(world, station, person);
        var velocity = new Double3(-2, 1, 3);
        pilot.Once = body => body.SetVelocity(velocity);
        world.Tick();
        Sample set = Take(world, station, person);
        Double3 inertialVelocity = set.Station.Rotation.Rotate(velocity) + Double3.Cross(AngularVelocity(before, set), set.Inertial - set.Station.Position);

        double worst = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            Sample s = Take(world, station, person);
            worst = Math.Max(worst, Double3.Distance(s.Inertial, set.Inertial + inertialVelocity * (s.Time - set.Time)));
        }

        worst.Should().BeLessThan(1e-5);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void APersonInTheAirOfARoomAtRestInTheStationMovesOnItsInertialLine()
    {
        // A room is a frame without a body at rest at 50 m from the station's axis, whose axes are the station's. The room's
        // linear gravity is its push reversed, ω² R = 4.9 m/s² away from the axis, and it acts in the room's axes at the
        // start of each step, so the turn of the step carries it into the axes at its end, as the station's turn carries the
        // person's chord. Left unturned, it would push the person sideways by ω³ R dt = 0.026 m/s², 1.3 cm in a second.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Tick(world, 3);
        FrameHandle room = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = At(50),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
        }));
        // The room reads its push from its second step on.
        Tick(world, 3);
        Entity person = world.Spawn(new SpawnDesc { Frame = room, Pose = At(2), Body = Person });
        Tick(world, 2);
        Double3 first = world.PoseIn(person, station.Space).Position;
        double firstTime = world.Time;
        world.Tick();
        Double3 velocity = (world.PoseIn(person, station.Space).Position - first) / (world.Time - firstTime);

        double worst = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(person, station.Space).Position, first + velocity * (world.Time - firstTime)));
        }

        world.PhysicsHomeOf(person).Should().Be(room, "the person stays in the room");
        worst.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnImpulseOnAPersonInTheAirOfTheStationGivesItTheInertialLineOfTheImpulsesChange()
    {
        // At 50 m from the axis the effective gravity is ω² r = 4.9 m/s², so the person keeps its walking form, and it
        // touches nothing. It moves on its inertial line. An impulse of 70 N s along the station's +Z changes its velocity
        // by 1 m/s along the station's +Z at the start of the step that applies it, in the axes of that moment, so after it
        // the person moves on the straight line through its place there with its old velocity plus that change.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        var pilot = new Pilot();
        Entity person = SpawnPerson(world, station, new Double3(50, 0, 0), pilot);
        world.Tick();
        var before = new List<Sample>();
        for (int tick = 0; tick < 30; tick++)
        {
            world.Tick();
            before.Add(Take(world, station, person));
        }
        pilot.Once = body => body.AddImpulse(new Vector3(0, 0, PersonMass));
        world.Tick();
        Sample impulse = Take(world, station, person);
        before.Add(impulse);
        double dt = world.DeltaTime;

        Double3 oldVelocity = (impulse.Inertial - before[^2].Inertial) / dt;
        double worstBefore = 0;
        for (int n = 0; n < before.Count; n++)
            worstBefore = Math.Max(worstBefore, Double3.Distance(before[n].Inertial, impulse.Inertial + oldVelocity * (before[n].Time - impulse.Time)));
        Double3 newVelocity = oldVelocity + impulse.Station.Rotation.Rotate(Double3.UnitZ);
        double worstAfter = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            Sample s = Take(world, station, person);
            worstAfter = Math.Max(worstAfter, Double3.Distance(s.Inertial, impulse.Inertial + newVelocity * (s.Time - impulse.Time)));
        }

        worstBefore.Should().BeLessThan(1e-5, "a person in the air moves on its inertial line");
        worstAfter.Should().BeLessThan(1e-5, "the impulse's change is turned with the station's axes of its moment, not one step more");
        world.Errors.Should().BeEmpty();
    }

    // ---- Hand-offs ----

    /// <summary>
    /// Spawns a person in <paramref name="frame"/> at 140 m on its X axis, with the spawn velocity that gives it 10 m/s along
    /// that axis in inertial space, when the frame's pose in inertial space is <paramref name="pose"/> and its inertial angular
    /// velocity in its own axes is <paramref name="frameSpin"/>: the place and the velocity in inertial space now. At 140 m
    /// from the station's axis the effective gravity is ω² r = 13.7 m/s², so the person keeps its walking form, and it touches
    /// nothing on its way out of the bubble of 150 m.
    /// </summary>
    private static Entity SpawnOutwardPerson(World world, FrameHandle frame, in DPose pose, Double3 frameSpin, out Double3 place, out Double3 velocity)
    {
        var start = new Double3(140, 0, 0);
        var inertialVelocity = new Double3(10, 0, 0);
        Entity person = world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose(start, DQuaternion.Identity),
            LinearVelocity = inertialVelocity - Double3.Cross(frameSpin, start),
            Body = Person,
        });
        place = pose.TransformPoint(start);
        velocity = pose.Rotation.Rotate(inertialVelocity);
        return person;
    }

    [Fact]
    public void APersonWhoLeavesTheStationsBubbleHandsOffWithTheVelocityOfItsInertialLine()
    {
        // In the air of the station the person moves on its inertial line, and Jolt's velocity is the chord of that line in
        // the station's turning axes. The hand-off into space carries its instant velocity at the end of the step, which the
        // chord rule's inverse gives from that chord: the velocity of the line, 10 m/s outward. The chord itself, read as an
        // instant velocity, is off by about ω² r dt / 2, 0.1 m/s.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Tick(world, 30);
        double start = world.Time;
        Entity person = SpawnOutwardPerson(world, station.Frame, station.Pose(world), new Double3(0, StationRate, 0), out Double3 place,
            out Double3 velocity);

        double worstPlace = 0;
        int ticks = TickUntil(world, () =>
        {
            worstPlace = Math.Max(worstPlace, Double3.Distance(world.PoseIn(person, station.Space).Position, place + velocity * (world.Time - start)));
            return world.GetFrame(person) == station.Space;
        }, 200);

        ticks.Should().BeLessThan(200, "the person leaves the bubble of 150 m within 2 s");
        Double3.Distance(world.VelocityIn(person, station.Space), velocity).Should().BeLessThan(1e-4);
        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            worstPlace = Math.Max(worstPlace, Double3.Distance(world.PoseIn(person, station.Space).Position, place + velocity * (world.Time - start)));
        }
        worstPlace.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>The angular acceleration of the outer frame in <see cref="APersonWhoLeavesTheStationIntoAParentThatSpinsUpKeepsItsInertialLine"/>, in rad/s².</summary>
    private const double OuterSpinUp = 0.02;

    [Fact]
    public void APersonWhoLeavesTheStationIntoAParentThatSpinsUpKeepsItsInertialLine()
    {
        // The station turns about the +X of an outer frame that spins up about its +Y from its spawn, so both frames turn,
        // and their rates change in every step. The hand-off reads the person's instant velocity in the station with the
        // station's turn of the step that just ended, and gives Jolt the chord behind it in the outer frame with the outer
        // frame's turn of that step, which the person's next update carries on along the line. In the outer frame, 150 m
        // from its axis after 1 s or more of spin-up, the Euler term alone is α r = 3 m/s², so the person keeps its walking
        // form there.
        using World world = NewWorld(out _, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        double spinStart = world.Time;
        FrameHandle outer = world.GetOwnedFrame(world.Spawn(SpinUp.Spawning(
            new SpawnDesc { Frame = space, AsFrame = new FrameSpec(FrameBounds.Sphere(1e4f)) }, world, OuterSpinUp)));
        // The tilt takes the spin's pole, +Y, to +X.
        var spin = new FrameSpin(DQuaternion.FromAxisAngle(Double3.UnitZ, -Math.PI / 2), StationRate);
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = outer, AsFrame = new FrameSpec(FrameBounds.Sphere(150)) }
            .Spinning(world, spin, firstMove: true)));
        Tick(world, world.Options.TickRate);
        DPose pose = world.Frames.Relative(station, space);
        Double3 inSpace = new Double3(0, OuterSpinUp * (world.Time - spinStart), 0) + world.Frames.Relative(outer, space).Rotation.Rotate(spin.AngularVelocity);
        double start = world.Time;
        Entity person = SpawnOutwardPerson(world, station, pose, pose.Rotation.Conjugate().Rotate(inSpace), out Double3 place, out Double3 velocity);

        double worstPlace = 0;
        int ticks = TickUntil(world, () =>
        {
            worstPlace = Math.Max(worstPlace, Double3.Distance(world.PoseIn(person, space).Position, place + velocity * (world.Time - start)));
            return world.GetFrame(person) == outer;
        }, 200);
        ticks.Should().BeLessThan(200, "the person leaves the bubble of 150 m within 2 s");

        for (int tick = 0; tick < 120; tick++)
        {
            world.Tick();
            worstPlace = Math.Max(worstPlace, Double3.Distance(world.PoseIn(person, space).Position, place + velocity * (world.Time - start)));
        }

        world.GetFrame(person).Should().Be(outer);
        world.IsStanding(person).Should().BeFalse();
        worstPlace.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }
}
