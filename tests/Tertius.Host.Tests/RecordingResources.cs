namespace Tertius.Host.Tests;

/// <summary>Resources that give each mesh, texture and material its own id, and keep each mesh and each material's description.</summary>
internal sealed class RecordingResources : IRenderResources
{
    private int next = 1;

    public Dictionary<MeshId, MeshData> Meshes { get; } = [];

    public Dictionary<MaterialId, MaterialDesc> Materials { get; } = [];

    public MeshId CreateMesh(MeshData mesh)
    {
        var id = new MeshId(next++);
        Meshes[id] = mesh;
        return id;
    }

    public TextureId CreateTexture(TextureData texture) => new(next++);

    public MaterialId CreateMaterial(MaterialDesc material)
    {
        var id = new MaterialId(next++);
        Materials[id] = material;
        return id;
    }
}
