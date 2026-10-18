using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The frame tree tells how each frame followed its owner at the last sync point (<see cref="FrameTree.Followed"/>), from
/// the frame's row in the followed buffer of the sample table: a sample that the owner wrote is rails, the record of a
/// frame with a dynamic hull is physics, and any other record, or no row, is static. These tests drive the table as the
/// sync point does: a write during the tick, a record in step 2, and the turn of the buffers in step 4.
/// </summary>
public class FrameTreeFollowedTests
{
    private readonly FrameTree frames = new();
    private readonly FrameHandle root;

    public FrameTreeFollowedTests() =>
        root = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));

    /// <summary>A frame of the owner <paramref name="id"/>, 100 m along +X in <paramref name="parent"/>, with a hull of <paramref name="motion"/>.</summary>
    private FrameHandle Child(uint id, FrameHandle parent, BodyMotion? motion)
    {
        FrameHandle frame = frames.Create(new Entity(id, 1), parent, new DPose(new Double3(100, 0, 0), DQuaternion.Identity), FrameBounds.Sphere(10));
        frames.SetHullMotion(frame, motion);
        return frame;
    }

    /// <summary>A sample at rest 5 m along +X.</summary>
    private static MotionSample Sample =>
        new(new DPose(new Double3(5, 0, 0), DQuaternion.Identity), Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Vector3.Zero);

    /// <summary>Ends a tick: step 4 turns the buffers.</summary>
    private void SyncPoint() => frames.Samples.MoveOn();

    [Fact]
    public void AFrameFollowsRailsAtTheSyncPointAfterTheStepThatAppliesTheSampleOfItsOwner()
    {
        FrameHandle moon = Child(2, root, motion: null);
        frames.Samples.Write(moon, root, Sample);

        SyncPoint();
        FollowKind whileApplied = frames.Followed(moon);
        SyncPoint();

        whileApplied.Should().Be(FollowKind.Static, "the step that applies the sample has not ended yet");
        frames.Followed(moon).Should().Be(FollowKind.Rails);
    }

    [Fact]
    public void AFrameWithADynamicHullFollowsPhysicsAtTheSyncPointOfTheStepThatMovedIt()
    {
        FrameHandle ship = Child(2, root, BodyMotion.Dynamic);

        frames.Samples.Record(ship, root, Sample);
        SyncPoint();

        frames.Followed(ship).Should().Be(FollowKind.Physics);
    }

    [Fact]
    public void AFrameWhoseHullParksAfterTheStepStillFollowsPhysicsAtThatSyncPoint()
    {
        FrameHandle ship = Child(2, root, BodyMotion.Dynamic);

        frames.Samples.Record(ship, root, Sample);
        frames.SetHullMotion(ship, BodyMotion.Static);
        SyncPoint();

        frames.Followed(ship).Should().Be(FollowKind.Physics, "step 4 changes the motion after step 2 took the record of the step");
    }

    [Theory]
    [InlineData(BodyMotion.Static)]
    [InlineData(BodyMotion.Kinematic)]
    public void AFrameWithAHullThatIsNotDynamicFollowsATeleportAsAStaticFrame(BodyMotion motion)
    {
        FrameHandle ship = Child(2, root, motion);

        frames.Samples.Record(ship, root, Sample);
        SyncPoint();

        frames.Followed(ship).Should().Be(FollowKind.Static);
    }

    [Fact]
    public void AFrameWithoutABodyFollowsAWriteOfItsOwnersTransformAsAStaticFrame()
    {
        FrameHandle room = Child(2, root, motion: null);

        frames.Samples.Record(room, root, Sample);
        SyncPoint();

        frames.Followed(room).Should().Be(FollowKind.Static);
    }

    [Fact]
    public void TheRecordOfAStepThatAppliedASampleLeavesTheFrameOnRails()
    {
        FrameHandle station = Child(2, root, BodyMotion.Kinematic);
        frames.Samples.Write(station, root, Sample);
        SyncPoint();

        frames.Samples.Record(station, root, Sample).Should().BeFalse("the sample is the record of the step");
        SyncPoint();

        frames.Followed(station).Should().Be(FollowKind.Rails);
    }

    [Fact]
    public void AFrameWithoutARowFollowsNothing()
    {
        FrameHandle ship = Child(2, root, BodyMotion.Dynamic);
        frames.Samples.Record(ship, root, Sample);
        SyncPoint();

        SyncPoint();

        frames.Followed(ship).Should().Be(FollowKind.Static, "physics made no record of the second step");
    }

    [Fact]
    public void ASampleThatConvertsIntoAnotherFrameKeepsWhatMadeIt()
    {
        FrameHandle ship = Child(2, root, BodyMotion.Dynamic);
        FrameHandle moon = Child(3, ship, motion: null);
        frames.Samples.Write(moon, ship, Sample);
        SyncPoint();
        SyncPoint();

        frames.Samples.ConvertInto(moon, root);

        frames.Followed(moon).Should().Be(FollowKind.Rails);
    }

    [Fact]
    public void AFrameInTheSlotOfADeadFrameFollowsNothingOfTheDeadFrame()
    {
        FrameHandle first = Child(2, root, motion: null);
        frames.Samples.Write(first, root, Sample);
        SyncPoint();
        SyncPoint();
        frames.Destroy(first);

        FrameHandle second = Child(3, root, motion: null);

        second.Index.Should().Be(first.Index, "the tree reuses the slot of the dead frame");
        frames.Followed(second).Should().Be(FollowKind.Static);
    }

    [Fact]
    public void AStaleFrameHasNoWayOfFollowing()
    {
        FrameHandle ship = Child(2, root, motion: null);
        frames.Destroy(ship);

        FluentActions.Invoking(() => frames.Followed(ship)).Should().Throw<InvalidOperationException>();
    }
}
