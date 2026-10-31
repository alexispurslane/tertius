namespace Tertius.Rendering;

/// <summary>
/// A mesh with its vertices as the global vertex buffer holds them: each vertex <see cref="GeometryStore.Staged"/>, with
/// the number of its card (<see cref="MeshCards.Number"/>). <see cref="Of"/> builds both from the mesh, so every vertex
/// of the mesh has its GPU vertex and no caller pairs a card with a vertex by hand.
/// </summary>
internal sealed class StagedMesh
{
    private StagedMesh(MeshData data, GpuVertex[] vertices) => (Data, Vertices) = (data, vertices);

    /// <summary>The mesh as the caller gave it: its indices, its rigging and its vertices before staging.</summary>
    public MeshData Data { get; }

    /// <summary>The GPU vertex of each vertex of <see cref="Data"/>, in the same order.</summary>
    public GpuVertex[] Vertices { get; }

    /// <summary>
    /// <paramref name="mesh"/>, staged. It walks the mesh's triangles, so the upload queue calls it on the caller's thread
    /// and the render thread never does.
    /// </summary>
    public static StagedMesh Of(MeshData mesh)
    {
        uint[] cards = MeshCards.Number(mesh.Vertices, mesh.Indices);
        var vertices = new GpuVertex[mesh.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = GpuVertex.Of(GeometryStore.Staged(mesh.Vertices[i]), cards[i]);
        return new(mesh, vertices);
    }
}
