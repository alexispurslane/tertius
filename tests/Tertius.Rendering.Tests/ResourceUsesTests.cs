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

        storage.Access.Should().Be(AccessFlags2.ShaderStorageReadBit | AccessFlags2.ShaderStorageWriteBit);
        ResourceUses
            .Writes(storage.Access)
            .Should()
            .BeTrue(
                "each occlusion pass reads what the pass before it wrote, and a write waits for the reads before it"
            );
    }
}
