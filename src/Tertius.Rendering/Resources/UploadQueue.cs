using System.Numerics;
using System.Runtime.CompilerServices;

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
internal sealed class UploadQueue(int maxTextureSize, long deformBytesLimit)
{
    private readonly Lock gate = new();
    private Pending pending = new();
    private Pending spare = new();
    private int meshCount;
    private int textureCount;
    private int materialCount;
    private readonly int maxTextureSize = maxTextureSize;
    private readonly long deformBytesLimit = deformBytesLimit;
    private long deformVertexBytes;
    private long deltaBytes;

    public MeshId AddMesh(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(mesh.Vertices);
        ArgumentNullException.ThrowIfNull(mesh.Indices);
        if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
            throw new ArgumentException("A mesh needs at least one vertex and one index.", nameof(mesh));
        if (mesh.Indices.Length % 3 != 0)
            throw new ArgumentException("A triangle list needs a multiple of three indices.", nameof(mesh));
        if (mesh.Rigging is { } rigging)
            CheckRigging(rigging, mesh.Vertices.Length);
        foreach (ref readonly Vertex vertex in mesh.Vertices.AsSpan())
            if (!IsFinite(vertex))
                throw new ArgumentException($"The vertex {vertex} holds a number that is not finite.", nameof(mesh));
        uint vertexCount = (uint)mesh.Vertices.Length;
        foreach (uint index in mesh.Indices)
            if (index >= vertexCount)
                throw new ArgumentException($"Index {index} is out of range for {vertexCount} vertices.", nameof(mesh));
        // On the caller's thread, so the render thread never walks a mesh's triangles.
        uint[] cards = MeshCards.Number(mesh.Vertices, mesh.Indices);
        PackedDeform? deform = MeshDeform.Pack(mesh, cards);
        lock (gate)
        {
            (long vertexBytes, long deltaBytesAfter) = deform is null
                ? (deformVertexBytes, deltaBytes)
                : DeformBytesWith(deform);
            var id = new MeshId(Next(ref meshCount, "meshes"));
            (deformVertexBytes, deltaBytes) = (vertexBytes, deltaBytesAfter);
            pending.Meshes.Add((id, mesh, cards));
            pending.Deforms.Add(deform);
            return id;
        }
    }

    /// <summary>
    /// The bytes of the deform vertices and of the deltas after <paramref name="deform"/>. Each store holds at most the
    /// device's maxStorageBufferRange (docs/plans/skinning-and-morphs.md, "Buffers and the region").
    /// </summary>
    /// <exception cref="InvalidOperationException">The deform vertices or the deltas pass the device's limit.</exception>
    private (long VertexBytes, long DeltaBytes) DeformBytesWith(PackedDeform deform)
    {
        long vertexBytes = deformVertexBytes + (long)deform.Vertices.Length * Unsafe.SizeOf<GpuDeformVertex>();
        long deltaBytesAfter = deltaBytes + (long)deform.Deltas.Length * Unsafe.SizeOf<GpuMorphDelta>();
        if (vertexBytes > deformBytesLimit || deltaBytesAfter > deformBytesLimit)
            throw new InvalidOperationException(
                $"The deform store is full: the deforming meshes need {vertexBytes} bytes of vertices and "
                    + $"{deltaBytesAfter} bytes of morph deltas, and the device's storage buffers hold {deformBytesLimit}."
            );
        return (vertexBytes, deltaBytesAfter);
    }

    /// <summary>
    /// Refuses rigging that does not match its mesh of <paramref name="vertexCount"/> vertices, so that no shader reads
    /// past a palette, a weight row or a store, and no NaN reaches one (docs/plans/skinning-and-morphs.md, "The mesh data").
    /// </summary>
    private static void CheckRigging(MeshRigging rigging, int vertexCount)
    {
        ArgumentNullException.ThrowIfNull(rigging.Rig, "mesh");
        if (rigging.Skin is { } skin)
            CheckSkin(skin, rigging.Rig, vertexCount);
        if (rigging.Morphs.IsDefault)
            throw new ArgumentException("The rigging's morph targets are not set.", "mesh");
        bool[] named = new bool[rigging.Rig.Morphs.Length];
        foreach (MorphTarget target in rigging.Morphs)
        {
            ArgumentNullException.ThrowIfNull(target, "mesh");
            bool inRig = target.Morph >= 0 && target.Morph < named.Length;
            if (!inRig)
                throw new ArgumentException($"A target names the morph {target.Morph}, which the rig lacks.", "mesh");
            if (named[target.Morph])
                throw new ArgumentException($"Two targets name the morph {target.Morph}.", "mesh");
            named[target.Morph] = true;
            CheckDeltas(target.Positions, vertexCount, longest: float.PositiveInfinity);
            // The longest difference of two unit vectors.
            CheckDeltas(target.Normals, vertexCount, longest: 2);
            CheckDeltas(target.Tangents, vertexCount, longest: 2);
        }
    }

    private static void CheckSkin(MeshSkin skin, Rig rig, int vertexCount)
    {
        ArgumentNullException.ThrowIfNull(skin.Vertices, "mesh");
        if (skin.Vertices.Length != vertexCount)
            throw new ArgumentException($"The skin has {skin.Vertices.Length} vertices, not {vertexCount}.", "mesh");
        if (skin.InverseBinds.IsDefault || skin.InverseBinds.Length != rig.Joints.Length)
            throw new ArgumentException($"The skin needs one inverse bind per joint, {rig.Joints.Length}.", "mesh");
        foreach (Pose bind in skin.InverseBinds)
            CheckInverseBind(bind);
        foreach (VertexSkin vertex in skin.Vertices)
            CheckVertexSkin(vertex, rig.Joints.Length);
    }

    private static void CheckInverseBind(Pose bind)
    {
        float lengthSquared = bind.Rotation.LengthSquared();
        // The rule of SceneBatcher.Interpolate: a rotation that the renderer can normalize.
        bool isRotation = float.IsFinite(lengthSquared) && lengthSquared > 1e-12f;
        if (!ShaderInput.IsFinite(bind.Position) || !isRotation)
            throw new ArgumentException($"The inverse bind {bind} is not finite or not a rotation.", "mesh");
    }

    private static void CheckVertexSkin(VertexSkin vertex, int joints)
    {
        bool inRig =
            vertex.Joint0 < joints && vertex.Joint1 < joints && vertex.Joint2 < joints && vertex.Joint3 < joints;
        if (!inRig)
            throw new ArgumentException($"The skin {vertex} names a joint past the rig's {joints}.", "mesh");
        Vector4 w = vertex.Weights;
        bool eachValid = IsWeight(w.X) && IsWeight(w.Y) && IsWeight(w.Z) && IsWeight(w.W);
        // In doubles, as MeshDeform.Pack scales them, so that large weights do not overflow.
        double sum = (double)w.X + w.Y + w.Z + w.W;
        if (!eachValid || sum <= 0)
            throw new ArgumentException(
                $"The skin {vertex} needs finite weights of at least 0, with a sum above 0.",
                "mesh"
            );
    }

    private static bool IsWeight(float weight) => float.IsFinite(weight) && weight >= 0;

    /// <summary>Refuses a delta array that is neither empty nor one per vertex, or a delta that is not finite or longer than <paramref name="longest"/>.</summary>
    private static void CheckDeltas(Vector3[] deltas, int vertexCount, float longest)
    {
        ArgumentNullException.ThrowIfNull(deltas, "mesh");
        if (deltas.Length != 0 && deltas.Length != vertexCount)
            throw new ArgumentException($"A delta array has {deltas.Length} entries, not 0 or {vertexCount}.", "mesh");
        foreach (Vector3 delta in deltas)
            if (!ShaderInput.IsFinite(delta) || delta.LengthSquared() > longest * longest)
                throw new ArgumentException($"The delta {delta} is not finite or longer than {longest}.", "mesh");
    }

    /// <summary>Whether every number of <paramref name="vertex"/> is finite. The shaders read no NaN or infinity (ShaderInput).</summary>
    private static bool IsFinite(in Vertex vertex) =>
        ShaderInput.IsFinite(vertex.Position)
        && ShaderInput.IsFinite(vertex.Normal)
        && ShaderInput.IsFinite(new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z))
        && float.IsFinite(vertex.Tangent.W)
        && float.IsFinite(vertex.TexCoord.X)
        && float.IsFinite(vertex.TexCoord.Y);

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
            CheckCutoff(material.Alpha);
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

    /// <summary>Refuses a cutoff that is not above 0 and at most 1, a NaN included: at 0, every texel would be core.</summary>
    private static void CheckCutoff(AlphaMode alpha)
    {
        if (alpha.Cutoff is not { } cutoff)
            return;
        bool inRange = cutoff is > 0 and <= 1;
        if (!inRange)
            throw new ArgumentOutOfRangeException(
                nameof(MaterialDesc.Alpha),
                alpha,
                "A cutoff lies above 0 and at most 1."
            );
    }

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
        /// <summary>Each mesh with the card number of each of its vertices, <see cref="MeshCards.Number"/>.</summary>
        public readonly List<(MeshId Id, MeshData Data, uint[] Cards)> Meshes = [];
        public readonly List<(TextureId Id, TextureData Data)> Textures = [];
        public readonly List<(MaterialId Id, MaterialDesc Desc)> Materials = [];

        /// <summary>The packed deformation of each entry of <see cref="Meshes"/>, at the same index, or null for a rigid mesh.</summary>
        public readonly List<PackedDeform?> Deforms = [];

        public bool IsEmpty => Meshes.Count == 0 && Textures.Count == 0 && Materials.Count == 0;

        public void Clear()
        {
            Meshes.Clear();
            Textures.Clear();
            Materials.Clear();
            Deforms.Clear();
        }
    }
}
