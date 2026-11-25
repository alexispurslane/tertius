using System.Numerics;
using System.Text.Json.Nodes;

namespace Tertius.Assets.Tests;

/// <summary>
/// The reader of cooked model files (<see cref="CookedModel"/>): it maps a .glb that the asset converter writes onto the
/// engine's meshes, materials and textures, with glTF's axes as the engine's, splits it into pieces and parts, and
/// refuses a broken file with a reason. Each test builds its .glb in memory with <see cref="GlbBuilder"/>.
/// </summary>
public sealed class CookedModelTests
{
    private const string Path = "test.glb";

    private static CookedModel Read(byte[] glb) => CookedModel.Parse(glb, Path).Ok!;

    private static GlbBuilder OneTriangle(JsonObject? material = null, JsonObject? node = null)
    {
        var glb = new GlbBuilder();
        int? index = material is null ? null : glb.Material(material);
        glb.Root(node ?? new JsonObject { ["name"] = "hull" }, glb.Mesh(glb.Triangle(index)));
        return glb;
    }

    private static CookedPart OnlyPart(CookedModel model) => model.Pieces.Single().Parts.Single();

    [Fact]
    public void EachVertexKeepsItsPositionNormalTangentAndUvInOrder()
    {
        MeshData mesh = OnlyPart(Read(OneTriangle().Build())).Mesh;

        mesh.Indices.Should().Equal(0u, 1u, 2u);
        mesh.Vertices.Select(v => v.Position)
            .Should()
            .Equal(new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0));
        mesh.Vertices.Select(v => v.Normal).Should().AllBeEquivalentTo(Vector3.UnitZ);
        mesh.Vertices.Select(v => v.Tangent)
            .Should()
            .Equal(Vector4.UnitX + Vector4.UnitW, Vector4.UnitX + Vector4.UnitW, Vector4.UnitX - Vector4.UnitW);
        mesh.Vertices.Select(v => v.TexCoord)
            .Should()
            .Equal(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.25f, 0.75f));
    }

    [Fact]
    public void AMissingTangentAttributeLeavesTheTangentZeroForTheShaderToPick()
    {
        var glb = new GlbBuilder();
        JsonObject primitive = glb.Primitive(
            positions: GlbBuilder.TrianglePositions,
            normals: GlbBuilder.TriangleNormals,
            tangents: null,
            uvs: GlbBuilder.TriangleUvs,
            indices: [0, 1, 2]
        );
        glb.Root(new JsonObject { ["name"] = "hull" }, glb.Mesh(primitive));

        OnlyPart(Read(glb.Build())).Mesh.Vertices.Should().AllSatisfy(v => v.Tangent.Should().Be(Vector4.Zero));
    }

    [Fact]
    public void EachSceneRootIsAPieceNamedAfterItsNode()
    {
        var glb = new GlbBuilder();
        int mesh = glb.Mesh(glb.Triangle());
        glb.Root(new JsonObject { ["name"] = "cart" }, mesh);
        glb.Root(new JsonObject { ["name"] = "lid" }, mesh);

        Read(glb.Build()).Pieces.Select(p => p.Name).Should().Equal("cart", "lid");
    }

    [Fact]
    public void GltfAxesAreEngineAxesSoARootTurnedHalfwayAboutUpFacesItsPieceToMinusZ()
    {
        // The converter writes a model's front toward -Z. A root turned 180 degrees about +Y keeps its mesh as it is and
        // carries the turn in its piece's pose, which takes +Z to -Z.
        var node = new JsonObject { ["name"] = "hull", ["rotation"] = new JsonArray(0f, 1f, 0f, 0f) };
        CookedPiece piece = Read(OneTriangle(node: node).Build()).Pieces.Single();

        var front = Vector3.Transform(Vector3.UnitZ, piece.Pose.Rotation);
        front.Z.Should().BeApproximately(-1, 1e-6f);
        piece.Parts.Single().Offset.Should().Be(Pose.Identity, "the root's own transform is the piece's pose");
    }

    [Fact]
    public void ARootsTranslationAndScaleAreThePiecesPoseAndScale()
    {
        var node = new JsonObject
        {
            ["name"] = "hull",
            ["translation"] = new JsonArray(10f, 0f, 0f),
            ["scale"] = new JsonArray(2f, 2f, 2f),
        };
        CookedPiece piece = Read(OneTriangle(node: node).Build()).Pieces.Single();

        piece.Pose.Position.Should().Be(new Vector3(10, 0, 0));
        piece.Scale.Should().Be(new Vector3(2, 2, 2));
    }

    [Fact]
    public void AChildsTransformBecomesItsPartsOffsetAndScaleAndItsVerticesStayLocal()
    {
        var glb = new GlbBuilder();
        int mesh = glb.Mesh(glb.Triangle());
        int child = glb.Node(
            new JsonObject
            {
                ["name"] = "windows",
                ["translation"] = new JsonArray(0f, 5f, 0f),
                ["rotation"] = new JsonArray(0f, 0.70710677f, 0f, 0.70710677f),
                ["scale"] = new JsonArray(3f, 3f, 3f),
            },
            mesh
        );
        glb.Root(new JsonObject { ["name"] = "ring", ["children"] = new JsonArray(child) });

        CookedPiece piece = Read(glb.Build()).Pieces.Single();
        CookedPart part = piece.Parts.Single();

        piece.Name.Should().Be("ring", "a child's mesh belongs to the piece of its root");
        part.Offset.Position.Should().Be(new Vector3(0, 5, 0));
        Quaternion
            .Dot(part.Offset.Rotation, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2))
            .Should()
            .BeApproximately(1, 1e-6f, "the offset carries the child's quarter turn about +Y");
        part.Scale.Should().Be(new Vector3(3, 3, 3));
        part.Mesh.Vertices[1].Position.Should().Be(new Vector3(1, 0, 0));
    }

    [Fact]
    public void AGrandchildsOffsetComposesItsParentsTransform()
    {
        var glb = new GlbBuilder();
        int mesh = glb.Mesh(glb.Triangle());
        int grandchild = glb.Node(
            new JsonObject { ["name"] = "lamp", ["translation"] = new JsonArray(1f, 0f, 0f) },
            mesh
        );
        int child = glb.Node(
            new JsonObject
            {
                ["name"] = "arm",
                ["translation"] = new JsonArray(0f, 2f, 0f),
                ["rotation"] = new JsonArray(0f, 0.70710677f, 0f, 0.70710677f),
                ["scale"] = new JsonArray(2f, 2f, 2f),
                ["children"] = new JsonArray(grandchild),
            }
        );
        glb.Root(new JsonObject { ["name"] = "post", ["children"] = new JsonArray(child) });

        CookedPart part = Read(glb.Build()).Pieces.Single().Parts.Single();

        // The arm turns +X a quarter about +Y to -Z and doubles it: the lamp sits 2 m along -Z from the arm.
        part.Offset.Position.X.Should().BeApproximately(0, 1e-5f);
        part.Offset.Position.Y.Should().BeApproximately(2, 1e-5f);
        part.Offset.Position.Z.Should().BeApproximately(-2, 1e-5f);
        part.Scale.Should().Be(new Vector3(2, 2, 2));
    }

    [Fact]
    public void ANodeMarkedAsAPieceIsAPieceOfItsOwnThatItsParentLeavesOut()
    {
        var glb = new GlbBuilder();
        int mesh = glb.Mesh(glb.Triangle());
        int door = glb.Node(
            new JsonObject { ["name"] = "PIECE_Door", ["translation"] = new JsonArray(0f, 0f, 4f) },
            mesh
        );
        int hatch = glb.Node(
            new JsonObject
            {
                ["name"] = "hatch",
                ["extras"] = new JsonObject { ["tertius_piece"] = 1 },
            },
            mesh
        );
        glb.Root(new JsonObject { ["name"] = "hull", ["children"] = new JsonArray(door, hatch) }, mesh);

        CookedModel model = Read(glb.Build());

        model.Pieces.Select(p => p.Name).Should().Equal("hull", "Door", "hatch");
        model.Pieces[0].Parts.Should().HaveCount(1, "the hull leaves out its two pieces");
        model.Pieces[1].Pose.Position.Should().Be(new Vector3(0, 0, 4), "a piece's pose is in its parent piece's axes");
        model.Pieces[1].IsRoot.Should().BeFalse();
    }

    [Fact]
    public void EachPrimitiveOfAMeshIsAPartWithItsOwnMaterial()
    {
        var glb = new GlbBuilder();
        int paint = glb.Material(new JsonObject { ["name"] = "paint" });
        int glass = glb.Material(new JsonObject { ["name"] = "glass" });
        glb.Root(new JsonObject { ["name"] = "hull" }, glb.Mesh(glb.Triangle(paint), glb.Triangle(glass)));

        Read(glb.Build()).Pieces.Single().Parts.Select(p => p.Material).Should().Equal(0, 1);
    }

    [Fact]
    public void TheMaterialKeepsItsBaseColorMetallicAndRoughnessFactors()
    {
        var material = new JsonObject
        {
            ["pbrMetallicRoughness"] = new JsonObject
            {
                ["baseColorFactor"] = new JsonArray(0.5f, 0.25f, 1f, 1f),
                ["metallicFactor"] = 0f,
                ["roughnessFactor"] = 0.6f,
            },
        };
        CookedMaterial read = Read(OneTriangle(material).Build()).Materials.Single();

        read.BaseColor.Should().Be(new Vector4(x: 0.5f, y: 0.25f, z: 1, w: 1));
        read.Metallic.Should().Be(0);
        read.Roughness.Should().Be(0.6f);
    }

    [Fact]
    public void TheEmissionIsTheEmissiveFactorTimesItsStrength()
    {
        var material = new JsonObject
        {
            ["emissiveFactor"] = new JsonArray(1f, 0.5f, 0f),
            ["extensions"] = new JsonObject
            {
                ["KHR_materials_emissive_strength"] = new JsonObject { ["emissiveStrength"] = 40f },
            },
        };

        Read(OneTriangle(material).Build()).Materials.Single().Emissive.Should().Be(new Vector3(40, 20, 0));
    }

    [Fact]
    public void AMaterialWithoutFactorsTakesTheGltfDefaults()
    {
        CookedMaterial read = Read(OneTriangle([]).Build()).Materials.Single();

        read.BaseColor.Should().Be(Vector4.One);
        (read.Metallic, read.Roughness).Should().Be((1f, 1f), "glTF's defaults are a rough metal");
        read.Emissive.Should().Be(Vector3.Zero);
        read.Alpha.Should().Be((AlphaMode)new OpaqueAlpha());
    }

    [Fact]
    public void APrimitiveWithoutAMaterialHasNoneAndDrawsWithTheDefault() =>
        OnlyPart(Read(OneTriangle().Build())).Material.Should().BeNull();

    [Theory]
    [InlineData("MASK", 0.3f, "cutout at 0.3")]
    [InlineData("MASK", 0f, "cutout at 0.5")]
    [InlineData("BLEND", 0f, "glass")]
    [InlineData("OPAQUE", 0f, "opaque")]
    public void TheAlphaModeMapsToTheEnginesAlpha(string mode, float cutoff, string expected)
    {
        var material = new JsonObject { ["alphaMode"] = mode };
        if (cutoff > 0)
            material["alphaCutoff"] = cutoff;
        AlphaMode alpha = Read(OneTriangle(material).Build()).Materials.Single().Alpha;

        string actual = alpha switch
        {
            CutoutAlpha(var c) => FormattableString.Invariant($"cutout at {c}"),
            GlassAlpha => "glass",
            OpaqueAlpha => "opaque",
            FringedAlpha => "fringed",
            null => "none",
        };
        actual.Should().Be(expected);
    }

    [Fact]
    public void ADoubleSidedMaterialAddsABackCopyOfEachTriangleWithTheNormalAndTangentSignTurned()
    {
        MeshData mesh = OnlyPart(Read(OneTriangle(new JsonObject { ["doubleSided"] = true }).Build())).Mesh;

        mesh.Indices.Should().Equal(0u, 1u, 2u, 3u, 5u, 4u);
        mesh.Vertices[3].Normal.Should().Be(-Vector3.UnitZ);
        mesh.Vertices[3]
            .Tangent.Should()
            .Be(new Vector4(x: 1, y: 0, z: 0, w: -1), "the bitangent keeps its direction on the back");
        mesh.Vertices[3].Position.Should().Be(mesh.Vertices[0].Position);
    }

    [Fact]
    public void AnEmbeddedPngDecodesToItsPixelsTopRowFirst()
    {
        // Two by two: red, green on the top row, blue, white below.
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
        var glb = new GlbBuilder();
        int texture = glb.Texture(GlbBuilder.Png(rgba, width: 2, height: 2));
        int material = glb.Material(
            new JsonObject
            {
                ["pbrMetallicRoughness"] = new JsonObject
                {
                    ["baseColorTexture"] = new JsonObject { ["index"] = texture },
                },
            }
        );
        glb.Root(new JsonObject { ["name"] = "hull" }, glb.Mesh(glb.Triangle(material)));
        CookedModel model = Read(glb.Build());

        int map = model.Materials.Single().BaseColorMap!.Value;
        model.Textures[map].Should().BeEquivalentTo(new TextureData(Width: 2, Height: 2, Rgba: rgba, Srgb: true));
    }

    [Fact]
    public void TheNormalAndMetallicRoughnessMapsAreLinearAndTheBaseColorMapIsSrgb()
    {
        var glb = new GlbBuilder();
        int color = glb.Texture(GlbBuilder.Png([200, 100, 50, 255], width: 1, height: 1));
        int normal = glb.Texture(GlbBuilder.Png([128, 128, 255, 255], width: 1, height: 1));
        int metallicRoughness = glb.Texture(GlbBuilder.Png([0, 200, 255, 255], width: 1, height: 1));
        int material = glb.Material(
            new JsonObject
            {
                ["normalTexture"] = new JsonObject { ["index"] = normal },
                ["pbrMetallicRoughness"] = new JsonObject
                {
                    ["baseColorTexture"] = new JsonObject { ["index"] = color },
                    ["metallicRoughnessTexture"] = new JsonObject { ["index"] = metallicRoughness },
                },
            }
        );
        glb.Root(new JsonObject { ["name"] = "hull" }, glb.Mesh(glb.Triangle(material)));
        CookedModel model = Read(glb.Build());
        CookedMaterial read = model.Materials.Single();

        model.Textures[read.BaseColorMap!.Value].Srgb.Should().BeTrue();
        model.Textures[read.NormalMap!.Value].Srgb.Should().BeFalse();
        model.Textures[read.MetallicRoughnessMap!.Value].Srgb.Should().BeFalse();
        model.Textures[read.MetallicRoughnessMap!.Value].Rgba.Should().Equal(0, 200, 255, 255);
    }

    [Fact]
    public void OneImageThatTwoMaterialsUseIsDecodedOnce()
    {
        var glb = new GlbBuilder();
        int color = glb.Texture(GlbBuilder.Png([200, 100, 50, 255], width: 1, height: 1));
        JsonObject Painted() =>
            new()
            {
                ["pbrMetallicRoughness"] = new JsonObject
                {
                    ["baseColorTexture"] = new JsonObject { ["index"] = color },
                },
            };
        int first = glb.Material(Painted());
        int second = glb.Material(Painted());
        glb.Root(new JsonObject { ["name"] = "hull" }, glb.Mesh(glb.Triangle(first), glb.Triangle(second)));

        CookedModel model = Read(glb.Build());

        model.Textures.Should().ContainSingle();
        model.Materials.Select(m => m.BaseColorMap).Should().Equal(0, 0);
    }

    [Fact]
    public void TheMaterialDescriptionCarriesTheFactorsAndTheUploadedMaps()
    {
        var material = new CookedMaterial
        {
            BaseColor = new(x: 0.5f, y: 0.5f, z: 0.5f, w: 0.4f),
            Metallic = 0.2f,
            Roughness = 0.7f,
            Emissive = new(3, 2, 1),
            Alpha = new GlassAlpha(),
            BaseColorMap = 0,
            NormalMap = 1,
        };
        TextureId[] uploaded = [new(7), new(8)];

        material
            .Desc(uploaded)
            .Should()
            .Be(
                new MaterialDesc(
                    BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 0.4f),
                    Roughness: 0.7f,
                    Emissive: new(3, 2, 1),
                    BaseColorMap: new(7),
                    NormalMap: new(8),
                    Model: new StandardShading(Metallic: 0.2f),
                    Alpha: new GlassAlpha()
                )
            );
    }

    [Fact]
    public void AMissingFileIsAMissingModelNotAnException()
    {
        string path = System.IO.Path.Combine(
            Directory.CreateTempSubdirectory("tertius-assets-").FullName,
            "absent.glb"
        );

        CookedModel.Load(path).Problem.Should().Be(new MissingModel(path));
    }

    [Fact]
    public void AFileThatIsNotAGlbIsMalformedWithAReasonThatSaysSo()
    {
        ModelProblem? problem = CookedModel.Parse("not a glb at all"u8.ToArray(), Path).Problem;

        problem.Should().BeOfType<MalformedModel>().Which.Reason.Should().Contain("glTF binary");
    }

    [Fact]
    public void ATruncatedFileIsMalformed()
    {
        byte[] glb = OneTriangle().Build();

        CookedModel.Parse(glb[..(glb.Length - 8)], Path).Problem.Should().BeOfType<MalformedModel>();
    }

    [Fact]
    public void BrokenJsonIsMalformed()
    {
        byte[] glb = OneTriangle().Build();
        glb[20] = (byte)'!';

        CookedModel.Parse(glb, Path).Problem.Should().BeOfType<MalformedModel>().Which.Reason.Should().Contain("JSON");
    }

    [Fact]
    public void AnAccessorPastTheEndOfTheBufferIsMalformedAndNamesTheAccessor()
    {
        GlbBuilder glb = OneTriangle();
        glb.Json["accessors"]![0]!["count"] = 1000;

        CookedModel
            .Parse(glb.Build(), Path)
            .Problem.Should()
            .BeOfType<MalformedModel>()
            .Which.Reason.Should()
            .Contain("accessor 0");
    }

    [Fact]
    public void AnIndexPastTheVerticesIsMalformed()
    {
        var glb = new GlbBuilder();
        JsonObject primitive = glb.Primitive(
            positions: GlbBuilder.TrianglePositions,
            normals: GlbBuilder.TriangleNormals,
            tangents: null,
            uvs: null,
            indices: [0, 1, 7]
        );
        glb.Root(new JsonObject { ["name"] = "hull" }, glb.Mesh(primitive));

        CookedModel
            .Parse(glb.Build(), Path)
            .Problem.Should()
            .BeOfType<MalformedModel>()
            .Which.Reason.Should()
            .Contain("7");
    }

    [Fact]
    public void APrimitiveWithoutNormalsIsMalformed()
    {
        var glb = new GlbBuilder();
        JsonObject primitive = glb.Primitive(
            positions: GlbBuilder.TrianglePositions,
            normals: null,
            tangents: null,
            uvs: GlbBuilder.TriangleUvs,
            indices: [0, 1, 2]
        );
        glb.Root(new JsonObject { ["name"] = "hull" }, glb.Mesh(primitive));

        CookedModel
            .Parse(glb.Build(), Path)
            .Problem.Should()
            .BeOfType<MalformedModel>()
            .Which.Reason.Should()
            .Contain("NORMAL");
    }

    [Fact]
    public void AnImageThatDoesNotDecodeIsMalformed()
    {
        var glb = new GlbBuilder();
        int texture = glb.Texture("not a png"u8.ToArray());
        int material = glb.Material(
            new JsonObject
            {
                ["pbrMetallicRoughness"] = new JsonObject
                {
                    ["baseColorTexture"] = new JsonObject { ["index"] = texture },
                },
            }
        );
        glb.Root(new JsonObject { ["name"] = "hull" }, glb.Mesh(glb.Triangle(material)));

        CookedModel
            .Parse(glb.Build(), Path)
            .Problem.Should()
            .BeOfType<MalformedModel>()
            .Which.Reason.Should()
            .Contain("image 0");
    }
}
