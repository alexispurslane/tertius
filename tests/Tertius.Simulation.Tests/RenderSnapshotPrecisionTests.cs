using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The snapshot gives every position relative to the camera, as a single-precision vector. The builder computes the
/// position of each object relative to the camera in double precision, along the frame tree, and narrows it to single
/// precision last. So an object next to the camera is exact however far the pair is from the origin of its frame, an
/// object in a frame a hundred million kilometres away is the single-precision number nearest to where it is, and two
/// objects that are close to each other but far from the origin are as close in the snapshot as they are in the world.
/// A single-precision number has 24 bits, so near 1e11 m its spacing is 8192 m.
/// </summary>
public class RenderSnapshotPrecisionTests
{
    [Fact]
    public void AnObjectNextToTheCameraIsExactEvenWhenBothAreFarFromTheOriginOfTheirFrame()
    {
        using var test = new TestWorld();
        // At 1.2e8 m a single-precision number is accurate to 8 m. The two positions differ by exactly 2 m.
        Entity camera = test.SpawnCamera(test.Root, TestWorld.At(123456789.125));
        Entity near = test.Show(test.Root, TestWorld.At(123456791.125));
        test.World.Camera = camera;

        test.Run(1);

        test.World.Latest().InstanceOf(near).Position.Should().Be(new Vector3(2, 0, 0));
    }

    [Fact]
    public void AnObjectInASiblingFrameAHundredBillionMetresAwayIsTheSinglePrecisionNumberNearestToItsPosition()
    {
        using var test = new TestWorld();
        FrameHandle home = test.SpawnStaticFrame(DPose.Identity, radius: 1e6f);
        FrameHandle distant = test.SpawnStaticFrame(new(new(1e11, 0, 0), PoseChecks.QuarterTurnAboutY), radius: 1e6f);
        Entity camera = test.SpawnCamera(home, TestWorld.At(12345.678));
        // The quarter turn maps the frame's (3, 4, 5) to (5, 4, -3), so the object is at (1e11 + 5, 4, -3) in the root.
        Entity far = test.Show(distant, TestWorld.At(3, 4, 5));
        test.World.Camera = camera;

        test.Run(1);

        Vector3 position = test.World.Latest().InstanceOf(far).Position;
        position.X.Should().Be((float)(1e11 + 5 - 12345.678));
        position.Y.Should().Be(4);
        position.Z.Should().Be(-3);
    }

    [Fact]
    public void AnObjectInASiblingFrameIsTurnedIntoTheAxesOfTheCamerasFrame()
    {
        using var test = new TestWorld();
        FrameHandle home = test.SpawnStaticFrame(DPose.Identity, radius: 1e6f);
        FrameHandle distant = test.SpawnStaticFrame(new(new(1e11, 0, 0), PoseChecks.QuarterTurnAboutY), radius: 1e6f);
        Entity far = test.Show(distant, DPose.Identity);
        test.World.Camera = test.SpawnCamera(home, DPose.Identity);

        test.Run(1);

        PoseChecks.AssertSameRotation(test.World.Latest().InstanceOf(far).Rotation, PoseChecks.QuarterTurnAboutY);
    }

    [Fact]
    public void TwoObjectsTwoMetresApartAHundredBillionMetresFromTheOriginAreTwoMetresApartInTheSnapshot()
    {
        using var test = new TestWorld();
        // Both frames are 1e11 m from the origin of the root, and the camera and the object are 2 m apart.
        FrameHandle cameraFrame = test.SpawnStaticFrame(TestWorld.At(1e11), radius: 1e6f);
        FrameHandle objectFrame = test.SpawnStaticFrame(TestWorld.At(1e11 + 3), radius: 1e6f);
        Entity camera = test.SpawnCamera(cameraFrame, TestWorld.At(1));
        Entity object_ = test.Show(objectFrame, TestWorld.At(0, 4, 5));
        test.World.Camera = camera;

        test.Run(1);

        // The object is at x = 1e11 + 3 and the camera at 1e11 + 1: exactly 2 m apart, with 4 m and 5 m to spare in y and z.
        test.World.Latest().InstanceOf(object_).Position.Should().Be(new Vector3(2, 4, 5));
    }

    [Fact]
    public void AnObjectAtTheCamerasPositionIsAtTheOriginOfTheSnapshotWhateverThePosition()
    {
        using var test = new TestWorld();
        FrameHandle distant = test.SpawnStaticFrame(TestWorld.At(1e11, -3e10, 7e9), radius: 1e6f);
        Entity camera = test.SpawnCamera(distant, TestWorld.At(123456.789, 0, 0));
        Entity companion = test.Show(distant, TestWorld.At(123456.789, 0, 0));
        test.World.Camera = camera;

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.InstanceOf(companion).Position.Should().Be(Vector3.Zero);
        snapshot.Camera.Position.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void ACameraMovesTheRenderOriginAndNotTheObjectsRelativeToEachOther()
    {
        using var test = new TestWorld();
        Entity left = test.Show(test.Root, TestWorld.At(100, 0, 0));
        Entity right = test.Show(test.Root, TestWorld.At(110, 0, 0));
        test.World.Camera = test.SpawnCamera(test.Root, TestWorld.At(-5000));

        test.Run(1);

        RenderSnapshot snapshot = test.World.Latest();
        snapshot.InstanceOf(left).Position.Should().Be(new Vector3(5100, 0, 0));
        snapshot.InstanceOf(right).Position.Should().Be(new Vector3(5110, 0, 0));
    }
}
