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
        var edges = new Dictionary<(Vector3, Vector3), int>();
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector3 a = mesh.Vertices[mesh.Indices[i]].Position, b = mesh.Vertices[mesh.Indices[i + 1]].Position, c = mesh.Vertices[mesh.Indices[i + 2]].Position;
            foreach (var edge in new[] { (a, b), (b, c), (c, a) })
                edges[edge] = edges.GetValueOrDefault(edge) + 1;
        }
        foreach (var ((from, to), count) in edges)
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
            Vertex a = mesh.Vertices[mesh.Indices[i]], b = mesh.Vertices[mesh.Indices[i + 1]], c = mesh.Vertices[mesh.Indices[i + 2]];
            Vector3 wound = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            wound.Length().Should().BeGreaterThan(1e-6f, "no triangle is degenerate");
            Vector3 face = Vector3.Normalize(wound);
            foreach (Vertex corner in (ReadOnlySpan<Vertex>)[a, b, c])
                Vector3.Dot(face, corner.Normal).Should().BeGreaterThanOrEqualTo(MathF.Cos(maxNormalAngle), $"the triangle at {a.Position} winds around its corner's normal {corner.Normal}");
            Vector3 center = (a.Position + b.Position + c.Position) / 3;
            inside(center + probe * face).Should().BeFalse($"the triangle at {center} faces out of the solid along {face}");
            inside(center - probe * face).Should().BeTrue($"the triangle at {center} has the solid behind it");
        }
    }
}
