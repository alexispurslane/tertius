namespace Tertius.Rendering.Tests;

/// <summary>
/// The device limits that the shared descriptor layouts need beyond what Vulkan guarantees (ARCHITECTURE.md, "Required
/// device features"): the storage buffers that the vertex and fragment stages see in the frame set, and the sampled images
/// that one stage of a pipeline layout sees. Validation is on.
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
    public void ADeviceWithFewerSampledImagesInAStageThanALayoutSeesCannotBindIt() =>
        DescriptorLayouts
            .SampledImageProblem(maxPerStageSampledImages: 16, sampledImages: 18)
            .Should()
            .Contain("18 sampled images", "a stage of a layout sees more sampled images than the device offers");

    [Fact]
    public void ADeviceWithASampledImageInAStageForEachThatALayoutSeesCanBindIt() =>
        DescriptorLayouts.SampledImageProblem(maxPerStageSampledImages: 18, sampledImages: 18).Should().BeNull();

    /// <summary>
    /// The count of sampled images that the device check compares with maxPerStageDescriptorSampledImages: per stage, the
    /// most that one stage of a layout sees, and without the bindless textures, whose set is updated after bind and counts
    /// against the separate update-after-bind limit.
    /// </summary>
    [Fact]
    public void TheSampledImagesOfALayoutAreTheMostThatOneStageSeesOutsideTheBindlessTextures()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var layouts = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);

        layouts
            .SampledImagesOf(layouts.SceneLayout)
            .Should()
            .Be(
                16,
                "the fragment stage sees the frame set's two tables, specular map, irradiance cube and distance cube, and the G-buffer set's eleven images"
            );
        layouts
            .SampledImagesOf(layouts.ImpostorLayout)
            .Should()
            .Be(18, "the impostor atlas set adds two images to the fragment stage");
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
