using System.Numerics;
using System.Text.Json;

namespace Tertius.Assets;

// The parts of glTF 2.0's JSON that the reader takes. System.Text.Json fills them with case-insensitive names, and a
// member that the file leaves out stays null, so the reader applies glTF's defaults.

internal sealed record Gltf(
    int? Scene,
    GltfScene[]? Scenes,
    GltfNode[]? Nodes,
    GltfMesh[]? Meshes,
    GltfSkin[]? Skins,
    GltfAccessor[]? Accessors,
    GltfBufferView[]? BufferViews,
    GltfMaterial[]? Materials,
    GltfTexture[]? Textures,
    GltfImage[]? Images
);

internal sealed record GltfSkin(int[] Joints, int? InverseBindMatrices);

internal sealed record GltfScene(int[]? Nodes);

internal sealed record GltfNode(
    string? Name,
    int? Mesh,
    int[]? Children,
    int? Skin,
    float[]? Translation,
    float[]? Rotation,
    float[]? Scale,
    float[]? Matrix,
    JsonElement? Extras
)
{
    /// <summary>The node's translation: its matrix's, or its translation, or none.</summary>
    public Vector3 LocalTranslation() => Decomposed() is { } d ? d.Translation : Vector(Translation, Vector3.Zero);

    /// <summary>The node's rotation: its matrix's, or its rotation in glTF's order x, y, z, w, or none.</summary>
    public Quaternion LocalRotation()
    {
        if (Decomposed() is { } d)
            return d.Rotation;
        return Rotation is [var x, var y, var z, var w]
            ? Quaternion.Normalize(new(x: x, y: y, z: z, w: w))
            : Quaternion.Identity;
    }

    public Vector3 LocalScale() => Decomposed() is { } d ? d.Scale : Vector(Scale, Vector3.One);

    /// <summary>The node's rigid place in its parent's axes.</summary>
    public Pose LocalPose() => new(LocalTranslation(), LocalRotation());

    private static Vector3 Vector(float[]? values, Vector3 fallback) =>
        values is [var x, var y, var z] ? new(x, y, z) : fallback;

    /// <summary>
    /// The parts of a node's matrix. glTF stores it column by column for column vectors, which is the element order of
    /// System.Numerics' row-vector matrix.
    /// </summary>
    private (Vector3 Translation, Quaternion Rotation, Vector3 Scale)? Decomposed()
    {
        if (Matrix is not { Length: 16 } m)
            return null;
        var matrix = new Matrix4x4(
            m11: m[0],
            m12: m[1],
            m13: m[2],
            m14: m[3],
            m21: m[4],
            m22: m[5],
            m23: m[6],
            m24: m[7],
            m31: m[8],
            m32: m[9],
            m33: m[10],
            m34: m[11],
            m41: m[12],
            m42: m[13],
            m43: m[14],
            m44: m[15]
        );
        if (
            !Matrix4x4.Decompose(
                matrix: matrix,
                scale: out Vector3 scale,
                rotation: out Quaternion rotation,
                translation: out Vector3 translation
            )
        )
            throw new GlbException($"the matrix of node {Name} has shear or no inverse");
        return (translation, rotation, scale);
    }
}

internal sealed record GltfMesh(GltfPrimitive[]? Primitives);

internal sealed record GltfPrimitive(Dictionary<string, int>? Attributes, int? Indices, int? Material, int? Mode);

internal sealed record GltfAccessor(int? BufferView, int ByteOffset, int ComponentType, int Count, string? Type);

internal sealed record GltfBufferView(int Buffer, int ByteOffset, int ByteLength, int? ByteStride);

internal sealed record GltfMaterial(
    GltfPbr? PbrMetallicRoughness,
    GltfTextureRef? NormalTexture,
    float[]? EmissiveFactor,
    string? AlphaMode,
    float? AlphaCutoff,
    bool DoubleSided,
    Dictionary<string, JsonElement>? Extensions
);

internal sealed record GltfPbr(
    float[]? BaseColorFactor = null,
    float? MetallicFactor = null,
    float? RoughnessFactor = null,
    GltfTextureRef? BaseColorTexture = null,
    GltfTextureRef? MetallicRoughnessTexture = null
);

internal sealed record GltfTextureRef(int Index);

internal sealed record GltfTexture(int? Source);

internal sealed record GltfImage(int? BufferView, string? MimeType);
