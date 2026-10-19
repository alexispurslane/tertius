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
}
