using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// What a material says by default, and the cutoffs that a renderer refuses (docs/plans/skin-and-hair.md, "The material
/// API"). The renderer's <see cref="VulkanRenderer.CreateMaterial"/> checks a material in its upload queue, so these tests
/// need no GPU.
/// </summary>
public sealed class MaterialDescTests
{
    [Fact]
    public void TheDefaultMaterialIsAStandardNonMetalAndOpaque()
    {
        var material = MaterialDesc.Color(Vector4.One, roughness: 0.5f);

        material.Model.Should().Be(new ShadingModel(new StandardShading(Metallic: 0)));
        material.Alpha.Should().Be(new AlphaMode(new OpaqueAlpha()));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.1f)]
    [InlineData(1.5f)]
    [InlineData(float.NaN)]
    public void ACutoffOutsideZeroToOneIsRefused(float cutoff)
    {
        var queue = new UploadQueue(maxTextureSize: 64, deformBytesLimit: 1L << 27);

        foreach (AlphaMode alpha in new AlphaMode[] { new CutoutAlpha(cutoff), new FringedAlpha(cutoff) })
            FluentActions
                .Invoking(() => queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.5f, Alpha: alpha)))
                .Should()
                .ThrowExactly<ArgumentOutOfRangeException>("a cutoff lies above 0 and at most 1, not at {0}", alpha);
    }

    [Fact]
    public void ACutoffOfOneIsAccepted()
    {
        var queue = new UploadQueue(maxTextureSize: 64, deformBytesLimit: 1L << 27);

        queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.5f, Alpha: new CutoutAlpha(Cutoff: 1)));
        queue.AddMaterial(new(BaseColor: Vector4.One, Roughness: 0.5f, Alpha: new FringedAlpha(Cutoff: 1)));

        queue.Drain().Materials.Should().HaveCount(2, "at 1, only a texel of full coverage is core");
    }
}
