using System.Numerics;
using System.Text.Json;

namespace Tertius.Assets;

/// <summary>
/// A cooked model file, read into memory: its pieces, its materials and its decoded textures (https://github.com/alexispurslane/tertius/wiki/asset-pipeline,
/// "Models: nodes and pieces"). The file is a glTF binary in engine axes, as the asset converter writes it: meters, +Y up,
/// the front toward -Z. So the reader turns nothing.
/// </summary>
/// <param name="Pieces">The pieces in node order, depth first: every scene root, and every node marked as a piece.</param>
/// <param name="Materials">The file's materials, which <see cref="CookedPart.Material"/> indexes.</param>
/// <param name="Textures">Each image in the color space of its role, which the materials' maps index.</param>
public sealed record CookedModel(
    IReadOnlyList<CookedPiece> Pieces,
    IReadOnlyList<CookedMaterial> Materials,
    IReadOnlyList<TextureData> Textures
)
{
    /// <summary>Reads the cooked file at <paramref name="path"/>, or gives a missing or malformed model.</summary>
    public static Result<CookedModel, ModelProblem> Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new MissingModel(path);
        }

        return Parse(bytes, path);
    }

    /// <summary>Reads a cooked file from <paramref name="glb"/>. <paramref name="path"/> only names it in a problem.</summary>
    public static Result<CookedModel, ModelProblem> Parse(byte[] glb, string path)
    {
        try
        {
            return GlbReader.Read(glb);
        }
        catch (GlbException ex)
        {
            return new MalformedModel(path, ex.Message);
        }
        catch (JsonException ex)
        {
            return new MalformedModel(path, $"the JSON chunk does not parse: {ex.Message}");
        }
    }
}

/// <summary>
/// One piece: a node that renders as one unit, with the geometry of its node and its descendants that are not pieces.
/// </summary>
/// <param name="Name">The node's name, without the prefix PIECE_.</param>
/// <param name="IsRoot">True for a scene root. A marked piece below a root is false, and a whole-file model leaves it out.</param>
/// <param name="Pose">The node's place: in the file's axes for a root, in its parent piece's axes for a marked piece.</param>
/// <param name="Scale">The node's scale along its own axes, with its parents' scales up to the same origin.</param>
/// <param name="Parts">The parts, each placed relative to the piece's origin.</param>
public sealed record CookedPiece(string Name, bool IsRoot, Pose Pose, Vector3 Scale, IReadOnlyList<CookedPart> Parts);

/// <summary>One mesh primitive of a piece, with its material and its node's transform relative to the piece's origin.</summary>
/// <param name="Mesh">The vertices in the node's own axes, with a back copy of each triangle for a double-sided material.</param>
/// <param name="Material">The index in <see cref="CookedModel.Materials"/>, or null for the renderer's default material.</param>
/// <param name="Offset">The node's rigid place relative to the piece's origin.</param>
/// <param name="Scale">The node's scale along its axes, with the scales between it and the piece.</param>
public sealed record CookedPart(MeshData Mesh, int? Material, Pose Offset, Vector3 Scale);

/// <summary>
/// A glTF metallic-roughness material as the engine draws it (https://github.com/alexispurslane/tertius/wiki/asset-pipeline, "Materials"). The maps are
/// indices in <see cref="CookedModel.Textures"/>, which <see cref="Desc"/> turns into the uploaded textures.
/// </summary>
public sealed record CookedMaterial
{
    /// <summary>Linear RGB and alpha. glTF's default is opaque white.</summary>
    public Vector4 BaseColor { get; init; } = Vector4.One;

    /// <summary>glTF's metallic factor, 1 by default.</summary>
    public float Metallic { get; init; } = 1;

    /// <summary>glTF's roughness factor, 1 by default.</summary>
    public float Roughness { get; init; } = 1;

    /// <summary>The emissive factor times KHR_materials_emissive_strength: the radiance in W/(m²·sr).</summary>
    public Vector3 Emissive { get; init; }

    public AlphaMode Alpha { get; init; }

    public int? BaseColorMap { get; init; }

    /// <summary>Roughness in G and metallic in B, glTF's layout, which the engine takes as it is.</summary>
    public int? MetallicRoughnessMap { get; init; }

    /// <summary>A tangent-space normal map with glTF's encoding: green toward +V of the image, OpenGL's convention.</summary>
    public int? NormalMap { get; init; }

    /// <summary>The engine's material, with each map from <paramref name="textures"/>, the uploaded <see cref="CookedModel.Textures"/>.</summary>
    public MaterialDesc Desc(IReadOnlyList<TextureId> textures) =>
        new(
            BaseColor: BaseColor,
            Roughness: Roughness,
            Emissive: Emissive,
            BaseColorMap: Map(BaseColorMap, textures),
            NormalMap: Map(NormalMap, textures),
            MetallicRoughnessMap: Map(MetallicRoughnessMap, textures),
            Model: new StandardShading(Metallic: Metallic),
            Alpha: Alpha
        );

    private static TextureId Map(int? index, IReadOnlyList<TextureId> textures) =>
        index is int i ? textures[i] : TextureId.None;
}
