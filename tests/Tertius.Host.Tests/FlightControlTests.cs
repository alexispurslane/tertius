using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using static Tertius.Testing.WorldTesting;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo shuttle's main engine and attitude autopilot, on the World and the real Jolt backend: each reads what a
/// component of its own entity added to the entity's events in the same tick, a <see cref="Throttle"/> or an
/// <see cref="AttitudeTarget"/>, and keeps nothing from an earlier tick. The shuttle starts parked, unturned, in a root
/// frame without gravity, and unparks on its first tick.
/// </summary>
public sealed class FlightControlTests : IDisposable
{
    private readonly World world = NewJoltWorld();
    private readonly FrameHandle space;
    private Entity shuttle;

    public FlightControlTests() => space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));

    public void Dispose() => world.Dispose();

    private void Launch(params IComponent[] components) =>
        shuttle = Shuttle.Park(world, space, DPose.Identity, components: [new Unpark(), .. components]);

    private static MainEngine Engine() => new(Shuttle.Mass * Shuttle.ThrustAcceleration);

    /// <summary>The shuttle's acceleration over <paramref name="ticks"/> ticks, in m/s², in the axes of its frame.</summary>
    private Double3 AccelerationOver(int ticks)
    {
        Double3 before = world.VelocityIn(shuttle, space);
        Tick(world, ticks);
        return (world.VelocityIn(shuttle, space) - before) / (ticks * world.DeltaTime);
    }

    [Fact]
    public void TheEnginePushesTheHullAlongItsNoseWithTheThrottleOfTheTick()
    {
        Launch(new ThrottleFor(ticks: int.MaxValue, value: 0.5f), Engine());
        Tick(world, 3);

        Double3 acceleration = AccelerationOver(30);

        acceleration.Z.Should().BeApproximately(-0.5 * Shuttle.ThrustAcceleration, 0.01);
        Math.Abs(acceleration.X).Should().BeLessThan(1e-6);
        Math.Abs(acceleration.Y).Should().BeLessThan(1e-6);
    }

    /// <summary>The throttle stops after 30 ticks, so the engine stops: it keeps no throttle from an earlier tick.</summary>
    [Fact]
    public void ATickWithoutAThrottleGivesNoThrust()
    {
        Launch(new ThrottleFor(ticks: 30, value: 1), Engine());
        Tick(world, 40);

        world.VelocityIn(shuttle, space).Length.Should().BeGreaterThan(10, "the engine pushed while the throttle came");
        AccelerationOver(30).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// A throttle that a component adds after the engine has run, at priority 11, comes too late for it, so the engine gives no thrust,
    /// rather than the throttle of the tick before.
    /// </summary>
    [Fact]
    public void AnEngineThatRunsBeforeTheComponentThatAddsItsThrottleGivesNoThrust()
    {
        Launch(new ThrottleFor(ticks: int.MaxValue, value: 1, priority: 11), Engine());

        Tick(world, 30);

        world.VelocityIn(shuttle, space).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>When one tick brings two throttles, the last one counts, so a component that runs later overrides an earlier one.</summary>
    [Fact]
    public void TheLastThrottleOfATickCounts()
    {
        Launch(new ThrottleFor(ticks: int.MaxValue, value: 1), new ThrottleFor(ticks: int.MaxValue, value: 0.25f), Engine());
        Tick(world, 3);

        AccelerationOver(30).Z.Should().BeApproximately(-0.25 * Shuttle.ThrustAcceleration, 0.01);
    }

    [Fact]
    public void AThrottleAboveFullIsAnErrorAndGivesNoThrust()
    {
        Launch(new ThrottleFor(ticks: int.MaxValue, value: 1.5f), Engine());

        Tick(world, 30);

        world.VelocityIn(shuttle, space).Length.Should().BeLessThan(1e-6);
        world.Errors.Should().Contain(error => error.Contains(nameof(MainEngine)) && error.Contains("A throttle lies from 0 to 1"));
    }

    /// <summary>
    /// A target a quarter turn about the roof's axis: the attitude autopilot turns the hull to it, at most
    /// <see cref="AttitudeAutopilot.MaxTurnRate"/>, about 10 s for the quarter turn, and holds it there.
    /// </summary>
    [Fact]
    public void TheAttitudeAutopilotTurnsTheHullToTheTargetOfEachTick()
    {
        DQuaternion target = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);
        Launch(new TargetFor(ticks: int.MaxValue, target), new AttitudeAutopilot(Shuttle.Inertia));

        Tick(world, 1200);

        double left = world.PoseIn(shuttle, space).Rotation.AngularVelocityTo(target, 1).Length;
        (left * 180 / Math.PI).Should().BeLessThan(1);
        world.AngularVelocityIn(shuttle, space).Length().Should().BeLessThan(0.01f);
    }

    /// <summary>
    /// The hull's principal axes tilt about 1 degree about X from its own (<see cref="Shuttle.Inertia"/>), so its inertia
    /// couples a turn about its Y with one about its Z. A target a quarter turn about the roof's axis: the autopilot turns
    /// the torque that it gives into the principal axes, so in each of the first 60 ticks that the hull turns, its angular
    /// velocity lies within 0.5 degrees of Y. With only the moments in the hull's own axes, the angle starts at 4.7
    /// degrees.
    /// </summary>
    [Fact]
    public void TheAutopilotTurnsAHullWithTiltedPrincipalAxesAboutTheAxisThatItAsksFor()
    {
        Launch(new TargetFor(ticks: int.MaxValue, DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)), new AttitudeAutopilot(Shuttle.Inertia));

        double worst = 0;
        int turning = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            System.Numerics.Vector3 angular = world.AngularVelocityIn(shuttle, space);
            if (angular.Length() < 1e-4f)
                continue;
            turning++;
            Double3 body = world.PoseIn(shuttle, space).Rotation.Conjugate().Rotate((Double3)angular);
            worst = Math.Max(worst, Math.Acos(Math.Clamp(body.Y / body.Length, -1, 1)) * 180 / Math.PI);
        }

        turning.Should().BeGreaterThan(50, "the hull turns from its first ticks");
        worst.Should().BeLessThan(0.5, "the hull turns about the axis that the autopilot asks for");
    }

    /// <summary>
    /// A target a quarter turn about the roof's axis, with a turn rate of 0.01 rad/s: after 10 s the hull turns at that rate,
    /// and has turned about 0.1 radians, not the 1.5 radians of <see cref="AttitudeAutopilot.MaxTurnRate"/>.
    /// </summary>
    [Fact]
    public void TheAttitudeAutopilotTurnsNoFasterThanTheTurnRateOfTheTarget()
    {
        DQuaternion target = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);
        Launch(new TargetFor(ticks: int.MaxValue, target, maxTurnRate: 0.01), new AttitudeAutopilot(Shuttle.Inertia));

        Tick(world, 600);

        world.AngularVelocityIn(shuttle, space).Length().Should().BeApproximately(0.01f, 0.0005f);
        (Math.PI / 2 - world.PoseIn(shuttle, space).Rotation.AngularVelocityTo(target, 1).Length).Should().BeApproximately(0.1, 0.01);
    }

    /// <summary>
    /// A turn rate of NaN would lift every cap, and a negative one would turn the hull away from its target. Each is an
    /// error that the world logs, as a bad throttle is for the main engine, and the hull gets no torque.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.01)]
    public void ATurnRateThatIsNaNOrBelowZeroIsAnErrorAndGivesNoTorque(double maxTurnRate)
    {
        DQuaternion target = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);
        Launch(new TargetFor(ticks: int.MaxValue, target, maxTurnRate), new AttitudeAutopilot(Shuttle.Inertia));

        Tick(world, 30);

        world.AngularVelocityIn(shuttle, space).Length().Should().BeLessThan(1e-6f);
        world.Errors.Should().Contain(error => error.Contains(nameof(AttitudeAutopilot)) && error.Contains("A turn rate lies from 0 up"));
    }

    /// <summary>The target stops after 60 ticks, while the hull turns, so the autopilot gives no more torque and the hull turns on at the rate it had.</summary>
    [Fact]
    public void ATickWithoutAnAttitudeTargetGivesNoTorque()
    {
        Launch(new TargetFor(ticks: 60, DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2)), new AttitudeAutopilot(Shuttle.Inertia));
        Tick(world, 70);
        System.Numerics.Vector3 turning = world.AngularVelocityIn(shuttle, space);

        Tick(world, 60);

        turning.Length().Should().BeGreaterThan(0.05f, "the autopilot turned the hull while the target came");
        (world.AngularVelocityIn(shuttle, space) - turning).Length().Should().BeLessThan(1e-4f);
    }

    /// <summary>
    /// The shuttle's frame has no point gravity to climb in, so the ascent autopilot refuses the ignition: it stays parked,
    /// and the hull stays static where it stood.
    /// </summary>
    [Fact]
    public void WithoutPointGravityTheAscentAutopilotStaysParked()
    {
        shuttle = Shuttle.Park(world, space, DPose.Identity, components: Shuttle.FlightControls(climbHeight: 30, plane: new LaunchPlane(space, -Double3.UnitX), station: Entity.Null));
        world.Post(Event.Create(shuttle, Entity.Null, new Ignition()));

        Tick(world, 30);

        world.GetComponent<AscentAutopilot>(shuttle)!.State.Value.Should().BeOfType<Parked>();
        world.PoseIn(shuttle, space).Position.Length.Should().Be(0);
        world.Frames.HullMotion(world.GetOwnedFrame(shuttle)).Should().Be(BodyMotion.Static);
    }

    /// <summary>A test component that adds a <see cref="Throttle"/> of <paramref name="value"/> to its entity's events on each of its first <paramref name="ticks"/> ticks.</summary>
    /// <summary>
    /// A hull in a frame that turns in the frame of its <see cref="LaunchPlane"/>, as earth-surface turns in earth: the
    /// plane's frame is a root with Earth's point gravity, and the hull's frame sits at its center, turned 90 degrees about
    /// +Y and turning about +Y at the sidereal rate. The hull's frame has no gravity of its own, so the autopilot can take
    /// Earth's G·M only from the plane's frame. Over the test's three ticks the missing pull moves the hull by 4 cm. The hull, dynamic, starts at
    /// <paramref name="place"/> with <paramref name="velocity"/>, both in the plane's frame, converted into its own frame by
    /// the frame's turn, with its nose along the velocity. Its components are an <see cref="AscentAutopilot"/> on the plane
    /// whose normal is -X, with <paramref name="climbHeight"/>, by default below zero, so that the ignition starts the climb
    /// at once, and an
    /// <see cref="AscentRecorder"/>. The world runs a tick, in which the spawn lands, posts the ignition, and runs the tick that
    /// handles it.
    /// </summary>
    private AscentRecorder AscentInATurnedFrame(Double3 place, Double3 velocity, double climbHeight = -1)
    {
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), GravityField.Point(DemoScenes.EarthMu)));
        var spin = new Tertius.Frames.Rails.FrameSpin(DQuaternion.Identity, DemoScenes.SiderealRate, PhaseAtEpoch: Math.PI / 2);
        FrameHandle turned = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = new DPose(Double3.Zero, spin.RotationAt(world.Time)),
            FirstMove = new MotionIntent(spin.Sample(Double3.Zero, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e8f)),
            Components = [new SpinMotion { Spin = spin }],
        }));
        DQuaternion turn = spin.RotationAt(world.Time);
        Double3 velocityInTurned = turn.Conjugate().Rotate(velocity - Double3.Cross(spin.AngularVelocity, place));
        var recorder = new AscentRecorder(planet);
        shuttle = world.Spawn(new SpawnDesc
        {
            Frame = turned,
            Pose = new DPose(turn.Conjugate().Rotate(place), turn.Conjugate() * AttitudeAutopilot.Facing(velocity, place)),
            LinearVelocity = velocityInTurned.ToVector3(),
            Body = Shuttle.Body(BodyMotion.Dynamic),
            AsFrame = new FrameSpec(new FrameBounds(Shuttle.Bubble)),
            Components = [new AscentAutopilot(Shuttle.Mass * Shuttle.ThrustAcceleration, Shuttle.ParkingOrbitRadius, climbHeight, new LaunchPlane(planet, -Double3.UnitX)), recorder],
        });
        Tick(world, 1);
        world.Post(Event.Create(shuttle, Entity.Null, new Ignition()));
        Tick(world, 2);
        return recorder;
    }

    /// <summary>The nose of an attitude target given in the axes of the hull's frame, in the axes of the plane's frame.</summary>
    private static Double3 NoseInPlaneAxes(AscentRecorder recorder) =>
        (recorder.FrameInPlane * recorder.Target!.Value.Rotation).Rotate(-Double3.UnitZ);

    private static double Angle(Double3 a, Double3 b) => Math.Acos(Math.Clamp(Double3.Dot(a.Normalized(), b.Normalized()), -1, 1));

    /// <summary>
    /// On the parking circle in the plane's frame, moving 45 degrees between the pole and the turn's way, so that neither
    /// the frame's 90 degrees about the pole nor its turn leaves the direction of the velocity alone, the autopilot is in
    /// its orbit case. The nose of its target, turned back from the axes of the hull's frame into the plane frame's axes,
    /// lies within 1e-6 rad of the velocity in the plane's frame. The velocity in the turning frame differs from it by the
    /// frame's turn, about 480 m/s.
    /// </summary>
    [Fact]
    public void TheAscentAutopilotAimsInTheFrameOfItsPlaneFromAHullInATurnedFrame()
    {
        double r = Shuttle.ParkingOrbitRadius;
        AscentRecorder recorder = AscentInATurnedFrame(new Double3(0, 0, r), new Double3(-1, 1, 0).Normalized() * Math.Sqrt(DemoScenes.EarthMu / r));

        world.GetComponent<AscentAutopilot>(shuttle)!.State.Value.Should().BeOfType<Orbit>();
        Angle(NoseInPlaneAxes(recorder), recorder.Velocity).Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// With a climb height of 1,000 km the autopilot stays in its ramp case, and the nose of its target, turned back into
    /// the plane frame's axes, lies within 1e-6 rad of the velocity in the plane's frame, as in the orbit case.
    /// </summary>
    [Fact]
    public void OnTheRampTheAscentAutopilotAimsAlongItsVelocityInTheFrameOfItsPlane()
    {
        double r = Shuttle.ParkingOrbitRadius;
        AscentRecorder recorder = AscentInATurnedFrame(new Double3(0, 0, r), new Double3(-1, 1, 0).Normalized() * 2e3, climbHeight: 1e6);

        world.GetComponent<AscentAutopilot>(shuttle)!.State.Value.Should().BeOfType<Ramp>();
        Angle(NoseInPlaneAxes(recorder), recorder.Velocity).Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// On the parking circle, 5 m/s slower than the circle and climbing at 1 m/s, the autopilot is in its climb case. Its throttle and the nose of its target, turned back
    /// into the plane frame's axes, are those of <see cref="AscentGuidance"/> on the hull's state in the plane's frame,
    /// with the plane's normal in that frame's axes and Earth's G·M.
    /// </summary>
    [Fact]
    public void TheAscentAutopilotReadsItsStateInTheFrameOfItsPlane()
    {
        double r = Shuttle.ParkingOrbitRadius;
        AscentRecorder recorder = AscentInATurnedFrame(new Double3(0, 0, r), new Double3(0, Math.Sqrt(DemoScenes.EarthMu / r) - 5, 1));
        var guidance = new AscentGuidance(DemoScenes.EarthMu, Shuttle.ParkingOrbitRadius, Shuttle.ThrustAcceleration, -Double3.UnitX);
        Double3 acceleration = guidance.Acceleration(recorder.Position, recorder.Velocity);

        world.GetComponent<AscentAutopilot>(shuttle)!.State.Value.Should().BeOfType<Climb>();
        recorder.Throttle.Should().BeLessThan(1, "the guidance asks for less than full throttle near the circle");
        recorder.Throttle.Should().BeApproximately((float)Math.Min(1, acceleration.Length / guidance.MaxAcceleration), 1e-6f);
        Angle(NoseInPlaneAxes(recorder), acceleration).Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// A test component at priority 5, after the ascent autopilot: in each tick it keeps the last <see cref="Throttle"/>
    /// and the last <see cref="AttitudeTarget"/> of its entity's events, the hull's state in <paramref name="plane"/> that
    /// the autopilot read, and the turn from the axes of the hull's frame to those of the plane's frame.
    /// </summary>
    private sealed class AscentRecorder(FrameHandle plane) : IComponent
    {
        public float? Throttle { get; private set; }
        public AttitudeTarget? Target { get; private set; }
        public Double3 Position { get; private set; }
        public Double3 Velocity { get; private set; }
        public DQuaternion FrameInPlane { get; private set; }

        public int Priority => 5;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            Throttle = null;
            Target = null;
            foreach (ref Throttle each in events.Of<Throttle>())
                Throttle = each.Value;
            foreach (ref AttitudeTarget each in events.Of<AttitudeTarget>())
                Target = each;
            DPose inPlane = self.PoseIn(plane);
            Position = inPlane.Position;
            Velocity = self.VelocityIn(plane);
            FrameInPlane = inPlane.Rotation * self.PoseIn(self.Frame).Rotation.Conjugate();
        }
    }

    private sealed class ThrottleFor(int ticks, float value, int priority = 0) : IComponent
    {
        private int handled;

        public int Priority => priority;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (handled++ < ticks)
                events.Add(new Throttle { Value = value });
        }
    }

    /// <summary>
    /// A test component that adds an <see cref="AttitudeTarget"/> of <paramref name="target"/>, with the turn rate
    /// <paramref name="maxTurnRate"/>, to its entity's events on each of its first <paramref name="ticks"/> ticks.
    /// </summary>
    private sealed class TargetFor(int ticks, DQuaternion target, double maxTurnRate = AttitudeAutopilot.MaxTurnRate) : IComponent
    {
        private int handled;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (handled++ < ticks)
                events.Add(new AttitudeTarget { Rotation = target, MaxTurnRate = maxTurnRate });
        }
    }
}

/// <summary>
/// <see cref="AttitudeAutopilot.Facing"/>, the rotation that points a hull's nose along one direction and its roof along
/// another. The nose is the rotated -Z, and the roof is the rotated +Y, as near to the given roof as it can be across the
/// nose. The rotation comes from the rotation matrix of the hull's axes, which has four ways to take its quaternion, one
/// for each of: a positive sum of the three diagonal entries, and the X, the Y or the Z entry being the largest of the
/// three when that sum is not positive. Each case below names the way that it reaches.
/// </summary>
public sealed class AttitudeFacingTests
{
    // The unit axes of the hull before it turns: the nose is -Z and the roof is +Y.
    private static readonly Double3 NoseAxis = -Double3.UnitZ;
    private static readonly Double3 RoofAxis = Double3.UnitY;

    /// <summary>
    /// Each row is a nose (three numbers) and a roof (three numbers). The quarter turns and the half turns lie on the
    /// axes, where two diagonal entries can tie at zero, and a tie does not count as larger. The slanted rows have
    /// nose and roof that are neither unit length nor at a right angle. The three slanted rows of the last three ways
    /// have their largest diagonal entry alone, by at least 0.4 over the next one, and every entry off the diagonal at
    /// least 0.4 away from zero, so that each sign in their quaternion matters.
    /// </summary>
    [Theory]
    // Positive sum of the diagonal entries.
    [InlineData(0, 0, -1, 0, 1, 0)] // no turn
    [InlineData(1, 0, 0, 0, 1, 0)] // a quarter turn about Y
    [InlineData(-1, -2, -3, 1, 2, 0)] // slanted, the sum is 2.4
    [InlineData(0, 0, -4, 0, 3, 2)] // the nose is already along -Z, and the roof is slanted toward it
    [InlineData(0, 0, -1, 0, 1e-6, -1)] // the roof is almost along the nose, but 1e-6 of its length across it is enough
    // The sum is not positive, and the X entry is the largest.
    [InlineData(0, 0, 1, 0, -1, 0)] // a half turn about X
    [InlineData(-3, -2, 2, 1, -3, 3)] // slanted, the diagonal is (0.00, -0.51, -0.49)
    // The sum is not positive, and the Y entry is the largest.
    [InlineData(0, 0, 1, 0, 1, 0)] // a half turn about Y
    [InlineData(-2, -3, 2, -1, -3, 3)] // slanted, the diagonal is (-0.51, 0.00, -0.49)
    // The sum is not positive, and the Z entry is the largest, or no entry is larger than both others: the last way.
    [InlineData(0, 0, -1, 0, -1, 0)] // a half turn about Z
    [InlineData(-2, -1, -1, 1, -3, -2)] // slanted, the diagonal is (-0.12, -0.71, 0.41)
    [InlineData(1, 0, 0, 0, -1, 0)] // the diagonal is (0, -1, 0): X and Z tie at zero, so the Z way
    [InlineData(0, -1, 0, 1, 0, 0)] // the diagonal is (0, 0, 0): all three tie, so the Z way
    [InlineData(0, 1, 0, 0, 0, -1)] // the diagonal is (-1, 0, 0): Y and Z tie at zero, so the Z way
    public void TheRotationTurnsItsNoseAxisAlongTheNoseAndItsRoofAxisAcrossTheNoseTowardTheRoof(
        double noseX, double noseY, double noseZ, double roofX, double roofY, double roofZ)
    {
        Double3 nose = new(noseX, noseY, noseZ);
        Double3 roof = new(roofX, roofY, roofZ);
        Double3 expectedNose = nose.Normalized();
        Double3 expectedRoof = (roof - expectedNose * Double3.Dot(roof, expectedNose)).Normalized();

        DQuaternion facing = AttitudeAutopilot.Facing(nose, roof);

        facing.IsUnit.Should().BeTrue($"{facing} is a rotation");
        ShouldBeNear(facing.Rotate(NoseAxis), expectedNose, "the rotated nose axis");
        ShouldBeNear(facing.Rotate(RoofAxis), expectedRoof, "the rotated roof axis");
    }

    /// <summary>A zero nose has no direction to point along, and a zero roof has no direction to turn toward.</summary>
    [Theory]
    [InlineData(0, 0, 0, 0, 1, 0)] // a zero nose
    [InlineData(0, 0, -1, 0, 0, 0)] // a zero roof
    [InlineData(0, 0, 0, 0, 0, 0)] // both
    public void ANoseOrARoofOfZeroLengthIsRefused(
        double noseX, double noseY, double noseZ, double roofX, double roofY, double roofZ)
    {
        Action facing = () => AttitudeAutopilot.Facing(new Double3(noseX, noseY, noseZ), new Double3(roofX, roofY, roofZ));

        facing.Should().Throw<ArgumentException>().WithMessage("*need a direction each*");
    }

    /// <summary>
    /// A roof along the nose, or against it, has no part across the nose, so it gives no turn about the nose. The test
    /// sizes the part across the nose against the roof's own length, so a long roof refuses what a short roof accepts.
    /// </summary>
    [Theory]
    [InlineData(0, 0, -1, 0, 0, -2)] // along the nose
    [InlineData(0, 0, -1, 0, 0, 3)] // against the nose
    [InlineData(1, 2, 3, 2, 4, 6)] // along a slanted nose
    [InlineData(0, 0, -1, 0, 1e-10, -1)] // across the nose by 1e-10 of its length, which is too little
    public void ARoofAlongTheNoseIsRefused(
        double noseX, double noseY, double noseZ, double roofX, double roofY, double roofZ)
    {
        Action facing = () => AttitudeAutopilot.Facing(new Double3(noseX, noseY, noseZ), new Double3(roofX, roofY, roofZ));

        facing.Should().Throw<ArgumentException>().WithMessage("*lies along the nose*");
    }

    private static void ShouldBeNear(Double3 actual, Double3 expected, string what)
    {
        actual.X.Should().BeApproximately(expected.X, 1e-9, $"X of {what} is {actual}, not {expected}");
        actual.Y.Should().BeApproximately(expected.Y, 1e-9, $"Y of {what} is {actual}, not {expected}");
        actual.Z.Should().BeApproximately(expected.Z, 1e-9, $"Z of {what} is {actual}, not {expected}");
    }
}
