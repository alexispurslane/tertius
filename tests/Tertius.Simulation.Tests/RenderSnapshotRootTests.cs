using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The rotation of the root frame in a render snapshot: the root frame of the camera's frame tree, turned into the axes of
/// the camera's frame. The renderer fixes the star field to the root's axes with it, so the stars do not turn with the
/// frame that the camera rides in.
/// </summary>
public class RenderSnapshotRootTests
{
    private static readonly DQuaternion QuarterTurnAboutY = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);
    private static readonly DQuaternion TiltAboutX = DQuaternion.FromAxisAngle(Double3.UnitX, 0.4);

    private static void ExpectNear(Quaternion actual, DQuaternion expected) =>
        Vector3.Distance(Vector3.Transform(Vector3.UnitX, actual), expected.Rotate(Double3.UnitX).ToVector3())
            .Should().BeLessThan(1e-6f, $"{actual} does not turn +X as {expected} does");

    private static void ExpectSame(Quaternion actual, DQuaternion expected)
    {
        // Two axes pin a rotation down.
        ExpectNear(actual, expected);
        Vector3.Distance(Vector3.Transform(Vector3.UnitY, actual), expected.Rotate(Double3.UnitY).ToVector3()).Should().BeLessThan(1e-6f);
    }

    [Fact]
    public void ACameraInTheRootFrameSeesTheRootsAxesUnturned()
    {
        using var test = new TestWorld();
        test.World.Camera = test.SpawnCamera(test.Root, new DPose(new Double3(3, 0, 0), QuarterTurnAboutY));

        test.Run(1);

        ExpectSame(test.World.Latest().RootRotation, DQuaternion.Identity);
    }

    [Fact]
    public void ACameraInANestedTurnedFrameGetsTheRootsAxesInItsFramesAxes()
    {
        using var test = new TestWorld();
        FrameHandle ship = test.SpawnStaticFrame(new DPose(new Double3(10, 0, 0), QuarterTurnAboutY));
        FrameHandle cabin = test.SpawnStaticFrame(new DPose(Double3.Zero, TiltAboutX), radius: 10, parent: ship);
        // The camera's own rotation turns the view, not the axes of its frame, so it does not count.
        test.World.Camera = test.SpawnCamera(cabin, new DPose(Double3.Zero, TiltAboutX));

        test.Run(1);

        // The cabin's axes are the root's axes turned by the ship's turn, then by the cabin's tilt. The root's axes, seen
        // from the cabin, are turned back by both.
        ExpectSame(test.World.Latest().RootRotation, (QuarterTurnAboutY * TiltAboutX).Conjugate());
    }

    [Fact]
    public void TheRootRotationOfThePreviousTickLetsTheSkyTurnSmoothlyWhileTheCamerasFrameTurns()
    {
        using var test = new TestWorld();
        FrameHandle turning = test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Spin, Spin: new FrameSpin(DQuaternion.Identity, 1)),
        }));
        test.World.Camera = test.SpawnCamera(turning, DPose.Identity);
        test.Run(1);
        Quaternion before = test.World.Latest().RootRotation;

        test.Run(1);
        RenderSnapshot after = test.World.Latest();

        after.PreviousRootRotation.Should().Be(before);
        ExpectSame(after.RootRotation, test.World.Frames.LocalPose(turning).Rotation.Conjugate());
        after.RootRotation.Should().NotBe(before, "the frame turned by 1 rad/s for one tick");
    }

    [Fact]
    public void WhenTheCameraChangesFrameTheSkyDoesNotBlendFromTheOldFramesAxes()
    {
        using var test = new TestWorld();
        FrameHandle ship = test.SpawnStaticFrame(new DPose(new Double3(10, 0, 0), QuarterTurnAboutY));
        Entity outside = test.SpawnCamera(test.Root, DPose.Identity), inside = test.SpawnCamera(ship, DPose.Identity);
        test.World.Camera = outside;
        test.Run(1);
        test.World.Latest();

        test.World.Camera = inside;
        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot.PreviousRootRotation.Should().Be(snapshot.RootRotation);
    }

    [Fact]
    public void WithoutACameraTheRootRotationIsTheIdentity()
    {
        using var test = new TestWorld();

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        (snapshot.RootRotation, snapshot.PreviousRootRotation).Should().Be((Quaternion.Identity, Quaternion.Identity));
    }
}
