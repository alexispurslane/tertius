using System.Numerics;
using Silk.NET.Vulkan;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>The meter's exposure-buffer visibility without a screen tracer or upscaler supplying an earlier compute read.</summary>
[Collection(GpuCollection.Name)]
public sealed class ExposureSynchronizationTests
{
    [Fact]
    public void TheMeterMakesTheProducingExposureVisibleToComputeWithoutScreenTracing()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 1,
            exposure: AutoExposure.Default,
            screenTrace: new NoScreenTrace(),
            upscaling: new NoUpscaling()
        );
        try
        {
            var visibility = new MeterVisibility();
            renderer.Graph.Add(FrameGraph.Stage.Exposure, visibility);
            RenderSnapshot snapshot = Sunlit();
            AddGraySquare(renderer, snapshot);
            for (int frame = 0; frame < 3; frame++)
            {
                Gpu.Star(snapshot: snapshot, irradiance: frame == 1 ? 16 : 1);
                float usedStops = Still(renderer: renderer, snapshot: snapshot, time: frame / 15.0).Stops;

                visibility
                    .Stages.Should()
                    .HaveFlag(
                        PipelineStageFlags2.ComputeShaderBit,
                        "the meter reads the producing exposure before any later consumer or adaptation"
                    );
                visibility.Access.Should().HaveFlag(AccessFlags2.ShaderStorageReadBit);
                float[] hdr = renderer.ReadHdr();
                uint[] expected = new uint[ExposureMeter.Bins];
                float scale = MathF.Pow(2, usedStops);
                for (int y = 0; y < Height; y += 2)
                for (int x = 0; x < Width; x += 2)
                {
                    int offset = (y * Width + x) * 4;
                    Vector3 source = new(hdr[offset], hdr[offset + 1], hdr[offset + 2]);
                    expected[ExposureMeter.BinOf(ExposureMeter.Luminance(source * scale))]++;
                }
                renderer.ReadExposureHistogram().Should().Equal(expected);
                Gpu.AssertClean(renderer);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Observes the real buffer dependency immediately after the meter dispatch, without changing it.</summary>
    private sealed class MeterVisibility : RenderPass
    {
        public PipelineStageFlags2 Stages { get; private set; }
        public AccessFlags2 Access { get; private set; }
        public override string Name => "meter visibility";

        public override void Record(FrameContext context)
        {
            Stages = context.Exposure.Buffer.State.Stages;
            Access = context.Exposure.Buffer.State.Access;
        }
    }
}
