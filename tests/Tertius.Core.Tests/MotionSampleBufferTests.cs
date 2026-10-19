using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The three buffers of the sample table (<see cref="MotionSampleTable"/>) on a tree built in the test: the written buffer
/// holds what the owner of a frame writes in the running tick, and every <see cref="MotionSampleTable.MoveOn"/> hands it on
/// to the applied buffer and then to the followed buffer. The tests read the buffers through the table, as the World does.
/// How the World and physics use the buffers is in MotionSampleTableTests (Simulation.Tests).
/// </summary>
public class MotionSampleBufferTests
{
    private readonly FrameTree frames = new();
    private readonly FrameHandle root;
    private readonly FrameHandle frame;

    public MotionSampleBufferTests()
    {
        root = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));
        frame = frames.Create(new Entity(2, 1), root, DPose.Identity, FrameBounds.Sphere(10));
    }

    /// <summary>A sample whose every field differs from the default, so that only the table can have given it back.</summary>
    private static readonly MotionSample Sample = new(
        new DPose(new Double3(1, 2, 3), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5)),
        new Double3(4, 5, 6), new Double3(7, 8, 9), new Double3(0.1, 0.2, 0.3),
        new Double3(0, 0, 0.4), new Double3(0, 0.5, 0));

    [Fact]
    public void A_sample_that_the_owner_writes_is_in_the_written_buffer_and_in_neither_of_the_other_two()
    {
        frames.Samples.Write(frame, root, Sample);

        frames.Samples.Written.TryGet(frame, out MotionSample written, out FrameHandle relativeTo).Should().BeTrue();
        written.Should().Be(Sample);
        relativeTo.Should().Be(root, "the owner wrote it relative to the frame's semantic parent");
        frames.Samples.Applied.TryGet(frame, out _).Should().BeFalse();
        frames.Samples.Followed.TryGet(frame, out _).Should().BeFalse();
    }

    [Fact]
    public void The_written_buffer_is_empty_again_after_the_buffers_move_on_and_the_sample_is_the_applied_one()
    {
        frames.Samples.Write(frame, root, Sample);

        frames.Samples.MoveOn();

        frames.Samples.Written.TryGet(frame, out _).Should().BeFalse("the next tick has written nothing yet");
        frames.Samples.Applied.TryGet(frame, out MotionSample applied).Should().BeTrue();
        applied.Should().Be(Sample);
    }

    [Fact]
    public void A_sample_goes_from_the_written_buffer_to_the_applied_buffer_to_the_followed_buffer_in_two_turns()
    {
        frames.Samples.Write(frame, root, Sample);

        frames.Samples.MoveOn();
        frames.Samples.MoveOn();

        frames.Samples.Written.TryGet(frame, out _).Should().BeFalse();
        frames.Samples.Applied.TryGet(frame, out _).Should().BeFalse();
        frames.Samples.Followed.TryGet(frame, out MotionSample followed).Should().BeTrue();
        followed.Should().Be(Sample);
    }
}
