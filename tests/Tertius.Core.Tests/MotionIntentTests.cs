using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The motion intent, a union of an end pose, a full sample and a steer target (docs/plans/rails-components.md, "The
/// motion intent" and "MoveKinematic as an end pose"): the step record that each case gives for a step that starts at a
/// pose, the pose that physics aims at, how each case converts into another frame, and the two reads of
/// <see cref="PhysicsIntent"/> that use it, <see cref="PhysicsIntent.StepRecord"/> and <see cref="PhysicsIntent.IsEmpty"/>.
/// </summary>
public class MotionIntentTests
{
    private const double Step = 1.0 / 60;

    private static readonly DPose Start = new(new Double3(10, -20, 30), DQuaternion.FromAxisAngle(Double3.UnitY, 0.4));
    private static readonly DPose End = new(new Double3(10.5, -19, 28), DQuaternion.FromAxisAngle(new Double3(1, 2, -1).Normalized(), 0.9));

    private static readonly MotionSample Sample = new(new DPose(new Double3(30, -40, 50), DQuaternion.FromAxisAngle(Double3.UnitX, 0.3)),
        ChordVelocity: new Double3(1, 2, 3), Velocity: new Double3(1.5, 2.5, 3.5), Acceleration: new Double3(-0.1, 0.2, 0.3),
        AngularVelocity: new Double3(0.01, 0.02, 0.03), AngularAcceleration: new Double3(0.001, -0.002, 0.003));

    // The old frame relative to the new one: away from its origin, turned about a tilted axis, moving and spinning, so a
    // conversion moves, turns and adds the frame's motion.
    private static readonly FrameMotion Motion = new(
        new DPose(new Double3(7e6, 2e5, -3e5), DQuaternion.FromAxisAngle(new Double3(0.2, 1, 0.4).Normalized(), 1.1)),
        Velocity: new Double3(-120, 7600, 35),
        AngularVelocity: new Double3(0.001, 0.02, -0.003));
    private static readonly Double3 RelativeAcceleration = new(-8.1, 0.4, 0.25);

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
        var teleport = new DPose(new Double3(-5, 2, 1), DQuaternion.FromAxisAngle(Double3.UnitZ, -0.7));
        var intent = new PhysicsIntent
        {
            Move = new MotionIntent(new EndPose(End)),
            TeleportPose = teleport,
            Flags = IntentFlags.Teleport,
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
            Move = new MotionIntent(new EndPose(End)),
            TeleportPose = new DPose(new Double3(-5, 2, 1), DQuaternion.Identity),
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
    public void AnEndPoseConvertsAsAPoseAndStaysAnEndPose()
    {
        MotionIntent converted = new MotionIntent(new EndPose(End)).Converted(Motion, RelativeAcceleration);

        converted.TryGetValue(out EndPose end).Should().BeTrue("the frames' motion gives an end pose no velocity");
        end.Pose.Should().Be(DPose.Compose(Motion.Pose, End));
    }

    [Fact]
    public void ASteerTargetConvertsAsAPoseAndStaysASteerTarget()
    {
        MotionIntent converted = new MotionIntent(new SteerTarget(End)).Converted(Motion, RelativeAcceleration);

        converted.TryGetValue(out SteerTarget steer).Should().BeTrue();
        steer.Pose.Should().Be(DPose.Compose(Motion.Pose, End));
    }

    [Fact]
    public void AFullSampleConvertsByTheRuleOfAHandOff()
    {
        MotionIntent converted = new MotionIntent(Sample).Converted(Motion, RelativeAcceleration);

        converted.TryGetValue(out MotionSample sample).Should().BeTrue();
        DQuaternion turn = Motion.Pose.Rotation;
        DPose composed = DPose.Compose(Motion.Pose, Sample.Pose);
        // A point that moves with the old frame, at the sample's place at the end of the step.
        Double3 carried = Motion.Velocity + Double3.Cross(Motion.AngularVelocity, turn.Rotate(Sample.Pose.Position));
        Distance(sample.Pose.Position, composed.Position).Should().BeLessThan(1e-6, "the place");
        Distance(sample.Pose.Rotation, composed.Rotation).Should().BeLessThan(1e-12, "the rotation");
        Distance(sample.ChordVelocity, turn.Rotate(Sample.ChordVelocity) + carried).Should().BeLessThan(1e-9, "the chord");
        Distance(sample.Velocity, turn.Rotate(Sample.Velocity) + carried).Should().BeLessThan(1e-9, "the velocity");
        Distance(sample.Acceleration, turn.Rotate(Sample.Acceleration) + RelativeAcceleration).Should().BeLessThan(1e-12, "the acceleration");
        Distance(sample.AngularVelocity, turn.Rotate(Sample.AngularVelocity) + Motion.AngularVelocity).Should().BeLessThan(1e-12, "the angular velocity");
        Distance(sample.AngularAcceleration, turn.Rotate(Sample.AngularAcceleration)).Should().BeLessThan(1e-12, "the angular acceleration");
    }

    [Fact]
    public void NoMotionIntentStaysNoneWhenItConverts()
    {
        default(MotionIntent).Converted(Motion, RelativeAcceleration).HasValue.Should().BeFalse();
    }

    // ---- The intent ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnIntentWithAMotionIntentIsNotEmpty()
    {
        new PhysicsIntent().IsEmpty.Should().BeTrue();
        new PhysicsIntent { Move = new MotionIntent(new EndPose(End)) }.IsEmpty.Should().BeFalse("an end pose");
        new PhysicsIntent { Move = new MotionIntent(Sample) }.IsEmpty.Should().BeFalse("a full sample");
        new PhysicsIntent { Move = new MotionIntent(new SteerTarget(End)) }.IsEmpty.Should().BeFalse("a steer target");
    }

    private static double Distance(Double3 a, Double3 b) => (a - b).Length;

    // q and -q are the same rotation.
    private static double Distance(DQuaternion a, DQuaternion b) => Math.Min(
        Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y) + Sq(a.Z - b.Z) + Sq(a.W - b.W)),
        Math.Sqrt(Sq(a.X + b.X) + Sq(a.Y + b.Y) + Sq(a.Z + b.Z) + Sq(a.W + b.W)));

    private static double Sq(double x) => x * x;
}
