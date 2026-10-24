using System.Numerics;

namespace Tertius.Host.Tests;

/// <summary>Checks that the tests of the demo's mesh builders share.</summary>
public static class MeshChecks
{
    /// <summary>
    /// The mesh has triangles, and it is closed: each directed edge of a triangle, by the positions of its ends, belongs
    /// to exactly one triangle, and exactly one triangle runs back along it. So it has no crack, no T-junction and no
    /// triangle that faces the wrong way against its neighbors.
    /// </summary>
    public static void ExpectClosed(MeshData mesh)
    {
        mesh.Indices.Should().NotBeEmpty("a mesh with no triangle proves nothing");
        var edges = new Dictionary<(Vector3 From, Vector3 To), int>();
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector3 a = mesh.Vertices[mesh.Indices[i]].Position,
                b = mesh.Vertices[mesh.Indices[i + 1]].Position,
                c = mesh.Vertices[mesh.Indices[i + 2]].Position;
            foreach ((Vector3 From, Vector3 To) edge in new[] { (a, b), (b, c), (c, a) })
                edges[edge] = edges.GetValueOrDefault(edge) + 1;
        }

        foreach (((Vector3 from, Vector3 to), int count) in edges)
        {
            count.Should().Be(1, $"one triangle runs from {from} to {to}");
            edges.GetValueOrDefault((to, from)).Should().Be(1, $"one triangle runs back from {to} to {from}");
        }
    }

    /// <summary>
    /// Each triangle has an area, winds counterclockwise around the normal of each of its corners, which is within
    /// <paramref name="maxNormalAngle"/> radians of the triangle's own normal, and faces out of the solid: a point
    /// <paramref name="probe"/> meters in front of its center is outside, by <paramref name="inside"/>, and one as far
    /// behind it is inside.
    /// </summary>
    public static void ExpectFacingOut(MeshData mesh, Func<Vector3, bool> inside, float probe, float maxNormalAngle)
    {
        mesh.Indices.Should().NotBeEmpty("a mesh with no triangle proves nothing");
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vertex a = mesh.Vertices[mesh.Indices[i]],
                b = mesh.Vertices[mesh.Indices[i + 1]],
                c = mesh.Vertices[mesh.Indices[i + 2]];
            var wound = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            wound.Length().Should().BeGreaterThan(1e-6f, "no triangle is degenerate");
            var face = Vector3.Normalize(wound);
            foreach (Vertex corner in (ReadOnlySpan<Vertex>)[a, b, c])
                Vector3
                    .Dot(face, corner.Normal)
                    .Should()
                    .BeGreaterThanOrEqualTo(
                        MathF.Cos(maxNormalAngle),
                        $"the triangle at {a.Position} winds around its corner's normal {corner.Normal}"
                    );
            Vector3 center = (a.Position + b.Position + c.Position) / 3;
            inside(center + probe * face)
                .Should()
                .BeFalse($"the triangle at {center} faces out of the solid along {face}");
            inside(center - probe * face).Should().BeTrue($"the triangle at {center} has the solid behind it");
        }
    }

    /// <summary>How many triangles of <paramref name="mesh"/> the segment from <paramref name="from"/> to
    /// <paramref name="to"/> crosses.</summary>
    public static int Crossings(MeshData mesh, Vector3 from, Vector3 to) => CrossingsAlong(mesh, from, to).Count();

    /// <summary>
    /// The point where the segment from <paramref name="from"/> to <paramref name="to"/> first crosses a triangle of
    /// <paramref name="mesh"/>, or null when it crosses none.
    /// </summary>
    public static Vector3? FirstCrossing(MeshData mesh, Vector3 from, Vector3 to)
    {
        float[] crossings = [.. CrossingsAlong(mesh, from, to)];
        return crossings.Length == 0 ? null : from + crossings.Min() * (to - from);
    }

    /// <summary>
    /// The parameter t, from 0 at <paramref name="from"/> to 1 at <paramref name="to"/>, of each crossing of the segment
    /// with a triangle of <paramref name="mesh"/>.
    /// </summary>
    private static IEnumerable<float> CrossingsAlong(MeshData mesh, Vector3 from, Vector3 to)
    {
        Vector3 along = to - from;
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector3 a = mesh.Vertices[mesh.Indices[i]].Position,
                b = mesh.Vertices[mesh.Indices[i + 1]].Position,
                c = mesh.Vertices[mesh.Indices[i + 2]].Position;
            // Möller and Trumbore: the segment's parameter t and the triangle's barycentric u and v of the crossing.
            Vector3 ab = b - a,
                ac = c - a,
                p = Vector3.Cross(along, ac);
            float det = Vector3.Dot(ab, p);
            if (MathF.Abs(det) < 1e-9f)
                continue;
            Vector3 s = from - a;
            float u = Vector3.Dot(s, p) / det;
            var q = Vector3.Cross(s, ab);
            float v = Vector3.Dot(along, q) / det,
                t = Vector3.Dot(ac, q) / det;
            if (u >= 0 && v >= 0 && u + v <= 1 && t >= 0 && t <= 1)
                yield return t;
        }
    }
}
