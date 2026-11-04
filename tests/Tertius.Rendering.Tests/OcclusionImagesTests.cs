using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The GPU plumbing of the occlusion passes (docs/plans/screen-space-illumination.md, "Images and buffers" and "Push
/// constants and sets"): the half-resolution images, their sets, a resize, and the layout that the four shaders share.
/// Validation is on.
/// </summary>
public sealed class OcclusionImagesTests
{
    [Theory]
    [InlineData(640u, 360u, 320u, 180u)]
    [InlineData(641u, 361u, 321u, 181u)]
    [InlineData(1u, 1u, 1u, 1u)]
    public void TheImagesTakeHalfTheOutputsSizeRoundedUp(uint width, uint height, uint halfWidth, uint halfHeight)
    {
        using var plumbing = new Plumbing(width, height);
        using OcclusionImages images = plumbing.CreateImages();

        Extent2D[] extents =
        [
            images.ViewDepth.Extent,
            images.Source.Extent,
            images.DepthAndNormal.Extent,
            images.Continuation.Extent,
            images.Edges.Extent,
            images.TermA.Extent,
            images.TermB.Extent,
        ];

        images.HalfExtent.Should().Be(new Extent2D(halfWidth, halfHeight));
        extents.Should().AllBeEquivalentTo(images.HalfExtent, "each texel stands for a 2 × 2 block of pixels");
    }

    [Fact]
    public void TheImagesHaveTheFormatsThatTheOcclusionShadersDeclare()
    {
        using var plumbing = new Plumbing(640, 360);
        using OcclusionImages images = plumbing.CreateImages();

        images.ViewDepth.Format.Should().Be(Format.R32Sfloat, "occlusion.glsl declares viewDepth r32f");
        images.Edges.Format.Should().Be(Format.R8Unorm, "occlusion.glsl declares occlusionEdges r8");
        images.TermA.Format.Should().Be(Format.R32G32B32A32Uint, "occlusion.glsl declares the terms rgba32ui");
        images.TermB.Format.Should().Be(Format.R32G32B32A32Uint, "occlusion.glsl declares the terms rgba32ui");
        images.Source.Format.Should().Be(Format.R16G16B16A16Sfloat, "occlusion.glsl declares occlusionSource rgba16f");
        images
            .DepthAndNormal.Format.Should()
            .Be(Format.R32G32Uint, "occlusion.glsl declares occlusionDepthAndNormal rg32ui");
        images.Continuation.Format.Should().Be(Format.R8Uint, "occlusion.glsl declares occlusionContinuation r8ui");
    }

    [Fact]
    public void MakingTheImagesAndWritingTheirSetsReportsNothingToValidation()
    {
        ValidationLog log;
        using (var plumbing = new Plumbing(641, 361))
        {
            log = plumbing.Device.Log;
            using OcclusionImages images = plumbing.CreateImages();
        }

        log.Snapshot().Should().BeEmpty("the sets must match their layouts and every image must be released");
    }

    [Fact]
    public void AResizeRecreatesTheImagesAtHalfTheNewSize()
    {
        using var plumbing = new Plumbing(640, 360);
        using OcclusionImages images = plumbing.CreateImages();

        plumbing.Resize(new(801, 451));
        images.Resize(plumbing.Targets);

        images.HalfExtent.Should().Be(new Extent2D(401, 226));
        images.TermB.Extent.Should().Be(images.HalfExtent);
    }

    [Fact]
    public void AResizeReportsNothingToValidation()
    {
        ValidationLog log;
        using (var plumbing = new Plumbing(640, 360))
        {
            log = plumbing.Device.Log;
            using OcclusionImages images = plumbing.CreateImages();
            plumbing.Resize(new(801, 451));
            images.Resize(plumbing.Targets);
        }

        log.Snapshot().Should().BeEmpty("the sets must point at the new images, and the old ones must be released");
    }

    [Fact]
    public void ADeviceWithTheSpecsMinimumOfFourStorageImagesInAStageCannotBindTheOcclusionSet() =>
        OcclusionImages
            .StorageImageProblem(maxPerStageStorageImages: 4)
            .Should()
            .Contain(
                "10 storage images",
                "the occlusion set binds every image of the four passes in one compute stage"
            );

    [Fact]
    public void ADeviceWithTenStorageImagesInAStageCanBindTheOcclusionSet() =>
        OcclusionImages.StorageImageProblem(maxPerStageStorageImages: 10).Should().BeNull();

    [Theory]
    [InlineData("occlusion_depth.comp")]
    [InlineData("occlusion_sectors.comp")]
    [InlineData("occlusion_denoise.comp")]
    [InlineData("occlusion_upsample.comp")]
    public unsafe void EachOcclusionShaderMakesAPipelineWithTheOcclusionLayout(string shader)
    {
        ValidationLog log;
        using (var plumbing = new Plumbing(640, 360))
        {
            log = plumbing.Device.Log;
            using var shaders = new ShaderLibrary(plumbing.Device, ShaderLibrary.EngineShaders);
            Pipeline pipeline = new ComputePipelineDesc
            {
                Name = shader,
                Shader = shader,
                Layout = plumbing.Layouts.OcclusionLayout,
            }.Create(plumbing.Device, shaders);
            plumbing.Device.Vk.DestroyPipeline(plumbing.Device.Device, pipeline, null);
        }

        log.Snapshot()
            .Should()
            .BeEmpty("the layout must hold every binding and push constant of the shader, and the device its features");
    }

    /// <summary>
    /// A validating headless device with the descriptor layouts and the render targets of one output size, whose direct
    /// diffuse image the occlusion images bind.
    /// </summary>
    private sealed class Plumbing : IDisposable
    {
        public Plumbing(uint width, uint height)
        {
            Device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
            Allocator = new DeviceAllocator(Device);
            Layouts = new DescriptorLayouts(device: Device, framesInFlight: 2, glowLevels: 0);
            Targets = new RenderTargets(
                device: Device,
                allocator: Allocator,
                layouts: Layouts,
                extent: new(width, height),
                glowLevels: 0,
                smaaTables: null,
                reflections: null,
                subsurface: false
            );
        }

        public VulkanDevice Device { get; }
        public DeviceAllocator Allocator { get; }
        public DescriptorLayouts Layouts { get; }
        public RenderTargets Targets { get; }

        public OcclusionImages CreateImages() =>
            new(device: Device, allocator: Allocator, layouts: Layouts, targets: Targets);

        /// <summary>Resizes the targets, as the renderer does, with the device idle.</summary>
        public void Resize(Extent2D extent)
        {
            Device.WaitIdle();
            Targets.Resize(extent);
        }

        public void Dispose()
        {
            Targets.Dispose();
            Layouts.Dispose();
            Allocator.Dispose();
            Device.Dispose();
        }
    }
}
