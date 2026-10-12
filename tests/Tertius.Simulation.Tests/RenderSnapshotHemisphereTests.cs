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

        HemisphereOf(test, box).HasValue.Should().BeFalse();
    }

    [Fact]
    public void InAUniformFieldUpPointsAgainstTheFieldInTheAxesOfTheCamerasFrame()
    {
        using var test = new TestWorld();
        // The frame lies on its side: a quarter turn about +X takes its +Y to the root's +Z.
        FrameHandle ship = Frame(test, new DPose(new Double3(5, 0, 0), QuarterTurnAboutX), GravityField.Uniform(new Vector3(0, -3, 0)));
        Entity box = test.Show(ship, new DPose(new Double3(1, 2, 0), DQuaternion.Identity));

        HemisphereUp up = HemisphereOf(test, box);

        up.TryGetValue(out UpAlong along).Should().BeTrue();
        ExpectNear(along.Direction, Vector3.UnitZ);
    }

    [Fact]
    public void InAPointFieldUpLeadsAwayFromTheFramesOriginGivenRelativeToTheInstance()
    {
        using var test = new TestWorld();
        FrameHandle planet = Frame(test, new DPose(new Double3(0, -30, 0), QuarterTurnAboutX), GravityField.Point(4e14));
        // The planet's quarter turn takes the box's +Z offset to the root's -Y: the box is at (0, -40, 0).
        Entity box = test.Show(planet, new DPose(new Double3(0, 0, 10), DQuaternion.Identity));

        HemisphereUp up = HemisphereOf(test, box);

        up.TryGetValue(out UpAwayFrom away).Should().BeTrue();
        ExpectNear(away.Center, new Vector3(0, 10, 0));
    }

    [Fact]
    public void APlanetsOwnCenterIsExactlyAtTheInstance()
    {
        using var test = new TestWorld();
        Entity planet = test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = new DPose(new Double3(0, -3e6, 1e5), DQuaternion.Identity),
            AsFrame = new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Point(4e14)),
            Render = RenderSnapshotSupport.Visible(),
        });

        HemisphereUp up = HemisphereOf(test, planet);

        // So the center moves with the planet's interpolated position, and does not wobble between ticks.
        up.Should().Be(HemisphereUp.AwayFrom(Vector3.Zero));
    }

    [Fact]
    public void WithTheCameraInATurnedFrameTheUpIsGivenInThatFramesAxes()
    {
        using var test = new TestWorld();
        // The camera's frame is turned a quarter turn about +X, so the root's +Y is its -Z and the root's -Y its +Z.
        FrameHandle cameraFrame = test.SpawnStaticFrame(new DPose(new Double3(7, 0, 0), QuarterTurnAboutX));
        FrameHandle ship = Frame(test, DPose.Identity, GravityField.Uniform(new Vector3(0, -3, 0)));
        FrameHandle planet = Frame(test, new DPose(new Double3(0, -30, 0), DQuaternion.Identity), GravityField.Point(4e14));
        Entity crate = test.Show(ship, new DPose(new Double3(1, 0, 0), DQuaternion.Identity));
        Entity rock = test.Show(planet, new DPose(new Double3(0, 31, 0), DQuaternion.Identity));
        test.World.Camera = test.SpawnCamera(cameraFrame, DPose.Identity);

        test.Run(1);
        RenderSnapshot snapshot = test.World.Latest();

        snapshot.InstanceOf(crate).Hemisphere.TryGetValue(out UpAlong along).Should().BeTrue();
        ExpectNear(along.Direction, -Vector3.UnitZ);
        // The planet's center is 31 m below the rock along the root's -Y.
        snapshot.InstanceOf(rock).Hemisphere.TryGetValue(out UpAwayFrom away).Should().BeTrue();
        ExpectNear(away.Center, new Vector3(0, 0, 31));
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

        snapshot.InstanceOf(inside).Hemisphere.TryGetValue(out UpAlong _).Should().BeTrue();
        snapshot.InstanceOf(outside).Hemisphere.HasValue.Should().BeFalse();
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

        up.TryGetValue(out UpAlong along).Should().BeTrue();
        ExpectNear(along.Direction, Vector3.UnitZ);
    }

    [Theory]
    [InlineData("a uniform field")]
    [InlineData("a point field")]
    public void AFieldThatPullsNowhereMixesTheSkyAndTheGroundEvenly(string kind)
    {
        using var test = new TestWorld();
        FrameHandle weightless = Frame(test, DPose.Identity, kind == "a uniform field" ? GravityField.Uniform(Vector3.Zero) : GravityField.Point(0));
        Entity box = test.Show(weightless, new DPose(new Double3(0, 2, 0), DQuaternion.Identity));

        HemisphereOf(test, box).HasValue.Should().BeFalse();
    }
}
