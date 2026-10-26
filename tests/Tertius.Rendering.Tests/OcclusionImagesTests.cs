using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The GPU plumbing of the occlusion passes (docs/plans/screen-space-illumination.md, "Images and buffers" and "Push
/// constants and sets"): the half-resolution images, the sector masks buffer, their sets, a resize, and the layout that
/// the five shaders share.
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

        Extent2D[] extents = [images.ViewDepth.Extent, images.Edges.Extent, images.TermA.Extent, images.TermB.Extent];

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
        images.TermA.Format.Should().Be(Format.R16G16B16A16Sfloat, "occlusion.glsl declares the terms rgba16f");
        images.TermB.Format.Should().Be(Format.R16G16B16A16Sfloat, "occlusion.glsl declares the terms rgba16f");
    }

    [Fact]
    public void TheSectorMasksBufferHoldsOneWordForEachSliceOfEachTexel()
    {
        using var plumbing = new Plumbing(641, 361);
        using OcclusionImages images = plumbing.CreateImages(slices: 5);

        images
            .SectorMasks.Size.Should()
            .Be(321ul * 181 * 5 * sizeof(uint), "occlusion.glsl's maskIndex reads this many");
    }

    [Fact]
    public void AResizeRecreatesTheSectorMasksBufferForTheNewHalfSize()
    {
        using var plumbing = new Plumbing(640, 360);
        using OcclusionImages images = plumbing.CreateImages(slices: 3);

        plumbing.Resize(new(801, 451));
        images.Resize(plumbing.Targets);

        images.SectorMasks.Size.Should().Be(401ul * 226 * 3 * sizeof(uint));
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
    public void ASectorMasksBufferPastTheDevicesStorageBufferRangeIsRefused()
    {
        using var plumbing = new Plumbing(641, 361);
        int slices = OneSlicePastTheRange(plumbing.Device, halfTexels: 321ul * 181);

        Action create = () => plumbing.CreateImages(slices: slices).Dispose();

        create
            .Should()
            .Throw<NotSupportedException>(
                "one descriptor of a storage buffer reaches at most maxStorageBufferRange bytes"
            )
            .WithMessage("*maxStorageBufferRange*");
    }

    [Fact]
    public void AResizePastTheDevicesStorageBufferRangeIsRefused()
    {
        using var plumbing = new Plumbing(1, 1);
        using OcclusionImages images = plumbing.CreateImages(
            slices: OneSlicePastTheRange(plumbing.Device, halfTexels: 321ul * 181)
        );

        plumbing.Resize(new(641, 361));
        Action resize = () => images.Resize(plumbing.Targets);

        resize.Should().Throw<NotSupportedException>().WithMessage("*maxStorageBufferRange*");
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

    [Theory]
    [InlineData("occlusion_depth.comp")]
    [InlineData("occlusion_sectors.comp")]
    [InlineData("occlusion_far_rays.comp")]
    [InlineData("occlusion_denoise.comp")]
    [InlineData("occlusion_upsample.comp")]
    public unsafe void EachOcclusionShaderMakesAPipelineWithTheOcclusionLayout(string shader)
    {
        ValidationLog log;
        using (var plumbing = new Plumbing(640, 360))
        {
            log = plumbing.Device.Log;
            using var shaders = new ShaderLibrary(plumbing.Device);
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
    /// The fewest slices whose masks over <paramref name="halfTexels"/> pass the device's maxStorageBufferRange: about
    /// 18,500 at 321 × 181 on a device of 2³² − 1 bytes, and 578 at the spec's least, 2²⁷.
    /// </summary>
    private static int OneSlicePastTheRange(VulkanDevice device, ulong halfTexels) =>
        (int)(device.Properties.Limits.MaxStorageBufferRange / (halfTexels * sizeof(uint))) + 1;

    /// <summary>A validating headless device with the descriptor layouts and the render targets of one output size.</summary>
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
                reflections: false,
                screenSpaceLight: true
            );
        }

        public VulkanDevice Device { get; }
        public DeviceAllocator Allocator { get; }
        public DescriptorLayouts Layouts { get; }
        public RenderTargets Targets { get; }

        public OcclusionImages CreateImages(int slices = 3) =>
            new(
                device: Device,
                allocator: Allocator,
                layouts: Layouts,
                targets: Targets,
                depthPyramid: Targets.DepthPyramidImage!,
                slices: slices
            );

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
