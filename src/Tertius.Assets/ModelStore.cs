using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace Tertius.Assets;

/// <summary>
/// The renderer models of one render-resource owner, loaded from cooked model files (docs/plans/asset-pipeline.md,
/// "Models: nodes and pieces"). <see cref="Load"/> makes each reference into one <see cref="ModelId"/> once and gives the
/// same <see cref="StoredModel"/> on every later call, so every placement of a model shares its meshes, materials and
/// textures. A store's handles belong to its <see cref="IRenderResources"/>: a second owner needs a store of its own.
/// The store is not thread-safe. Call it where the owner creates its resources.
/// </summary>
/// <param name="resources">The owner of every handle that the store makes.</param>
/// <param name="content">The content root that the references resolve against.</param>
public sealed class ModelStore(IRenderResources resources, ContentFolders content)
{
    private readonly Dictionary<AssetReference, StoredModel> models = [];
    private readonly Dictionary<string, UploadedFile> files = [with(StringComparer.Ordinal)];
    private readonly Dictionary<MeshData, MeshId> meshes = [with(ReferenceEqualityComparer.Instance)];

    /// <summary>
    /// The model that <paramref name="reference"/> names, uploaded on the first call. A reference with a piece names that
    /// piece: a scene root or a marked node, with its parts placed from the piece's origin at the piece's scale. A
    /// reference without a piece names every root piece of the file, each at its root's pose. Gives
    /// <see cref="MissingModel"/> when the cooked file is absent, <see cref="MalformedModel"/> when it does not read, and
    /// <see cref="MissingPiece"/> when the file has no such piece. A problem is not kept, so a later call reads the file
    /// again: a setup run in between makes the model load.
    /// </summary>
    public Result<StoredModel, ModelProblem> Load(AssetReference reference)
    {
        if (models.TryGetValue(reference, out StoredModel? stored))
            return stored;

        string path = content.CookedModel(reference);
        return File(path) switch
        {
            ModelProblem problem => problem,
            UploadedFile file => Parts(file: file, piece: reference.Piece, path: path) switch
            {
                ModelProblem problem => problem,
                ImmutableArray<PlacedPart> parts => Keep(reference, parts, path),
                null => throw new UnreachableException("Parts gives parts or a problem."),
            },
            null => throw new UnreachableException("File gives a file or a problem."),
        };
    }

    private Result<StoredModel, ModelProblem> Keep(
        AssetReference reference,
        ImmutableArray<PlacedPart> parts,
        string path
    )
    {
        Rig? rig = null;
        foreach (PlacedPart part in parts)
        {
            if (part.Mesh.Rigging is not { } rigging)
                continue;
            if (rig is not null && rig != rigging.Rig)
                return new MalformedModel(path, "the meshes of one posed model must share a rig");
            rig = rigging.Rig;
        }
        StoredModel model = Upload(parts) with { Rig = rig };
        models.Add(reference, model);
        return model;
    }

    /// <summary>The file at <paramref name="path"/> with its textures and materials uploaded, read on the first call.</summary>
    private Result<UploadedFile, ModelProblem> File(string path)
    {
        if (files.TryGetValue(path, out UploadedFile? uploaded))
            return uploaded;

        return CookedModel.Load(path) switch
        {
            ModelProblem problem => problem,
            CookedModel model => Upload(path, model),
            null => throw new UnreachableException("CookedModel.Load gives a model or a problem."),
        };
    }

    /// <summary>Uploads the textures and materials of <paramref name="model"/>, the file at <paramref name="path"/>.</summary>
    private UploadedFile Upload(string path, CookedModel model)
    {
        TextureId[] textures = [.. model.Textures.Select(resources.CreateTexture)];
        MaterialId[] materials = [.. model.Materials.Select(m => resources.CreateMaterial(m.Desc(textures)))];
        var uploaded = new UploadedFile(Pieces: model.Pieces, Materials: materials);
        files.Add(path, uploaded);
        return uploaded;
    }

    /// <summary>
    /// The parts of the piece <paramref name="piece"/>, or of every root piece when it is null, each placed relative to
    /// the model's origin.
    /// </summary>
    private static Result<ImmutableArray<PlacedPart>, ModelProblem> Parts(UploadedFile file, string? piece, string path)
    {
        if (piece is null)
            return WholeFile(file);
        if (file.Pieces.FirstOrDefault(p => p.Name == piece) is not CookedPiece named)
            return new MissingPiece(Path: path, Piece: piece, Pieces: [.. file.Pieces.Select(p => p.Name)]);
        return Piece(named, file);
    }

    /// <summary>The parts of every root piece of <paramref name="file"/>, each piece at its root's pose and scale.</summary>
    private static ImmutableArray<PlacedPart> WholeFile(UploadedFile file) =>
        [
            .. file
                .Pieces.Where(p => p.IsRoot)
                .SelectMany(p => p.Parts.Select(part => Place(part: part, pose: p.Pose, scale: p.Scale, file: file))),
        ];

    /// <summary>The parts of <paramref name="piece"/> from its own origin, at its scale.</summary>
    private static ImmutableArray<PlacedPart> Piece(CookedPiece piece, UploadedFile file) =>
        [.. piece.Parts.Select(part => Place(part: part, pose: Pose.Identity, scale: piece.Scale, file: file))];

    /// <summary>
    /// <paramref name="part"/> of a piece whose node sits at <paramref name="pose"/> with <paramref name="scale"/>: the
    /// piece's scale stretches the part's offset and multiplies its scale, and the pose then places both.
    /// </summary>
    private static PlacedPart Place(CookedPart part, Pose pose, Vector3 scale, UploadedFile file) =>
        new(
            Mesh: part.Mesh,
            Material: part.Material is int index ? file.Materials[index] : MaterialId.None,
            Offset: Pose.Compose(pose, new(scale * part.Offset.Position, part.Offset.Rotation)),
            Scale: scale * part.Scale
        );

    /// <summary>Uploads each part's mesh, unless an earlier model uploaded it, and then the model of the parts.</summary>
    private StoredModel Upload(ImmutableArray<PlacedPart> parts)
    {
        ImmutableArray<ModelPart>.Builder modelParts = ImmutableArray.CreateBuilder<ModelPart>(parts.Length);
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        float modelRadius = 0;
        foreach (PlacedPart part in parts)
        {
            float partRadius = 0;
            foreach (Vertex vertex in part.Mesh.Vertices)
            {
                Vector3 local = part.Scale * vertex.Position;
                Vector3 placed = part.Offset.TransformPoint(local);
                partRadius = MathF.Max(partRadius, local.Length());
                modelRadius = MathF.Max(modelRadius, placed.Length());
                min = Vector3.Min(min, placed);
                max = Vector3.Max(max, placed);
            }

            modelParts.Add(
                new(
                    Mesh: Mesh(part.Mesh),
                    Material: part.Material,
                    Offset: part.Offset,
                    Scale: part.Scale,
                    BoundingRadius: partRadius
                )
            );
        }

        // A model without vertices has empty bounds at its origin.
        BoundingBox bounds = min.X <= max.X ? new(min, max) : new(Vector3.Zero, Vector3.Zero);
        ModelId id = resources.CreateModel(new ModelDesc(modelParts.MoveToImmutable()));
        return new(Id: id, Bounds: bounds, BoundingRadius: modelRadius);
    }

    private MeshId Mesh(MeshData mesh)
    {
        if (!meshes.TryGetValue(mesh, out MeshId id))
        {
            id = resources.CreateMesh(mesh);
            meshes.Add(mesh, id);
        }

        return id;
    }

    /// <summary>A read file: its pieces, and its materials as uploaded, in the order of <see cref="CookedModel.Materials"/>.</summary>
    private sealed record UploadedFile(IReadOnlyList<CookedPiece> Pieces, MaterialId[] Materials);

    /// <summary>A part placed relative to the model's origin, before its mesh is uploaded.</summary>
    private readonly record struct PlacedPart(MeshData Mesh, MaterialId Material, Pose Offset, Vector3 Scale);
}

/// <summary>A model that a <see cref="ModelStore"/> uploaded, and its size.</summary>
/// <param name="Id">The renderer model, for <c>RenderData.Model</c>.</param>
/// <param name="Bounds">The box that holds every part where its offset and scale place it, in the model's axes, in m.</param>
/// <param name="BoundingRadius">
/// The radius about the model's origin that holds every part, in m, for <c>RenderData.BoundingRadius</c> times the
/// entity's largest scale.
/// </param>
public sealed record StoredModel(ModelId Id, BoundingBox Bounds, float BoundingRadius)
{
    public Rig? Rig { get; init; }
}
