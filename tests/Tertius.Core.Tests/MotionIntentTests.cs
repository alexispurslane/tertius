using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The motion intent, a union of an end pose, a full sample and a steer target (docs/plans/rails-components.md, "The
/// motion intent" and "MoveKinematic as an end pose"): the step record that each case gives for a step that starts at a
/// pose, the pose that physics aims at, how each case converts into another frame, and the two reads of
/// <see cref="PhysicsIntent"/> that use it, <see cref="PhysicsIntent.StepRecord"/> and <see cref="PhysicsIntent.IsEmpty"/>
/// .
/// </summary>
public class MotionIntentTests
{
    private const double Step = 1.0 / 60;

    private static readonly DPose Start = new(new(10, -20, 30), DQuaternion.FromAxisAngle(Double3.UnitY, 0.4));

    private static readonly DPose End = new(new(10.5, -19, 28),
        DQuaternion.FromAxisAngle(new Double3(1, 2, -1).Normalized(), 0.9));

    private static readonly MotionSample Sample = new(
        new(new(30, -40, 50), DQuaternion.FromAxisAngle(Double3.UnitX, 0.3)),
        ChordVelocity: new(1, 2, 3), Velocity: new(1.5, 2.5, 3.5), Acceleration: new(-0.1, 0.2, 0.3),
        AngularVelocity: new(0.01, 0.02, 0.03), AngularAcceleration: new(0.001, -0.002, 0.003));

    // The old frame relative to the new one where the step starts and where it ends (see FrameTree.MotionRelativeInStep):
    // away from the new frame's origin, turned about a tilted axis, moving, accelerating, spinning and spinning up, so a
    // conversion moves, turns and adds the frame's motion. The two ends differ far more than one step would make them, so a
    // part that converts by the wrong end misses by much more than rounding.
    private static readonly FrameMotion StartMotion = new(
        new(new(7e6, 2e5, -3e5), DQuaternion.FromAxisAngle(new Double3(0.2, 1, 0.4).Normalized(), 1.1)),
        Velocity: new(-120, 7600, 35),
        AngularVelocity: new(0.001, 0.02, -0.003),
        Acceleration: Double3.Zero,
        AngularAcceleration: Double3.Zero);

    private static readonly FrameMotion EndMotion = new(
        new(new(7e6 - 2, 2e5 + 127, -3e5 + 0.5),
            DQuaternion.FromAxisAngle(new Double3(0.25, 1, 0.3).Normalized(), 1.2)),
        Velocity: new(-121, 7601, 36),
        AngularVelocity: new(0.002, 0.03, -0.004),
        Acceleration: new(-8.1, 0.4, 0.25),
        AngularAcceleration: new(0.01, -0.02, 0.005));

    // ---- The step record ----------------------------------------------------------------------------------------

    [Fact]
    public void AnEndPoseGivesAConstantVelocityRecordFromWhereTheStepStarts()
    {
        var intent = new MotionIntent(new EndPose(End));

        intent.TryRecordFrom(Start, Step, out MotionSample record).Should().BeTrue();

        record.Pose.Should().Be(End, "the record ends exactly on the end pose");
        Distance(record.ChordVelocity, (End.Position - Start.Position) / Step).Should().BeLessThan(1e-12);
        record.Velocity.Should().Be(record.ChordVelocity, "the velocity does not change during the step");
        record.Acceleration.Should().Be(Double3.Zero);
        record.AngularAcceleration.Should().Be(Double3.Zero);
        // The constant angular velocity, in the frame's axes, turns the start's rotation into the end's over the step.
        double rate = record.AngularVelocity.Length;
        DQuaternion turned = DQuaternion.FromAxisAngle(record.AngularVelocity / rate, rate * Step) * Start.Rotation;
        Distance(turned, End.Rotation).Should().BeLessThan(1e-12);
    }

    [Fact]
    public void AFullSampleIsItsOwnRecordWhereverTheStepStarts()
    {
        var intent = new MotionIntent(Sample);

        intent.TryRecordFrom(Start, Step, out MotionSample fromStart).Should().BeTrue();
        intent.TryRecordFrom(End, Step, out MotionSample fromEnd).Should().BeTrue();

        fromStart.Should().Be(Sample);
        fromEnd.Should().Be(Sample);
    }

    [Fact]
    public void ASteerTargetGivesNoStepRecord()
    {
        new MotionIntent(new SteerTarget(End)).TryRecordFrom(Start, Step, out _).Should().BeFalse(
            "physics measures the motion of a dynamic body that it steers");
    }

    [Fact]
    public void NoMotionIntentGivesNoStepRecord()
    {
        default(MotionIntent).TryRecordFrom(Start, Step, out _).Should().BeFalse();
    }

    [Fact]
    public void TheStepStartsAtTheTeleportPoseWhenTheSameIntentTeleports()
    {
        var teleport = new DPose(new(-5, 2, 1), DQuaternion.FromAxisAngle(Double3.UnitZ, -0.7));
        var intent = new PhysicsIntent
        {
            Move = new(new EndPose(End)),
            TeleportPose = teleport,
            Flags = IntentFlags.Teleport
        };

        intent.StepRecord(Start, Step, out MotionSample record).Should().BeTrue();

        new MotionIntent(new EndPose(End)).TryRecordFrom(teleport, Step, out MotionSample fromTeleport);
        record.Should().Be(fromTeleport, "the teleport puts the entity at its pose before the step moves it");
    }

    [Fact]
    public void WithoutATeleportTheStepStartsWhereTheEntityStands()
    {
        var intent = new PhysicsIntent
        {
            Move = new(new EndPose(End)),
            TeleportPose = new(new(-5, 2, 1), DQuaternion.Identity)
        };

        intent.StepRecord(Start, Step, out MotionSample record).Should().BeTrue();

        new MotionIntent(new EndPose(End)).TryRecordFrom(Start, Step, out MotionSample fromStanding);
        record.Should().Be(fromStanding, "a teleport pose without its flag is no teleport");
    }

    // ---- The aim pose -------------------------------------------------------------------------------------------

    [Fact]
    public void EveryCaseGivesThePoseThatPhysicsAimsAt()
    {
        new MotionIntent(new EndPose(End)).AimPose.Should().Be(End);
        new MotionIntent(Sample).AimPose.Should().Be(Sample.Pose);
        new MotionIntent(new SteerTarget(Start)).AimPose.Should().Be(Start);
    }

    // ---- Conversion into another frame --------------------------------------------------------------------------

    [Fact]
    public void AnEndPoseConvertsByThePoseWhereTheStepEndsAndStaysAnEndPose()
    {
        MotionIntent converted = new MotionIntent(new EndPose(End)).Converted(StartMotion, EndMotion, Step);

        converted.TryGetValue(out EndPose end).Should().BeTrue("the frames' motion gives an end pose no velocity");
        end.Pose.Should().Be(DPose.Compose(EndMotion.Pose, End), "an end pose is a pose at the end of the step");
    }

    [Fact]
    public void ASteerTargetConvertsByThePoseWhereTheStepEndsAndStaysASteerTarget()
    {
        MotionIntent converted = new MotionIntent(new SteerTarget(End)).Converted(StartMotion, EndMotion, Step);

        converted.TryGetValue(out SteerTarget steer).Should().BeTrue();
        steer.Pose.Should().Be(DPose.Compose(EndMotion.Pose, End), "a steer target is a pose at the end of the step");
    }

    [Fact]
    public void AFullSampleConvertsItsPoseItsVelocitiesAndItsAccelerationsByTheRelationWhereTheStepEnds()
    {
        // The sample describes the end of its step, so each of these parts converts at that moment, by the rules of a point
        // and of a body in a frame that moves, turns and spins up.
        MotionIntent converted = new MotionIntent(Sample).Converted(StartMotion, EndMotion, Step);

        converted.TryGetValue(out MotionSample sample).Should().BeTrue();
        Double3 place = Sample.Pose.Position;
        DPose composed = DPose.Compose(EndMotion.Pose, Sample.Pose);
        Distance(sample.Pose.Position, composed.Position).Should().BeLessThan(1e-6, "the place");
        Distance(sample.Pose.Rotation, composed.Rotation).Should().BeLessThan(1e-12, "the rotation");
        Distance(sample.Velocity, EndMotion.VelocityToParent(place, Sample.Velocity)).Should()
            .BeLessThan(1e-9, "the velocity");
        Distance(sample.Acceleration, EndMotion.AccelerationToParent(place, Sample.Velocity, Sample.Acceleration))
            .Should().BeLessThan(1e-9, "the acceleration");
        Distance(sample.AngularVelocity, EndMotion.AngularToParent(Sample.AngularVelocity)).Should()
            .BeLessThan(1e-12, "the angular velocity");
        Distance(sample.AngularAcceleration,
                EndMotion.AngularAccelerationToParent(Sample.AngularAcceleration, Sample.AngularVelocity))
            .Should().BeLessThan(1e-12, "the angular acceleration");
    }

    [Fact]
    public void AFullSamplesChordConvertsByWhereTheFrameStoodAtBothEndsOfTheStep()
    {
        // The chord is the move of the sample's point over the step divided by the step: from where it started, its place less
        // the chord times the step, in the frame where the frame stood at the start, to its place in the frame where the frame
        // stood at the end.
        MotionIntent converted = new MotionIntent(Sample).Converted(StartMotion, EndMotion, Step);

        converted.TryGetValue(out MotionSample sample).Should().BeTrue();
        Double3 place = Sample.Pose.Position;
        Double3 expected = (EndMotion.Pose.TransformPoint(place) -
                            StartMotion.Pose.TransformPoint(place - Sample.ChordVelocity * Step)) / Step;
        Distance(sample.ChordVelocity, expected).Should().BeLessThan(1e-6, "the chord");
    }

    [Fact]
    public void ASampleAtRestInASpinningFrameConvertsIntoAMotionOnItsCircleTowardTheAxis()
    {
        // The frame spins at ω about +Y in place, and has turned by ω dt over the step. A sample at rest at r = (100, 0, 0) in it
        // moves on a circle in the parent: at ω × r, with ω² r toward the axis, and with the chord of the arc of the step.
        const double rate = 0.31321;
        var spin = new Double3(0, rate, 0);
        DQuaternion endAxes = DQuaternion.FromAxisAngle(Double3.UnitY, 0.7);
        DQuaternion startAxes = DQuaternion.FromAxisAngle(Double3.UnitY, 0.7 - rate * Step);
        var start = new FrameMotion(new(Double3.Zero, startAxes), Double3.Zero, spin, Double3.Zero, Double3.Zero);
        var end = new FrameMotion(new(Double3.Zero, endAxes), Double3.Zero, spin, Double3.Zero, Double3.Zero);
        var place = new Double3(100, 0, 0);
        var atRest = new MotionSample(new(place, DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero,
            Double3.Zero, Double3.Zero);

        new MotionIntent(atRest).Converted(start, end, Step).TryGetValue(out MotionSample sample).Should().BeTrue();

        Distance(sample.Velocity, endAxes.Rotate(new Double3(0, 0, -rate * 100))).Should()
            .BeLessThan(1e-12, "the velocity on the circle");
        Distance(sample.Acceleration, endAxes.Rotate(new Double3(-rate * rate * 100, 0, 0))).Should()
            .BeLessThan(1e-12, "the acceleration toward the axis");
        Distance(sample.ChordVelocity, (endAxes.Rotate(place) - startAxes.Rotate(place)) / Step).Should()
            .BeLessThan(1e-9, "the chord of the arc");
        Distance(sample.AngularVelocity, spin).Should().BeLessThan(1e-12, "the frame's spin");
    }

    [Fact]
    public void NoMotionIntentStaysNoneWhenItConverts()
    {
        default(MotionIntent).Converted(StartMotion, EndMotion, Step).HasValue.Should().BeFalse();
    }

    // ---- The intent ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnIntentWithAMotionIntentIsNotEmpty()
    {
        new PhysicsIntent().IsEmpty.Should().BeTrue();
        new PhysicsIntent { Move = new(new EndPose(End)) }.IsEmpty.Should().BeFalse("an end pose");
        new PhysicsIntent { Move = new(Sample) }.IsEmpty.Should().BeFalse("a full sample");
        new PhysicsIntent { Move = new(new SteerTarget(End)) }.IsEmpty.Should().BeFalse("a steer target");
    }

    private static double Distance(Double3 a, Double3 b) => (a - b).Length;

    // q and -q are the same rotation.
    private static double Distance(DQuaternion a, DQuaternion b) => Math.Min(
        Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y) + Sq(a.Z - b.Z) + Sq(a.W - b.W)),
        Math.Sqrt(Sq(a.X + b.X) + Sq(a.Y + b.Y) + Sq(a.Z + b.Z) + Sq(a.W + b.W)));

    private static double Sq(double x) => x * x;
}
