using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;
using Tertius.Testing;

namespace Tertius.Core.Tests;

/// <summary>
/// The motion of one frame relative to another over the step that the front intents describe
/// (<see cref="FrameTree.MotionRelativeInStep"/>, docs/plans/spin-gravity.md, "V7, the relative motion of two frames"), on a
/// tree built in the test. Each test runs steps as the sync point leaves the tree (see <see cref="RunStep"/>), and then asks
/// for the motion over the next step, as steps 5 and 7b of the sync point ask while the tree stands where that step
/// starts. <c>Start</c> is the relation at the start of the step: each level at its tree pose, or at the pose of a
/// teleport in the step's intents, with its row's velocities and no accelerations. <c>End</c> is the relation at the end of
/// the step, which each level gives by its kind.
/// <para>
/// The station of the tests is a frame without a body and without a field, which turns in place in a root frame without a
/// field about its +Y at 0.31321 rad/s, by the full samples of a spin. A rate ω about +Y gives a place at (r, 0, 0) the
/// centrifugal acceleration (ω² r, 0, 0), and a velocity (u, 0, 0) the Coriolis acceleration (0, 0, 2 ω u).
/// </para>
/// </summary>
public class FrameTreeMotionInStepTests
{
    private const double Step = 1.0 / 60;
    private const double StationRate = 0.31321;
    private static readonly FrameSpin StationSpin = new(Tilt: DQuaternion.Identity, Rate: StationRate);

    private readonly FrameTree frames = new();
    private readonly FrameHandle space;
    private uint nextOwner = 2;

    public FrameTreeMotionInStepTests()
    {
        space = frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null, localPose: DPose.Identity, frameBounds: FrameBounds.Sphere(1e9f));
    }

    /// <summary>A new frame of a new owner in <paramref name="parent"/> at <paramref name="place"/>, with a hull of
    /// <paramref name="hull"/> or none.</summary>
    private FrameHandle Frame(FrameHandle parent, Double3 place, BodyMotion? hull = null)
    {
        FrameHandle frame = frames.Create(frameOwner: new(nextOwner++, 1), parent: parent, localPose: new(place, DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(1e4f));
        frames.SetHullMotion(frame, hull);
        return frame;
    }

    /// <summary>The intents of a step in which the owner of each frame of <paramref name="intents"/> wrote its intent, relative to the frame's parent.</summary>
    private PhysicsIntent[] Intents(params (FrameHandle Frame, PhysicsIntent Intent)[] intents)
    {
        var all = new PhysicsIntent[nextOwner];
        foreach ((FrameHandle frame, PhysicsIntent intent) in intents)
            all[frame.Entity.Index] = intent with { Frame = frames.Parent(frame) };
        return all;
    }

    private static PhysicsIntent Moving(MotionIntent move) => new() { Move = move };

    private static PhysicsIntent Moving(in MotionSample sample) => Moving(new MotionIntent(sample));

    private static PhysicsIntent EndingAt(Double3 place, DQuaternion? rotation = null) =>
        Moving(new MotionIntent(new EndPose(new(place, rotation ?? DQuaternion.Identity))));

    /// <summary>The spin sample of the station in the step that ends at <paramref name="stepEnd"/>.</summary>
    private static PhysicsIntent Spinning(double stepEnd) => Moving(StationSpin.Sample(Double3.Zero, stepEnd, Step));

    /// <summary>
    /// One step as the sync point leaves the tree: the tree updates, fixes the turns of a step of <see cref="Step"/> seconds
    /// that applies <paramref name="intents"/>, and takes the step records of that step with what <paramref name="owners"/>
    /// says.
    /// </summary>
    private void RunStep(PhysicsIntent[] intents, StepOwners? owners = null)
    {
        frames.Update();
        frames.BeginStep(intents, Step, lastStepFailed: false);
        frames.TakeStepRecords(stepIntents: intents, step: Step, owners: owners);
    }

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance, string what) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{what}: {actual} is not {expected}");

    private static void ExpectNear(DQuaternion actual, DQuaternion expected, double tolerance, string what) =>
        (2 * DQuaternion.Identity.AngularVelocityTo(actual * expected.Conjugate(), 2).Length).Should()
        .BeLessThan(tolerance, $"{what}: {actual} is not {expected}");

    private static void ExpectMotion(in FrameMotion actual, in FrameMotion expected, double tolerance, string which)
    {
        ExpectNear(actual: actual.Pose.Position, expected: expected.Pose.Position, tolerance: tolerance, what: $"{which} place");
        ExpectNear(actual: actual.Pose.Rotation, expected: expected.Pose.Rotation, tolerance: tolerance, what: $"{which} rotation");
        ExpectNear(actual: actual.Velocity, expected: expected.Velocity, tolerance: tolerance, what: $"{which} velocity");
        ExpectNear(actual: actual.AngularVelocity, expected: expected.AngularVelocity, tolerance: tolerance, what: $"{which} angular velocity");
        ExpectNear(actual: actual.Acceleration, expected: expected.Acceleration, tolerance: tolerance, what: $"{which} acceleration");
        ExpectNear(actual: actual.AngularAcceleration, expected: expected.AngularAcceleration, tolerance: tolerance,
            what: $"{which} angular acceleration");
    }

    // ---- Each kind of level -------------------------------------------------------------------------------------

    [Fact]
    public void ALevelOnEndPosesStartsTheStepAtItsTreePoseWithItsRowsVelocityAndEndsItOnTheRecordOfItsNextEndPose()
    {
        FrameHandle cart = Frame(space, Double3.Zero);
        RunStep(Intents((cart, EndingAt(new(1, 0, 0)))));
        DQuaternion turned = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.6);

        (FrameMotion start, FrameMotion end) =
            frames.MotionRelativeInStep(of: cart, to: space, stepIntents: Intents((cart, EndingAt(new(3, 0, 0), turned))), step: Step);

        var row = new FrameMotion(Pose: new(new(1, 0, 0), DQuaternion.Identity), Velocity: new(60, 0, 0), AngularVelocity: Double3.Zero, Acceleration: Double3.Zero,
            AngularAcceleration: Double3.Zero);
        ExpectMotion(actual: start, expected: row, tolerance: 1e-9, which: "the start's");
        var record = new FrameMotion(Pose: new(new(3, 0, 0), turned), Velocity: new(120, 0, 0),
            AngularVelocity: DQuaternion.Identity.AngularVelocityTo(turned, Step),
            Acceleration: Double3.Zero, AngularAcceleration: Double3.Zero);
        ExpectMotion(actual: end, expected: record, tolerance: 1e-9, which: "the end's");
    }

    [Fact]
    public void ALevelOnAFullSampleEndsTheStepWithTheSamplesAccelerations()
    {
        FrameHandle ship = Frame(space, Double3.Zero);
        RunStep(Intents((ship, EndingAt(new(1, 0, 0)))));
        var sample = new MotionSample(Pose: new(new(2, 0.5, 0), DQuaternion.FromAxisAngle(Double3.UnitX, 0.2)),
            ChordVelocity: new(60, 30, 0), Velocity: new(61, 30.5, 0), Acceleration: new(120, 60, -3),
            AngularVelocity: new(12, 0, 0.5), AngularAcceleration: new(0.4, -0.2, 0.1));

        (_, FrameMotion end) = frames.MotionRelativeInStep(of: ship, to: space, stepIntents: Intents((ship, Moving(sample))), step: Step);

        ExpectMotion(actual: end,
            expected: new(Pose: sample.Pose, Velocity: sample.Velocity, AngularVelocity: sample.AngularVelocity, Acceleration: sample.Acceleration, AngularAcceleration: sample.AngularAcceleration),
            tolerance: 1e-9, which: "the end's");
    }

    [Fact]
    public void ALevelWithoutAMotionIntentEndsTheStepAtRestWhereItStarts()
    {
        // The room moved in the last step, so its row holds a velocity, with which the step starts.
        FrameHandle room = Frame(space, Double3.Zero);
        RunStep(Intents((room, EndingAt(new(1, 0, 0)))));

        (FrameMotion start, FrameMotion end) = frames.MotionRelativeInStep(of: room, to: space, stepIntents: Intents(), step: Step);

        ExpectNear(actual: start.Velocity, expected: new(60, 0, 0), tolerance: 1e-9, what: "the start's velocity");
        ExpectMotion(actual: end,
            expected: new(Pose: new(new(1, 0, 0), DQuaternion.Identity), Velocity: Double3.Zero, AngularVelocity: Double3.Zero, Acceleration: Double3.Zero, AngularAcceleration: Double3.Zero),
            tolerance: 1e-12, which: "the end's");
    }

    [Fact]
    public void ALevelThatTeleportsStartsTheStepAtTheTeleportsPose()
    {
        FrameHandle cart = Frame(space, Double3.Zero);
        RunStep(Intents((cart, EndingAt(new(1, 0, 0)))));
        var teleport = new DPose(new(10, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.3));
        PhysicsIntent intent = EndingAt(new(11, 0, 0), teleport.Rotation) with
        {
            Flags = IntentFlags.Teleport, TeleportPose = teleport
        };

        (FrameMotion start, FrameMotion end) = frames.MotionRelativeInStep(of: cart, to: space, stepIntents: Intents((cart, intent)), step: Step);

        ExpectNear(actual: start.Pose.Position, expected: teleport.Position, tolerance: 1e-12, what: "the start's place");
        ExpectNear(actual: start.Pose.Rotation, expected: teleport.Rotation, tolerance: 1e-12, what: "the start's rotation");
        ExpectNear(actual: end.Velocity, expected: new(60, 0, 0), tolerance: 1e-9, what: "the end pose's record runs from the teleport");
    }

    [Fact]
    public void
        ALevelWithADynamicHullEndsTheStepWherePhysicsCarriesItFromItsRowUnderItsProperAccelerationAndTheParentsGravityAndRotationTerms()
    {
        // The ship's hull moves outward along +X at 1 m/s at 50 m from the station's axis, thrusts along its +Y, and spins
        // up about +Z. Jolt adds the acceleration to the velocity and then moves the body by the new velocity, so from the
        // row's instant velocity v the body moves by the chord v + a dt / 2. The acceleration is the accelerometer rule read
        // backward: the proper acceleration turned out of the hull's axes at the end of the step, plus the station's
        // centrifugal term (ω² 50, 0, 0) at the hull's place and its Coriolis term (0, 0, 2 ω) on the hull's velocity. The
        // station turns steadily, so it has no Euler term.
        FrameHandle station = Frame(space, Double3.Zero);
        FrameHandle ship = Frame(station, Double3.Zero, BodyMotion.Dynamic);
        var record = new MotionSample(Pose: new(new(50, 0, 0), DQuaternion.Identity), ChordVelocity: new(1, 0, 0), Velocity: new(1, 0, 0),
            Acceleration: Double3.Zero,
            AngularVelocity: new(0, 0, 0.2), AngularAcceleration: new(0, 0, 0.1));
        var thrust = new Vector3(0, 0.5f, 0);
        for (int k = 1; k <= 2; k++)
            RunStep(Intents((station, Spinning(k * Step))), new StepOwners().WithHullRecord(ship, record, thrust));

        (_, FrameMotion end) = frames.MotionRelativeInStep(of: ship, to: station, stepIntents: Intents((station, Spinning(3 * Step))), step: Step);

        Double3 spin = frames.AngularVelocity(ship) + new Double3(0, 0, 0.1) * Step;
        DQuaternion rotation = DQuaternion.FromAxisAngle(spin, spin.Length * Step);
        Double3 acceleration = rotation.Rotate((Double3)thrust) +
                               new Double3(StationRate * StationRate * 50, 0, 2 * StationRate);
        var expected = new FrameMotion(
            Pose: new(new Double3(50, 0, 0) + (new Double3(1, 0, 0) + acceleration * (Step / 2)) * Step, rotation),
            Velocity: new Double3(1, 0, 0) + acceleration * Step, AngularVelocity: spin, Acceleration: acceleration, AngularAcceleration: new(0, 0, 0.1));
        ExpectMotion(actual: end, expected: expected, tolerance: 1e-9, which: "the end's");
    }

    [Fact]
    public void ALevelThatDiesHasItsTreeRelationAtBothEndsOfTheStep()
    {
        // The cart has an end pose for the step, but it dies at step 5, so it never takes it: the rows of its contents convert
        // by its relation at the moment of its death.
        FrameHandle cart = Frame(space, Double3.Zero);
        RunStep(Intents((cart, EndingAt(new(1, 0, 0)))));
        bool[] dying = new bool[nextOwner];
        dying[cart.Entity.Index] = true;

        (FrameMotion start, FrameMotion end) =
            frames.MotionRelativeInStep(of: cart, to: space, stepIntents: Intents((cart, EndingAt(new(3, 0, 0)))), step: Step, dying: dying);

        var row = new FrameMotion(Pose: new(new(1, 0, 0), DQuaternion.Identity), Velocity: new(60, 0, 0), AngularVelocity: Double3.Zero, Acceleration: Double3.Zero,
            AngularAcceleration: Double3.Zero);
        ExpectMotion(actual: start, expected: row, tolerance: 1e-12, which: "the start's");
        ExpectMotion(actual: end, expected: row, tolerance: 1e-12, which: "the end's");
    }

    // ---- Paths of several levels --------------------------------------------------------------------------------

    [Fact]
    public void ARoomAtRestInASpinningStationEndsTheStepMovingOnItsCircleWithTheAccelerationTowardTheAxis()
    {
        const double radius = 100;
        FrameHandle station = Frame(space, Double3.Zero);
        FrameHandle room = Frame(station, new(radius, 0, 0));
        for (int k = 1; k <= 2; k++)
            RunStep(Intents((station, Spinning(k * Step))));
        MotionSample stationEnd = StationSpin.Sample(Double3.Zero, 3 * Step, Step);

        (_, FrameMotion end) = frames.MotionRelativeInStep(of: room, to: space, stepIntents: Intents((station, Spinning(3 * Step))), step: Step);

        DQuaternion axes = stationEnd.Pose.Rotation;
        ExpectNear(actual: end.Pose.Position, expected: axes.Rotate(new Double3(radius, 0, 0)), tolerance: 1e-12, what: "the place");
        ExpectNear(actual: end.Velocity, expected: axes.Rotate(new Double3(0, 0, -StationRate * radius)), tolerance: 1e-12,
            what: "the velocity on the circle");
        ExpectNear(actual: end.Acceleration, expected: axes.Rotate(new Double3(-StationRate * StationRate * radius, 0, 0)), tolerance: 1e-12,
            what: "the acceleration toward the axis");
        ExpectNear(actual: end.AngularVelocity, expected: new(0, StationRate, 0), tolerance: 1e-12, what: "the station's spin");
    }

    [Fact]
    public void TheMotionDownAPathUndoesTheMotionUpItAtBothEndsOfTheStep()
    {
        FrameHandle station = Frame(space, Double3.Zero);
        FrameHandle room = Frame(station, new(100, 0, 0));
        FrameHandle cart = Frame(room, Double3.Zero);
        for (int k = 1; k <= 2; k++)
            RunStep(Intents((station, Spinning(k * Step)), (cart, EndingAt(new(k, 0, 0)))));
        PhysicsIntent[] next = Intents((station, Spinning(3 * Step)), (cart, EndingAt(new(3, 0.5, 0))));

        (FrameMotion upStart, FrameMotion upEnd) = frames.MotionRelativeInStep(of: cart, to: space, stepIntents: next, step: Step);
        (FrameMotion downStart, FrameMotion downEnd) = frames.MotionRelativeInStep(of: space, to: cart, stepIntents: next, step: Step);

        var rest = new FrameMotion(Pose: DPose.Identity, Velocity: Double3.Zero, AngularVelocity: Double3.Zero, Acceleration: Double3.Zero, AngularAcceleration: Double3.Zero);
        ExpectMotion(actual: downStart.MotionToParent(upStart), expected: rest, tolerance: 1e-9, which: "the start's");
        ExpectMotion(actual: downEnd.MotionToParent(upEnd), expected: rest, tolerance: 1e-9, which: "the end's");
    }

    [Fact]
    public void WithoutATeleportTheStartsPoseIsTheTreesRelativePoseToTheBit()
    {
        // The parts of an intent that convert by the start, such as a teleport and the forces, keep the tree's relation exactly.
        // The two frames' root is far from the origin of the virtual root and turned, so a path through the virtual root
        // would round otherwise.
        FrameHandle root = frames.Create(frameOwner: new(nextOwner++, 1), parent: FrameHandle.Null,
            localPose: new(new(3e7, -2e6, 5e5), DQuaternion.FromAxisAngle(new Double3(1, 2, 3).Normalized(), 0.7)),
            frameBounds: FrameBounds.Sphere(1e9f));
        FrameHandle station = Frame(root, new(1e6, -3e5, 2e4));
        FrameHandle room = Frame(station, new(100, 0, 3));
        FrameHandle cart = Frame(root, new(-5e5, 7, 1));
        for (int k = 1; k <= 2; k++)
            RunStep(Intents((station, Spinning(k * Step)),
                (cart, EndingAt(new(-5e5 + k, 7, 1), DQuaternion.FromAxisAngle(Double3.UnitX, 0.1 * k)))));

        (FrameMotion start, _) = frames.MotionRelativeInStep(of: room, to: cart, stepIntents: Intents((station, Spinning(3 * Step))), step: Step);

        start.Pose.Should().Be(frames.Relative(room, cart));
    }

    [Fact]
    public void TheMotionOverAStepAllocatesNothing()
    {
        FrameHandle station = Frame(space, Double3.Zero);
        FrameHandle room = Frame(station, new(100, 0, 0));
        FrameHandle cart = Frame(space, new(0, 0, 50));
        for (int k = 1; k <= 2; k++)
            RunStep(Intents((station, Spinning(k * Step)), (cart, EndingAt(new(0, k, 50)))));
        PhysicsIntent[] next = Intents((station, Spinning(3 * Step)), (cart, EndingAt(new(0, 3, 50))));
        frames.MotionRelativeInStep(of: room, to: cart, stepIntents: next, step: Step);

        Allocations.Fewest(() => frames.MotionRelativeInStep(of: room, to: cart, stepIntents: next, step: Step)).Should().Be(0);
    }
}
