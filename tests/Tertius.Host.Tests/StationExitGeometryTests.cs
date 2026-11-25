using Tertius.Demo;
using Tertius.Physics;
using Tertius.Simulation;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using StationExit = Tertius.Demo.DemoScenes.StationExit;

namespace Tertius.Host.Tests;

/// <summary>
/// The lift and the openings of the exit (docs/plans/blue-danube-demo.md, "The lift"), on the real station in the real
/// Jolt backend: a platform in a hole in the deck at the top of the spoke at θ = 180 degrees, and an opening in the wheel's
/// ceiling where the platform passes. Rays find what is solid and what is open. Every place is in the station's axes.
/// </summary>
public sealed class StationExitGeometryTests : IDisposable
{
    private readonly JoltPhysicsBackend backend = new(JoltBackendOptions.Unpaced);
    private readonly World world;
    private readonly SceneInstance scene = new();
    private readonly FrameHandle frame;

    public StationExitGeometryTests()
    {
        world = new(
            new()
            {
                MaxDegreeOfParallelism = 1,
                DebugChecks = false,
                DisposePhysics = true,
            },
            backend
        );
        Entity station = DemoStation.Spawn(
            world: world,
            parent: RendezvousAutopilotTests.Earth(world),
            scene: scene,
            resources: RenderResources.None
        );
        frame = world.GetOwnedFrame(station);
        world.Tick();
    }

    private static StationLayout Layout => DemoStation.Layout;

    public void Dispose() => world.Dispose();

    /// <summary>How far a ray from <paramref name="from"/> along <paramref name="direction"/> goes before it meets a body, or null when it meets none within 2,100 m.</summary>
    private double? Ray(Double3 from, Double3 direction)
    {
        var hits = new List<RayCastHit>();
        backend.CastRay(
            frame: frame,
            origin: from,
            direction: direction,
            start: 0,
            end: 2100,
            filter: new EveryEntity(),
            kind: RayCastKind.Nearest,
            hits: hits
        );
        return hits.Count == 0 ? null : hits[0].Distance;
    }

    private sealed class EveryEntity : IPhysicsQueryFilter
    {
        public bool Accepts(Entity entity) => true;
    }

    /// <summary>Where the wheel's floor lies along -X at the place (y, <paramref name="z"/>): on the plane of the facet under it.</summary>
    private static double FloorX(double z)
    {
        WheelFloor floor = Layout.WheelFloor;
        Double3 normal = floor.Normal(new Double3(-Layout.FloorRadius, 0, z));
        return (floor.FacetDistance - z * normal.Z) / normal.X;
    }

    /// <summary>The lift's floor lies flush with the deck: a ray along -X onto its middle meets it where a ray beside it meets the deck.</summary>
    [Fact]
    public void TheLiftsFloorLiesFlushWithTheDeck()
    {
        Double3 middle = StationExit.LiftTop.Position;
        Ray(middle + new Double3(5, 0, 0), -Double3.UnitX).Should().BeApproximately(5, 1e-3);
        Ray(middle + new Double3(5, Layout.LiftHalf + 1, 0), -Double3.UnitX).Should().BeApproximately(5, 1e-3);
    }

    /// <summary>
    /// Below the deck the lift's way is clear down the whole spoke, through the ceiling's opening, to the wheel's floor: a
    /// ray along -X from under the lift's floor at each corner of the floor, 5 cm in, meets the floor.
    /// </summary>
    [Fact]
    public void TheLiftsWayIsClearToTheWheelsFloor()
    {
        double under = Layout.Deck - Layout.HubWall - 0.5;
        foreach (double y in new[] { -Layout.LiftHalf + 0.05, Layout.LiftHalf - 0.05 })
        foreach (double z in new[] { Layout.LiftAcross - Layout.LiftHalf + 0.05, Layout.LiftAcross + Layout.LiftHalf - 0.05 })
            Ray(new(under, y, z), -Double3.UnitX)
                .Should()
                .BeApproximately(under - FloorX(z), 1e-2, $"nothing lies in the lift's way at y = {y} m, z = {z} m");
    }

    /// <summary>Rays along -X onto the deck beside the lift's hole, inside the spoke and beyond it, meet the deck.</summary>
    [Theory]
    [InlineData(0, -0.5)]
    [InlineData(3, 2.5)]
    [InlineData(-3, 2.5)]
    [InlineData(0, 6)]
    [InlineData(10, 0)]
    public void TheDeckIsSolidBesideTheLiftsHole(double y, double z) =>
        Ray(new(Layout.Deck + 2, y, z), -Double3.UnitX)
            .Should()
            .BeApproximately(2, 1e-3, $"the deck is solid at y = {y} m, z = {z} m");

    /// <summary>
    /// Rays along -X from 5 m above the ceiling meet the ceiling everywhere but in the opening under the spoke, which the
    /// lift passes, and there they meet the floor. The opening spans one segment, 12.15 m, across the spoke's inside.
    /// </summary>
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(7, 5.8, false)]
    [InlineData(-7, -5.8, false)]
    [InlineData(0, 6.3, true)]
    [InlineData(0, -6.3, true)]
    [InlineData(8.5, 0, true)]
    [InlineData(-12, 3, true)]
    public void TheCeilingIsOpenOnlyUnderTheSpokesMiddle(double y, double z, bool solid)
    {
        double from = -(Layout.CeilingRadius - 5);
        double? distance = Ray(new(from, y, z), -Double3.UnitX);
        if (solid)
        {
            distance.Should().NotBeNull();
            double face = Math.Sqrt(Layout.CeilingRadius * Layout.CeilingRadius - z * z);
            distance.Value.Should().BeApproximately(face + from, 0.02, $"the ceiling is solid at y = {y} m, z = {z} m");
        }
        else
        {
            distance.Should().BeApproximately(from - FloorX(z), 0.02, $"the ceiling is open at y = {y} m, z = {z} m");
        }
    }

    /// <summary>
    /// The lift has railings on its antispinward, spinward and -Y sides, and opens toward +Y: from its middle 0.5 m above
    /// its floor, rays meet the inner faces of the railings toward -Z, +Z and -Y, and a ray toward +Y leaves it.
    /// </summary>
    [Fact]
    public void TheLiftHasRailingsOnThreeSidesAndOpensTowardTheAxisPlus()
    {
        Double3 middle = StationExit.LiftTop.Position + new Double3(0.5, 0, 0);
        double inner = Layout.LiftHalf - StationExit.RailThickness;
        Ray(middle, -Double3.UnitZ).Should().BeApproximately(inner, 1e-3, "the antispinward railing");
        Ray(middle, Double3.UnitZ).Should().BeApproximately(inner, 1e-3, "the spinward railing");
        Ray(middle, -Double3.UnitY).Should().BeApproximately(inner, 1e-3, "the railing toward -Y");
        Ray(middle, Double3.UnitY).Should().BeGreaterThan(Layout.LiftHalf + 1, "the lift opens toward +Y");
    }

    /// <summary>
    /// The person's way across the deck keeps its capsule clear of the parked hull, by more than 3 m, and of the lift's
    /// railings: its lane lies in the lift's middle across the axis, and it meets the lift's open side.
    /// </summary>
    [Fact]
    public void ThePersonsWayAcrossTheDeckIsClearOfTheHullAndTheRailings()
    {
        Waypoint[] way = StationExit.WayOut(FrameHandle.Null, frame);
        Double3 hatch = Layout.ParkedPose.TransformPoint(DemoScenes.Shuttle.Hatch.Center);
        way[1].Position.Y.Should().BeApproximately(hatch.Y, 1e-6, "the first waypoint outside lies in front of the hatch");
        foreach (Waypoint point in way[1..])
        {
            point.Frame.Should().Be(frame);
            point.Position.X.Should().BeApproximately(Layout.Deck + StationExit.Standing, 1e-6, "on the deck");
            point.Position.Z.Should().Be(Layout.LiftAcross, "in the lift's lane");
            (point.Position.Z - StationExit.PersonRadius - Layout.ParkedBounds.Max.Z).Should().BeGreaterThan(3);
        }

        way[1].Position.Y.Should().BeGreaterThan(Layout.LiftHalf + 1, "the lane meets the lift from its open side");
        way[^1].Position.Should().Be(StationExit.LiftTop.Position + new Double3(StationExit.Standing, 0, 0));
    }

    /// <summary>
    /// Each still camera of the exit holds its places inside its picture. <c>deck-camera</c> sees the corners of the
    /// hatch's doorway and of the lift's floor. <c>shaft-camera</c> sees the corners of the lift's floor from below.
    /// <c>wheel-camera</c> sees the lift's floor at the foot, the edges of the ceiling's opening, and a person at the
    /// stroll's end.
    /// </summary>
    [Fact]
    public void TheCamerasSeeTheirPlaces()
    {
        DPose parked = Layout.ParkedPose;
        BoundingBox hatch = DemoScenes.Shuttle.Hatch,
            lift = Layout.LiftFootprint;
        var deck = new List<Double3>();
        foreach (double y in new double[] { hatch.Min.Y, hatch.Max.Y })
        foreach (double z in new double[] { hatch.Min.Z, hatch.Max.Z })
            deck.Add(parked.TransformPoint(new(DemoScenes.Shuttle.Hull.X, y, z)));
        var shaft = new List<Double3>();
        var wheel = new List<Double3>();
        double foot = -StationExit.Ride.Foot;
        foreach (double y in new double[] { lift.Min.Y, lift.Max.Y })
        foreach (double z in new double[] { lift.Min.Z, lift.Max.Z })
        {
            deck.Add(new(Layout.Deck, y, z));
            shaft.Add(new(Layout.Deck - StationExit.LiftFloorThickness, y, z));
            wheel.Add(new(foot, y, z));
            wheel.Add(new(foot + StationExit.RailHeight, y, z));
        }

        double edge = Layout.CeilingRadius + Layout.CeilingThickness,
            opening = Layout.CeilingOpeningHalf;
        foreach (double z in new[] { -opening, opening })
            wheel.Add(new(-Math.Sqrt(edge * edge - z * z), 0, z));
        Double3 strollEnd = StationExit.Stroll(frame)[^1].Position;
        wheel.Add(strollEnd);
        wheel.Add(strollEnd + WheelFloor.UpAt(strollEnd) * 0.9);

        foreach (
            (string camera, List<Double3> points) in new[]
            {
                ("deck-camera", deck),
                ("shaft-camera", shaft),
                ("wheel-camera", wheel),
            }
        )
        {
            DPose view = world.PoseIn(scene[camera], frame);
            foreach (Double3 point in points)
                Picture
                    .Contains(Picture.Project(view, point, world.VerticalFov))
                    .Should()
                    .BeTrue(
                        $"{camera} sees {point}, which projects to {Picture.Project(view, point, world.VerticalFov)}"
                    );
        }
    }
}
