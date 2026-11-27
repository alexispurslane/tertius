using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>The GPU reduction keeps counts across every lane block of the full scene-luminance histogram.</summary>
[Collection(GpuCollection.Name)]
public sealed class SceneExposureHistogramTests
{
    [Fact]
    public void AUniformSceneHistogramPlacesItsMedianAtMinusSixteenStops()
    {
        uint[] counts = Enumerable.Repeat(1u, 2304).ToArray();

        float stops = Adapt(counts);

        stops
            .Should()
            .BeApproximately(
                16.5f,
                0.001f,
                "half of the -160 to +128 stop interval ends at -16, which the highlight target offsets by +0.5"
            );
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(768)]
    [InlineData(1024)]
    [InlineData(1280)]
    [InlineData(1536)]
    [InlineData(1792)]
    [InlineData(2048)]
    [InlineData(2303)]
    public void CountsOnBothSidesOfEveryReductionBlockMatchTheIndependentSequentialMeter(int boundary)
    {
        uint[] counts = new uint[2304];
        counts[boundary - 1] = 101;
        counts[boundary] = 71;
        counts[1280] += 500;
        float expected = ExposureMeter.TargetStops(
            reading: ExposureMeter.Reading(counts, 0.1f, 0.5f),
            usedStops: 0,
            compensationStops: 0.5f,
            minStops: -64,
            maxStops: 64
        );
        expected.Should().BeInRange(-63f, 63f, "the exposure clamp must not hide a reduction error");

        float actual = Adapt(counts);

        actual.Should().BeApproximately(expected, 0.001f);
    }

    private static float Adapt(uint[] counts)
    {
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = 16,
                Height = 16,
                FramesInFlight = 1,
                WarmUpFrames = 0,
                Impostors = false,
                ScreenTrace = new NoScreenTrace(),
                Bloom = new NoBloom(),
                Upscaling = new NoUpscaling(),
                EnvironmentMap = new NoEnvironmentMap(),
                Exposure = new AutoExposure(
                    MinStops: -64,
                    MaxStops: 64,
                    CompensationStops: 0.5f,
                    DarkShare: 0.1f,
                    BrightShare: 0.5f,
                    IntoDarkSeconds: 0,
                    IntoLightSeconds: 0
                ),
            }
        );
        try
        {
            renderer.Graph.Add(FrameGraph.Stage.Exposure, new HistogramInput(counts));
            RenderSnapshot scene = Gpu.Snapshot(System.Numerics.Vector3.UnitZ, starIrradiance: 0);
            renderer.Render(scene, 1f);
            scene.Time = 1.0 / 60;

            renderer.Render(scene, 1f);

            return renderer.ReadExposureStops().Stops;
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Supplies known GPU counts after metering and before the ordinary adapt dispatch.</summary>
    private sealed unsafe class HistogramInput(uint[] counts) : RenderPass
    {
        public override string Name => "test scene histogram input";

        public override void Record(FrameContext context)
        {
            GpuBuffer histogram = context.Exposure.Histogram;
            context.Barriers.Buffer(histogram, BufferUse.TransferDestination);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
            fixed (uint* values = counts)
                context.Vk.CmdUpdateBuffer(
                    commandBuffer: context.CommandBuffer,
                    dstBuffer: histogram.Handle,
                    dstOffset: 0,
                    dataSize: (uint)(counts.Length * sizeof(uint)),
                    pData: values
                );
        }
    }
}
