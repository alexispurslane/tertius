using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The two variants of a pipeline that shades (docs/plans/skin-and-hair.md, "Both sides of a hair card", and "Cost"):
/// the two-sided variant draws hair, and the one-sided variant draws every other material, so only the two-sided variant
/// keeps the code that shades a fiber.
/// </summary>
public sealed class SidedPipelineTests
{
    private static readonly GraphicsPipelineDesc Glass = new()
    {
        Name = "glass",
        VertexShader = "mesh.vert",
        FragmentShader = "transparent.frag",
        Layout = default,
        ColorAttachments = [new(Format.R16G16B16A16Sfloat)],
        Constants = new ShaderConstants(EnvironmentMap: true) { CutoutAlpha = true },
    };

    [Fact]
    public void TheOneSidedVariantShadesNoFiberAndKeepsTheDescsOtherConstantsAndCullMode()
    {
        GraphicsPipelineDesc oneSided = SidedPipeline.VariantOf(Glass, twoSided: false);

        oneSided.Constants.Should().Be(Glass.Constants!.Value with { OneSided = true });
        oneSided.CullMode.Should().Be(CullModeFlags.BackBit);
    }

    [Fact]
    public void TheTwoSidedVariantKeepsTheDescsConstantsAndCullsNoFace()
    {
        GraphicsPipelineDesc twoSided = SidedPipeline.VariantOf(Glass, twoSided: true);

        twoSided
            .Constants.Should()
            .Be(Glass.Constants, "the two-sided variant draws hair, so it keeps the fiber's code");
        twoSided.CullMode.Should().Be(CullModeFlags.None);
    }

    [Fact]
    public void TheOneSidedVariantOfADescWithoutConstantsTakesTheShadersDefaultsAndShadesNoFiber()
    {
        GraphicsPipelineDesc oneSided = SidedPipeline.VariantOf(Glass with { Constants = null }, twoSided: false);

        oneSided.Constants.Should().Be(new ShaderConstants(EnvironmentMap: false) { OneSided = true });
    }

    [Fact]
    public unsafe void EveryPipelineButTheOneSidedVariantSpecializesItsShadersToShadeFibers()
    {
        SpecializationStorage storage = default;

        uint byDefault = FiberValue(
            ShaderConstants.Specialization(new ShaderConstants(EnvironmentMap: true), &storage)
        );
        uint oneSided = FiberValue(
            ShaderConstants.Specialization(new ShaderConstants(EnvironmentMap: true) { OneSided = true }, &storage)
        );

        byDefault.Should().Be(1u, "a pipeline that a hair material can reach shades its fibers");
        oneSided.Should().Be(0u, "no hair material reaches the one-sided variant");
    }

    /// <summary>The value that <paramref name="info"/> gives the constant of <see cref="ShaderConstants.ShadesFibersId"/>.</summary>
    private static unsafe uint FiberValue(SpecializationInfo* info)
    {
        for (int i = 0; i < info->MapEntryCount; i++)
        {
            SpecializationMapEntry entry = info->PMapEntries[i];
            if (entry.ConstantID == ShaderConstants.ShadesFibersId)
                return *(uint*)((byte*)info->PData + entry.Offset);
        }

        throw new InvalidOperationException("The specialization has no entry for the fiber constant.");
    }
}
