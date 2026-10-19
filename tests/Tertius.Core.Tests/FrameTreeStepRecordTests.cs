using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The step record of a frame in a step in flight, which the frame tree derives from the motion intent of the frame's owner
/// in the intents of that step (<see cref="FrameTree.StepRecordInStep"/>, docs/plans/rails-components.md, "The motion
/// intent"): a full sample is its own record, an end pose gives the motion at a constant velocity from the frame's pose in
/// its semantic parent where the step starts, and a steer target or no motion intent gives none.
/// </summary>
public class FrameTreeStepRecordTests
{
    private const double Step = 1.0 / 60;
    private readonly FrameTree frames = new();
    private readonly FrameHandle root;
    private readonly FrameHandle frame;

    public FrameTreeStepRecordTests()
    {
        root = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));
        frame = frames.Create(new Entity(4, 1), root, new DPose(new Double3(100, 0, 0), DQuaternion.Identity), FrameBounds.Sphere(10));
    }

    /// <summary>The intents of a step in which the owner of <see cref="frame"/> has <paramref name="move"/>, relative to the root.</summary>
    private PhysicsIntent[] Moving(in MotionIntent move, IntentFlags flags = IntentFlags.None, in DPose teleport = default)
    {
        var intents = new PhysicsIntent[frame.Entity.Index + 1];
        intents[frame.Entity.Index] = new PhysicsIntent { Frame = root, Move = move, Flags = flags, TeleportPose = teleport };
        return intents;
    }

    [Fact]
    public void AFrameWhoseOwnerHasAFullSampleTakesTheSampleAsItsStepRecord()
    {
        var sample = new MotionSample(new DPose(new Double3(101, 2, 3), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.2)),
            new Double3(60, 1, 2), new Double3(61, 1, 2), new Double3(0, -9.8, 0), new Double3(0, 0, 0.3), Double3.Zero);

        frames.StepRecordInStep(frame, Moving(new MotionIntent(sample)), Step, out MotionSample record).Should().BeTrue();

        record.Should().Be(sample);
    }

    [Fact]
    public void AnEndPoseGivesTheFrameTheConstantVelocityRecordFromItsPoseInItsParent()
    {
        var end = new DPose(new Double3(101, 0, 0), DQuaternion.Identity);

        frames.StepRecordInStep(frame, Moving(new MotionIntent(new EndPose(end))), Step, out MotionSample record).Should().BeTrue();

        Double3 chord = (end.Position - new Double3(100, 0, 0)) / Step;
        record.Should().Be(new MotionSample(end, chord, chord, Double3.Zero, Double3.Zero, Double3.Zero));
    }

    [Fact]
    public void AnEndPoseInTheStepOfATeleportStartsAtTheTeleportsPose()
    {
        var end = new DPose(new Double3(51, 0, 0), DQuaternion.Identity);
        var teleport = new DPose(new Double3(50, 0, 0), DQuaternion.Identity);

        frames.StepRecordInStep(frame, Moving(new MotionIntent(new EndPose(end)), IntentFlags.Teleport, teleport), Step, out MotionSample record)
            .Should().BeTrue();

        record.ChordVelocity.Should().Be(new Double3(1, 0, 0) / Step);
    }

    [Fact]
    public void AFrameWhoseOwnerHasASteerTargetOrNoMotionIntentHasNoStepRecord()
    {
        var target = new DPose(new Double3(101, 0, 0), DQuaternion.Identity);

        frames.StepRecordInStep(frame, Moving(new MotionIntent(new SteerTarget(target))), Step, out _).Should().BeFalse("a steer target only steers a body");
        frames.StepRecordInStep(frame, Moving(default), Step, out _).Should().BeFalse();
        frames.StepRecordInStep(frame, [], Step, out _).Should().BeFalse("a slot past the end of the intents holds none");
        frames.StepRecordInStep(frame, new PhysicsIntent[frame.Entity.Index], Step, out _).Should().BeFalse("nor does the slot at their end");
    }
}
