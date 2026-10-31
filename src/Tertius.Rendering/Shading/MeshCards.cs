using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The cards of a mesh (docs/plans/skin-and-hair.md, "The card number"): its connected strips of triangles. Two triangles
/// lie on one card when a chain of triangles joins them, each with a vertex position of the next, so a strip whose
/// exporter splits its vertices at a sharp bend or a seam stays one card.
/// </summary>
internal static class MeshCards
{
    /// <summary>
    /// The card number of each vertex of the triangle list <paramref name="indices"/>: the cards count from 0 in the order
    /// of their first triangles, and every vertex of a triangle carries the triangle's card. A vertex of no triangle takes
    /// a number after them.
    /// </summary>
    public static uint[] Number(Vertex[] vertices, uint[] indices)
    {
        // A forest over the vertices: each tree is one card, its root the vertex that stands for it.
        int[] parent = [.. Enumerable.Range(0, vertices.Length)];
        var firstAt = new Dictionary<Vector3, int>(vertices.Length);
        for (int vertex = 0; vertex < vertices.Length; vertex++)
            if (!firstAt.TryAdd(vertices[vertex].Position, vertex))
                Join(parent, vertex, firstAt[vertices[vertex].Position]);
        for (int corner = 0; corner < indices.Length; corner += 3)
        {
            Join(parent, (int)indices[corner], (int)indices[corner + 1]);
            Join(parent, (int)indices[corner], (int)indices[corner + 2]);
        }

        // The triangles' order first, so a card's number is the place of its first triangle among the cards.
        var cardOfRoot = new Dictionary<int, uint>();
        foreach (uint index in indices)
            cardOfRoot.TryAdd(Root(parent, (int)index), (uint)cardOfRoot.Count);
        uint[] cards = new uint[vertices.Length];
        for (int vertex = 0; vertex < vertices.Length; vertex++)
        {
            int root = Root(parent, vertex);
            cardOfRoot.TryAdd(root, (uint)cardOfRoot.Count);
            cards[vertex] = cardOfRoot[root];
        }

        return cards;
    }

    /// <summary>The root of <paramref name="vertex"/>'s tree, halving its path on the way.</summary>
    private static int Root(int[] parent, int vertex)
    {
        while (parent[vertex] != vertex)
            vertex = parent[vertex] = parent[parent[vertex]];
        return vertex;
    }

    private static void Join(int[] parent, int a, int b) => parent[Root(parent, a)] = Root(parent, b);
}
