using System.Numerics;
using Tertius.Demo;
using Tertius.Physics;
using Tertius.Simulation;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using StationExit = Tertius.Demo.DemoScenes.StationExit;

namespace Tertius.Host.Tests;

/// <summary>
/// The openings and the chute of the exit (docs/plans/blue-danube-demo.md, "The exit", "The chute"), on the real station
/// in the real Jolt backend: a hole in the deck over a trough in the spoke at θ = 180 degrees, and an opening in the
/// wheel's ceiling where the trough passes. Rays along the spoke, -X in the station's axes, find what is solid and what is
/// open. Every place is in the station's axes.
/// </summary>
public sealed class StationExitGeometryTests : IDisposable
{
    private readonly JoltPhysicsBackend backend = new();
    private readonly World world;
    private readonly SceneInstance scene = new();
    private readonly FrameHandle frame;

    public StationExitGeometryTests()
    {
        world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = false, DisposePhysics = true }, backend);
        Entity station = DemoStation.Spawn(world, RendezvousAutopilotTests.Earth(world), scene, RenderResources.None);
        frame = world.GetOwnedFrame(station);
        world.Tick();
    }

    public void Dispose() => world.Dispose();

    private static Chute Chute => StationExit.Chute;

    /// <summary>How far a ray from <paramref name="from"/> along <paramref name="direction"/> goes before it meets a body, or null when it meets none within 120 m.</summary>
    private double? Ray(Double3 from, Double3 direction)
    {
        var hits = new List<RayCastHit>();
        backend.CastRay(frame, from, direction, 0, 120, new EveryEntity(), RayCastKind.Nearest, hits);
        return hits.Count == 0 ? null : hits[0].Distance;
    }

    private sealed class EveryEntity : IPhysicsQueryFilter
    {
        public bool Accepts(Entity entity) => true;
    }

    /// <summary>
    /// Where the wheel's floor lies along -X at the place <paramref name="z"/>, y = 0: the floor's facet from θ = 178.59
    /// to 180 degrees, the chord between its corners 100 m from the axis, or its mirror image for z above 0.
    /// </summary>
    private static double FloorX(double z)
    {
        z = -Math.Abs(z);
        double step = Math.Tau / DemoStation.Segments, theta = Math.PI - step;
        Double3 a = new(DemoStation.FloorRadius * Math.Cos(theta), 0, -DemoStation.FloorRadius * Math.Sin(theta)), b = new(-DemoStation.FloorRadius, 0, 0);
        return a.X + (b.X - a.X) * (z - a.Z) / (b.Z - a.Z);
    }

    /// <summary>
    /// The chute's top, the hole in the deck, lies inside the spoke's mouth. Its antispinward face is the plane of the back
    /// plate's face, and its faces across the axis are the planes of the rails' inner faces, so its faces run on into the
    /// trough's.
    /// </summary>
    [Fact]
    public void TheChutesTopLiesInsideTheSpokeMouthAndItsFacesRunOnIntoTheTrough()
    {
        BoundingBox hole = StationExit.DeckHole, mouth = DemoStation.SpokeMouth;
        hole.Min.X.Should().Be(mouth.Min.X, "the hole goes through the deck's wall");
        hole.Max.X.Should().Be(DemoStation.Deck);
        hole.Min.Y.Should().BeGreaterThanOrEqualTo(mouth.Min.Y);
        hole.Max.Y.Should().BeLessThanOrEqualTo(mouth.Max.Y);
        hole.Min.Z.Should().BeGreaterThanOrEqualTo(mouth.Min.Z);
        hole.Max.Z.Should().BeLessThanOrEqualTo(mouth.Max.Z);
        ((double)hole.Min.Z).Should().BeApproximately(Chute.BackFace, 1e-6, "the hole's antispinward face is the plane of the back plate's face");
        ((double)hole.Max.Y).Should().BeApproximately(Chute.HalfWidth, 1e-6, "the hole's face toward +Y is the plane of a rail's inner face");
        ((double)hole.Min.Y).Should().BeApproximately(-Chute.HalfWidth, 1e-6, "the hole's face toward -Y is the plane of a rail's inner face");
        Chute.Top.Should().Be(DemoStation.Deck, "the chute's faces start at the deck");
    }

    /// <summary>A ray down the middle of the trough passes through the deck's hole and the ceiling's opening, and meets the wheel's floor.</summary>
    [Fact]
    public void ARayDownTheTroughPassesTheDeckAndTheCeilingAndMeetsTheFloor()
    {
        var from = new Double3(-6, 0, -0.7);
        Ray(from, -Double3.UnitX).Should().BeApproximately(-6 - FloorX(-0.7), 1e-3, "nothing lies in the trough between the bay and the floor");
    }

    /// <summary>Rays along -X onto the deck beside the hole, inside the spoke's mouth and beyond it, meet the deck.</summary>
    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(0, -1.5)]
    [InlineData(1.0, -0.6)]
    [InlineData(-1.0, -0.6)]
    [InlineData(1.5, 1.5)]
    [InlineData(5, 0)]
    public void TheDeckIsSolidBesideTheHole(double y, double z) =>
        Ray(new Double3(-6, y, z), -Double3.UnitX).Should().BeApproximately(2, 1e-3, $"the deck is solid at y = {y} m, z = {z} m");

    /// <summary>
    /// Rays along -X from inside the wheel's tube's height, 4.5 m above the ceiling, meet the ceiling everywhere but in
    /// the opening under the spoke, which the trough passes, and there they meet the floor.
    /// </summary>
    [Theory]
    [InlineData(0, -0.2, false)]
    [InlineData(1.2, 0.5, false)]
    [InlineData(-1.2, 1.0, false)]
    [InlineData(0, 1.45, true)]
    [InlineData(0, -1.45, true)]
    [InlineData(3, 0, true)]
    [InlineData(-6, -3, true)]
    public void TheCeilingIsOpenOnlyUnderTheSpokesMiddle(double y, double z, bool solid)
    {
        double? distance = Ray(new Double3(-90, y, z), -Double3.UnitX);
        if (solid)
        {
            distance.Should().NotBeNull();
            // The ceiling's face toward the axis is a circle of 94.5 m, and its facets lie within 7 mm inside it.
            double face = Math.Sqrt(DemoStation.CeilingRadius * DemoStation.CeilingRadius - z * z);
            distance!.Value.Should().BeApproximately(face - 90, 0.01, $"the ceiling is solid at y = {y} m, z = {z} m");
        }
        else
        {
            distance.Should().BeApproximately(-90 - FloorX(z), 0.01, $"the ceiling is open at y = {y} m, z = {z} m");
        }
    }

    /// <summary>A ray across the trough meets the back plate's face, and a ray along the axis inside it meets a rail's inner face.</summary>
    [Fact]
    public void TheTroughHasABackPlateAndTwoRails()
    {
        Ray(new Double3(-50, 0, 0), -Double3.UnitZ).Should().BeApproximately(-Chute.BackFace, 1e-3, "the back plate's face lies at z = -1 m");
        Ray(new Double3(-50, 0, -0.7), Double3.UnitY).Should().BeApproximately(Chute.HalfWidth, 1e-3, "a rail's inner face lies at y = 0.6 m");
        Ray(new Double3(-50, 0, -0.7), -Double3.UnitY).Should().BeApproximately(Chute.HalfWidth, 1e-3, "the other rail's inner face lies at y = -0.6 m");
        Ray(new Double3(-50, 0, -0.2), Double3.UnitY).Should().BeApproximately(1.75, 1e-3, "the trough is open toward the spoke's middle beyond its rails");
        Ray(new Double3(-96, 0, -0.7), -Double3.UnitZ).Should().BeApproximately(-Chute.BackFace - 0.7, 1e-3, "the back plate runs on below the ceiling");
    }

    /// <summary>The trough ends from 2 to 6 cm above the wheel's floor, so the person's feet land on the floor between the rails.</summary>
    [Fact]
    public void TheChuteEndsJustAboveTheWheelsFloor()
    {
        foreach (double z in new[] { Chute.BackFace - 0.1, Chute.BackFace + 0.6 })
            (Chute.End - FloorX(z)).Should().BeInRange(0.02, 0.06, $"the trough's end lies just above the floor at z = {z} m");
    }

    /// <summary>
    /// Each still camera of the exit holds its places inside its picture. <c>deck-camera</c> sees the corners of the
    /// hatch's doorway, the corners of the chute's top and a person 1.8 m tall at both ends of the walk along the deck.
    /// <c>spoke-camera</c> sees the corners of the trough's top under the deck. <c>wheel-camera</c> sees the corners of the
    /// chute's end, the edges of the ceiling's opening, and a person at the stroll's end.
    /// </summary>
    [Fact]
    public void TheCamerasSeeTheirPlaces()
    {
        DPose parked = DemoStation.ParkedPose;
        BoundingBox hatch = DemoScenes.Shuttle.Hatch, hole = StationExit.DeckHole;
        var deck = new List<Double3>();
        foreach (double y in new double[] { hatch.Min.Y, hatch.Max.Y })
            foreach (double z in new double[] { hatch.Min.Z, hatch.Max.Z })
                deck.Add(parked.TransformPoint(new Double3(DemoScenes.Shuttle.Hull.X, y, z)));
        foreach (double y in new double[] { hole.Min.Y, hole.Max.Y })
            foreach (double z in new double[] { hole.Min.Z, hole.Max.Z })
                deck.Add(new Double3(DemoStation.Deck, y, z));
        foreach (double y in new double[] { 5, 0 })
        {
            deck.Add(new Double3(DemoStation.Deck, y, 0.6));
            deck.Add(new Double3(DemoStation.Deck + 1.8, y, 0.6));
        }
        var spoke = new List<Double3>();
        foreach (double y in new double[] { hole.Min.Y, hole.Max.Y })
            foreach (double z in new double[] { hole.Min.Z, hole.Max.Z })
                spoke.Add(new Double3(hole.Min.X, y, z));
        var wheel = new List<Double3>();
        foreach (double y in new[] { -Chute.HalfWidth - 0.1, Chute.HalfWidth + 0.1 })
            foreach (double z in new[] { Chute.BackFace - 0.1, Chute.BackFace + 0.6 })
                wheel.Add(new Double3(Chute.End, y, z));
        double edge = DemoStation.CeilingRadius + DemoStation.CeilingThickness;
        foreach (double y in new[] { -1.75, 1.75 })
            foreach (double z in new[] { -1.16, 1.16 })
                wheel.Add(new Double3(-Math.Sqrt(edge * edge - z * z), y, z));
        foreach (double radius in new[] { DemoStation.FloorRadius, DemoStation.FloorRadius - 1.8 })
            wheel.Add(new Double3(-Math.Sqrt(radius * radius - 4.5 * 4.5), 0, 4.5));

        foreach ((string camera, List<Double3> points) in new[] { ("deck-camera", deck), ("spoke-camera", spoke), ("wheel-camera", wheel) })
        {
            DPose view = world.PoseIn(scene[camera], frame);
            foreach (Double3 point in points)
                Picture.Contains(Picture.Project(view, point, world.VerticalFov)).Should().BeTrue($"{camera} sees {point}, which projects to {Picture.Project(view, point, world.VerticalFov)}");
        }
    }
}
