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
    public void ADeviceWithTheSpecsMinimumOfFourStorageBuffersInAStageCannotBindALayoutWhoseStageSeesTen() =>
        Descriptors(storageBuffers: 10, storageImages: 0, sampledImages: 0)
            .Problem(LeastLimits)
            .Should()
            .Contain("10 storage buffers", "the frame set's vertex stage sees ten storage buffers");

    [Fact]
    public void ADeviceWithTheSpecsMinimumOfFourStorageImagesInAStageCannotBindALayoutWhoseStageSeesTen() =>
        Descriptors(storageBuffers: 0, storageImages: 10, sampledImages: 0)
            .Problem(LeastLimits)
            .Should()
            .Contain("10 storage images", "the pyramid tail's compute stage writes ten mips");

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
                15,
                "the fragment stage sees the frame set's three tables, specular map, irradiance cube and distance cube, and the G-buffer set's nine images"
            );
        layouts
            .DescriptorsOf(layouts.ImpostorLayout)
            .SampledImages.Most.Should()
            .Be(17, "the impostor atlas set adds two images to the fragment stage");
        layouts
            .DescriptorsOf(layouts.ScreenTraceLayout)
            .SampledImages.Most.Should()
            .Be(
                17,
                "the compute stage sees the frame set's images with the blue-noise mask, the G-buffer set's, the depth "
                    + "pyramid's two and the light pyramid's two: one above the spec's least, as the impostor layout's fragment stage"
            );
    }

    /// <summary>
    /// Each frame in flight adds a frame set and a deform set to the shared pool, so the pool's size of each descriptor
    /// type grows by what those two layouts take. A driver that counts each type, such as RADV, refuses a set past the
    /// pool's size of a type, while MoltenVK gives it (ARCHITECTURE.md, "Descriptor pools").
    /// </summary>
    [Theory]
    [InlineData(DescriptorType.UniformBuffer)]
    [InlineData(DescriptorType.StorageBuffer)]
    [InlineData(DescriptorType.Sampler)]
    [InlineData(DescriptorType.SampledImage)]
    [InlineData(DescriptorType.StorageImage)]
    public void EachFrameInFlightGrowsTheSharedPoolByWhatItsFrameSetAndDeformSetTake(DescriptorType type)
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var two = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);
        using var three = new DescriptorLayouts(device: device, framesInFlight: 3, glowLevels: 0);

        (three.SharedPoolSizeOf(type) - two.SharedPoolSizeOf(type))
            .Should()
            .Be(
                two.SizeOf(two.FrameSet, type) + two.SizeOf(two.DeformSet, type),
                $"the third frame's frame set and deform set take that many descriptors of {type} from the shared pool"
            );
    }

    [Fact]
    public void TheDeformLayoutsComputeStageSeesSixStorageBuffers()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var layouts = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);

        layouts
            .DescriptorsOf(layouts.DeformLayout)
            .StorageBuffers.Compute.Should()
            .Be(
                6,
                "the window, the deform vertices, the morph deltas, the pose rows, the job table and the kept positions"
            );
    }

    /// <summary>
    /// The per-stage limits that the renderer needs of a device: the most that one stage of any of its layouts sees, which
    /// ARCHITECTURE.md, "Required device features", states.
    /// </summary>
    [Fact]
    public void TheLayoutsNeedOfAStageTenStorageBuffersTwentyOneStorageImagesAndSeventeenSampledImages()
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

        all.Max(counts => counts.StorageBuffers.Most).Should().Be(10, "the frame set's vertex stage sees ten");
        all.Max(counts => counts.StorageImages.Most)
            .Should()
            .Be(21, "the screen tracer's compute stage sees fifteen, the light-change age's five and the hit light");
        all.Max(counts => counts.SampledImages.Most)
            .Should()
            .Be(17, "the impostor layout's fragment stage sees seventeen");
    }

    /// <summary>
    /// The light-change age of a precise specular history adds five storage images to the screen tracer's compute stage,
    /// the two light-age records, the two samples' ages and FSR's light-change mask, and one to the accumulator's, the
    /// sample age of its signal, which both of its dispatches read. The original history keeps H13's shaders, which have
    /// no light age.
    /// </summary>
    [Fact]
    public void APreciseHistoryAddsTheLightAgesFiveStorageImagesToTheScreenTracerAndOneToTheAccumulator()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var original = new DescriptorLayouts(
            device: device,
            framesInFlight: 2,
            glowLevels: 0,
            historyMode: SpecularHistoryMode.Original
        );
        using var layouts = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);

        layouts
            .DescriptorsOf(layouts.ScreenTraceLayout)
            .StorageImages.Compute.Should()
            .Be(original.DescriptorsOf(original.ScreenTraceLayout).StorageImages.Compute + 5);
        layouts
            .DescriptorsOf(layouts.AccumulateLayout)
            .StorageImages.Compute.Should()
            .Be(original.DescriptorsOf(original.AccumulateLayout).StorageImages.Compute + 1);
    }

    [Fact]
    public void TheCappedPreciseHistoryHasTheSameLayoutsAsTheUncapped()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var uncapped = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);
        using var layouts = new DescriptorLayouts(
            device: device,
            framesInFlight: 2,
            glowLevels: 0,
            historyMode: SpecularHistoryMode.PreciseCapped
        );

        layouts
            .DescriptorsOf(layouts.ScreenTraceLayout)
            .Should()
            .Be(uncapped.DescriptorsOf(uncapped.ScreenTraceLayout));
        layouts.DescriptorsOf(layouts.AccumulateLayout).Should().Be(uncapped.DescriptorsOf(uncapped.AccumulateLayout));
    }

    /// <summary>
    /// The diagnostic of uncovered surfaces adds one storage image more to each: the diagnostic image, which the resolve
    /// writes and the temporal dispatch completes.
    /// </summary>
    [Fact]
    public void TheUncoveredDiagnosticAddsOneStorageImageToTheScreenTracerAndOneToTheAccumulator()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var lightAge = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);
        using var layouts = new DescriptorLayouts(
            device: device,
            framesInFlight: 2,
            glowLevels: 0,
            instrumentation: HistoryInstrumentation.LightAgeDiagnostic
        );

        layouts
            .DescriptorsOf(layouts.ScreenTraceLayout)
            .StorageImages.Compute.Should()
            .Be(lightAge.DescriptorsOf(lightAge.ScreenTraceLayout).StorageImages.Compute + 1);
        layouts
            .DescriptorsOf(layouts.AccumulateLayout)
            .StorageImages.Compute.Should()
            .Be(lightAge.DescriptorsOf(lightAge.AccumulateLayout).StorageImages.Compute + 1);
    }

    /// <summary>
    /// The accumulation trace adds one storage image to the accumulator, the trace, and none to the screen tracer.
    /// </summary>
    [Fact]
    public void TheAccumulationTraceAddsOneStorageImageToTheAccumulatorOnly()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var control = new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0);
        using var layouts = new DescriptorLayouts(
            device: device,
            framesInFlight: 2,
            glowLevels: 0,
            instrumentation: HistoryInstrumentation.ControlTrace
        );

        layouts
            .DescriptorsOf(layouts.ScreenTraceLayout)
            .StorageImages.Compute.Should()
            .Be(control.DescriptorsOf(control.ScreenTraceLayout).StorageImages.Compute);
        layouts
            .DescriptorsOf(layouts.AccumulateLayout)
            .StorageImages.Compute.Should()
            .Be(control.DescriptorsOf(control.AccumulateLayout).StorageImages.Compute + 1);
    }

    /// <summary>
    /// The layouts own the renderer's specular history and its instrumentation, and the signal scratch reads both from
    /// them, so the rule that an instrumentation needs a precise history lives here, before any layout exists.
    /// </summary>
    [Theory]
    [InlineData(nameof(HistoryInstrumentation.LightAgeDiagnostic))]
    [InlineData(nameof(HistoryInstrumentation.ControlTrace))]
    public void LayoutsRefuseAnInstrumentationOfTheOriginalHistory(string instrumentation)
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);

        Action create = () =>
            new DescriptorLayouts(
                device: device,
                framesInFlight: 2,
                glowLevels: 0,
                historyMode: SpecularHistoryMode.Original,
                instrumentation: Enum.Parse<HistoryInstrumentation>(instrumentation)
            ).Dispose();

        create.Should().Throw<ArgumentException>().WithMessage("*needs a precise specular history*");
        device.Log.Snapshot().Should().BeEmpty("the refusal makes no Vulkan object");
    }

    /// <summary>
    /// A device whose stages see 16 sampled images, the spec's least, makes the scene layout, which needs 15, and is
    /// refused at the impostor layout, which needs 17, before vkCreatePipelineLayout: no validation message reports a layout above the
    /// limit, and the refusal destroys the samplers and layouts that the constructor made before it.
    /// </summary>
    [Fact]
    public void ALayoutThatNeedsMoreSampledImagesInAStageThanTheDeviceAllowsIsRefusedBeforeItIsMade()
    {
        var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        try
        {
            PhysicalDeviceProperties properties = device.Properties;
            properties.Limits.MaxPerStageDescriptorSampledImages = 16;
            Report(device, properties);

            Action create = () => new DescriptorLayouts(device: device, framesInFlight: 2, glowLevels: 0).Dispose();

            create
                .Should()
                .Throw<NotSupportedException>()
                .WithMessage("The impostor layout: * 17 sampled images, above the device*limit of 16 sampled images *");
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
