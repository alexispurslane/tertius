using System.Numerics;
using System.Text.Json.Nodes;

namespace Tertius.Assets.Tests;

/// <summary>
/// The model store (<see cref="ModelStore"/>): it loads a reference once into meshes, materials, textures and one
/// <see cref="ModelId"/> of its resource owner, shares every handle among later loads, places each part by its node, and
/// gives a missing or broken file as a result. The models are tiny .glb files in a temporary content root.
/// </summary>
public sealed class ModelStoreTests
{
    private readonly string root = Directory.CreateTempSubdirectory("tertius-assets-").FullName;

    private ContentFolders Content => new(root);

    private void Write(string reference, GlbBuilder glb)
    {
        string path = Content.CookedModel(AssetReference.Parse(reference));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, glb.Build());
    }

    private static StoredModel Loaded(ModelStore store, string reference) =>
        store.Load(AssetReference.Parse(reference)).Ok!;

    /// <summary>A ship: a hull of two materials, paint and glass, and a fin 2 m up, turned a quarter about +Y, doubled.</summary>
    private static GlbBuilder Ship()
    {
        var glb = new GlbBuilder();
        int paint = glb.Material(
            new JsonObject
            {
                ["pbrMetallicRoughness"] = new JsonObject { ["baseColorFactor"] = new JsonArray(0.8f, 0.8f, 0.8f, 1f) },
            }
        );
        int glass = glb.Material(new JsonObject { ["alphaMode"] = "BLEND" });
        int fin = glb.Node(
            new JsonObject
            {
                ["name"] = "fin",
                ["translation"] = new JsonArray(0f, 2f, 0f),
                ["rotation"] = new JsonArray(0f, 0.70710677f, 0f, 0.70710677f),
                ["scale"] = new JsonArray(2f, 2f, 2f),
            },
            glb.Mesh(glb.Triangle(paint))
        );
        glb.Root(
            new JsonObject { ["name"] = "hull", ["children"] = new JsonArray(fin) },
            glb.Mesh(glb.Triangle(paint), glb.Triangle(glass))
        );
        return glb;
    }

    /// <summary>A crate: one triangle with a base color map and a normal map.</summary>
    private static GlbBuilder Crate()
    {
        var glb = new GlbBuilder();
        int color = glb.Texture(GlbBuilder.Png([150, 110, 60, 255], width: 1, height: 1));
        int normal = glb.Texture(GlbBuilder.Png([128, 128, 255, 255], width: 1, height: 1));
        int material = glb.Material(
            new JsonObject
            {
                ["normalTexture"] = new JsonObject { ["index"] = normal },
                ["pbrMetallicRoughness"] = new JsonObject
                {
                    ["baseColorTexture"] = new JsonObject { ["index"] = color },
                    ["metallicFactor"] = 0f,
                },
            }
        );
        glb.Root(new JsonObject { ["name"] = "crate" }, glb.Mesh(glb.Triangle(material)));
        return glb;
    }

    [Fact]
    public void AShipBecomesOneModelWithAPartForEachPrimitiveAndItsOwnMaterial()
    {
        Write("pack:ships/ship.model", Ship());
        var resources = new CountingResources();

        StoredModel ship = Loaded(new ModelStore(resources, Content), "pack:ships/ship.model");

        ModelDesc desc = resources.Models.Single();
        desc.Parts.Should().HaveCount(3);
        desc.Parts.Select(p => p.Material).Distinct().Should().HaveCount(2, "the hull's paint and glass are two materials");
        resources.Materials[desc.Parts[1].Material.Value].Alpha.Should().Be((AlphaMode)new GlassAlpha());
        ship.Id.Should().Be(new ModelId(0));
    }

    [Fact]
    public void APartsOffsetAndScaleComeFromItsNode()
    {
        Write("pack:ships/ship.model", Ship());
        var resources = new CountingResources();

        Loaded(new ModelStore(resources, Content), "pack:ships/ship.model");

        ModelPart hull = resources.Models.Single().Parts[0];
        ModelPart fin = resources.Models.Single().Parts[2];
        (hull.Offset, hull.Scale).Should().Be((Pose.Identity, Vector3.One));
        fin.Offset.Position.Should().Be(new Vector3(0, 2, 0));
        fin.Scale.Should().Be(new Vector3(2, 2, 2));
        Vector3.Transform(Vector3.UnitX, fin.Offset.Rotation).Z.Should().BeApproximately(-1, 1e-6f);
    }

    [Fact]
    public void APartsBoundingRadiusHoldsItsScaledMeshAboutItsOrigin()
    {
        Write("pack:ships/ship.model", Ship());
        var resources = new CountingResources();

        Loaded(new ModelStore(resources, Content), "pack:ships/ship.model");

        // The triangle's farthest vertex is 1 m from its origin, and the fin doubles it.
        resources.Models.Single().Parts.Select(p => p.BoundingRadius).Should().Equal(1f, 1f, 2f);
    }

    [Fact]
    public void TheModelsBoundsHoldEveryPartWhereItsNodePlacesIt()
    {
        Write("pack:ships/ship.model", Ship());

        StoredModel ship = Loaded(new ModelStore(new CountingResources(), Content), "pack:ships/ship.model");

        // The fin's triangle turned to -Z and doubled reaches (0, 2, -2) and (0, 4, 0).
        ship.Bounds.Min.Z.Should().BeApproximately(-2, 1e-5f);
        ship.Bounds.Max.Y.Should().BeApproximately(4, 1e-5f);
        ship.BoundingRadius.Should().BeApproximately(4, 1e-5f);
    }

    [Fact]
    public void AFurnitureModelUploadsItsColorMapInSrgbAndItsNormalMapLinear()
    {
        Write("pack:props/crate.gltf", Crate());
        var resources = new CountingResources();

        Loaded(new ModelStore(resources, Content), "pack:props/crate.gltf");

        MaterialDesc material = resources.Materials.Single();
        resources.Textures[material.BaseColorMap.Value].Srgb.Should().BeTrue();
        resources.Textures[material.NormalMap.Value].Srgb.Should().BeFalse();
        material.Model.Should().Be((ShadingModel)new StandardShading(Metallic: 0));
    }

    [Fact]
    public void ARepeatedLoadGivesTheSameModelAndUploadsNothingNew()
    {
        Write("pack:props/crate.gltf", Crate());
        var resources = new CountingResources();
        var store = new ModelStore(resources, Content);

        StoredModel first = Loaded(store, "pack:props/crate.gltf");
        StoredModel second = Loaded(store, "pack:props/crate.gltf");

        second.Should().BeSameAs(first);
        (resources.Meshes.Count, resources.Textures.Count, resources.Materials.Count, resources.Models.Count)
            .Should()
            .Be((1, 2, 1, 1));
    }

    [Fact]
    public void TwoPiecesOfOneFileShareItsMaterialsAndTextures()
    {
        var glb = Crate();
        int mesh = glb.Mesh(glb.Triangle(material: 0));
        glb.Root(new JsonObject { ["name"] = "lid", ["translation"] = new JsonArray(0f, 1f, 0f) }, mesh);
        Write("pack:props/crate.gltf", glb);
        var resources = new CountingResources();
        var store = new ModelStore(resources, Content);

        StoredModel crate = Loaded(store, "pack:props/crate.gltf#crate");
        StoredModel lid = Loaded(store, "pack:props/crate.gltf#lid");

        lid.Id.Should().NotBe(crate.Id);
        (resources.Textures.Count, resources.Materials.Count).Should().Be((2, 1));
        resources.Models[1].Parts.Single().Offset.Should().Be(Pose.Identity, "a piece's parts are placed from its own origin");
    }

    [Fact]
    public void AWholeFileReferencePlacesEachRootPieceAtItsRootsPose()
    {
        var glb = Crate();
        int mesh = glb.Mesh(glb.Triangle(material: 0));
        glb.Root(new JsonObject { ["name"] = "lid", ["translation"] = new JsonArray(0f, 1f, 0f) }, mesh);
        Write("pack:props/crate.gltf", glb);
        var resources = new CountingResources();

        Loaded(new ModelStore(resources, Content), "pack:props/crate.gltf");

        resources.Models.Single().Parts.Select(p => p.Offset.Position).Should().Equal(Vector3.Zero, Vector3.UnitY);
    }

    [Fact]
    public void ASeparateStoreForAnotherOwnerUploadsAnew()
    {
        Write("pack:props/crate.gltf", Crate());
        var first = new CountingResources();
        var second = new CountingResources();

        Loaded(new ModelStore(first, Content), "pack:props/crate.gltf");
        Loaded(new ModelStore(second, Content), "pack:props/crate.gltf");

        second.Models.Should().ContainSingle();
        second.Textures.Should().HaveCount(2);
    }

    [Fact]
    public void APrimitiveWithoutAMaterialDrawsWithTheRenderersDefault()
    {
        var glb = new GlbBuilder();
        glb.Root(new JsonObject { ["name"] = "plain" }, glb.Mesh(glb.Triangle()));
        Write("pack:props/plain.gltf", glb);
        var resources = new CountingResources();

        Loaded(new ModelStore(resources, Content), "pack:props/plain.gltf");

        resources.Models.Single().Parts.Single().Material.Should().Be(MaterialId.None);
        resources.Materials.Should().BeEmpty();
    }

    [Fact]
    public void AMissingFileIsAMissingModelAndUploadsNothing()
    {
        var resources = new CountingResources();
        AssetReference reference = AssetReference.Parse("pack:ships/absent.model");

        ModelProblem? problem = new ModelStore(resources, Content).Load(reference).Problem;

        problem.Should().Be(new MissingModel(Content.CookedModel(reference)));
        (resources.Meshes.Count + resources.Models.Count).Should().Be(0);
    }

    [Fact]
    public void ABrokenFileIsAMalformedModel()
    {
        string path = Content.CookedModel(AssetReference.Parse("pack:ships/broken.model"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "broken");

        new ModelStore(new CountingResources(), Content)
            .Load(AssetReference.Parse("pack:ships/broken.model"))
            .Problem.Should()
            .BeOfType<MalformedModel>();
    }

    [Fact]
    public void AnUnknownPieceIsAMissingPieceThatListsThePieces()
    {
        Write("pack:ships/ship.model", Ship());

        ModelProblem? problem = new ModelStore(new CountingResources(), Content)
            .Load(AssetReference.Parse("pack:ships/ship.model#wing"))
            .Problem;

        problem.Should().BeOfType<MissingPiece>().Which.Pieces.Should().Equal("hull");
    }

    /// <summary>Render resources that keep what each Create call gets and number each kind from 0.</summary>
    private sealed class CountingResources : IRenderResources
    {
        public List<MeshData> Meshes { get; } = [];
        public List<TextureData> Textures { get; } = [];
        public List<MaterialDesc> Materials { get; } = [];
        public List<ModelDesc> Models { get; } = [];

        public MeshId CreateMesh(MeshData mesh)
        {
            Meshes.Add(mesh);
            return new(Meshes.Count - 1);
        }

        public TextureId CreateTexture(TextureData texture)
        {
            Textures.Add(texture);
            return new(Textures.Count - 1);
        }

        public MaterialId CreateMaterial(MaterialDesc material)
        {
            Materials.Add(material);
            return new(Materials.Count - 1);
        }

        public ModelId CreateModel(ModelDesc model)
        {
            Models.Add(model);
            return new(Models.Count - 1);
        }
    }
}
