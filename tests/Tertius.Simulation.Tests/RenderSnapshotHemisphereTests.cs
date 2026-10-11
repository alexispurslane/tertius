using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Which way is up for the hemisphere ambient light of each instance: against the gravity field of the instance's own
/// frame, at its place. The sky color comes from above and the ground color from below. Without gravity, the mix is
/// even. The renderer reads it in the axes of the camera's frame, so it does not depend on the frame that the camera
/// rides in.
/// </summary>
public class RenderSnapshotHemisphereTests
{
    private static readonly DQuaternion QuarterTurnAboutX = DQuaternion.FromAxisAngle(Double3.UnitX, Math.PI / 2);

    private static FrameHandle Frame(TestWorld test, DPose pose, GravityField gravity) =>
        test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = pose,
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static, gravity),
        }));

    private static HemisphereUp HemisphereOf(TestWorld test, Entity entity)
    {
        test.World.Camera = test.SpawnCamera(test.Root, new DPose(new Double3(0, 0, 50), DQuaternion.Identity));
        test.Run(1);
        return test.World.Latest().InstanceOf(entity).Hemisphere;
    }

    private static void ExpectNear(Vector3 actual, Vector3 expected) =>
        Vector3.Distance(actual, expected).Should().BeLessThan(1e-5f, $"{actual} is not {expected}");

    [Fact]
    public void InAFrameWithoutGravityTheSkyAndTheGroundMixEvenly()
    {
        using var test = new TestWorld();
        Entity box = test.Show(test.Root, DPose.Identity);

        HemisphereOf(test, box).Kind.Should().Be(HemisphereKind.Even);
    }

    [Fact]
    public void InAUniformFieldUpPointsAgainstTheFieldInTheAxesOfTheCamerasFrame()
    {
        using var test = new TestWorld();
        // The frame lies on its side: a quarter turn about +X takes its +Y to the root's +Z.
        FrameHandle ship = Frame(test, new DPose(new Double3(5, 0, 0), QuarterTurnAboutX), GravityField.Uniform(new Vector3(0, -3, 0)));
        Entity box = test.Show(ship, new DPose(new Double3(1, 2, 0), DQuaternion.Identity));

        HemisphereUp up = HemisphereOf(test, box);

        up.Kind.Should().Be(HemisphereKind.Along);
        ExpectNear(up.Value, Vector3.UnitZ);
    }

    [Fact]
    public void InAPointFieldUpLeadsAwayFromTheFramesOrigin()
    {
        using var test = new TestWorld();
        FrameHandle planet = Frame(test, new DPose(new Double3(0, -30, 0), QuarterTurnAboutX), GravityField.Point(4e14));
        Entity box = test.Show(planet, new DPose(new Double3(0, 0, 10), DQuaternion.Identity));

        HemisphereUp up = HemisphereOf(test, box);

        // Relative to the render origin, the camera at (0, 0, 50) in the root frame.
        up.Kind.Should().Be(HemisphereKind.AwayFrom);
        ExpectNear(up.Value, new Vector3(0, -30, -50));
    }

    [Fact]
    public void OutsideTheRegionOfAUniformFieldTheSkyAndTheGroundMixEvenly()
    {
        using var test = new TestWorld();
        var cabin = new BoundingBox(new Vector3(-2), new Vector3(2));
        FrameHandle ship = Frame(test, DPose.Identity, GravityField.Uniform(new Vector3(0, -9.81f, 0), cabin));
        Entity inside = test.Show(ship, new DPose(new Double3(1, 1, 1), DQuaternion.Identity));
        Entity outside = test.Show(ship, new DPose(new Double3(0, 3, 0), DQuaternion.Identity));

        test.World.Camera = test.SpawnCamera(test.Root, new DPose(new Double3(0, 0, 50), DQuaternion.Identity));
        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot.InstanceOf(inside).Hemisphere.Kind.Should().Be(HemisphereKind.Along);
        snapshot.InstanceOf(outside).Hemisphere.Kind.Should().Be(HemisphereKind.Even);
    }

    [Fact]
    public void AFrameOwnerTakesTheFieldOfItsOwnFrameAtItsOrigin()
    {
        using var test = new TestWorld();
        // The hull sits in the root frame, which has no gravity, but it is the ship: the cabin's field holds at its origin.
        Entity hull = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = new DPose(new Double3(0, 0, -5), QuarterTurnAboutX),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100), MotionDriver.Static,
                GravityField.Uniform(new Vector3(0, -9.81f, 0), new BoundingBox(new Vector3(-1), new Vector3(1)))),
            Render = RenderSnapshotSupport.Visible(),
        });

        HemisphereUp up = HemisphereOf(test, hull);

        up.Kind.Should().Be(HemisphereKind.Along);
        ExpectNear(up.Value, Vector3.UnitZ);
    }

    [Theory]
    [InlineData(GravityKind.Uniform)]
    [InlineData(GravityKind.Point)]
    public void AFieldThatPullsNowhereMixesTheSkyAndTheGroundEvenly(GravityKind kind)
    {
        using var test = new TestWorld();
        FrameHandle weightless = Frame(test, DPose.Identity, kind == GravityKind.Uniform ? GravityField.Uniform(Vector3.Zero) : GravityField.Point(0));
        Entity box = test.Show(weightless, new DPose(new Double3(0, 2, 0), DQuaternion.Identity));

        HemisphereOf(test, box).Kind.Should().Be(HemisphereKind.Even);
    }
}
