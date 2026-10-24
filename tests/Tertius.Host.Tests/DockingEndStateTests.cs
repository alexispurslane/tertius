using System.Numerics;
using Tertius.Demo;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The end state of the docking, which the exit starts from (docs/plans/blue-danube-demo.md, "The docking", "The parked
/// pose and the end state for the exit"): the docked shuttle parks belly on the deck, port side on the bay's -Z wall, with
/// its tail 1.5 m inside the slot's wall. So it leaves the mouth of the spoke under the deck free, its hatch opens onto the
/// deck toward that mouth, and the spin gravity holds it in the corner. Every place is in the station's axes.
/// </summary>
public sealed class DockingEndStateTests
{
    /// <summary>The half extents of the bay inside the hub's walls, in meters, in the station's axes.</summary>
    private static Vector3 Bay => DemoStation.HubHalf - new Vector3(DemoStation.HubWall);

    /// <summary>The corners of the shuttle's outer collision shape at <see cref="DemoStation.ParkedPose"/>, in the station's axes.</summary>
    private static Double3[] ParkedCorners() =>
        [
            .. ((ConvexHullShapeDesc)Shuttle.HullShape).Points.Select(point =>
                DemoStation.ParkedPose.TransformPoint(point)
            ),
        ];

    /// <summary>The angle between two rotations, in radians.</summary>
    private static double AngleBetween(DQuaternion a, DQuaternion b) => a.AngularVelocityTo(b, 1).Length;

    /// <summary>
    /// At the parked pose, every corner of the hull lies inside the bay. The lowest corners lie on the deck at x = -8 m,
    /// the farthest to port on the bay's -Z wall at z = -8 m, and the tail lies 1.5 m inside the slot wall's inner face.
    /// </summary>
    [Fact]
    public void TheParkedShuttleRestsOnTheDeckWithItsPortSideOnTheBaysWall()
    {
        Double3[] corners = ParkedCorners();
        corners.Min(c => c.X).Should().BeApproximately(DemoStation.Deck, 1e-5, "the belly lies on the deck");
        corners.Min(c => c.Z).Should().BeApproximately(-Bay.Z, 1e-5, "the port side lies on the bay's -Z wall");
        corners
            .Max(c => c.Y)
            .Should()
            .BeApproximately(Bay.Y - DemoStation.ParkedTailGap, 1e-5, "the tail lies inside the slot's wall");
        DemoStation.ParkedTailGap.Should().Be(1.5f);
        foreach (Double3 corner in corners)
        {
            Math.Abs(corner.X).Should().BeLessThanOrEqualTo(Bay.X + 1e-5, $"the corner {corner} lies inside the bay");
            Math.Abs(corner.Y).Should().BeLessThanOrEqualTo(Bay.Y + 1e-5, $"the corner {corner} lies inside the bay");
            Math.Abs(corner.Z).Should().BeLessThanOrEqualTo(Bay.Z + 1e-5, $"the corner {corner} lies inside the bay");
        }

        DemoStation.ParkedPose.Position.Should().Be(new Double3(-6, 3, -5));
    }

    /// <summary>
    /// The docked rotation puts the nose along the station's -Y, into the bay, the roof along +X, away from the deck, and so
    /// the starboard side, with the hatch, along +Z.
    /// </summary>
    [Fact]
    public void TheDockedRotationPutsTheBellyOnTheDeckAndTheNoseIntoTheBay()
    {
        DQuaternion docked = DemoStation.DockedRotation;
        (docked.Rotate(-Double3.UnitZ) - -Double3.UnitY)
            .Length.Should()
            .BeLessThan(1e-12, "the nose points into the bay");
        (docked.Rotate(Double3.UnitY) - Double3.UnitX)
            .Length.Should()
            .BeLessThan(1e-12, "the roof points away from the deck");
        (docked.Rotate(Double3.UnitX) - Double3.UnitZ)
            .Length.Should()
            .BeLessThan(1e-12, "the starboard side points along +Z");
        AngleBetween(DemoStation.ParkedPose.Rotation, docked).Should().BeLessThan(1e-12);
    }

    /// <summary>The hull's corners at the parked pose lie in <see cref="DemoStation.ParkedBounds"/>, and touch each of its six faces.</summary>
    [Fact]
    public void TheParkedHullFitsItsBounds()
    {
        Double3[] corners = ParkedCorners();
        BoundingBox bounds = DemoStation.ParkedBounds;
        corners.Min(c => c.X).Should().BeApproximately(bounds.Min.X, 1e-4);
        corners.Max(c => c.X).Should().BeApproximately(bounds.Max.X, 1e-4);
        corners.Min(c => c.Y).Should().BeApproximately(bounds.Min.Y, 1e-4);
        corners.Max(c => c.Y).Should().BeApproximately(bounds.Max.Y, 1e-4);
        corners.Min(c => c.Z).Should().BeApproximately(bounds.Min.Z, 1e-4);
        corners.Max(c => c.Z).Should().BeApproximately(bounds.Max.Z, 1e-4);
        bounds.Should().Be(new BoundingBox(new(-8, -5, -8), new(-4, 11, -2)));
    }

    /// <summary>
    /// The parked shuttle's box and the mouth of the spoke under the deck share no point: 0.25 m of deck lies between them
    /// across the axis. The mouth is the inside of the spoke at θ = 180 degrees, 3.5 m on a side, through the deck's wall.
    /// </summary>
    [Fact]
    public void TheParkedShuttleLeavesTheSpokeMouthFree()
    {
        BoundingBox mouth = DemoStation.SpokeMouth,
            parked = DemoStation.ParkedBounds;
        mouth.Should().Be(new BoundingBox(new(-8.5f, -1.75f, -1.75f), new(-8, 1.75f, 1.75f)));
        (mouth.Min.Z - parked.Max.Z)
            .Should()
            .BeApproximately(0.25f, 1e-5f, "the hull ends 0.25 m short of the mouth across the axis");
        ParkedCorners()
            .Should()
            .OnlyContain(corner => corner.Z < mouth.Min.Z - 0.2, "no part of the hull reaches over the mouth");
    }

    /// <summary>
    /// The hatch opens toward the station's +Z, onto the deck between the hull and the mouth. Across the deck, its doorway
    /// lies from 1 to 4 m from the mouth, a short walk.
    /// </summary>
    [Fact]
    public void TheParkedHatchOpensOntoTheDeckTowardTheSpokeMouth()
    {
        DPose parked = DemoStation.ParkedPose;
        (parked.Rotation.Rotate(Double3.UnitX) - Double3.UnitZ)
            .Length.Should()
            .BeLessThan(1e-12, "the hatch's outward normal is +Z");
        BoundingBox hatch = Shuttle.Hatch,
            mouth = DemoStation.SpokeMouth;
        // The doorway's outer face, in the station's axes: on the hull's starboard face, from the hatch's lowest to its
        // highest point and from its forward to its aft edge.
        Double3 a = parked.TransformPoint(new(hatch.Max.X, hatch.Min.Y, hatch.Min.Z));
        Double3 b = parked.TransformPoint(new(hatch.Max.X, hatch.Max.Y, hatch.Max.Z));
        (double minY, double maxY) = (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
        double doorwayZ = a.Z;
        doorwayZ.Should().BeApproximately(b.Z, 1e-9);
        doorwayZ.Should().BeLessThan(mouth.Min.Z, "the doorway lies on the hull's side of the mouth");
        double alongTheAxis = Math.Max(0, Math.Max(minY - mouth.Max.Y, mouth.Min.Y - maxY));
        double across = mouth.Min.Z - doorwayZ;
        double walk = Math.Sqrt(alongTheAxis * alongTheAxis + across * across);
        walk.Should().BeInRange(1, 4, "the person takes a short walk on the deck from the doorway to the mouth");
    }

    /// <summary>
    /// The spin gravity at the parked center of mass, ω² r away from the axis, points into the deck and into the bay's -Z
    /// wall: 0.773 m/s², leaning 39 degrees from the deck's normal toward the wall. So the corner holds the hull.
    /// </summary>
    [Fact]
    public void TheSpinPullsTheParkedShuttleIntoTheCorner()
    {
        Double3 center = DemoStation.ParkedPose.TransformPoint(HostTesting.ShuttleCenterOfMass);
        double rate = DemoScenes.SpinStation.Rate;
        Double3 gravity = new Double3(center.X, 0, center.Z) * (rate * rate);
        gravity.X.Should().BeNegative("the spin gravity presses the hull into the deck");
        gravity.Z.Should().BeNegative("the spin gravity presses the hull into the -Z wall");
        gravity.Length.Should().BeApproximately(0.773, 0.001);
        (Math.Atan2(-gravity.Z, -gravity.X) * 180 / Math.PI).Should().BeApproximately(39.4, 0.2);
    }

    /// <summary>
    /// The station flies its orbit in a root frame with Earth's point gravity, and a dynamic shuttle hull spawns at the
    /// parked pose in the station's turning frame, at rest there, with no push. The corner holds it: after 10 s it lies
    /// within 3 cm and 0.5 degrees of the pose, still in the station's frame. The cabin's gravity at the seat stand is
    /// ω² times the seat's distance from the station's axis, 0.783 m/s², and leans 30 degrees from the floor's down
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
                Pose = DemoStation.ParkedPose,
                Body = Shuttle.Body(BodyMotion.Dynamic),
                AsFrame = new(new(Shuttle.Bubble)),
            }
        );

        Tick(world, 600);

        world.GetFrame(shuttle).Should().Be(frame, "the shuttle stays in the station's frame");
        DPose pose = world.PoseIn(shuttle, frame);
        (pose.Position - DemoStation.ParkedPose.Position)
            .Length.Should()
            .BeLessThan(0.03, "the corner holds the hull at its place");
        AngleBetween(pose.Rotation, DemoStation.DockedRotation)
            .Should()
            .BeLessThan(0.5 * Math.PI / 180, "the corner holds the hull's rotation");

        double rate = DemoScenes.SpinStation.Rate;
        Double3 seat = DemoStation.ParkedPose.TransformPoint(Shuttle.SeatStand);
        double fromTheAxis = Math.Sqrt(seat.X * seat.X + seat.Z * seat.Z);
        Double3 felt = world.Frames.FeltGravity(world.GetOwnedFrame(shuttle), Shuttle.SeatStand);
        felt.Length.Should()
            .BeApproximately(
                rate * rate * fromTheAxis,
                0.03 * rate * rate * fromTheAxis,
                "the cabin feels the spin gravity of its radius"
            );
        felt.Length.Should().BeApproximately(0.783, 0.03);
        (Math.Acos(Double3.Dot(felt, -Double3.UnitY) / felt.Length) * 180 / Math.PI)
            .Should()
            .BeApproximately(30, 2, "the gravity leans from the floor's down");
        felt.X.Should().BeNegative("the gravity leans toward port, away from the station's axis");
        world.Errors.Should().BeEmpty();
    }
}
