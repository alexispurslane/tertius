namespace Tertius.Rendering.Tests;

/// <summary>
/// The device limit that the shared descriptor layouts need beyond what Vulkan guarantees (ARCHITECTURE.md, "Required
/// device features"): the storage buffers that the vertex and fragment stages see in the frame set. Validation is on.
/// </summary>
public sealed class DescriptorLayoutsTests
{
    [Fact]
    public void ADeviceWithTheSpecsMinimumOfFourStorageBuffersInAStageCannotBindTheFrameSet() =>
        DescriptorLayouts
            .StorageBufferProblem(maxPerStageStorageBuffers: 4)
            .Should()
            .Contain("8 storage buffers", "the frame set's vertex and fragment stages see eight storage buffers");

    [Fact]
    public void ADeviceWithAStorageBufferInAStageForEachOfTheFrameSetsCanBindIt() =>
        DescriptorLayouts
            .StorageBufferProblem(maxPerStageStorageBuffers: DescriptorLayouts.FrameSetStorageBuffers)
            .Should()
            .BeNull();

    [Fact]
    public void TheSceneLayoutsStorageBuffersAreTheOnesThatTheDeviceCheckCounts()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var layouts = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);

        layouts
            .StorageBuffersOf(layouts.SceneLayout)
            .Should()
            .Be(
                DescriptorLayouts.FrameSetStorageBuffers,
                "the scene layout's only storage buffers are the frame set's, which every graphics stage sees"
            );
        layouts
            .StorageBuffersOf(layouts.ImpostorLayout)
            .Should()
            .Be(DescriptorLayouts.FrameSetStorageBuffers, "the impostor atlas set adds no storage buffer");
    }

    [Fact]
    public void TheDeformLayoutBindsNoMoreStorageBuffersThanTheFrameSetNeedsOfEveryDevice()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var layouts = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);

        layouts
            .StorageBuffersOf(layouts.DeformLayout)
            .Should()
            .Be(5, "the window, the deform vertices, the morph deltas, the pose rows and the job table")
            .And.BeLessThanOrEqualTo(
                DescriptorLayouts.FrameSetStorageBuffers,
                "the renderer refuses a device whose stages see fewer storage buffers than the frame set holds"
            );
    }
}
