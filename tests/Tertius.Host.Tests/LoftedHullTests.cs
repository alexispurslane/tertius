using System.Numerics;
using Tertius.Demo;
using Shuttle = Tertius.Demo.DemoScenes.Shuttle;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo's generator of lofted hulls, <see cref="LoftedHull"/>, with no World: the shuttle's hull,
/// <see cref="Shuttle.Loft"/>, and small hulls that show one rule each. The expected geometry comes from the tables of
/// docs/plans/blue-danube-demo.md, "The shuttle's tram shape and glass canopy", worked out here, not from the generator.
/// All positions are in the hull's axes, in meters: +Y is up, -Z is the nose and +X is starboard.
/// </summary>
public sealed class LoftedHullTests
{
    /// <summary>The full cross-section, from z = -5 to the tail at z = 8, corners 0 to 9 counterclockwise seen from +Z.</summary>
    private static readonly Vector2[] Full =
        [new(-2.8f, -2), new(2.8f, -2), new(3, -1.8f), new(3, -1.2f), new(3, 1.2f), new(2.2f, 2), new(-2.2f, 2), new(-3, 1.2f), new(-3, -1.2f), new(-3, -1.8f)];

    /// <summary>The nose's front, at z = -8, with the same corners.</summary>
    private static readonly Vector2[] Front =
        [new(-2.2f, -2), new(2.2f, -2), new(2.4f, -1.8f), new(2.4f, -1.2f), new(2.4f, -0.2f), new(1.6f, 0.6f), new(-1.6f, 0.6f), new(-2.4f, -0.2f), new(-2.4f, -1.2f), new(-2.4f, -1.8f)];

    private const double Wall = 0.2;

    /// <summary>The doorway, <see cref="Shuttle.Hatch"/>, through the starboard wall.</summary>
    private static readonly (double X0, double X1, double Y0, double Y1, double Z0, double Z1) Doorway = (2.8, 3, -1.7, 0.4, 1.4, 2.6);

    /// <summary>The pocket's middle in x and y: on the line along Z through the center of mass, (0, -0.090).</summary>
    private const double PocketY = -0.090;

    /// <summary>The pocket, 1.4 m square and 0.6 m deep, and its housing, 1.8 m square from z = 7.2 to the aft wall at 7.8.</summary>
    private static readonly (double Half, double Z0) Pocket = (0.7, 7.4);
    private static readonly (double Half, double Z0, double Z1) Housing = (0.9, 7.2, 7.8);

    /// <summary>The outer cross-section at <paramref name="z"/>: the corners move on straight lines from the front to the full one over z = -8 to -5.</summary>
    private static Vector2[] OuterAt(double z)
    {
        if (z >= -5)
            return Full;
        float t = (float)((z + 8) / 3);
        return [.. Front.Zip(Full, (front, full) => front + (full - front) * t)];
    }

    /// <summary>
    /// How far <paramref name="p"/> lies inside the convex, counterclockwise <paramref name="polygon"/>, in its plane: the least
    /// distance to the line of an edge, negative outside.
    /// </summary>
    private static double Inside(Vector2[] polygon, double x, double y)
    {
        double least = double.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
            double ex = b.X - a.X, ey = b.Y - a.Y, length = Math.Sqrt(ex * ex + ey * ey);
            // The inside lies to the left of each edge.
            least = Math.Min(least, (ex * (y - a.Y) - ey * (x - a.X)) / length);
        }
        return least;
    }

    private static bool InOuter(Vector3 p) => Math.Abs(p.Z) <= 8 && Inside(OuterAt(p.Z), p.X, p.Y) >= 0;

    /// <summary>The inner skin is each cross-section moved in by the wall along each edge's normal, between z = -7.8 and 7.8.</summary>
    private static bool InInnerLoft(Vector3 p) => Math.Abs(p.Z) <= 8 - Wall && Inside(OuterAt(p.Z), p.X, p.Y) >= Wall;

    private static bool InHousing(Vector3 p) =>
        Math.Abs(p.X) <= Housing.Half && Math.Abs(p.Y - PocketY) <= Housing.Half && p.Z >= Housing.Z0 && p.Z <= Housing.Z1;

    private static bool InCabin(Vector3 p) => InInnerLoft(p) && !InHousing(p);

    private static bool InDoorway(Vector3 p) =>
        p.X >= Doorway.X0 && p.X <= Doorway.X1 && p.Y >= Doorway.Y0 && p.Y <= Doorway.Y1 && p.Z >= Doorway.Z0 && p.Z <= Doorway.Z1;

    private static bool InPocket(Vector3 p) => Math.Abs(p.X) <= Pocket.Half && Math.Abs(p.Y - PocketY) <= Pocket.Half && p.Z >= Pocket.Z0 && p.Z <= 8;

    /// <summary>The drawn hull's solid: inside the outer loft, and outside the cabin, the doorway and the tail pocket.</summary>
    private static bool InSolid(Vector3 p) => InOuter(p) && !InCabin(p) && !InDoorway(p) && !InPocket(p);

    /// <summary>
    /// How far <paramref name="p"/> lies from the nearest skin of the drawn hull: the outer and inner skins, the doorway's
    /// sides, the pocket and the housing. Only an estimate where the skins meet, enough to leave out points near them.
    /// </summary>
    private static double FromASkin(Vector3 p)
    {
        Vector2[] outer = OuterAt(p.Z);
        double inside = Inside(outer, p.X, p.Y);
        double near = Math.Min(Math.Abs(inside), Math.Abs(inside - Wall));
        near = Math.Min(near, Math.Min(Math.Abs(Math.Abs(p.Z) - 8), Math.Abs(Math.Abs(p.Z) - (8 - Wall))));
        foreach (double plane in new[] { Doorway.Y0, Doorway.Y1 })
            near = Math.Min(near, Math.Abs(p.Y - plane));
        foreach (double plane in new[] { Doorway.Z0, Doorway.Z1, Pocket.Z0, Housing.Z0 })
            near = Math.Min(near, Math.Abs(p.Z - plane));
        foreach (double half in new[] { Pocket.Half, Housing.Half })
            near = Math.Min(near, Math.Min(Math.Abs(Math.Abs(p.X) - half), Math.Abs(Math.Abs(p.Y - PocketY) - half)));
        return near;
    }

    [Fact]
    public void TheHullsMeshIsClosed()
    {
        MeshChecks.ExpectClosed(Shuttle.Loft.Mesh());
    }

    /// <summary>
    /// Each triangle has the drawn solid behind it and the outside in front of it, 5 mm away. The thinnest part of the
    /// solid is the wall below the doorway, 1.7 cm tall at the inner skin. Every face is flat, so each vertex has its
    /// triangle's normal.
    /// </summary>
    [Fact]
    public void TheHullsMeshFacesOutOfItsSolid()
    {
        MeshChecks.ExpectFacingOut(Shuttle.Loft.Mesh(), InSolid, probe: 0.005f, maxNormalAngle: 0.001f);
    }

    /// <summary>
    /// A segment across the starboard wall, from 0.5 m inside the cabin to 0.5 m outside the hull, crosses no triangle at
    /// the doorway's middle or 5 cm inside each of its corners. 5 cm outside each of its four sides, it crosses the inner
    /// and the outer skin. Below the doorway the inner skin there is the chamfer above the floor.
    /// </summary>
    [Fact]
    public void TheHatchIsAHoleThroughTheStarboardWall()
    {
        MeshData mesh = Shuttle.Loft.Mesh();
        double middleY = (Doorway.Y0 + Doorway.Y1) / 2, middleZ = (Doorway.Z0 + Doorway.Z1) / 2;
        double halfY = (Doorway.Y1 - Doorway.Y0) / 2, halfZ = (Doorway.Z1 - Doorway.Z0) / 2;
        int Across(double y, double z) => MeshChecks.Crossings(mesh, new Vector3(2.3f, (float)y, (float)z), new Vector3(3.5f, (float)y, (float)z));

        Across(middleY, middleZ).Should().Be(0, "the doorway is open at its middle");
        foreach (double sy in new[] { -1.0, 1 })
        {
            foreach (double sz in new[] { -1.0, 1 })
                Across(middleY + sy * (halfY - 0.05), middleZ + sz * (halfZ - 0.05)).Should().Be(0, $"the doorway is open 5 cm inside its corner ({sy}, {sz})");
            Across(middleY + sy * (halfY + 0.05), middleZ).Should().Be(2, $"the wall is closed 5 cm past the doorway's side {sy} in y");
            Across(middleY, middleZ + sy * (halfZ + 0.05)).Should().Be(2, $"the wall is closed 5 cm past the doorway's side {sy} in z");
        }
    }

    /// <summary>
    /// A line along -Z from 1 m behind the tail through the pocket's middle first meets the pocket's floor, at z = 7.4. A
    /// line 5 cm outside each side of the pocket first meets the tail face, at z = 8.
    /// </summary>
    [Fact]
    public void TheTailPocketIsARecessClosedAtItsDepth()
    {
        MeshData mesh = Shuttle.Loft.Mesh();
        float FirstMet(double x, double y) => MeshChecks.FirstCrossing(mesh, new Vector3((float)x, (float)y, 9), new Vector3((float)x, (float)y, 0))!.Value.Z;

        FirstMet(0, PocketY).Should().BeApproximately(7.4f, 1e-4f, "the pocket is 0.6 m deep");
        double outside = Pocket.Half + 0.05;
        foreach ((double x, double y) in new[] { (outside, PocketY), (-outside, PocketY), (0, PocketY + outside), (0, PocketY - outside) })
            FirstMet(x, y).Should().BeApproximately(8f, 1e-4f, $"the tail face is flat at ({x}, {y}), outside the pocket");
    }

    /// <summary>The planes of the faces of the convex hull of <paramref name="points"/>: unit normals out, and their offsets.</summary>
    private static List<(Vector3 Normal, float Offset)> HullPlanes(IReadOnlyList<Vector3> points)
    {
        var planes = new List<(Vector3, float)>();
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
                for (int k = j + 1; k < points.Count; k++)
                {
                    Vector3 normal = Vector3.Cross(points[j] - points[i], points[k] - points[i]);
                    if (normal.Length() < 1e-6f)
                        continue;
                    normal = Vector3.Normalize(normal);
                    float offset = Vector3.Dot(normal, points[i]);
                    bool below = points.All(p => Vector3.Dot(normal, p) <= offset + 1e-5f), above = points.All(p => Vector3.Dot(normal, p) >= offset - 1e-5f);
                    if (below)
                        planes.Add((normal, offset));
                    else if (above)
                        planes.Add((-normal, -offset));
                }
        return planes;
    }

    /// <summary>The greatest distance of <paramref name="p"/> outside one of the <paramref name="planes"/>: below 0 inside the hull, 0 on its surface.</summary>
    private static float Beyond(List<(Vector3 Normal, float Offset)> planes, Vector3 p) => planes.Max(plane => Vector3.Dot(plane.Normal, p) - plane.Offset);

    /// <summary>
    /// The outer collision shape is the convex hull of the 24 true corners of the cross-sections at z = -8, -5 and 8. The
    /// sill corners lie on the straight sides and add nothing. Every vertex of the drawn hull lies inside it or within
    /// 1 mm outside, and every vertex of the outer skin lies within 1 mm of its surface.
    /// </summary>
    [Fact]
    public void TheShapeIsTheConvexHullOfTheCrossSectionsCorners()
    {
        int[] trueCorners = [0, 1, 2, 4, 5, 6, 7, 9];
        Vector3[] expected =
        [
            .. trueCorners.Select(c => new Vector3(Front[c], -8)),
            .. trueCorners.Select(c => new Vector3(Full[c], -5)),
            .. trueCorners.Select(c => new Vector3(Full[c], 8)),
        ];

        ShapeDesc shape = Shuttle.Loft.Shape();

        ConvexHullShapeDesc hull = shape.Should().BeOfType<ConvexHullShapeDesc>().Subject;
        hull.Points.Should().BeEquivalentTo(expected);
        hull.ConvexRadius.Should().Be(ShapeDesc.DefaultConvexRadius, "its edges meet the ramp's foot with the box's rounding");
        List<(Vector3, float)> planes = HullPlanes(hull.Points);
        foreach (Vertex vertex in Shuttle.Loft.Mesh().Vertices)
        {
            Vector3 p = vertex.Position;
            Beyond(planes, p).Should().BeLessThan(1e-3f, $"the vertex at {p} lies in the hull");
            bool onOuterSkin = Math.Abs(Inside(OuterAt(p.Z), p.X, p.Y)) < 1e-4 && !InDoorway(p) || Math.Abs(Math.Abs(p.Z) - 8) < 1e-4 && !InPocket(p with { Z = 7.9f });
            if (onOuterSkin)
                Beyond(planes, p).Should().BeGreaterThan(-1e-3f, $"the vertex at {p} of the outer skin lies on the hull");
        }
    }

    /// <summary>
    /// A grid of points 5 cm apart, offset so that no point lies on a joint of the pieces. A point in the wall, which
    /// takes in the end walls, the housing and the tail pocket, lies in exactly one piece of the interior. A point in the
    /// cabin, in the doorway or outside the hull lies in none. Points within 1 mm of a skin are left out.
    /// </summary>
    [Fact]
    public void TheInteriorFillsTheWallsAndLeavesTheCabinAndTheDoorwayOpen()
    {
        var pieces = Shuttle.Loft.Interior().Select(child =>
        {
            child.Pose.Should().Be(Pose.Identity);
            ConvexHullShapeDesc hull = child.Shape.Should().BeOfType<ConvexHullShapeDesc>().Subject;
            hull.ConvexRadius.Should().Be(0, "the pieces meet the drawn skins exactly");
            return (Min: hull.Points.Aggregate(Vector3.Min), Max: hull.Points.Aggregate(Vector3.Max), Planes: HullPlanes(hull.Points));
        }).ToArray();
        pieces.Should().HaveCount(22, "eight faces in the nose and in the body, the starboard side's in four, the two end walls and the housing");

        const float step = 0.05f, offset = 0.0123f;
        int checkedPoints = 0;
        for (float x = -3.1f + offset; x < 3.1f; x += step)
            for (float y = -2.1f + offset; y < 2.1f; y += step)
                for (float z = -8.1f + offset; z < 8.1f; z += step)
                {
                    var p = new Vector3(x, y, z);
                    if (InOuter(p) && FromASkin(p) < 1e-3)
                        continue;
                    int expected = InOuter(p) && !InCabin(p) && !InDoorway(p) ? 1 : 0;
                    int count = 0;
                    foreach (var piece in pieces)
                    {
                        if (p.X < piece.Min.X || p.Y < piece.Min.Y || p.Z < piece.Min.Z || p.X > piece.Max.X || p.Y > piece.Max.Y || p.Z > piece.Max.Z)
                            continue;
                        if (Beyond(piece.Planes, p) < 0)
                            count++;
                    }
                    count.Should().Be(expected, $"the point {p} lies in {expected} piece");
                    checkedPoints++;
                }
        checkedPoints.Should().BeGreaterThan(500_000);
    }

    /// <summary>
    /// A prism of two equal rectangles, 2 m across X from x = 1 to 3, 3 m across Y from y = -1 to 2 and 4 m long from z = 0 to 4, of
    /// 12 kg: its center of mass is the box's middle, and its moments are those of a solid box, m (b² + c²) / 12, about axes along
    /// its own.
    /// </summary>
    [Fact]
    public void TheMassOfAPrismIsTheBoxsMass()
    {
        Vector2[] rectangle = [new(1, -1), new(3, -1), new(3, 2), new(1, 2)];
        var prism = new LoftedHull([new HullSection(0, rectangle), new HullSection(4, rectangle)], wall: 0.2f, ribs: [0]);

        HullMass mass = prism.MassOf(12);

        ExpectNear(mass.CenterOfMass, new Vector3(2, 0.5f, 2), "the center of mass");
        ExpectNear(mass.Inertia.Moments, new Vector3(9 + 16, 4 + 16, 4 + 9), "the moments, m (b² + c²) / 12 with m = 12");
        ExpectNear(mass.Inertia.Axes, Quaternion.Identity, "the principal axes");
    }

    /// <summary>
    /// A frustum of two squares, centered on (1, -0.5) in x and y, 2 m on a side at z = 0 and 1 m at z = 3, of 7 kg. Take
    /// the side s(t) = a + (b - a) t / h, with a = 2, b = 1, h = 3 and t = z the height. The closed formulas: the volume
    /// V = h (a² + ab + b²) / 3, the center of mass at t̄ = h (a² + 2ab + 3b²) / (4 (a² + ab + b²)), about the axis along Z the moment
    /// ρ ∫ s⁴ / 6 dt with ∫ s⁴ dt = h (a⁴ + a³b + a²b² + ab³ + b⁴) / 5, and about the axes along X and Y through the center of
    /// mass ρ ∫ (s⁴ / 12 + s² t²) dt - m t̄², with ∫ s² t² dt = h³ (a² + 3ab + 6b²) / 30, where ρ = m / V.
    /// </summary>
    [Fact]
    public void TheMassOfAFrustumIsTheFrustumsMass()
    {
        Vector2[] Square(float half) => [new(1 - half, -0.5f - half), new(1 + half, -0.5f - half), new(1 + half, -0.5f + half), new(1 - half, -0.5f + half)];
        var frustum = new LoftedHull([new HullSection(0, Square(1)), new HullSection(3, Square(0.5f))], wall: 0.1f, ribs: [0]);
        const double a = 2, b = 1, h = 3, m = 7;
        double volume = h * (a * a + a * b + b * b) / 3, rho = m / volume;
        double center = h * (a * a + 2 * a * b + 3 * b * b) / (4 * (a * a + a * b + b * b));
        double s4 = h * (a * a * a * a + a * a * a * b + a * a * b * b + a * b * b * b + b * b * b * b) / 5;
        double s2t2 = h * h * h * (a * a + 3 * a * b + 6 * b * b) / 30;
        double across = rho * (s4 / 12 + s2t2) - m * center * center, along = rho * s4 / 6;

        HullMass mass = frustum.MassOf((float)m);

        ExpectNear(mass.CenterOfMass, new Vector3(1, -0.5f, (float)center), "the center of mass");
        ExpectNear(mass.Inertia.Moments, new Vector3((float)across, (float)across, (float)along), "the moments");
        // The moments about X and Y are equal, so any two axes across Z are principal. The axis along Z is the frustum's.
        ExpectNear(Vector3.Transform(Vector3.UnitZ, mass.Inertia.Axes), Vector3.UnitZ, "the principal axis along Z");
    }

    /// <summary>Each component within a relative 1e-6 of the largest, as <see cref="LoftedHull.MassOf"/> rounds to floats once.</summary>
    private static void ExpectNear(Vector3 actual, Vector3 expected, string what)
    {
        float scale = MathF.Max(1, MathF.Max(MathF.Abs(expected.X), MathF.Max(MathF.Abs(expected.Y), MathF.Abs(expected.Z))));
        (actual - expected).Length().Should().BeLessThan(1e-6f * scale, $"{what} is {actual}, not {expected}");
    }

    private static void ExpectNear(Quaternion actual, Quaternion expected, string what) =>
        MathF.Abs(Quaternion.Dot(actual, expected)).Should().BeGreaterThan(1 - 1e-6f, $"{what} are {actual}, not {expected}");

    /// <summary>A small box hull, 4 m wide, 3 m tall and 6 m long, in three bays of 2 m.</summary>
    private static readonly Vector2[] BoxSection = [new(-2, -1.5f), new(2, -1.5f), new(2, 1.5f), new(-2, 1.5f)];

    private static LoftedHull SmallBox() => new([new HullSection(-3, BoxSection), new HullSection(3, BoxSection)], wall: 0.2f, ribs: [-3, -1, 1]);

    [Fact]
    public void CrossSectionsWithDifferentCountsOfCornersAreAnError()
    {
        Vector2[] triangle = [new(-2, -1.5f), new(2, -1.5f), new(0, 1.5f)];
        Action build = () => new LoftedHull([new HullSection(-3, BoxSection), new HullSection(3, triangle)], wall: 0.2f, ribs: [-3]);

        build.Should().Throw<ArgumentException>().WithMessage("*corners*");
    }

    [Fact]
    public void ACrossSectionThatIsNotConvexIsAnError()
    {
        Vector2[] dented = [new(-2, -1.5f), new(2, -1.5f), new(2, 1.5f), new(0, 0.5f), new(-2, 1.5f)];
        Action build = () => new LoftedHull([new HullSection(-3, dented), new HullSection(3, dented)], wall: 0.2f, ribs: [-3]);

        build.Should().Throw<ArgumentException>().WithMessage("*not convex*");
    }

    [Fact]
    public void ACrossSectionThatRunsClockwiseIsAnError()
    {
        Vector2[] clockwise = [.. BoxSection.Reverse()];
        Action build = () => new LoftedHull([new HullSection(-3, clockwise), new HullSection(3, clockwise)], wall: 0.2f, ribs: [-3]);

        build.Should().Throw<ArgumentException>().WithMessage("*counterclockwise*");
    }

    [Fact]
    public void AnEdgeThatIsNotParallelAcrossTheCrossSectionsIsAnError()
    {
        Vector2[] skewed = [new(-2, -1.5f), new(2, -1.5f), new(2.5f, 1.5f), new(-2, 1.5f)];
        Action build = () => new LoftedHull([new HullSection(-3, BoxSection), new HullSection(3, skewed)], wall: 0.2f, ribs: [-3]);

        build.Should().Throw<ArgumentException>().WithMessage("*not parallel*");
    }

    [Theory]
    [InlineData(-3, 3.5f)] // past the tail
    [InlineData(-3.5f, 1)] // the first rib place is not the first cross-section's
    [InlineData(-3, -2.9f)] // in the front wall, before the cabin where the inner skin starts
    public void ARibPlaceOutsideTheLoftIsAnError(float first, float second)
    {
        Action build = () => new LoftedHull([new HullSection(-3, BoxSection), new HullSection(3, BoxSection)], wall: 0.2f, ribs: [first, second]);

        build.Should().Throw<ArgumentException>().WithMessage("*rib*");
    }

    [Fact]
    public void RibPlacesThatAreNotInOrderAreAnError()
    {
        Action build = () => new LoftedHull([new HullSection(-3, BoxSection), new HullSection(3, BoxSection)], wall: 0.2f, ribs: [-3, 1, -1]);

        build.Should().Throw<ArgumentException>().WithMessage("*order*");
    }

    /// <summary>A cell is flat only when no cross-section lies inside its bay, so every cross-section but the last lies at a rib place.</summary>
    [Fact]
    public void ACrossSectionBetweenRibPlacesIsAnError()
    {
        Vector2[] smaller = [new(-1.5f, -1.5f), new(1.5f, -1.5f), new(1.5f, 1), new(-1.5f, 1)];
        Action build = () => new LoftedHull([new HullSection(-3, smaller), new HullSection(0, BoxSection), new HullSection(3, BoxSection)], wall: 0.2f, ribs: [-3, -1, 1]);

        build.Should().Throw<ArgumentException>().WithMessage("*rib place*");
    }

    /// <summary>Bay 2 of the small box runs from z = -1 to 1. A door from z = -1.2 reaches past its panel's edge.</summary>
    [Fact]
    public void ADoorThatReachesTheEdgeOfItsPanelIsAnError()
    {
        Action door = () => SmallBox().Door(new BoundingBox(new Vector3(1.8f, -1, -1.2f), new Vector3(2, 1, 0.5f)), bay: 2, fromCorner: 1, toCorner: 2);

        door.Should().Throw<ArgumentException>().WithMessage("*edge of its panel*");
    }

    /// <summary>
    /// The edges from corner 1 to corner 3 bend at corner 2. In the small box they are its starboard side and its roof,
    /// which meet at a right angle. In a pointed section, (-2, -1.5), (2, -1.5), (3, 0), (2, 1.5), (-2, 1.5), (-3, 0), they
    /// are two sides that meet at a point, though their ends lie on one line across X.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADoorInAPanelWhoseEdgesDoNotLieInOnePlaneIsAnError(bool pointed)
    {
        Vector2[] pointedSection = [new(-2, -1.5f), new(2, -1.5f), new(3, 0), new(2, 1.5f), new(-2, 1.5f), new(-3, 0)];
        LoftedHull hull = pointed
            ? new([new HullSection(-3, pointedSection), new HullSection(3, pointedSection)], wall: 0.2f, ribs: [-3, -1, 1])
            : SmallBox();
        Action door = () => hull.Door(new BoundingBox(new Vector3(1.8f, -1, -0.5f), new Vector3(2, 1, 0.5f)), bay: 2, fromCorner: 1, toCorner: 3);

        door.Should().Throw<ArgumentException>().WithMessage("*one plane*");
    }

    /// <summary>The small box's cabin reaches 1.8 m from its middle to each side, so a housing 2 m square reaches its wall.</summary>
    [Fact]
    public void APocketWhoseHousingReachesTheWallIsAnError()
    {
        Action pocket = () => SmallBox().TailPocket(half: 1.2f, depth: 0.5f, housing: 0.2f);

        pocket.Should().Throw<ArgumentException>().WithMessage("*housing*");
    }
}
