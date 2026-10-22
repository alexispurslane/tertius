using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// <c>Snapshot.TryGetPose</c> and <see cref="Snapshot.TryGetPoseIn"/>: an entity's pose in its own frame, and in any
/// other frame, composed through the frame tree. A turned frame at (100, 0, 0) is the fixture: a rotation of +90 degrees
/// about +Y maps (x, y, z) to (z, y, -x), and its inverse maps (x, y, z) to (-z, y, x).
/// </summary>
public class FramePoseQueryTests
{
    private static readonly DPose TurnedPose = new(new(100, 0, 0), PoseChecks.QuarterTurnAboutY);

    [Fact]
    public void AnEntityInTheAskedFrameHasItsLocalPose()
    {
        using var test = new TestWorld();
        FrameHandle turned = test.SpawnStaticFrame(TurnedPose);
        Entity entity = test.World.SpawnAt(turned, new(3, 4, 5));

        test.World.Snapshot.TryGetPoseIn(entity, turned, out DPose pose).Should().BeTrue();

        pose.Position.Should().Be(new Double3(3, 4, 5));
    }

    [Fact]
    public void AnEntityInAChildFrameHasItsPoseComposedWithTheFramesInTheParentFrame()
    {
        using var test = new TestWorld();
        FrameHandle turned = test.SpawnStaticFrame(TurnedPose);
        Entity entity = test.World.SpawnAt(turned, new(3, 4, 5));

        test.World.Snapshot.TryGetPoseIn(entity, test.Root, out DPose pose).Should().BeTrue();

        // (3, 4, 5) turned by the frame is (5, 4, -3), and the frame sits 100 m along +X.
        PoseChecks.AssertNear(pose.Position, new(105, 4, -3));
        PoseChecks.AssertSameRotation(pose.Rotation, PoseChecks.QuarterTurnAboutY);
    }

    [Fact]
    public void AnEntityInTheParentFrameHasItsPoseInAChildFrameThroughTheInverseOfTheFramesPose()
    {
        using var test = new TestWorld();
        FrameHandle turned = test.SpawnStaticFrame(TurnedPose);
        Entity entity = test.SpawnAt(new(105, 4, -3));

        test.World.Snapshot.TryGetPoseIn(entity, turned, out DPose pose).Should().BeTrue();

        // The inverse of the example above: (105, 4, -3) - (100, 0, 0) = (5, 4, -3), turned back to (3, 4, 5).
        PoseChecks.AssertNear(pose.Position, new(3, 4, 5));
    }

    [Fact]
    public void AnEntityInASiblingFrameHasItsPoseComposedThroughTheParentFrame()
    {
        using var test = new TestWorld();
        FrameHandle turned = test.SpawnStaticFrame(TurnedPose);
        FrameHandle plain = test.SpawnStaticFrame(TestWorld.At(0, 0, 1000));
        Entity entity = test.World.SpawnAt(turned, new(3, 4, 5));

        test.World.Snapshot.TryGetPoseIn(entity, plain, out DPose pose).Should().BeTrue();

        // (105, 4, -3) in the root, minus the plain frame's (0, 0, 1000), with no turn.
        PoseChecks.AssertNear(pose.Position, new(105, 4, -1003));
    }

    [Fact]
    public void TheOwnerOfARootFrameHasTheFramesOwnPoseInAnotherFrame()
    {
        using var test = new TestWorld();
        FrameHandle turned = test.SpawnStaticFrame(TurnedPose);

        test.World.Snapshot.TryGetPoseIn(test.Root.Entity, turned, out DPose pose).Should().BeTrue();

        // The root frame's origin is at (-100, 0, 0) in the turned frame's parent axes, which the inverse turn maps to (0, 0, -100).
        PoseChecks.AssertNear(pose.Position, new(0, 0, -100));
    }

    [Fact]
    public void AnEntityInARootFrameHasItsFrameAndPoseFromTheFrontSnapshot()
    {
        using var test = new TestWorld();
        Entity entity = test.SpawnAt(new(7, 8, 9));
        Entity rootOwner = test.Root.Entity;

        FrameHandle frame = test.World.Snapshot.FrameOf(entity);
        test.World.Snapshot.TryGetPoseIn(entity, frame, out DPose pose).Should().BeTrue();
        test.World.Snapshot.TryGetPoseIn(rootOwner, test.Root, out DPose ownerPose).Should().BeTrue();

        frame.Should().Be(test.Root);
        pose.Position.Should().Be(new Double3(7, 8, 9));
        test.World.Snapshot.FrameOf(rootOwner).IsNull.Should().BeTrue("the owner of a root frame is in no frame");
        ownerPose.Should().Be(DPose.Identity, "the owner of a root frame is at the origin of its own frame");
    }

    [Fact]
    public void AFrameThatHasBeenDestroyedCannotBeAskedForAPose()
    {
        using var test = new TestWorld();
        FrameHandle gone = test.SpawnStaticFrame(TurnedPose);
        Entity entity = test.SpawnAt(new(1, 2, 3));
        test.World.Destroy(gone.Entity);

        test.World.Snapshot.TryGetPoseIn(entity, gone, out DPose pose).Should().BeFalse();

        pose.Should().Be(default(DPose));
    }

    [Fact]
    public void AnEntityThatHasBeenDestroyedHasNoPoseInAnyFrame()
    {
        using var test = new TestWorld();
        Entity gone = test.SpawnAt(new(1, 2, 3));
        test.World.Destroy(gone);

        test.World.Snapshot.TryGetPoseIn(gone, test.Root, out _).Should().BeFalse();
        test.World.Snapshot.TryGetVelocityIn(gone, test.Root, out Double3 velocity).Should().BeFalse();
        velocity.Should().Be(Double3.Zero);
    }
}
