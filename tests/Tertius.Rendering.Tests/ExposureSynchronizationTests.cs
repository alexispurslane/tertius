using System.Numerics;
using Silk.NET.Vulkan;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>The analytic meter reads scene luminance and adaptation synchronizes exposure without a tracer or upscaler.</summary>
[Collection(GpuCollection.Name)]
public sealed class ExposureSynchronizationTests
{
    [Fact]
    public void TheAdaptationSynchronizesItsExposureWriteWithoutScreenTracing()
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
            var visibility = new AdaptVisibility();
            renderer.Graph.Add(FrameGraph.Stage.ExposureAdapt, visibility);
            RenderSnapshot snapshot = Sunlit();
            AddGraySquare(renderer, snapshot);
            for (int frame = 0; frame < 3; frame++)
            {
                Gpu.Star(snapshot: snapshot, irradiance: frame == 1 ? 16 : 1);
                Still(renderer: renderer, snapshot: snapshot, time: frame / 15.0);

                visibility
                    .Stages.Should()
                    .HaveFlag(
                        PipelineStageFlags2.ComputeShaderBit,
                        "the adaptation reads and writes exposure even when no tracer supplied an earlier compute read"
                    );
                visibility
                    .Access.Should()
                    .HaveFlag(AccessFlags2.ShaderStorageReadBit | AccessFlags2.ShaderStorageWriteBit);
                float[] hdr = renderer.ReadHdr();
                uint[] expected = new uint[ExposureMeter.Bins];
                for (int y = 0; y < Height; y += 2)
                for (int x = 0; x < Width; x += 2)
                {
                    int offset = (y * Width + x) * 4;
                    Vector3 source = new(hdr[offset], hdr[offset + 1], hdr[offset + 2]);
                    expected[ExposureMeter.BinOf(ExposureMeter.Luminance(source))]++;
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

    /// <summary>Observes the real buffer dependency immediately after adaptation, without changing it.</summary>
    private sealed class AdaptVisibility : RenderPass
    {
        public PipelineStageFlags2 Stages { get; private set; }
        public AccessFlags2 Access { get; private set; }
        public override string Name => "adaptation visibility";

        public override void Record(FrameContext context)
        {
            Stages = context.Exposure.Buffer.State.Stages;
            Access = context.Exposure.Buffer.State.Access;
        }
    }
}
