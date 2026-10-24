using System.Numerics;
using Tertius.Demo;
using static Tertius.Host.Tests.MeshChecks;

namespace Tertius.Host.Tests;

/// <summary>
/// BoxMesh: meshes of boxes whose walls across X or Z can have openings through them, doorways and windows. The mesh of
/// each solid, a box or a hollow box with its openings, must be closed, with every edge shared by exactly two triangles
/// that run along it in opposite directions, so it has no crack and no T-junction. Every triangle must wind
/// counterclockwise around its normal, and the normal must point out of the solid.
/// </summary>
public sealed class BoxMeshTests
{
    /// <summary>A slab 0.5 m thick across X, 4 m by 4 m.</summary>
    private static readonly BoundingBox Slab = new(new(0, -2, -2), new(0.5f, 2, 2));

    /// <summary>A doorway through <see cref="Slab"/>: 1 m wide along Z and 2 m tall, centered.</summary>
    private static readonly Opening Doorway = Opening.ThroughX(new(new(0, -1, -0.5f), new(0.5f, 1, 0.5f)));

    private static readonly MeshData
        SlabWithDoorway = new BoxMesh().Box(Slab, openings: Doorway).Sides(Doorway).Build();

    /// <summary>A slab 0.5 m thick across Z, 6 m wide along X and 4 m tall.</summary>
    private static readonly BoundingBox SlabAcrossZ = new(new(-3, -2, 0), new(3, 2, 0.5f));

    /// <summary>A window through <see cref="SlabAcrossZ"/>: 4 m wide along X and 1 m tall, above the slab's middle.</summary>
    private static readonly Opening WindowAcrossZ = Opening.ThroughZ(new(new(-2, 0.2f, 0), new(2, 1.2f, 0.5f)));

    /// <summary>A long slab 0.5 m thick across X, 12 m along Z and 4 m tall, as a side wall of the ship is.</summary>
    private static readonly BoundingBox LongSlab = new(new(0, -2, -6), new(0.5f, 2, 6));

    /// <summary>
    /// A row of openings through <see cref="LongSlab"/> of different heights, as in the ship's port wall: a doorway near the
    /// floor, two windows at one height, and a smaller window higher up.
    /// </summary>
    private static readonly Opening[] Row =
    [
        Opening.ThroughX(new(new(0, -1.8f, -4.5f), new(0.5f, 0.2f, -3.5f))),
        Opening.ThroughX(new(new(0, -0.5f, -2), new(0.5f, 1, 0))),
        Opening.ThroughX(new(new(0, -0.5f, 1), new(0.5f, 1, 3))),
        Opening.ThroughX(new(new(0, 0, 4), new(0.5f, 1.5f, 5)))
    ];

    /// <summary>
    /// The ship's hull in small: a box 20 m long along Z and 10 m wide along X, with walls 0.5 m thick around a room. See
    /// <see cref="HollowOpenings"/> for the holes in the walls.
    /// </summary>
    private static readonly BoundingBox HollowOuter = new(new(-5, -3, -10), new(5, 3, 10));

    /// <summary>The room inside <see cref="HollowOuter"/>.</summary>
    private static readonly BoundingBox HollowRoom = new(new(-4.5f, -2.5f, -9.5f), new(4.5f, 2.5f, 9.5f));

    /// <summary>
    /// The holes in the walls of <see cref="HollowOuter"/>. The -X wall has a doorway near the floor and two windows. The
    /// +X wall has three windows. The -Z wall has one wide window, from -2 m to 2 m along X and from -1 m to 0.5 m along Y.
    /// The +Z wall has none.
    /// </summary>
    private static readonly Opening[] HollowOpenings =
    [
        Opening.ThroughX(new(new(-5, -2.3f, 2), new(-4.5f, -0.2f, 3))),
        Opening.ThroughX(new(new(-5, -1, -6), new(-4.5f, 0.2f, -4))),
        Opening.ThroughX(new(new(-5, -1, -2), new(-4.5f, 0.2f, 0))),
        Opening.ThroughX(new(new(4.5f, -1, -6), new(5, 0.2f, -4))),
        Opening.ThroughX(new(new(4.5f, -1, -2), new(5, 0.2f, 0))),
        Opening.ThroughX(new(new(4.5f, -1, 4), new(5, 0.2f, 6))),
        Opening.ThroughZ(new(new(-2, -1, -10), new(2, 0.5f, -9.5f)))
    ];

    private static IEnumerable<(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal)> Triangles(MeshData mesh)
    {
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vertex a = mesh.Vertices[mesh.Indices[i]],
                b = mesh.Vertices[mesh.Indices[i + 1]],
                c = mesh.Vertices[mesh.Indices[i + 2]];
            a.Normal.Should().Be(b.Normal, "a face is flat");
            a.Normal.Should().Be(c.Normal, "a face is flat");
            yield return (a.Position, b.Position, c.Position, a.Normal);
        }
    }

    /// <summary>
    /// Each triangle winds counterclockwise around the normal of each corner, which is the triangle's own because every
    /// face is flat (0.001 rad allows for rounding), and a point 1 cm in front of its center is outside the solid.
    /// </summary>
    private static void ExpectFacingOut(MeshData mesh, Func<Vector3, bool> inside) =>
        MeshChecks.ExpectFacingOut(mesh: mesh, inside: inside, probe: 0.01f, maxNormalAngle: 0.001f);

    private static bool Within(BoundingBox box, Vector3 point) => box.Contains(point);

    private static bool WithinAny(IEnumerable<Opening> openings, Vector3 point) =>
        openings.Any(opening => Within(opening.Bounds, point));

    /// <summary>
    /// True if the line along <paramref name="axis"/> through the point (<paramref name="u"/>, <paramref name="v"/>) of the
    /// other two axes, in the order X, Y, Z, crosses a triangle. With <paramref name="keep"/>, only the triangles whose
    /// center it accepts count. The test is strict: a line that runs exactly along the edge of a triangle crosses none. A
    /// plain quad has an edge along its diagonal, so a probe that must hit one has to be off both diagonals, and a probe
    /// that must miss one proves nothing on a diagonal.
    /// </summary>
    private static bool LineHits(MeshData mesh, Vector3 axis, float u, float v, Func<Vector3, bool>? keep = null) =>
        Triangles(mesh).Where(t => keep is null || keep((t.A + t.B + t.C) / 3)).Any(t =>
        {
            // The triangle's shadow on the plane across the axis contains the point.
            static float Side(Vector2 p, Vector2 q, Vector2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);

            Vector2 Shadow(Vector3 point) => axis == Vector3.UnitX ? new(point.Y, point.Z) :
                axis == Vector3.UnitY ? new(point.X, point.Z) : new(point.X, point.Y);

            Vector2 p = new(u, v), a = Shadow(t.A), b = Shadow(t.B), c = Shadow(t.C);
            float ab = Side(a, b, p), bc = Side(b, c, p), ca = Side(c, a, p);
            return (ab > 0 && bc > 0 && ca > 0) || (ab < 0 && bc < 0 && ca < 0);
        });

    /// <summary>True if the line along X through (<paramref name="y"/>, <paramref name="z"/>) crosses a triangle.</summary>
    private static bool LineAlongXHits(MeshData mesh, float y, float z) => LineHits(mesh: mesh, axis: Vector3.UnitX, u: y, v: z);

    /// <summary>True if the line along Z through (<paramref name="x"/>, <paramref name="y"/>) crosses a triangle that
    /// <paramref name="keep"/> accepts.</summary>
    private static bool LineAlongZHits(MeshData mesh, float x, float y, Func<Vector3, bool>? keep = null) =>
        LineHits(mesh: mesh, axis: Vector3.UnitZ, u: x, v: y, keep: keep);

    [Fact]
    public void ABoxIsClosedAndFacesOut()
    {
        MeshData box = new BoxMesh().Box(Slab).Build();

        // Six faces of two triangles each: no face has an extra vertex.
        box.Indices.Length.Should().Be(6 * 2 * 3);
        ExpectClosed(box);
        ExpectFacingOut(box, p => Within(Slab, p));
    }

    [Fact]
    public void AnInwardBoxIsClosedAndFacesItsInside()
    {
        MeshData room = new BoxMesh().Box(Slab, inward: true).Build();

        room.Indices.Length.Should().Be(6 * 2 * 3);
        ExpectClosed(room);
        ExpectFacingOut(room, p => !Within(Slab, p));
    }

    [Fact]
    public void ASlabWithADoorwayIsClosedAndFacesOutOfTheSlabAndIntoTheDoorway()
    {
        // Each face with the doorway is four trapezoids of two triangles each. The four other faces of the slab and the
        // four faces inside the doorway have two triangles each.
        SlabWithDoorway.Indices.Length.Should().Be((2 * 4 * 2 + 4 * 2 + 4 * 2) * 3);
        ExpectClosed(SlabWithDoorway);
        ExpectFacingOut(SlabWithDoorway, p => Within(Slab, p) && !Within(Doorway.Bounds, p));
    }

    [Fact]
    public void ALineThroughTheDoorwayMissesTheSlabAndALineBesideItHitsBothFaces()
    {
        // (0.2, 0.1) is inside the doorway and on neither diagonal of the slab's faces, which run along y = z and y = -z.
        // A plain slab without the doorway would be crossed there.
        LineAlongXHits(SlabWithDoorway, 0.2f, 0.1f).Should()
            .BeFalse("the line goes through the doorway and is not on an edge of a triangle");
        LineAlongXHits(SlabWithDoorway, 0, 1.5f).Should().BeTrue();
        LineAlongXHits(SlabWithDoorway, 1.5f, 0).Should().BeTrue();
    }

    [Fact]
    public void AHollowBoxWithADoorwayThroughOneWallIsClosed()
    {
        // The ship's hull without the airlock's chamber: a box 0.5 m thick on every side around a room, with a doorway
        // through the room's -X wall near its floor.
        var outer = new BoundingBox(new(-10, -4, -30), new(10, 4, 30));
        var room = new BoundingBox(new(-9.5f, -3.5f, -29.5f), new(9.5f, 3.5f, 29.5f));
        Opening doorway = Opening.ThroughX(new(new(-10, -3.3f, 9.4f), new(-9.5f, -1.2f, 10.6f)));
        MeshData hull = new BoxMesh().Box(outer, openings: doorway).Box(room, inward: true, openings: doorway)
            .Sides(doorway).Build();

        hull.Indices.Length.Should().Be((2 * 4 * 2 + 10 * 2 + 4 * 2) * 3);
        ExpectClosed(hull);
        ExpectFacingOut(hull, p => Within(outer, p) && !Within(room, p) && !Within(doorway.Bounds, p));
    }

    private static MeshData SlabAcrossZWithWindow() =>
        new BoxMesh().Box(SlabAcrossZ, openings: WindowAcrossZ).Sides(WindowAcrossZ).Build();

    [Fact]
    public void ASlabAcrossZWithAWindowIsClosedAndFacesOutOfTheSlabAndIntoTheWindow()
    {
        MeshData slab = SlabAcrossZWithWindow();

        ExpectClosed(slab);
        ExpectFacingOut(slab, p => Within(SlabAcrossZ, p) && !Within(WindowAcrossZ.Bounds, p));
    }

    [Fact]
    public void ALineAlongZThroughTheWindowMissesTheSlabAndALineBelowItHitsBothFaces()
    {
        MeshData slab = SlabAcrossZWithWindow();

        LineAlongZHits(mesh: slab, x: 0, y: 0.7f).Should().BeFalse();
        LineAlongZHits(mesh: slab, x: 0, y: -1).Should().BeTrue();
        LineAlongZHits(mesh: slab, x: 2.5f, y: 0.7f).Should().BeTrue();
    }

    private static MeshData LongSlabWithRow()
    {
        BoxMesh mesh = new BoxMesh().Box(LongSlab, openings: Row);
        foreach (Opening opening in Row)
            mesh.Sides(opening);
        return mesh.Build();
    }

    [Fact]
    public void ASlabWithARowOfOpeningsOfDifferentHeightsIsClosedAndFacesOutOfTheSlabAndIntoEachOpening()
    {
        MeshData slab = LongSlabWithRow();

        ExpectClosed(slab);
        ExpectFacingOut(slab, p => Within(LongSlab, p) && !WithinAny(Row, p));
    }

    [Fact]
    public void ALineThroughEachOpeningOfTheRowMissesTheSlabAndALineBesideOrAboveItHits()
    {
        MeshData slab = LongSlabWithRow();

        foreach (Opening opening in Row)
            LineAlongXHits(slab, opening.Bounds.Center.Y, opening.Bounds.Center.Z).Should()
                .BeFalse($"the line through {opening.Bounds.Center} goes through the opening");
        // Between the openings, above the lower ones and below the higher one, and at the slab's ends.
        foreach ((float y, float z) in new (float Y, float Z)[]
                 {
                     (0, -3), (0, 0.5f), (0, 3.5f), (1.3f, -1), (1.3f, 2), (-1, 4.5f), (1.8f, -4), (-1.9f, 0),
                     (0, -5.5f), (0, 5.5f)
                 })
            LineAlongXHits(slab, y, z).Should().BeTrue($"the line through ({y}, {z}) meets the slab");
    }

    private static MeshData HollowBoxWithOpenings()
    {
        BoxMesh mesh = new BoxMesh().Box(HollowOuter, openings: HollowOpenings)
            .Box(HollowRoom, inward: true, openings: HollowOpenings);
        foreach (Opening opening in HollowOpenings)
            mesh.Sides(opening);
        return mesh.Build();
    }

    [Fact]
    public void AHollowBoxWithOpeningsThroughBothSideWallsAndAnEndWallIsClosed()
    {
        MeshData hull = HollowBoxWithOpenings();

        ExpectClosed(hull);
        ExpectFacingOut(hull, p => Within(HollowOuter, p) && !Within(HollowRoom, p) && !WithinAny(HollowOpenings, p));
    }

    [Fact]
    public void ALineAlongZThroughTheWindowInTheEndWallLeavesTheHollowBoxAndMeetsTheOtherEndWall()
    {
        MeshData hull = HollowBoxWithOpenings();

        // (0.1, 0.1) is inside the window in the -Z wall, and on neither diagonal of the faces of the +Z wall, which has
        // no window. A line along the edge of a triangle crosses none, so the middle of the room, (0, 0), would prove
        // nothing: it is on the diagonals of both walls.
        LineAlongZHits(mesh: hull, x: 0.1f, y: 0.1f, keep: center => center.Z < 0).Should()
            .BeFalse("a line along the room goes out through the window in the -Z wall");
        LineAlongZHits(mesh: hull, x: 0.1f, y: 0.1f, keep: center => center.Z > 0).Should()
            .BeTrue("the +Z wall has no window, so the same line meets both of its faces");
    }

    [Fact]
    public void TwoOpeningsThatOverlapAlongTheirWallAreAnError()
    {
        Opening low = Opening.ThroughX(new(new(0, -1.5f, -1), new(0.5f, -0.5f, 1)));
        Opening high = Opening.ThroughX(new(new(0, 0.5f, 0), new(0.5f, 1.5f, 1.5f)));

        Action stacked = () => new BoxMesh().Box(Slab, openings: [low, high]);

        stacked.Should().Throw<ArgumentException>().WithParameterName("openings").WithMessage("*overlap*");
    }

    [Fact]
    public void AnOpeningThatReachesTheEdgeOfItsFaceIsAnError()
    {
        // A doorway down to the floor would cut the slab's bottom face too.
        Opening toTheFloor = Opening.ThroughX(new(new(0, -2, -0.5f), new(0.5f, 1, 0.5f)));

        Action cut = () => new BoxMesh().Box(Slab, openings: toTheFloor);

        cut.Should().Throw<ArgumentException>().WithParameterName("openings").WithMessage("*inside*");
    }

    [Fact]
    public void AnOpeningThatCutsNoFaceOfTheBoxIsAnError()
    {
        // The window goes through a wall across Z, and the slab's faces across Z are at its ends, far from the window.
        Action cut = () => new BoxMesh().Box(Slab, openings: WindowAcrossZ);

        cut.Should().Throw<ArgumentException>().WithMessage("*no face*");
    }

    [Fact]
    public void AnOpeningThatGoesThroughNoAxisCannotGetSides()
    {
        // The default opening has no axis, so it has no faces across it to add.
        Action sides = () => new BoxMesh().Sides(default);

        sides.Should().Throw<ArgumentException>().WithParameterName("opening").WithMessage("*no axis*");
    }
}
