using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Free bodies in a turning frame, on the World and the real Jolt backend (docs/plans/spin-gravity.md, "The two-turn
/// update", "The angular form", "Impulses" and "The flow per kind of entity"): physics keeps every awake dynamic body on its
/// inertial line, and its spin at its inertial angular velocity, as the frame's axes see them. The station of the tests is
/// a frame without a body and without a field, which turns in place in a root frame without a field about its +Y at
/// 0.31321 rad/s, the rate that gives 9.81 m/s² at 100 m. The root frame's axes are inertial space.
/// <para>
/// Most stations here spawn without a first motion intent, together with their bodies, so that every body's first chord
/// comes from a step in which the station held still (docs/plans/spin-gravity.md, "Frames without a last turn"): there a
/// body's spawn velocity is its inertial velocity, and its chord holds no turn, so its inertial line is known exactly. From
/// its second step on the station turns at its full rate, and the bodies keep that line. The expected path of each body is
/// worked out from inertial space and the station's rotations at the ends of the steps, never from the turns that the engine
/// derives.
/// </para>
/// </summary>
public class SpinStationBodyTests
{
    private const double StationRate = 0.31321;
    private static readonly BodyDesc UndampedCrate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10);
    // A ball of 10 kg and radius 0.5 m has the moment of inertia 2/5 m r² = 1 kg m² about every axis.
    private static readonly BodyDesc UndampedBall = new(new SphereShapeDesc(0.5f), Mass: 10, LinearDamping: 0, AngularDamping: 0);
    private static readonly BodyDesc Ball = new(new SphereShapeDesc(0.5f), Mass: 10);
    private static readonly BodyDesc Person = new(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);

    private sealed record Station(FrameHandle Space, FrameHandle Frame)
    {
        /// <summary>The station's pose in inertial space, at the end of the last step.</summary>
        public DPose Pose(World world) => world.Frames.LocalPose(Frame);
    }

    /// <summary>
    /// A root frame without a field, and in it a station that turns in place, with a bubble of radius
    /// <paramref name="bubble"/>, at <paramref name="rate"/> rad/s, or at <see cref="StationRate"/>.
    /// </summary>
    private static Station SpawnStation(World world, bool firstMove, float bubble = 150, double rate = StationRate)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(new SpawnDesc { Frame = space, AsFrame = new FrameSpec(FrameBounds.Sphere(bubble)) }
            .Spinning(world, new FrameSpin(DQuaternion.Identity, rate), firstMove));
        return new Station(space, world.GetOwnedFrame(owner));
    }

    private static Entity SpawnIn(World world, Station station, BodyDesc body, Double3 place, Vector3 angularVelocity = default,
        IComponent? component = null) =>
        world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = new DPose(place, DQuaternion.Identity),
            AngularVelocity = angularVelocity,
            Body = body,
            Components = component is null ? [] : [component],
        });

    /// <summary>The angle between two rotations, in radians.</summary>
    private static double AngleBetween(DQuaternion a, DQuaternion b) => 2 * DQuaternion.Identity.AngularVelocityTo(a * b.Conjugate(), 2).Length;

    /// <summary>
    /// The expected path of a free body in the station without a field: its inertial line, with Jolt's linear damping
    /// relative to the station's axes (docs/plans/spin-gravity.md, "Jolt's damping in the shadow"). In each step the chord of
    /// the body in the station's axes is the chord that keeps it on its inertial line, times Jolt's damping factor
    /// max(0, 1 − c dt), which Jolt computes in floats. Without damping it is the inertial line. A force adds its
    /// acceleration times the step to the chord before the damping, in the station's axes, as Jolt adds it.
    /// </summary>
    private sealed class ExpectedPath(Double3 place, float damping, float dt)
    {
        private readonly double factor = MathF.Max(0f, 1f - damping * dt);
        // The body's places in inertial space at the ends of the last two steps.
        private Double3 previous = place;

        public Double3 Place { get; private set; } = place;

        /// <summary>
        /// Moves the body over one step, in which the station went from <paramref name="start"/> to <paramref name="end"/>,
        /// and a force gave the body the acceleration <paramref name="push"/>, in m/s², in the station's axes.
        /// </summary>
        public void Step(in DPose start, in DPose end, Double3 push = default)
        {
            Double3 from = start.InverseTransformPoint(Place);
            Double3 free = end.InverseTransformPoint(2 * Place - previous);
            previous = Place;
            Place = end.TransformPoint(from + (free - from + push * ((double)dt * dt)) * factor);
        }
    }

    /// <summary>
    /// The expected rotation of a free body in the station: it keeps its inertial angular velocity, with Jolt's angular
    /// damping relative to the station's axes (docs/plans/spin-gravity.md, "The angular form" and "Jolt's damping in the
    /// shadow"). Jolt turns a body in the station's axes by the rotation of its angular velocity times the step. In each
    /// step that turn is the one that keeps the body's turn in inertial space over the step that ended, Q_end⁻¹ E Q_start,
    /// with Q the station's rotation and E the body's inertial turn of the step before, and Jolt shrinks its angle by its
    /// damping factor max(0, 1 − c dt), in floats.
    /// </summary>
    private sealed class ExpectedTumble(DQuaternion rotation, DQuaternion firstTurn, float damping, float dt)
    {
        private readonly double factor = MathF.Max(0f, 1f - damping * dt);
        // The body's inertial turn over the last step.
        private DQuaternion turn = firstTurn;

        /// <summary>The body's rotation in inertial space.</summary>
        public DQuaternion Rotation { get; private set; } = rotation;

        public void Step(DQuaternion start, DQuaternion end)
        {
            Double3 chord = DQuaternion.Identity.AngularVelocityTo(end.Conjugate() * turn * start, dt) * factor;
            DQuaternion inStation = DQuaternion.FromAxisAngle(chord, chord.Length * dt) * (start.Conjugate() * Rotation);
            DQuaternion next = end * inStation;
            turn = next * Rotation.Conjugate();
            Rotation = next;
        }
    }

    // ---- Rest and free motion ----

    [Fact]
    public void AnUndampedCrateAtRestInInertialSpaceKeepsItsPlaceAndItsRotationThereWhileTheStationTurnsUnderIt()
    {
        // Over 10 minutes, as the design asks. Without the velocity shadow, the float rounding of Jolt's velocity moved the
        // crate 0.8 mm in a minute and 24 mm in 10 minutes, in a run of this test before the shadow came. The shadow keeps
        // the update's chord and place in doubles and takes Jolt's rounding back in every step (docs/plans/spin-gravity.md,
        // "V6, the velocity shadow").
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world, firstMove: false);
        var place = new Double3(50, 0, 0);
        Entity crate = SpawnIn(world, station, UndampedCrate, place);
        DPose start = DPose.Compose(station.Pose(world), new DPose(place, DQuaternion.Identity));

        double worstPlace = 0, worstAngle = 0, farthestInStation = 0;
        for (int tick = 0; tick < 600 * world.Options.TickRate; tick++)
        {
            world.Tick();
            DPose pose = world.PoseIn(crate, station.Space);
            worstPlace = Math.Max(worstPlace, Double3.Distance(pose.Position, start.Position));
            worstAngle = Math.Max(worstAngle, AngleBetween(pose.Rotation, start.Rotation));
            farthestInStation = Math.Max(farthestInStation, Double3.Distance(world.PoseIn(crate, station.Frame).Position, place));
        }

        // Within 1 mm, as the design asks, and in fact within 0.5 µm: the shadow also takes back Jolt's rounding of the
        // crate's place in every step, without which the crate drifted 1.6 µm in a run of this test.
        worstPlace.Should().BeLessThan(5e-7, "a free body moves on a straight line in inertial space, and this one is at rest there");
        worstAngle.Should().BeLessThan(1e-4, "a free body keeps its inertial angular velocity, which is zero here");
        farthestInStation.Should().BeGreaterThan(99, "the station turned under the crate, so in its axes the crate went round the axis");
        world.GetFrame(crate).Should().Be(station.Frame);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnUndampedCrateAtRestInInertialSpaceInAFallingFrameInsideTheStationKeepsItsPlaceThere()
    {
        // Under these rules every group falls in a numeric frame, so the crate lives in a falling frame below the station.
        // The frame's origin moves on the inertial line of a free body at its place, and the crate's update acts on its
        // semantic place and chord, its home's offset and frame velocity included, so it feels only the difference
        // (docs/plans/spin-gravity.md, "The origin of a falling frame").
        using World world = NewWorld(out _, debugChecks: false, configure: options => options.NumericFrames = NumericFrameTesting.EveryGroupFalls);
        Station station = SpawnStation(world, firstMove: false);
        var place = new Double3(50, 0, 0);
        Entity crate = SpawnIn(world, station, UndampedCrate, place);
        Double3 start = station.Pose(world).TransformPoint(place);

        double worst = 0;
        bool fell = false;
        for (int tick = 0; tick < 20 * world.Options.TickRate; tick++)
        {
            world.Tick();
            fell |= world.Frames.IsNumeric(world.PhysicsHomeOf(crate));
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(crate, station.Space).Position, start));
        }

        fell.Should().BeTrue("the crate fell in a numeric frame below the station");
        worst.Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnUndampedCrateThatGetsAFallingFrameWhileTheStationTurnsKeepsItsPlaceInInertialSpace()
    {
        // Under these rules a group of free bodies gets a falling frame from 10 m/s relative to its semantic frame. The crate
        // is at rest in inertial space 50 m from the axis, so once the station turns it moves at 15.7 m/s in the station's
        // axes, and it moves from the station's system into a falling frame below it. Its velocity shadow moves with it, by
        // the difference of the two homes' places and frame velocities, as its state does. A shadow that stayed behind would
        // read the move as a contact of 15.7 m/s and turn it.
        NumericFrameOptions rules = NumericFrameTesting.EveryGroupFalls with { CreateSpeed = 10, LeaveSpeed = 4 };
        using World world = NewWorld(out _, debugChecks: false, configure: options => options.NumericFrames = rules);
        Station station = SpawnStation(world, firstMove: false);
        var place = new Double3(50, 0, 0);
        Entity crate = SpawnIn(world, station, UndampedCrate, place);
        Double3 start = station.Pose(world).TransformPoint(place);
        world.Tick();
        world.PhysicsHomeOf(crate).Should().Be(station.Frame, "the crate rested in the station's axes while the station held still");

        double worst = 0;
        bool fell = false;
        for (int tick = 0; tick < 20 * world.Options.TickRate; tick++)
        {
            world.Tick();
            fell |= world.Frames.IsNumeric(world.PhysicsHomeOf(crate));
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(crate, station.Space).Position, start));
        }

        fell.Should().BeTrue("the crate fell in a numeric frame below the station");
        worst.Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACrateWithTheDefaultDampingThatStartsAtRestInInertialSpaceFollowsItsExpectedPath()
    {
        // In the station's axes the crate first moves round the axis at the rate of the station backward. The damping takes
        // that motion relative to the station's axes away, like air that turns with the station, so the crate is drawn into
        // the station's turn and drifts outward.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world, firstMove: false, bubble: 1000);
        var place = new Double3(20, 0, 0);
        Entity crate = SpawnIn(world, station, Crate, place);
        var path = new ExpectedPath(station.Pose(world).TransformPoint(place), Crate.LinearDamping, world.DeltaTime);
        // In its first step the station holds still, and the crate with it.
        world.Tick();

        double worst = 0;
        for (int tick = 1; tick < 30 * world.Options.TickRate; tick++)
        {
            DPose start = station.Pose(world);
            world.Tick();
            path.Step(start, station.Pose(world));
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(crate, station.Space).Position, path.Place));
        }

        worst.Should().BeLessThan(1e-3);
        Double3 inStation = world.PoseIn(crate, station.Frame).Position;
        Math.Sqrt(inStation.X * inStation.X + inStation.Z * inStation.Z).Should().BeGreaterThan(21, "the damping drew the crate into the turn, and it drifted outward");
        world.GetFrame(crate).Should().Be(station.Frame);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACrateWithTheDefaultDampingInFreeMotionKeepsItsShadowForTenMinutesAndFollowsItsExpectedPath()
    {
        // Jolt's damping changes the velocity by 8.3e-4 of its value in each step at 60 Hz, far beyond the rounding that the
        // shadow allows, so the shadow expects the damped velocity (docs/plans/spin-gravity.md, fork O3). Then every update
        // after the first starts from the shadow, which the chain of its places shows: each place is the one before plus the
        // new chord times the step, exactly, where an update that started from Jolt's place would break the chain.
        // The damping draws a crate without a floor into the station's turn, and the centrifugal term then carries it outward
        // at a rate that grows with its distance from the axis: at the spike's 0.31321 rad/s the distance grows by e in 16 s,
        // so the crate would leave any bubble in 10 minutes. At 0.02 rad/s it grows by e in about 200 s, and in a run of this
        // test it went from 30 m to 514 m in 10 minutes.
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        Station station = SpawnStation(world, firstMove: false, bubble: 2000, rate: 0.02);
        var place = new Double3(30, 0, 0);
        Entity crate = SpawnIn(world, station, Crate, place);
        var path = new ExpectedPath(station.Pose(world).TransformPoint(place), Crate.LinearDamping, world.DeltaTime);
        double dt = world.DeltaTime;
        // In its first step the station holds still, and the crate with it. Its second step is the first that turns, whose
        // update starts from Jolt's state and writes the first shadow.
        world.Tick();

        double worst = 0;
        int broken = 0, missing = 0;
        ShadowTracking? last = null;
        for (int tick = 1; tick < 600 * world.Options.TickRate; tick++)
        {
            DPose start = station.Pose(world);
            world.Tick();
            path.Step(start, station.Pose(world));
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(crate, station.Space).Position, path.Place));
            if (!backend.ShadowOf(crate).TryGetValue(out ShadowTracking shadow))
            {
                missing++;
                last = null;
                continue;
            }
            if (last is { } before && (shadow.Step != before.Step + 1 || shadow.Place != before.Place + shadow.Chord * dt))
                broken++;
            last = shadow;
        }

        missing.Should().Be(0, "a dynamic body in a turning frame keeps a shadow after every update");
        broken.Should().Be(0, "every update after the first starts from the shadow, and none takes the contact branch");
        worst.Should().BeLessThan(1e-3);
        Double3 inStation = world.PoseIn(crate, station.Frame).Position;
        Math.Sqrt(inStation.X * inStation.X + inStation.Z * inStation.Z).Should().BeGreaterThan(300, "the damping drew the crate into the turn, and it drifted outward");
        world.GetFrame(crate).Should().Be(station.Frame);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ASteadyForceOnACrateInTheStationKeepsItsShadowAndGivesTheCrateItsExpectedPath()
    {
        // Jolt adds the force of an intent to the body's velocity in the step, beside the update's force, so the shadow
        // expects it too. A shadow that left it out would take the force for a contact in every step. The crate weighs 16 kg,
        // whose inverse Jolt holds exactly in a float, so the force of 16 N gives it exactly 1 m/s² along the station's +Z.
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        Station station = SpawnStation(world, firstMove: false, bubble: 1000);
        var place = new Double3(30, 0, 0);
        var pilot = new Pilot();
        Entity crate = SpawnIn(world, station, UndampedCrate with { Mass = 16 }, place, component: pilot);
        var path = new ExpectedPath(station.Pose(world).TransformPoint(place), 0, world.DeltaTime);
        double dt = world.DeltaTime;
        world.Tick();
        // The tick after this one writes the first force, which acts in the step beside the tick after it.
        pilot.EveryTick = body => body.AddForce(new Vector3(0, 0, 16));
        var push = new Double3(0, 0, 1);

        double worst = 0;
        int broken = 0;
        ShadowTracking? last = null;
        for (int tick = 1; tick < 30 * world.Options.TickRate; tick++)
        {
            DPose start = station.Pose(world);
            world.Tick();
            path.Step(start, station.Pose(world), tick == 1 ? Double3.Zero : push);
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(crate, station.Space).Position, path.Place));
            backend.ShadowOf(crate).TryGetValue(out ShadowTracking shadow).Should().BeTrue();
            if (last is { } before && (shadow.Step != before.Step + 1 || shadow.Place != before.Place + shadow.Chord * dt))
                broken++;
            last = shadow;
        }

        broken.Should().Be(0, "every update after the first starts from the shadow");
        worst.Should().BeLessThan(1e-6);
        world.GetFrame(crate).Should().Be(station.Frame);
        world.Errors.Should().BeEmpty();
    }

    // ---- Impulses ----

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    public void AnImpulseGivesACrateInTheStationTheInertialLineOfItsChange(int tickRate)
    {
        // The impulse acts at the start of its step, in the station's axes at that moment, so the crate leaves its place in
        // inertial space along that direction, at Δv = J / m = 1 m/s. If the update carried the change as a chord of the step
        // that ended, it would turn it once more, by the station's turn of a step: 5.2 mm/s sideways at 60 Hz.
        using World world = NewWorld(out _, debugChecks: false, configure: options => options.TickRate = tickRate);
        Station station = SpawnStation(world, firstMove: false);
        var pilot = new Pilot();
        Entity crate = SpawnIn(world, station, UndampedCrate, new Double3(30, 0, 0), component: pilot);
        Double3 rest = station.Pose(world).TransformPoint(new Double3(30, 0, 0));
        Tick(world, tickRate / 2);

        var change = new Double3(0, 0.6, 0.8);
        pilot.Once = body => body.AddImpulse((change * UndampedCrate.Mass).ToVector3());
        world.Tick();
        // The tick wrote the impulse, which acts in the next step, from the station's pose and the time that the tick ended.
        DPose atImpulse = station.Pose(world);
        double impulseTime = world.Time;
        Double3 inertialChange = atImpulse.Rotation.Rotate(change);

        double worst = 0;
        for (int tick = 0; tick < 5 * tickRate; tick++)
        {
            world.Tick();
            Double3 expected = rest + inertialChange * (world.Time - impulseTime);
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(crate, station.Space).Position, expected));
        }

        worst.Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("drain")]
    [InlineData("recenter")]
    public void AnImpulseOnACrateInAFallingFrameInsideTheStationGivesItTheInertialLineOfItsChange(string shift)
    {
        // The crate falls in a numeric frame below the station, at rest relative to its origin. After the impulse it moves
        // at 2 m/s relative to that origin. Under the default rules that is past the 1 m/s at which the frame takes the
        // members' common velocity over, so the frame shifts its velocity. Under the other rules the frame takes no velocity
        // over, and its origin moves to the crate once the crate is 1 m from it. Either shift moves every member, its
        // velocity shadow too. A shadow that kept its old velocities would read the shift as a contact and turn it, and one
        // that kept its old place would take the offset back in one step.
        NumericFrameOptions rules = shift == "drain"
            ? NumericFrameTesting.EveryGroupFalls
            : NumericFrameTesting.EveryGroupFalls with { DrainSpeed = 1e6, RecenterDistance = 1 };
        using World world = NewWorld(out _, debugChecks: false, configure: options => options.NumericFrames = rules);
        Station station = SpawnStation(world, firstMove: false);
        var pilot = new Pilot();
        Entity crate = SpawnIn(world, station, UndampedCrate, new Double3(30, 0, 0), component: pilot);
        Double3 rest = station.Pose(world).TransformPoint(new Double3(30, 0, 0));
        Tick(world, 30);
        world.Frames.IsNumeric(world.PhysicsHomeOf(crate)).Should().BeTrue("the crate fell in a numeric frame below the station");

        var change = new Double3(0, 1.2, 1.6);
        pilot.Once = body => body.AddImpulse((change * UndampedCrate.Mass).ToVector3());
        world.Tick();
        DPose atImpulse = station.Pose(world);
        double impulseTime = world.Time;
        Double3 inertialChange = atImpulse.Rotation.Rotate(change);

        double worst = 0;
        for (int tick = 0; tick < 300; tick++)
        {
            world.Tick();
            Double3 expected = rest + inertialChange * (world.Time - impulseTime);
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(crate, station.Space).Position, expected));
        }

        worst.Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnAngularImpulseGivesABallInTheStationTheInertialSpinOfItsChange()
    {
        // The ball sits on the axis and does not turn in inertial space. The angular impulse of 2 N m s about the station's
        // +X at the start of its step gives it 2 rad/s about that direction, which stays fixed in inertial space while the
        // station turns about +Y.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world, firstMove: false);
        var pilot = new Pilot();
        Entity ball = SpawnIn(world, station, UndampedBall, Double3.Zero, component: pilot);
        DQuaternion still = station.Pose(world).Rotation;
        Tick(world, 30);

        var change = new Double3(2, 0, 0);
        pilot.Once = body => body.AddAngularImpulse(change.ToVector3());
        world.Tick();
        DPose atImpulse = station.Pose(world);
        double impulseTime = world.Time;
        Double3 inertialChange = atImpulse.Rotation.Rotate(change);

        double worst = 0;
        for (int tick = 0; tick < 300; tick++)
        {
            world.Tick();
            DQuaternion expected = DQuaternion.FromAxisAngle(inertialChange, inertialChange.Length * (world.Time - impulseTime)) * still;
            worst = Math.Max(worst, AngleBetween(world.PoseIn(ball, station.Space).Rotation, expected));
        }

        worst.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    // ---- Tumbling ----

    [Fact]
    public void AnUndampedTumblingBallKeepsItsInertialAngularVelocity()
    {
        // The ball spins at 2 rad/s about +X, across the station's axis. Jolt keeps a body's angular velocity in the axes of
        // its system, which turn with the station, so without the angular form the ball's spin axis would go round with the
        // station.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world, firstMove: false);
        var spin = new Vector3(2, 0, 0);
        Entity ball = SpawnIn(world, station, UndampedBall, Double3.Zero, spin);
        DQuaternion start = station.Pose(world).Rotation;
        Double3 inertialSpin = start.Rotate((Double3)spin);
        double spawnTime = world.Time;

        double worst = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            world.Tick();
            DQuaternion expected = DQuaternion.FromAxisAngle(inertialSpin, inertialSpin.Length * (world.Time - spawnTime)) * start;
            worst = Math.Max(worst, AngleBetween(world.PoseIn(ball, station.Space).Rotation, expected));
        }

        worst.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ATumblingBallWithTheDefaultDampingFollowsItsExpectedRotation()
    {
        // The damping slows the ball's spin relative to the station's axes, not relative to inertial space.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world, firstMove: false);
        var spin = new Vector3(2, 0, 0);
        Entity ball = SpawnIn(world, station, Ball, Double3.Zero, spin);
        DQuaternion still = station.Pose(world).Rotation;
        // In its first step the station holds still. Jolt turns the ball by its chord, the spin plus half a step of its
        // damping (see AngularChord), shrunk by the damping factor.
        float dt = world.DeltaTime;
        double firstChord = spin.X * (1 + Ball.AngularDamping * 0.5f * dt) * MathF.Max(0f, 1f - Ball.AngularDamping * dt);
        DQuaternion firstTurn = DQuaternion.FromAxisAngle(still.Rotate(Double3.UnitX), firstChord * dt);
        var tumble = new ExpectedTumble(firstTurn * still, firstTurn, Ball.AngularDamping, dt);
        world.Tick();

        double worst = 0;
        for (int tick = 1; tick < 600; tick++)
        {
            DQuaternion start = station.Pose(world).Rotation;
            world.Tick();
            tumble.Step(start, station.Pose(world).Rotation);
            worst = Math.Max(worst, AngleBetween(world.PoseIn(ball, station.Space).Rotation, tumble.Rotation));
        }

        worst.Should().BeLessThan(1e-4);
        world.Errors.Should().BeEmpty();
    }

    // ---- Steer targets ----

    [Fact]
    public void ACrateSteeredToATargetInTheStationReachesIt()
    {
        // The steering velocities carry the crate to the target in the station's coordinates over the step, so the update
        // of a free body does not act on them. If it did, the crate would miss by about ω² r dt², 2.7 mm at 100 m, and turn
        // by the station's turn of a step, 5.2 mrad.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world, firstMove: true);
        Tick(world, 10);
        var pilot = new Pilot();
        // Without damping, so that Jolt's damping does not shorten the steering chord.
        Entity crate = SpawnIn(world, station, UndampedCrate, new Double3(100, 0, 0), component: pilot);
        var target = new DPose(new Double3(99.9, 0.2, 0.1), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.2));
        pilot.EveryTick = body => body.SteerTo(target);
        world.Tick();

        for (int tick = 0; tick < 10; tick++)
        {
            world.Tick();
            DPose pose = world.PoseIn(crate, station.Frame);
            Double3.Distance(pose.Position, target.Position).Should().BeLessThan(1e-4, $"after tick {tick}");
            AngleBetween(pose.Rotation, target.Rotation).Should().BeLessThan(1e-4, $"after tick {tick}");
        }
        world.Errors.Should().BeEmpty();
    }

    // ---- Rigid characters ----

    [Fact]
    public void AWeightlessPersonNearTheHubMovesOnAStraightLineInInertialSpace()
    {
        // At 0.2 m from the axis the station's centrifugal term is ω² r = 0.02 m/s², below the 0.05 m/s² of the rigid form,
        // so the person is a dynamic body whose rotation is locked, and physics keeps it on its inertial line like any
        // dynamic body. It moves away from the axis at about ω r, and stays below the 0.1 m/s² at which it walks again.
        // The station spawns with its first motion intent, so it turns steadily from its first step; a station that spun up
        // in one step would give the person an Euler term of ω / dt times r, which makes it walk for that step. The line is
        // the one through the person's places in inertial space at the ends of its first two steps.
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        Station station = SpawnStation(world, firstMove: true);
        Entity person = SpawnIn(world, station, Person, new Double3(0.2, 0, 0));
        world.Tick();
        Double3 first = world.PoseIn(person, station.Space).Position;
        world.Tick();
        Double3 second = world.PoseIn(person, station.Space).Position;
        double secondTime = world.Time;

        double worst = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            world.Tick();
            Double3 expected = second + (second - first) * ((world.Time - secondTime) / world.DeltaTime);
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(person, station.Space).Position, expected));
        }

        worst.Should().BeLessThan(1e-3);
        backend.BodiesIn(station.Frame).Should().Contain(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary &&
            BodyUserData.Names(facts.UserData, person) && facts.Motion == JoltPhysicsSharp.MotionType.Dynamic, "the person is in its rigid form");
        world.Errors.Should().BeEmpty();
    }
}
