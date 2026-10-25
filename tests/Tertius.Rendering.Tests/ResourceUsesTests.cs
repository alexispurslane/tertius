using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>What each use of an image means to <see cref="BarrierBatch"/>: its layout, stages and access.</summary>
public sealed class ResourceUsesTests
{
    [Fact]
    public void AStorageImageInAComputeShaderIsUsedInTheGeneralLayout()
    {
        ResourceState storage = ResourceUses.Describe(ImageUse.StorageCompute);

        storage.Layout.Should().Be(ImageLayout.General, "Vulkan reads and writes storage images only in General");
        storage.Stages.Should().Be(PipelineStageFlags2.ComputeShaderBit);
    }

    [Fact]
    public void AStorageUseInAComputeShaderReadsAndWritesSoAnyTwoStorageUsesGetABarrier()
    {
        ResourceState storage = ResourceUses.Describe(ImageUse.StorageCompute);

        storage
            .Access.Should()
            .HaveFlag(AccessFlags2.ShaderStorageReadBit)
            .And.HaveFlag(AccessFlags2.ShaderStorageWriteBit);
        ResourceUses
            .Writes(storage.Access)
            .Should()
            .BeTrue(
                "each occlusion pass reads what the pass before it wrote, and a write waits for the reads before it"
            );
    }

    [Fact]
    public void AStorageUseInAComputeShaderAlsoSamplesSoAPyramidLevelSeesTheLevelThatTheDispatchBeforeWrote() =>
        ResourceUses
            .Describe(ImageUse.StorageCompute)
            .Access.Should()
            .HaveFlag(
                AccessFlags2.ShaderSampledReadBit,
                "each build of a pyramid level samples the level below, which the dispatch before wrote as a storage image"
            );

    [Fact]
    public void ASampledReadOfAStorageImageInAComputeShaderStaysInTheGeneralLayout()
    {
        ResourceState sampled = ResourceUses.Describe(ImageUse.SampledComputeGeneral);

        sampled.Layout.Should().Be(ImageLayout.General, "the pyramids live in General, where their builds write them");
        sampled.Stages.Should().Be(PipelineStageFlags2.ComputeShaderBit);
        sampled.Access.Should().Be(AccessFlags2.ShaderSampledReadBit);
    }
}
