namespace Tertius.Rendering;

/// <summary>
/// Receives meshes, textures and materials from any thread and hands them to the render thread in one batch per
/// frame. Ids are dense and assigned under the same lock that queues the data, so a batch never holds a material
/// whose textures are missing from it or from an earlier batch.
/// </summary>
/// <remarks>
/// Each kind of id counts from 0. The built-in default textures and the default material live in slots that no id
/// names (<see cref="TextureStore.Slot"/>, <see cref="MaterialStore.Slot"/>), so the zeroed id,
/// <see cref="TextureId.None"/>, is the only way to ask for a default, and no id handed out here is ever None.
/// </remarks>
internal sealed class UploadQueue(int maxTextureSize)
{
    private readonly Lock gate = new();
    private Pending pending = new();
    private Pending spare = new();
    private int meshCount;
    private int textureCount;
    private int materialCount;
    private readonly int maxTextureSize = maxTextureSize;

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
            if (index >= vertexCount)
                throw new ArgumentException($"Index {index} is out of range for {vertexCount} vertices.", nameof(mesh));
        lock (gate)
        {
            var id = new MeshId(Next(ref meshCount, "meshes"));
            pending.Meshes.Add((id, mesh));
            return id;
        }
    }

    public TextureId AddTexture(TextureData texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(texture.Rgba);
        if (
            texture.Width <= 0
            || texture.Height <= 0
            || texture.Width > maxTextureSize
            || texture.Height > maxTextureSize
        )
            throw new ArgumentOutOfRangeException(
                nameof(texture),
                $"Texture size must be 1 to {maxTextureSize} pixels per side."
            );
        if (texture.Rgba.Length != texture.Width * texture.Height * 4)
            throw new ArgumentException(
                $"Expected {texture.Width * texture.Height * 4} bytes of RGBA8, got {texture.Rgba.Length}.",
                nameof(texture)
            );
        lock (gate)
        {
            if (textureCount >= TextureStore.UserCapacity)
                throw new InvalidOperationException(
                    $"The bindless texture array is full ({TextureStore.UserCapacity} textures besides the defaults)."
                );
            var id = new TextureId(Next(ref textureCount, "textures"));
            pending.Textures.Add((id, texture));
            return id;
        }
    }

    public MaterialId AddMaterial(MaterialDesc material)
    {
        ArgumentNullException.ThrowIfNull(material);
        lock (gate)
        {
            CheckTexture(material.BaseColorMap, nameof(material.BaseColorMap));
            CheckTexture(material.NormalMap, nameof(material.NormalMap));
            CheckTexture(material.MetallicRoughnessMap, nameof(material.MetallicRoughnessMap));
            var id = new MaterialId(Next(ref materialCount, "materials"));
            pending.Materials.Add((id, material));
            return id;
        }
    }

    /// <summary>
    /// Takes the next id from a counter. The counter must not wrap: after <see cref="int.MaxValue"/> it would count
    /// up through the negative numbers to −1, the value of None, and a real resource would then read as None.
    /// </summary>
    private static int Next(ref int counter, string kind) =>
        counter == int.MaxValue
            ? throw new InvalidOperationException($"This renderer has run out of ids for {kind}.")
            : counter++;

    /// <summary>A material's texture slot is None (the default texture) or a texture this renderer created.</summary>
    private void CheckTexture(TextureId texture, string slot)
    {
        if (!texture.IsNone && (uint)texture.Value >= (uint)textureCount)
            throw new ArgumentException($"{slot} refers to {texture}, which this renderer did not create.", slot);
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
        public readonly List<(MeshId Id, MeshData Data)> Meshes = [];
        public readonly List<(TextureId Id, TextureData Data)> Textures = [];
        public readonly List<(MaterialId Id, MaterialDesc Desc)> Materials = [];

        public bool IsEmpty => Meshes.Count == 0 && Textures.Count == 0 && Materials.Count == 0;

        public void Clear()
        {
            Meshes.Clear();
            Textures.Clear();
            Materials.Clear();
        }
    }
}
