using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The end state of the docking, which the exit starts from (docs/plans/blue-danube-demo.md, "The docking", "The parked
/// pose and the end state for the exit", "The fit to the art"): the docked shuttle parks on its wheels on the deck, port
/// side on the cradle rail, with its tail 1.5 m inside the bay's +Y end. So it leaves the lift's hole in the deck free,
/// its hatch opens spinward onto the deck's stair, whose foot lies in the lift's lane, and the spin gravity holds it
/// against the rail. Every place is in the station's axes.
/// </summary>
public sealed class DockingEndStateTests
{
    /// <summary>The bay inside the hub's walls, in meters, in the station's axes.</summary>
    private static BoundingBox Bay => DemoStation.Layout.Bay;

    /// <summary>The corners of the shuttle's hull box at <see cref="StationLayout.ParkedPose"/>, in the station's axes.</summary>
    private static Double3[] ParkedCorners() =>
        [.. Shuttle.HullCorners.Select(corner => DemoStation.Layout.ParkedPose.TransformPoint(corner))];

    /// <summary>The angle between two rotations, in radians.</summary>
    private static double AngleBetween(DQuaternion a, DQuaternion b) => a.AngularVelocityTo(b, 1).Length;

    /// <summary>
    /// At the parked pose, every corner of the hull lies inside the bay. The lowest corners, the wheels, lie on the deck at
    /// x = -102.2 m, the farthest to port on the cradle rail's face at z = -10.92 m, and the tail lies 1.5 m inside the
    /// bay's +Y end.
    /// </summary>
    [Fact]
    public void TheParkedShuttleRestsOnTheDeckWithItsPortSideOnTheCradleRail()
    {
        Double3[] corners = ParkedCorners();
        corners.Min(c => c.X).Should().BeApproximately(DemoStation.Layout.Deck, 1e-5, "the wheels stand on the deck");
        corners
            .Min(c => c.Z)
            .Should()
            .BeApproximately(DemoStation.Layout.CradleAcross, 1e-5, "the port side lies on the cradle rail");
        corners
            .Max(c => c.Y)
            .Should()
            .BeApproximately(Bay.Max.Y - DemoStation.Layout.ParkedTailGap, 1e-5, "the tail lies inside the bay's end");
        DemoStation.Layout.ParkedTailGap.Should().Be(1.5f);
        foreach (Double3 corner in corners)
        {
            corner.X.Should().BeInRange(Bay.Min.X - 1e-5, Bay.Max.X + 1e-5, $"the corner {corner} lies inside the bay");
            corner.Y.Should().BeInRange(Bay.Min.Y - 1e-5, Bay.Max.Y + 1e-5, $"the corner {corner} lies inside the bay");
            corner.Z.Should().BeInRange(Bay.Min.Z - 1e-5, Bay.Max.Z + 1e-5, $"the corner {corner} lies inside the bay");
        }

        (DemoStation.Layout.ParkedPose.Position - new Double3(-99.7102, 21.4344, -4)).Length.Should().BeLessThan(1e-3);
    }

    /// <summary>
    /// The docked rotation puts the nose along the station's -Y, into the bay, the roof along +X, away from the deck, and so
    /// the starboard side, with the hatch, along +Z.
    /// </summary>
    [Fact]
    public void TheDockedRotationPutsTheBellyOnTheDeckAndTheNoseIntoTheBay()
    {
        DQuaternion docked = StationLayout.DockedRotation;
        (docked.Rotate(-Double3.UnitZ) - -Double3.UnitY)
            .Length.Should()
            .BeLessThan(1e-12, "the nose points into the bay");
        (docked.Rotate(Double3.UnitY) - Double3.UnitX)
            .Length.Should()
            .BeLessThan(1e-12, "the roof points away from the deck");
        (docked.Rotate(Double3.UnitX) - Double3.UnitZ)
            .Length.Should()
            .BeLessThan(1e-12, "the starboard side points along +Z");
        AngleBetween(DemoStation.Layout.ParkedPose.Rotation, docked).Should().BeLessThan(1e-12);
    }

    /// <summary>
    /// The hull's corners at the parked pose lie in <see cref="StationLayout.ParkedBounds"/>, and touch each of its six
    /// faces: 9.27 m tall from the deck, 29.09 m long along the axis from 6.41 m to 35.5 m, and 13.84 m wide from the rail.
    /// </summary>
    [Fact]
    public void TheParkedHullFitsItsBounds()
    {
        Double3[] corners = ParkedCorners();
        BoundingBox bounds = DemoStation.Layout.ParkedBounds;
        corners.Min(c => c.X).Should().BeApproximately(bounds.Min.X, 1e-4);
        corners.Max(c => c.X).Should().BeApproximately(bounds.Max.X, 1e-4);
        corners.Min(c => c.Y).Should().BeApproximately(bounds.Min.Y, 1e-4);
        corners.Max(c => c.Y).Should().BeApproximately(bounds.Max.Y, 1e-4);
        corners.Min(c => c.Z).Should().BeApproximately(bounds.Min.Z, 1e-4);
        corners.Max(c => c.Z).Should().BeApproximately(bounds.Max.Z, 1e-4);
        (bounds.Min - new Vector3(-104.611f, 6.4136f, -10.92f)).Length().Should().BeLessThan(1e-3f);
        (bounds.Max - new Vector3(-95.3406f, 35.5f, 2.92f)).Length().Should().BeLessThan(1e-3f);
    }

    /// <summary>
    /// The parked shuttle's box and the lift's hole in the deck share no point: 4.36 m of deck lies between the hull's nose
    /// and the hole along the axis. The hole is the lift's floor, 4 m on a side, with 5 cm round it, through the deck.
    /// </summary>
    [Fact]
    public void TheParkedShuttleLeavesTheLiftsHoleFree()
    {
        BoundingBox hole = DemoStation.Layout.LiftHole,
            parked = DemoStation.Layout.ParkedBounds;
        (hole.Min - new Vector3(-107.311f, -2.05f, 1.75f)).Length().Should().BeLessThan(1e-4f);
        (hole.Max - new Vector3(-104.611f, 2.05f, 5.85f)).Length().Should().BeLessThan(1e-4f);
        (parked.Min.Y - hole.Max.Y)
            .Should()
            .BeApproximately(4.3636f, 1e-3f, "the hull's nose ends 4.36 m short of the hole along the axis");
        ParkedCorners()
            .Should()
            .OnlyContain(corner => corner.Y > hole.Max.Y + 4, "no part of the hull reaches over the hole");
    }

    /// <summary>
    /// The hatch opens toward the station's +Z, onto the deck's stair. The stair's foot lies in the lift's lane, within
    /// 0.25 m of the lift's middle across the axis, so the person walks straight along the axis down the ramp, 19.66 m long,
    /// and on 1.33 m from the foot waypoint to the hole, with no Coriolis force on the way.
    /// </summary>
    [Fact]
    public void TheParkedHatchOpensOntoTheStairInTheLiftsLane()
    {
        DPose parked = DemoStation.Layout.ParkedPose;
        (parked.Rotation.Rotate(Double3.UnitX) - Double3.UnitZ)
            .Length.Should()
            .BeLessThan(1e-12, "the hatch's outward normal is +Z");
        BoundingBox mouth = DemoStation.Layout.LiftHole;
        Double3 foot = DemoScenes.StationExit.StairPlace + DemoScenes.StationExit.Stair.AtFoot(above: 0);

        foot.X.Should().BeApproximately(DemoStation.Layout.Deck, 1e-4, "the stair's foot stands on the deck");
        foot.Z.Should()
            .BeApproximately(DemoStation.Layout.LiftAcross, 0.25, "the stair's foot lies in the lift's lane");
        (foot.Y - mouth.Max.Y).Should().BeApproximately(1.33, 0.02, "the person walks along the axis to the hole");
    }

    /// <summary>
    /// The spin gravity at the parked center of mass, ω² r away from the axis, points into the deck and into the cradle
    /// rail: 0.491 m/s², leaning 2.3 degrees from the deck's normal toward the rail. So the corner holds the hull.
    /// </summary>
    [Fact]
    public void TheSpinPullsTheParkedShuttleIntoTheCorner()
    {
        Double3 center = DemoStation.Layout.ParkedPose.TransformPoint(HostTesting.ShuttleCenterOfMass);
        double rate = DemoStation.Layout.Rate;
        Double3 gravity = new Double3(center.X, 0, center.Z) * (rate * rate);
        gravity.X.Should().BeNegative("the spin gravity presses the hull into the deck");
        gravity.Z.Should().BeNegative("the spin gravity presses the hull into the cradle rail");
        gravity.Length.Should().BeApproximately(0.491, 0.001);
        (Math.Atan2(-gravity.Z, -gravity.X) * 180 / Math.PI).Should().BeApproximately(2.3, 0.1);
    }

    /// <summary>
    /// The station flies its orbit in a root frame with Earth's point gravity, and a dynamic shuttle hull spawns at the
    /// parked pose in the station's turning frame, at rest there, with no push. The deck and the rail hold it: after 10 s
    /// it lies within 3 cm and 0.5 degrees of the pose, still in the station's frame. The cabin's gravity at the seat stand
    /// is ω² times the seat's distance from the station's axis, 0.487 m/s², and leans 2.31 degrees from the floor's down
    /// toward port.
    /// </summary>
    [Fact]
    public void ADynamicShuttleAtTheParkedPoseStaysInTheCorner()
    {
        using World world = RendezvousAutopilotTests.NewWorld();
        var log = new List<string>();
        world.Log += log.Add;
        FrameHandle earth = RendezvousAutopilotTests.Earth(world);
        Entity station = DemoStation.Spawn(world: world, parent: earth, scene: new(), resources: RenderResources.None);
        FrameHandle frame = world.GetOwnedFrame(station);
        Entity shuttle = world.Spawn(
            new()
            {
                Frame = frame,
                Pose = DemoStation.Layout.ParkedPose,
                Body = Shuttle.Body(BodyMotion.Dynamic),
                AsFrame = new(new(Shuttle.Bubble)),
            }
        );

        Tick(world, 600);

        world.GetFrame(shuttle).Should().Be(frame, "the shuttle stays in the station's frame");
        DPose pose = world.PoseIn(shuttle, frame);
        (pose.Position - DemoStation.Layout.ParkedPose.Position)
            .Length.Should()
            .BeLessThan(0.03, "the corner holds the hull at its place");
        AngleBetween(pose.Rotation, StationLayout.DockedRotation)
            .Should()
            .BeLessThan(0.5 * Math.PI / 180, "the corner holds the hull's rotation");

        double rate = DemoStation.Layout.Rate;
        Double3 seat = DemoStation.Layout.ParkedPose.TransformPoint(Shuttle.SeatStand);
        double fromTheAxis = Math.Sqrt(seat.X * seat.X + seat.Z * seat.Z);
        Double3 felt = world.Frames.FeltGravity(world.GetOwnedFrame(shuttle), Shuttle.SeatStand);
        felt.Length.Should()
            .BeApproximately(
                rate * rate * fromTheAxis,
                0.03 * rate * rate * fromTheAxis,
                "the cabin feels the spin gravity of its radius"
            );
        felt.Length.Should().BeApproximately(0.487, 0.005);
        (Math.Acos(Double3.Dot(felt, -Double3.UnitY) / felt.Length) * 180 / Math.PI)
            .Should()
            .BeApproximately(2.31, 0.3, "the gravity leans from the floor's down");
        felt.X.Should().BeNegative("the gravity leans toward port, away from the station's axis");
        world.Errors.Should().BeEmpty();
    }
}
