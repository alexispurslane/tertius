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

        oneSided.Constants.Should().Be(Glass.Constants!.Value with { DropsFibers = true });
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

        oneSided.Constants.Should().Be(new ShaderConstants(EnvironmentMap: false) { DropsFibers = true });
    }

    [Fact]
    public unsafe void OnlyAPipelineThatDropsFibersSpecializesItsShadersToDropThem()
    {
        SpecializationStorage storage = default;

        uint byDefault = FiberValue(
            ShaderConstants.Specialization(new ShaderConstants(EnvironmentMap: true), &storage)
        );
        uint dropping = FiberValue(
            ShaderConstants.Specialization(new ShaderConstants(EnvironmentMap: true) { DropsFibers = true }, &storage)
        );

        byDefault.Should().Be(0u, "a pipeline that a hair material can reach shades its fibers");
        dropping.Should().Be(1u, "the one-sided variant, which no hair material reaches, drops them");
    }

    /// <summary>The value that <paramref name="info"/> gives the constant of <see cref="ShaderConstants.DropsFibersId"/>.</summary>
    private static unsafe uint FiberValue(SpecializationInfo* info)
    {
        for (int i = 0; i < info->MapEntryCount; i++)
        {
            SpecializationMapEntry entry = info->PMapEntries[i];
            if (entry.ConstantID == ShaderConstants.DropsFibersId)
                return *(uint*)((byte*)info->PData + entry.Offset);
        }

        throw new InvalidOperationException("The specialization has no entry for the fiber constant.");
    }
}
