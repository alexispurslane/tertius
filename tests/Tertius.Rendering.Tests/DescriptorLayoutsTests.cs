using System.Reflection;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The device limits that the shared descriptor layouts need beyond what Vulkan guarantees (ARCHITECTURE.md, "Required
/// device features"): the storage buffers, storage images and sampled images that one shader stage of a pipeline layout
/// sees, which the renderer compares with the device's per-stage limits before it makes the layout. Validation is on.
/// </summary>
public sealed class DescriptorLayoutsTests
{
    /// <summary>The specification's least per-stage limits: what every Vulkan device offers.</summary>
    private static readonly PhysicalDeviceLimits LeastLimits = new()
    {
        MaxPerStageDescriptorStorageBuffers = 4,
        MaxPerStageDescriptorStorageImages = 4,
        MaxPerStageDescriptorSampledImages = 16,
    };

    private static LimitedDescriptors Descriptors(uint storageBuffers, uint storageImages, uint sampledImages) =>
        new(
            StorageBuffers: new(Vertex: 0, Fragment: storageBuffers, Compute: 0),
            StorageImages: new(Vertex: 0, Fragment: 0, Compute: storageImages),
            SampledImages: new(Vertex: 0, Fragment: sampledImages, Compute: 0)
        );

    private static DescriptorSetLayoutBinding Binding(DescriptorType type, uint count, ShaderStageFlags stages) =>
        new()
        {
            DescriptorType = type,
            DescriptorCount = count,
            StageFlags = stages,
        };

    [Fact]
    public void ADeviceWithTheSpecsMinimumOfFourStorageBuffersInAStageCannotBindALayoutWhoseStageSeesEight() =>
        Descriptors(storageBuffers: 8, storageImages: 0, sampledImages: 0)
            .Problem(LeastLimits)
            .Should()
            .Contain("8 storage buffers", "the frame set's vertex and fragment stages see eight storage buffers");

    [Fact]
    public void ADeviceWithTheSpecsMinimumOfFourStorageImagesInAStageCannotBindALayoutWhoseStageSeesTen() =>
        Descriptors(storageBuffers: 0, storageImages: 10, sampledImages: 0)
            .Problem(LeastLimits)
            .Should()
            .Contain("10 storage images", "the depth tail's compute stage writes ten mips");

    [Fact]
    public void ADeviceWithTheSpecsMinimumOfSixteenSampledImagesInAStageCannotBindALayoutWhoseStageSeesEighteen() =>
        Descriptors(storageBuffers: 0, storageImages: 0, sampledImages: 18)
            .Problem(LeastLimits)
            .Should()
            .Contain("18 sampled images", "the impostor layout's fragment stage sees eighteen sampled images");

    [Fact]
    public void ADeviceWhoseStagesSeeTheSpecsMinimumOfEachDescriptorCanBindALayoutThatNeedsNoMore() =>
        Descriptors(storageBuffers: 4, storageImages: 4, sampledImages: 16).Problem(LeastLimits).Should().BeNull();

    [Fact]
    public void TheDeviceLimitBoundsTheDescriptorsOfEachStageAloneNotTheirSumOverTheStages()
    {
        LimitedDescriptors counts = default(LimitedDescriptors)
            .With(
                binding: Binding(DescriptorType.StorageBuffer, count: 3, stages: ShaderStageFlags.VertexBit),
                updatedAfterBind: false
            )
            .With(
                binding: Binding(DescriptorType.StorageBuffer, count: 3, stages: ShaderStageFlags.FragmentBit),
                updatedAfterBind: false
            );

        counts
            .Problem(LeastLimits)
            .Should()
            .BeNull("each stage sees three storage buffers, within the four of every device");
    }

    [Fact]
    public void TheSampledImagesOfASetThatIsUpdatedAfterBindCountAgainstAnotherLimit() =>
        default(LimitedDescriptors)
            .With(
                binding: Binding(DescriptorType.SampledImage, count: 4096, stages: ShaderStageFlags.FragmentBit),
                updatedAfterBind: true
            )
            .Problem(LeastLimits)
            .Should()
            .BeNull("maxPerStageDescriptorUpdateAfterBindSampledImages bounds the bindless textures");

    [Fact]
    public void TheSceneLayoutsStorageBuffersAreTheFrameSets()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var layouts = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);

        layouts
            .DescriptorsOf(layouts.SceneLayout)
            .StorageBuffers.Most.Should()
            .Be(
                DescriptorLayouts.FrameSetStorageBuffers,
                "the scene layout's only storage buffers are the frame set's, which every graphics stage sees"
            );
        layouts
            .DescriptorsOf(layouts.ImpostorLayout)
            .StorageBuffers.Most.Should()
            .Be(DescriptorLayouts.FrameSetStorageBuffers, "the impostor atlas set adds no storage buffer");
    }

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
            .DescriptorsOf(layouts.SceneLayout)
            .SampledImages.Most.Should()
            .Be(
                16,
                "the fragment stage sees the frame set's two tables, specular map, irradiance cube and distance cube, and the G-buffer set's eleven images"
            );
        layouts
            .DescriptorsOf(layouts.ImpostorLayout)
            .SampledImages.Most.Should()
            .Be(18, "the impostor atlas set adds two images to the fragment stage");
    }

    [Fact]
    public void TheDeformLayoutsComputeStageSeesFiveStorageBuffers()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var layouts = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);

        layouts
            .DescriptorsOf(layouts.DeformLayout)
            .StorageBuffers.Compute.Should()
            .Be(5, "the window, the deform vertices, the morph deltas, the pose rows and the job table");
    }

    /// <summary>
    /// The per-stage limits that the renderer needs of a device: the most that one stage of any of its layouts sees, which
    /// ARCHITECTURE.md, "Required device features", states.
    /// </summary>
    [Fact]
    public void TheLayoutsNeedOfAStageEightStorageBuffersTenStorageImagesAndEighteenSampledImages()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var layouts = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);

        LimitedDescriptors[] all =
        [
            .. typeof(DescriptorLayouts)
                .GetProperties()
                .Where(property => property.PropertyType == typeof(PipelineLayout))
                .Select(property => layouts.DescriptorsOf((PipelineLayout)property.GetValue(layouts)!)),
        ];

        all.Max(counts => counts.StorageBuffers.Most).Should().Be(8, "the frame set's graphics stages see eight");
        all.Max(counts => counts.StorageImages.Most).Should().Be(10, "the depth tail's compute stage writes ten mips");
        all.Max(counts => counts.SampledImages.Most)
            .Should()
            .Be(18, "the impostor layout's fragment stage sees eighteen");
    }

    /// <summary>
    /// A device whose stages see 17 sampled images makes the scene layout, which needs 16, and is refused at the
    /// impostor layout, which needs 18, before vkCreatePipelineLayout: no validation message reports a layout above the
    /// limit, and the refusal destroys the samplers and layouts that the constructor made before it.
    /// </summary>
    [Fact]
    public void ALayoutThatNeedsMoreSampledImagesInAStageThanTheDeviceAllowsIsRefusedBeforeItIsMade()
    {
        var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        try
        {
            PhysicalDeviceProperties properties = device.Properties;
            properties.Limits.MaxPerStageDescriptorSampledImages = 17;
            Report(device, properties);

            Action create = () => new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0).Dispose();

            create
                .Should()
                .Throw<NotSupportedException>()
                .WithMessage("The impostor layout: * 18 sampled images, above the device*limit of 17 sampled images *");
        }
        finally
        {
            device.Dispose();
        }

        device
            .Log.Snapshot()
            .Should()
            .BeEmpty("the check runs before the layout exists, and the refusal leaves no object of the device alive");
    }

    /// <summary>
    /// Makes <paramref name="device"/> report <paramref name="properties"/> to the renderer. No test device reports
    /// per-stage limits as low as the specification allows, so a test of a refusal lowers the limits that the renderer
    /// reads.
    /// </summary>
    private static void Report(VulkanDevice device, PhysicalDeviceProperties properties) =>
        typeof(VulkanDevice)
            .GetField(
                $"<{nameof(VulkanDevice.Properties)}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!
            .SetValue(device, properties);
}
