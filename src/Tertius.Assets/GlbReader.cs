using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using StbImageSharp;

namespace Tertius.Assets;

/// <summary>A cooked file that breaks a rule of the reader. <see cref="CookedModel.Parse"/> turns it into a malformed model.</summary>
internal sealed class GlbException(string message) : Exception(message);

/// <summary>
/// Reads a glTF binary (glTF 2.0, "GLB File Format Specification") into a <see cref="CookedModel"/>. It takes what the
/// asset converter writes: float attributes, triangle lists, embedded PNG or JPEG images, and the materials of
/// <see cref="CookedMaterial"/>. Anything else is a <see cref="GlbException"/> with the reason.
/// </summary>
internal static class GlbReader
{
    private const uint Magic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinChunk = 0x004E4942;
    private const int FloatComponent = 5126;
    private const int TrianglesMode = 4;
    private const string PiecePrefix = "PIECE_";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static CookedModel Read(byte[] glb)
    {
        (byte[] json, ReadOnlyMemory<byte> bin) = Chunks(glb);
        Gltf gltf =
            JsonSerializer.Deserialize<Gltf>(json, JsonOptions) ?? throw new GlbException("the JSON chunk is null");
        var file = new FileReader(gltf, bin);
        return file.Model();
    }

    private static (byte[] Json, ReadOnlyMemory<byte> Bin) Chunks(byte[] glb)
    {
        bool isGlb = glb.Length >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(glb) == Magic;
        if (!isGlb)
            throw new GlbException("the file is not a glTF binary: it does not start with the magic glTF");
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8));
        if (declared != glb.Length)
            throw new GlbException($"the header gives {declared} bytes, and the file has {glb.Length}");
        byte[]? json = null;
        ReadOnlyMemory<byte> bin = ReadOnlyMemory<byte>.Empty;
        int at = 12;
        while (at + 8 <= glb.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(at)));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(at + 4));
            if (at + 8 + length > glb.Length)
                throw new GlbException($"the chunk at byte {at} runs past the end of the file");
            if (type == JsonChunk)
                json = glb.AsSpan(at + 8, length).ToArray();
            else if (type == BinChunk)
                bin = glb.AsMemory(at + 8, length);
            at += 8 + length;
        }

        return (json ?? throw new GlbException("the file has no JSON chunk"), bin);
    }

    /// <summary>One file's JSON and binary chunk, and the textures that its materials have decoded so far.</summary>
    private sealed class FileReader(Gltf gltf, ReadOnlyMemory<byte> bin)
    {
        private sealed record SkinBinding(Rig Rig, Pose[] Binds, int[] Remap);

        private readonly Dictionary<int, SkinBinding> skins = [];
        private readonly List<TextureData> textures = [];
        private readonly Dictionary<(int Image, bool Srgb), int> textureIndices = [];
        private readonly List<CookedPiece> pieces = [];

        public CookedModel Model()
        {
            CookedMaterial[] materials = [.. (gltf.Materials ?? []).Select(Cooked)];
            int scene = gltf.Scene ?? 0;
            int[] roots = gltf.Scenes is { Length: > 0 } scenes ? scenes[scene].Nodes ?? [] : [];
            foreach (int root in roots)
            {
                GltfNode node = Node(root);
                Piece(index: root, isRoot: true, pose: node.LocalPose(), scale: node.LocalScale());
            }

            return new(Pieces: pieces, Materials: materials, Textures: textures);
        }

        /// <summary>Adds the piece of node <paramref name="index"/> and, after it, the pieces marked below it.</summary>
        private void Piece(int index, bool isRoot, Pose pose, Vector3 scale)
        {
            GltfNode node = Node(index);
            var parts = new List<CookedPart>();
            var marked = new List<(int Node, Pose Pose, Vector3 Scale)>();
            Collect(index: index, offset: Pose.Identity, scale: Vector3.One, parts: parts, marked: marked);
            pieces.Add(new(Name: PieceName(node), IsRoot: isRoot, Pose: pose, Scale: scale, Parts: parts));
            foreach ((int child, Pose childPose, Vector3 childScale) in marked)
                Piece(index: child, isRoot: false, pose: childPose, scale: childScale);
        }

        /// <summary>
        /// Adds the parts of node <paramref name="index"/> at <paramref name="offset"/> and <paramref name="scale"/> from
        /// the piece's origin, and walks its children. A marked child starts a piece of its own.
        /// </summary>
        private void Collect(
            int index,
            Pose offset,
            Vector3 scale,
            List<CookedPart> parts,
            List<(int Node, Pose Pose, Vector3 Scale)> marked
        )
        {
            GltfNode node = Node(index);
            if (node.Mesh is int mesh)
                foreach (GltfPrimitive primitive in Mesh(mesh).Primitives ?? [])
                    parts.Add(Part(primitive: primitive, offset: offset, scale: scale, skin: node.Skin));
            foreach (int child in node.Children ?? [])
            {
                GltfNode childNode = Node(child);
                // The child's transform in the piece's axes: the parent's scale stretches the child's translation.
                var childOffset = Pose.Compose(
                    offset,
                    new(scale * childNode.LocalTranslation(), childNode.LocalRotation())
                );
                Vector3 childScale = scale * childNode.LocalScale();
                if (IsMarked(childNode))
                    marked.Add((child, childOffset, childScale));
                else
                    Collect(index: child, offset: childOffset, scale: childScale, parts: parts, marked: marked);
            }
        }

        private static bool IsMarked(GltfNode node) =>
            (node.Name?.StartsWith(PiecePrefix, StringComparison.Ordinal) ?? false)
            || (
                node.Extras is { ValueKind: JsonValueKind.Object } extras
                && extras.TryGetProperty("tertius_piece", out JsonElement mark)
                && mark.ValueKind == JsonValueKind.Number
                && mark.GetDouble() == 1
            );

        private static string PieceName(GltfNode node)
        {
            string name = node.Name ?? "";
            return name.StartsWith(PiecePrefix, StringComparison.Ordinal) ? name[PiecePrefix.Length..] : name;
        }

        private CookedPart Part(GltfPrimitive primitive, Pose offset, Vector3 scale, int? skin)
        {
            if ((primitive.Mode ?? TrianglesMode) != TrianglesMode)
                throw new GlbException(
                    $"a primitive has mode {primitive.Mode}, and the reader takes triangle lists only"
                );
            Vertex[] vertices = Vertices(primitive.Attributes ?? []);
            uint[] indices = TriangleIndices(primitive, vertices.Length);
            bool doubleSided = primitive.Material is int m && MaterialAt(m).DoubleSided;
            MeshData mesh = doubleSided ? WithBackFaces(vertices, indices) : new(vertices, indices);
            if (skin is int index)
                mesh = mesh with
                {
                    Rigging = Rigging(
                        index: index,
                        attributes: primitive.Attributes ?? [],
                        count: vertices.Length,
                        doubleSided: doubleSided
                    ),
                };
            return new(Mesh: mesh, Material: primitive.Material, Offset: offset, Scale: scale);
        }

        private MeshRigging Rigging(int index, Dictionary<string, int> attributes, int count, bool doubleSided)
        {
            if (!skins.TryGetValue(index, out SkinBinding? skin))
            {
                skin = ReadSkin(At(gltf.Skins, index, "skin"));
                skins.Add(index, skin);
            }
            if (
                !attributes.TryGetValue("JOINTS_0", out int joint)
                || !attributes.TryGetValue("WEIGHTS_0", out int weight)
            )
                throw new GlbException("a skinned primitive needs JOINTS_0 and WEIGHTS_0");
            GltfAccessor accessor = At(gltf.Accessors, joint, "joint accessor");
            float[] weights = Floats(weight, 4);
            if (weights.Length != count * 4 || accessor.Count != count || accessor.Type != "VEC4")
                throw new GlbException("skin attributes must hold four influences per vertex");
            int size = accessor.ComponentType switch
            {
                5121 => 1,
                5123 => 2,
                _ => throw new GlbException("joint indices must be unsigned bytes or shorts"),
            };
            ReadOnlySpan<byte> data = Elements(
                index: joint,
                accessor: accessor,
                elementSize: 4 * size,
                stride: out int stride
            );
            var vertices = new VertexSkin[doubleSided ? 2 * count : count];
            Span<ushort> ids = stackalloc ushort[4];
            for (int i = 0; i < count; i++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int start = i * stride + c * size;
                    ids[c] = size == 1 ? data[start] : BinaryPrimitives.ReadUInt16LittleEndian(data[start..]);
                    if (ids[c] >= skin.Rig.Joints.Length)
                        throw new GlbException("vertex joint index lies outside the skin");
                    ids[c] = (ushort)skin.Remap[ids[c]];
                }
                Vector4 influence = new(weights.AsSpan(4 * i, 4));
                if (
                    !Vector4.GreaterThanOrEqualAll(influence, Vector4.Zero)
                    || !float.IsFinite(Vector4.Dot(influence, Vector4.One))
                    || influence == Vector4.Zero
                )
                    throw new GlbException("a skinned vertex needs finite nonnegative weights with a positive sum");
                vertices[i] = new(Joint0: ids[0], Joint1: ids[1], Joint2: ids[2], Joint3: ids[3], Weights: influence);
            }
            if (doubleSided)
                vertices.AsSpan(0, count).CopyTo(vertices.AsSpan(count));
            return new(skin.Rig, new(vertices, [.. skin.Binds]), []);
        }

        private SkinBinding ReadSkin(GltfSkin skin)
        {
            if (skin.Joints is not { Length: > 0 and <= Rig.MaxJoints } nodes)
                throw new GlbException("a cooked skin needs one to 65,535 joints");
            var indices = new Dictionary<int, int>();
            for (int i = 0; i < nodes.Length; i++)
                if (!indices.TryAdd(nodes[i], i))
                    throw new GlbException("a skin repeats a joint");
            int[] parents = [.. Enumerable.Repeat(-1, nodes.Length)];
            for (int i = 0; i < nodes.Length; i++)
                foreach (int child in Node(nodes[i]).Children ?? [])
                    if (indices.TryGetValue(child, out int j))
                        parents[j] = i;
            var joints = new List<RigJoint>(nodes.Length);
            var inverse = new Pose[nodes.Length];
            int[] remap = [.. Enumerable.Repeat(-1, nodes.Length)];
            float[]? matrices = skin.InverseBindMatrices is int bind ? Floats(bind, 16) : null;
            if (matrices is not null && matrices.Length != 16 * nodes.Length)
                throw new GlbException("a skin needs one inverse bind per joint");
            for (int i = 0; i < nodes.Length; i++)
                Visit(i);
            try
            {
                return new(new Rig([.. joints], []), inverse, remap);
            }
            catch (ArgumentException e)
            {
                throw new GlbException(e.Message);
            }

            void Visit(int i)
            {
                if (remap[i] >= 0)
                    return;
                if (remap[i] == -2)
                    throw new GlbException("a skin contains a parent cycle");
                remap[i] = -2;
                int parent = parents[i];
                if (parent >= 0)
                    Visit(parent);
                int index = joints.Count;
                GltfNode node = Node(nodes[i]);
                if (Vector3.DistanceSquared(node.LocalScale(), Vector3.One) > 1e-8f)
                    throw new GlbException("a cooked joint must have unit scale");
                joints.Add(new(node.Name ?? $"joint-{i}", parent < 0 ? null : remap[parent], node.LocalPose()));
                inverse[index] = matrices is null ? Pose.Identity : Bind(matrices.AsSpan(16 * i, 16));
                remap[i] = index;
            }
        }

        private static Pose Bind(ReadOnlySpan<float> m)
        {
            Matrix4x4 matrix = MemoryMarshal.Read<Matrix4x4>(MemoryMarshal.AsBytes(m));
            if (
                !Matrix4x4.Decompose(
                    matrix: matrix,
                    scale: out Vector3 scale,
                    rotation: out Quaternion rotation,
                    translation: out Vector3 position
                )
                || Vector3.DistanceSquared(scale, Vector3.One) > 1e-8f
            )
                throw new GlbException("a cooked joint must have a rigid inverse bind");
            return new(position, rotation);
        }

        /// <summary>
        /// The vertices of a primitive's <paramref name="attributes"/>: POSITION and NORMAL, which it needs, and TANGENT
        /// and TEXCOORD_0, which are zero when the primitive has none.
        /// </summary>
        private Vertex[] Vertices(Dictionary<string, int> attributes)
        {
            if (!attributes.TryGetValue("POSITION", out int positionAccessor))
                throw new GlbException("a primitive has no POSITION attribute");
            if (!attributes.TryGetValue("NORMAL", out int normalAccessor))
                throw new GlbException("a primitive has no NORMAL attribute");
            float[] positions = Floats(positionAccessor, components: 3);
            float[] normals = Floats(normalAccessor, components: 3);
            int count = positions.Length / 3;
            float[] tangents = attributes.TryGetValue("TANGENT", out int t)
                ? Floats(t, components: 4)
                : new float[4 * count];
            float[] uvs = attributes.TryGetValue("TEXCOORD_0", out int u)
                ? Floats(u, components: 2)
                : new float[2 * count];
            if (normals.Length / 3 != count || tangents.Length / 4 != count || uvs.Length / 2 != count)
                throw new GlbException("the attributes of a primitive have different vertex counts");

            var vertices = new Vertex[count];
            for (int i = 0; i < count; i++)
                vertices[i] = new()
                {
                    Position = new(positions[3 * i], positions[3 * i + 1], positions[3 * i + 2]),
                    Normal = new(normals[3 * i], normals[3 * i + 1], normals[3 * i + 2]),
                    Tangent = new(
                        x: tangents[4 * i],
                        y: tangents[4 * i + 1],
                        z: tangents[4 * i + 2],
                        w: tangents[4 * i + 3]
                    ),
                    TexCoord = new(uvs[2 * i], uvs[2 * i + 1]),
                };
            return vertices;
        }

        /// <summary>
        /// The triangle list of <paramref name="primitive"/>, each index below <paramref name="count"/>: its index accessor,
        /// or the vertices in order when it has none.
        /// </summary>
        private uint[] TriangleIndices(GltfPrimitive primitive, int count)
        {
            uint[] indices = primitive.Indices is int accessor
                ? Indices(accessor)
                : [.. Enumerable.Range(0, count).Select(i => (uint)i)];
            if (indices.Length % 3 != 0)
                throw new GlbException($"a triangle list has {indices.Length} indices, not a multiple of 3");
            foreach (uint index in indices)
                if (index >= count)
                    throw new GlbException($"the index {index} lies past the {count} vertices of its primitive");
            return indices;
        }

        /// <summary>
        /// The mesh with a back copy of each triangle: reversed winding, the normal turned and the tangent's sign turned,
        /// so that the bitangent keeps its direction (docs/plans/asset-pipeline.md, "Meshes", step 6).
        /// </summary>
        private static MeshData WithBackFaces(Vertex[] vertices, uint[] indices)
        {
            var both = new Vertex[vertices.Length * 2];
            vertices.CopyTo(both, 0);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vertex v = vertices[i];
                v.Normal = -v.Normal;
                v.Tangent.W = -v.Tangent.W;
                both[vertices.Length + i] = v;
            }

            uint[] allIndices = new uint[indices.Length * 2];
            indices.CopyTo(allIndices, 0);
            uint back = (uint)vertices.Length;
            for (int i = 0; i < indices.Length; i += 3)
            {
                allIndices[indices.Length + i] = indices[i] + back;
                allIndices[indices.Length + i + 1] = indices[i + 2] + back;
                allIndices[indices.Length + i + 2] = indices[i + 1] + back;
            }

            return new(both, allIndices);
        }

        private CookedMaterial Cooked(GltfMaterial material)
        {
            GltfPbr pbr = material.PbrMetallicRoughness ?? new();
            float[] baseColor = pbr.BaseColorFactor ?? [1, 1, 1, 1];
            float[] emissive = material.EmissiveFactor ?? [0, 0, 0];
            float strength = EmissiveStrength(material);
            return new()
            {
                BaseColor = new(x: baseColor[0], y: baseColor[1], z: baseColor[2], w: baseColor[3]),
                Metallic = pbr.MetallicFactor ?? 1,
                Roughness = pbr.RoughnessFactor ?? 1,
                Emissive = new Vector3(emissive[0], emissive[1], emissive[2]) * strength,
                Alpha = material.AlphaMode switch
                {
                    "MASK" => new CutoutAlpha(material.AlphaCutoff ?? 0.5f),
                    "BLEND" => new GlassAlpha(),
                    _ => new OpaqueAlpha(),
                },
                BaseColorMap = Texture(pbr.BaseColorTexture, srgb: true),
                MetallicRoughnessMap = Texture(pbr.MetallicRoughnessTexture, srgb: false),
                NormalMap = Texture(material.NormalTexture, srgb: false),
            };
        }

        private static float EmissiveStrength(GltfMaterial material)
        {
            if (material.Extensions?.GetValueOrDefault("KHR_materials_emissive_strength") is not { } extension)
                return 1;
            return extension.TryGetProperty("emissiveStrength", out JsonElement value) ? value.GetSingle() : 1;
        }

        /// <summary>The index in the model's textures of a material's map, decoding its image on its first use.</summary>
        private int? Texture(GltfTextureRef? reference, bool srgb)
        {
            if (reference is null)
                return null;
            GltfTexture texture = At(gltf.Textures, reference.Index, "texture");
            int image = texture.Source ?? throw new GlbException($"texture {reference.Index} has no image");
            if (textureIndices.TryGetValue((image, srgb), out int known))
                return known;
            textures.Add(Decode(image, srgb));
            textureIndices[(image, srgb)] = textures.Count - 1;
            return textures.Count - 1;
        }

        private TextureData Decode(int index, bool srgb)
        {
            GltfImage image = At(gltf.Images, index, "image");
            int view = image.BufferView ?? throw new GlbException($"image {index} is not embedded in the file");
            byte[] bytes = View(view).ToArray();
            ImageResult result;
            try
            {
                result = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            }
#pragma warning disable CA1031 // StbImageSharp throws the base Exception for every file that it cannot decode.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                throw new GlbException($"image {index} does not decode: {ex.Message}");
            }

            return new(Width: result.Width, Height: result.Height, Rgba: result.Data, Srgb: srgb);
        }

        private float[] Floats(int index, int components)
        {
            GltfAccessor accessor = At(gltf.Accessors, index, "accessor");
            if (accessor.ComponentType != FloatComponent)
                throw new GlbException($"accessor {index} has component type {accessor.ComponentType}, not float");
            if (Components(accessor.Type) != components)
                throw new GlbException($"accessor {index} has type {accessor.Type}, not {components} components");
            float[] values = new float[accessor.Count * components];
            ReadOnlySpan<byte> data = Elements(
                index: index,
                accessor: accessor,
                elementSize: 4 * components,
                stride: out int stride
            );
            for (int i = 0; i < accessor.Count; i++)
            for (int c = 0; c < components; c++)
                values[i * components + c] = BinaryPrimitives.ReadSingleLittleEndian(data[(i * stride + 4 * c)..]);
            return values;
        }

        private uint[] Indices(int index)
        {
            GltfAccessor accessor = At(gltf.Accessors, index, "accessor");
            int size = accessor.ComponentType switch
            {
                5121 => 1,
                5123 => 2,
                5125 => 4,
                _ => throw new GlbException($"accessor {index} has index component type {accessor.ComponentType}"),
            };
            ReadOnlySpan<byte> data = Elements(
                index: index,
                accessor: accessor,
                elementSize: size,
                stride: out int stride
            );
            uint[] values = new uint[accessor.Count];
            for (int i = 0; i < accessor.Count; i++)
            {
                ReadOnlySpan<byte> at = data[(i * stride)..];
                values[i] = size switch
                {
                    1 => at[0],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(at),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(at),
                };
            }

            return values;
        }

        /// <summary>The bytes of an accessor's elements, after a check that every element lies inside its buffer view.</summary>
        private ReadOnlySpan<byte> Elements(int index, GltfAccessor accessor, int elementSize, out int stride)
        {
            int viewIndex = accessor.BufferView ?? throw new GlbException($"accessor {index} has no buffer view");
            GltfBufferView view = At(gltf.BufferViews, viewIndex, "buffer view");
            stride = view.ByteStride ?? elementSize;
            long end = accessor.ByteOffset + (long)stride * Math.Max(0, accessor.Count - 1) + elementSize;
            if (accessor.Count > 0 && end > view.ByteLength)
                throw new GlbException(
                    $"accessor {index} needs {end} bytes of buffer view {viewIndex}, which has {view.ByteLength}"
                );
            return View(viewIndex).Span[accessor.ByteOffset..];
        }

        private ReadOnlyMemory<byte> View(int index)
        {
            GltfBufferView view = At(gltf.BufferViews, index, "buffer view");
            if (view.Buffer != 0 || view.ByteOffset + (long)view.ByteLength > bin.Length)
                throw new GlbException($"buffer view {index} lies outside the binary chunk");
            return bin.Slice(view.ByteOffset, view.ByteLength);
        }

        private static int Components(string? type) =>
            type switch
            {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                "MAT4" => 16,
                _ => 0,
            };

        private GltfNode Node(int index) => At(gltf.Nodes, index, "node");

        private GltfMesh Mesh(int index) => At(gltf.Meshes, index, "mesh");

        private GltfMaterial MaterialAt(int index) => At(gltf.Materials, index, "material");

        private static T At<T>(T[]? list, int index, string what) =>
            list is not null && index >= 0 && index < list.Length
                ? list[index]
                : throw new GlbException($"{what} {index} does not exist");
    }
}
