namespace Tertius.Rendering;

/// <summary>
/// Receives meshes, textures and materials from any thread and hands them to the render thread in one batch per
/// frame. Ids are dense and assigned under the same lock that queues the data, so a batch never holds a material
/// whose textures are missing from it or from an earlier batch.
/// </summary>
internal sealed class UploadQueue
{
    private readonly Lock gate = new();
    private Pending pending = new();
    private Pending spare = new();
    private int meshCount;
    private int textureCount = TextureStore.FirstUserIndex;
    private int materialCount = MaterialStore.FirstUserIndex;
    private readonly int maxTextureSize;

    public UploadQueue(int maxTextureSize)
    {
        this.maxTextureSize = maxTextureSize;
    }

    public MeshId AddMesh(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(mesh.Vertices);
        ArgumentNullException.ThrowIfNull(mesh.Indices);
        if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
            throw new ArgumentException("A mesh needs at least one vertex and one index.", nameof(mesh));
        if (mesh.Indices.Length % 3 != 0)
            throw new ArgumentException("A triangle list needs a multiple of three indices.", nameof(mesh));
        uint vertexCount = (uint)mesh.Vertices.Length;
        foreach (uint index in mesh.Indices)
        {
            if (index >= vertexCount)
                throw new ArgumentException($"Index {index} is out of range for {vertexCount} vertices.", nameof(mesh));
        }
        lock (gate)
        {
            var id = new MeshId(meshCount++);
            pending.Meshes.Add((id.Value, mesh));
            return id;
        }
    }

    public TextureId AddTexture(TextureData texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(texture.Rgba);
        if (texture.Width <= 0 || texture.Height <= 0 || texture.Width > maxTextureSize || texture.Height > maxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(texture), $"Texture size must be 1 to {maxTextureSize} pixels per side.");
        if (texture.Rgba.Length != texture.Width * texture.Height * 4)
            throw new ArgumentException($"Expected {texture.Width * texture.Height * 4} bytes of RGBA8, got {texture.Rgba.Length}.", nameof(texture));
        lock (gate)
        {
            if (textureCount >= TextureStore.Capacity)
                throw new InvalidOperationException($"The bindless texture array is full ({TextureStore.Capacity} textures).");
            var id = new TextureId(textureCount++);
            pending.Textures.Add((id.Value, texture));
            return id;
        }
    }

    public MaterialId AddMaterial(MaterialDesc material)
    {
        ArgumentNullException.ThrowIfNull(material);
        lock (gate)
        {
            CheckTexture(material.Albedo, nameof(material.Albedo));
            CheckTexture(material.Normal, nameof(material.Normal));
            CheckTexture(material.SpecularGloss, nameof(material.SpecularGloss));
            var id = new MaterialId(materialCount++);
            pending.Materials.Add((id.Value, material));
            return id;
        }
    }

    private void CheckTexture(TextureId texture, string slot)
    {
        if (texture.Value >= textureCount)
            throw new ArgumentException($"{slot} refers to texture {texture.Value}, which this renderer did not create.", slot);
    }

    /// <summary>Takes everything queued so far. The returned batch is valid until the next call.</summary>
    public Pending Drain()
    {
        lock (gate)
        {
            (pending, spare) = (spare, pending);
            pending.Clear();
            return spare;
        }
    }

    /// <summary>One frame's worth of queued resources, each list in id order.</summary>
    public sealed class Pending
    {
        public readonly List<(int Id, MeshData Data)> Meshes = new();
        public readonly List<(int Id, TextureData Data)> Textures = new();
        public readonly List<(int Id, MaterialDesc Desc)> Materials = new();

        public bool IsEmpty => Meshes.Count == 0 && Textures.Count == 0 && Materials.Count == 0;

        public void Clear()
        {
            Meshes.Clear();
            Textures.Clear();
            Materials.Clear();
        }
    }
}
