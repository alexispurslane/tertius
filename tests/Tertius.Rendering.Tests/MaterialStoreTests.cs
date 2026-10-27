using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A material's row in the material buffer and its slot's facts for the batcher (docs/plans/skin-and-hair.md, "The
/// material on the GPU"). The slot tests upload through a headless renderer.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class MaterialStoreTests
{
    private static MaterialDesc Material(ShadingModel model = default, AlphaMode alpha = default) =>
        new(BaseColor: Vector4.One, Roughness: 0.5f, Model: model, Alpha: alpha);

    [Fact]
    public void AStandardMaterialKeepsItsMetallic() =>
        MaterialStore.ToGpu(Material(new StandardShading(Metallic: 0.75f))).Metallic.Should().Be(0.75f);

    [Fact]
    public void ASkinOrHairMaterialHasNoMetallic()
    {
        MaterialStore.ToGpu(Material(new SkinShading())).Metallic.Should().Be(0);
        MaterialStore.ToGpu(Material(new HairShading(StrandDirection.PlusV))).Metallic.Should().Be(0);
    }

    public static TheoryData<ShadingModel, uint> ShadingWords =>
        new()
        {
            // Bits 0 and 1 hold the model's code: 0 standard, 1 skin, 2 hair. Bits 2 and 3 hold a hair's strand direction.
            { new StandardShading(Metallic: 1), 0u },
            { new SkinShading(), 1u },
            { new HairShading(StrandDirection.PlusU), 2u },
            { new HairShading(StrandDirection.MinusU), 2u | (1u << 2) },
            { new HairShading(StrandDirection.PlusV), 2u | (2u << 2) },
            { new HairShading(StrandDirection.MinusV), 2u | (3u << 2) },
        };

    [Theory]
    [MemberData(nameof(ShadingWords))]
    public void TheShadingWordNamesTheModelAndAHairsStrandDirection(ShadingModel model, uint word)
    {
        ShadingCode.Word(model).Should().Be(word);
        MaterialStore.ToGpu(Material(model)).Shading.Should().Be(word);
    }

    [Theory]
    [InlineData(false, 0.3f)]
    [InlineData(true, 0.6f)]
    public void ACutoutOrFringedMaterialCarriesItsCutoff(bool fringed, float cutoff)
    {
        AlphaMode alpha = fringed ? new FringedAlpha(cutoff) : new CutoutAlpha(cutoff);

        MaterialStore.ToGpu(Material(alpha: alpha)).AlphaCutoff.Should().Be(cutoff);
    }

    [Fact]
    public void AnOpaqueOrGlassMaterialCarriesNone()
    {
        MaterialStore.ToGpu(Material(alpha: new OpaqueAlpha())).AlphaCutoff.Should().Be(0);
        MaterialStore.ToGpu(Material(alpha: new GlassAlpha())).AlphaCutoff.Should().Be(0);
    }

    [Fact]
    public void EachSlotsInfoHoldsItsAlphaMode()
    {
        AlphaMode[] modes = [new GlassAlpha(), new CutoutAlpha(0.5f), new OpaqueAlpha(), new FringedAlpha(0.25f)];
        using VulkanRenderer renderer = Gpu.Headless(width: 160, height: 90);
        MaterialId[] ids = [.. modes.Select(alpha => renderer.CreateMaterial(Material(alpha: alpha)))];

        renderer.Render(Gpu.Snapshot(Vector3.UnitY), 1f);

        MaterialInfo[] infos = renderer.MaterialInfos.ToArray();
        ids.Select(id => infos[MaterialStore.Slot(id)].Alpha).Should().Equal(modes);
    }

    [Fact]
    public void OnlyAHairSlotIsTwoSided()
    {
        ShadingModel[] models = [new StandardShading(0.5f), new SkinShading(), new HairShading(StrandDirection.PlusV)];
        using VulkanRenderer renderer = Gpu.Headless(width: 160, height: 90);
        MaterialId[] ids = [.. models.Select(model => renderer.CreateMaterial(Material(model: model)))];

        renderer.Render(Gpu.Snapshot(Vector3.UnitY), 1f);

        MaterialInfo[] infos = renderer.MaterialInfos.ToArray();
        ids.Select(id => infos[MaterialStore.Slot(id)].TwoSided)
            .Should()
            .Equal(
                [false, false, true],
                "a hair card is a sheet of strands with no back, and every other surface is solid"
            );
    }

    [Fact]
    public void TheDefaultSlotIsOpaque()
    {
        using VulkanRenderer renderer = Gpu.Headless(width: 160, height: 90);

        renderer.Render(Gpu.Snapshot(Vector3.UnitY), 1f);

        renderer
            .MaterialInfos.ToArray()
            .Should()
            .ContainSingle("the default material is the only slot")
            .Which.Alpha.Should()
            .Be(new AlphaMode(new OpaqueAlpha()));
    }
}
