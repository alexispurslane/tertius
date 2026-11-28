using System.Text.Json;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>Image-memory requirements at the actual native and FSR render extents, before and after the glossy format change.</summary>
[Collection(GpuCollection.Name)]
public sealed class GlossyImageMemoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGlossyImagesReportTheirMemoryRequirementsAtTheMeasuredResolution(bool fsr)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 1280,
            height: 720,
            framesInFlight: 1,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            upscaling: fsr ? new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0) : new NoUpscaling()
        );
        try
        {
            var probe = new MemoryProbe(renderer);
            renderer.Graph.Add(FrameGraph.Stage.Composite, probe);
            RenderSnapshot scene = ExposureScenes.Sunlit();
            ExposureScenes.AddGraySquare(renderer, scene);
            renderer.Render(scene, 1);
            var report = new
            {
                mode = renderer.ScreenTraceImages.Specular.HistoryMode.ToString(),
                fsr,
                output = new[] { renderer.OutputSize.Width, renderer.OutputSize.Height },
                rows = probe.Rows,
                beforeBytes = probe.Rows.Sum(row => (decimal)row.BeforeBytes),
                afterBytes = probe.Rows.Sum(row => (decimal)row.AfterBytes),
                deltaBytes = probe.Rows.Sum(row => (decimal)row.AfterBytes - row.BeforeBytes),
                units = "Vulkan image memory requirements in bytes; allocator block reservations excluded",
            };
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            TestContext.Current.TestOutputHelper!.WriteLine(json);
            if (Environment.GetEnvironmentVariable("TERTIUS_GLOSSY_MEMORY_EVIDENCE") is { } folder)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, $"{report.mode}-{fsr}.json"), json);
            }
            report.deltaBytes.Should().BePositive();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private sealed record MemoryRow(
        string Name,
        uint Width,
        uint Height,
        string BeforeFormat,
        string AfterFormat,
        ulong BeforeBytes,
        ulong AfterBytes
    );

    private sealed unsafe class MemoryProbe(VulkanRenderer renderer) : RenderPass
    {
        public List<MemoryRow> Rows { get; } = [];
        public override string Name => "glossy image memory probe";

        public override void Record(FrameContext context)
        {
            if (Rows.Count > 0)
                return;
            ScreenTraceImages tracer = renderer.ScreenTraceImages;
            TemporalSignal signal = tracer.Specular;
            const Format half = Format.R16G16B16A16Sfloat;
            bool original = signal.HistoryMode == SpecularHistoryMode.Original;
            Add(context: context, image: tracer.HitLight, before: half);
            Add(context: context, image: tracer.OwnLight, before: half);
            Add(context: context, image: tracer.BlockLight, before: half);
            Add(
                context: context,
                image: signal.Sample(0),
                before: half,
                extra: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit
            );
            Add(context: context, image: signal.Fast(0, 0), before: half);
            Add(context: context, image: signal.Fast(0, 1), before: half);
            Add(context: context, image: signal.Slow(0), before: original ? half : signal.Slow(0).Format);
            Extent2D extent = signal.Sample(0).Extent;
            ulong scratch = Requirements(
                context: context,
                extent: extent,
                format: Format.R32G32B32A32Sfloat,
                usage: ImageUsageFlags.StorageBit
            );
            Rows.Add(
                new(
                    Name: "two shared glossy scratch images",
                    Width: extent.Width,
                    Height: extent.Height,
                    BeforeFormat: original ? "absent" : "RGBA32F",
                    AfterFormat: "RGBA32F",
                    BeforeBytes: original ? 0 : scratch * 2,
                    AfterBytes: scratch * 2
                )
            );
            ulong emptyHalf = Requirements(
                context: context,
                extent: new(1, 1),
                format: half,
                usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferDstBit
            );
            ulong emptyScene = Requirements(
                context: context,
                extent: new(1, 1),
                format: Format.R32G32B32A32Sfloat,
                usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferDstBit
            );
            Rows.Add(
                new(
                    Name: "typed composite empty images",
                    Width: 1,
                    Height: 1,
                    BeforeFormat: "one RGBA16F",
                    AfterFormat: "RGBA32F plus RGBA16F",
                    BeforeBytes: emptyHalf,
                    AfterBytes: emptyHalf + emptyScene
                )
            );
        }

        private void Add(FrameContext context, GpuImage image, Format before, ImageUsageFlags extra = 0)
        {
            context.Vk.GetImageMemoryRequirements(context.Device.Device, image.Handle, out MemoryRequirements actual);
            ulong baseline = Requirements(
                context: context,
                extent: image.Extent,
                format: before,
                usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit | extra
            );
            Rows.Add(
                new(
                    Name: image.Name,
                    Width: image.Extent.Width,
                    Height: image.Extent.Height,
                    BeforeFormat: before.ToString(),
                    AfterFormat: image.Format.ToString(),
                    BeforeBytes: baseline,
                    AfterBytes: actual.Size
                )
            );
        }

        private static ulong Requirements(FrameContext context, Extent2D extent, Format format, ImageUsageFlags usage)
        {
            var create = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new(extent.Width, extent.Height, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
            };
            context
                .Vk.CreateImage(
                    device: context.Device.Device,
                    pCreateInfo: &create,
                    pAllocator: null,
                    pImage: out Image image
                )
                .Should()
                .Be(Result.Success);
            try
            {
                context.Vk.GetImageMemoryRequirements(context.Device.Device, image, out MemoryRequirements result);
                return result.Size;
            }
            finally
            {
                context.Vk.DestroyImage(context.Device.Device, image, null);
            }
        }
    }
}
