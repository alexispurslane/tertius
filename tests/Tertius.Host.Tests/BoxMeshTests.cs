using System.Numerics;
using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// BoxMesh: meshes of boxes whose walls across X can have a doorway through them. A mesh of solids must be closed, with
/// every edge shared by exactly two triangles that run along it in opposite directions, so it has no crack and no
/// T-junction. Every triangle must wind counterclockwise around its normal, and the normal must point out of the solid.
/// </summary>
public sealed class BoxMeshTests
{
    /// <summary>A slab 0.5 m thick across X, 4 m by 4 m.</summary>
    private static readonly BoundingBox Slab = new(new Vector3(0, -2, -2), new Vector3(0.5f, 2, 2));

    /// <summary>A doorway through <see cref="Slab"/>: 1 m wide along Z and 2 m tall, centered.</summary>
    private static readonly BoundingBox Doorway = new(new Vector3(0, -1, -0.5f), new Vector3(0.5f, 1, 0.5f));

    private static readonly MeshData SlabWithDoorway = new BoxMesh().Box(Slab, doorway: Doorway).Doorway(Doorway).Build();

    private static IEnumerable<(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal)> Triangles(MeshData mesh)
    {
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vertex a = mesh.Vertices[mesh.Indices[i]], b = mesh.Vertices[mesh.Indices[i + 1]], c = mesh.Vertices[mesh.Indices[i + 2]];
            a.Normal.Should().Be(b.Normal, "a face is flat");
            a.Normal.Should().Be(c.Normal, "a face is flat");
            yield return (a.Position, b.Position, c.Position, a.Normal);
        }
    }

    /// <summary>Every directed edge of every triangle, by the positions of its ends.</summary>
    private static void ExpectClosed(MeshData mesh)
    {
        var edges = new Dictionary<(Vector3, Vector3), int>();
        foreach (var (a, b, c, _) in Triangles(mesh))
        {
            foreach (var edge in new[] { (a, b), (b, c), (c, a) })
                edges[edge] = edges.GetValueOrDefault(edge) + 1;
        }
        foreach (var ((from, to), count) in edges)
        {
            count.Should().Be(1, $"one triangle runs from {from} to {to}");
            edges.GetValueOrDefault((to, from)).Should().Be(1, $"one triangle runs back from {to} to {from}");
        }
    }

    /// <summary>Each triangle winds counterclockwise around its normal, and a point just off it along the normal is outside.</summary>
    private static void ExpectFacingOut(MeshData mesh, Func<Vector3, bool> inside)
    {
        foreach (var (a, b, c, normal) in Triangles(mesh))
        {
            Vector3 wound = Vector3.Cross(b - a, c - a);
            wound.Length().Should().BeGreaterThan(1e-6f, "no triangle is degenerate");
            Vector3.Dot(Vector3.Normalize(wound), normal).Should().BeApproximately(1, 1e-5f, $"the triangle {a}, {b}, {c} winds around {normal}");
            Vector3 center = (a + b + c) / 3;
            inside(center + 0.01f * normal).Should().BeFalse($"the triangle at {center} faces out of the solid along {normal}");
            inside(center - 0.01f * normal).Should().BeTrue($"the triangle at {center} has the solid behind it");
        }
    }

    private static bool Within(BoundingBox box, Vector3 point) => box.Contains(point);

    /// <summary>True if the line along X through (<paramref name="y"/>, <paramref name="z"/>) crosses a triangle.</summary>
    private static bool LineAlongXHits(MeshData mesh, float y, float z) =>
        Triangles(mesh).Any(t =>
        {
            // The triangle's shadow on the YZ plane contains the point.
            static float Side(Vector2 p, Vector2 q, Vector2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            Vector2 p = new(y, z), a = new(t.A.Y, t.A.Z), b = new(t.B.Y, t.B.Z), c = new(t.C.Y, t.C.Z);
            float ab = Side(a, b, p), bc = Side(b, c, p), ca = Side(c, a, p);
            return (ab > 0 && bc > 0 && ca > 0) || (ab < 0 && bc < 0 && ca < 0);
        });

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
        ExpectFacingOut(SlabWithDoorway, p => Within(Slab, p) && !Within(Doorway, p));
    }

    [Fact]
    public void ALineThroughTheDoorwayMissesTheSlabAndALineBesideItHitsBothFaces()
    {
        LineAlongXHits(SlabWithDoorway, 0, 0).Should().BeFalse();
        LineAlongXHits(SlabWithDoorway, 0, 1.5f).Should().BeTrue();
        LineAlongXHits(SlabWithDoorway, 1.5f, 0).Should().BeTrue();
    }

    [Fact]
    public void AHollowBoxWithADoorwayThroughOneWallIsClosed()
    {
        // The ship's shape: a box 0.5 m thick on every side around a room, with a doorway through the room's -X wall.
        var outer = new BoundingBox(new Vector3(-10, -4, -30), new Vector3(10, 4, 30));
        var room = new BoundingBox(new Vector3(-9.5f, -3.5f, -29.5f), new Vector3(9.5f, 3.5f, 29.5f));
        var doorway = new BoundingBox(new Vector3(-10, -3.3f, 9.4f), new Vector3(-9.5f, -1.2f, 10.6f));
        MeshData hull = new BoxMesh().Box(outer, doorway: doorway).Box(room, inward: true, doorway: doorway).Doorway(doorway).Build();

        hull.Indices.Length.Should().Be((2 * 4 * 2 + 10 * 2 + 4 * 2) * 3);
        ExpectClosed(hull);
        ExpectFacingOut(hull, p => Within(outer, p) && !Within(room, p) && !Within(doorway, p));
    }
}
