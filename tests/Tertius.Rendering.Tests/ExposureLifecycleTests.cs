using System.Numerics;
using Silk.NET.Vulkan;
using static Tertius.Rendering.Tests.ExposureScenes;

namespace Tertius.Rendering.Tests;

/// <summary>A displayed cut keeps the exposure that produced its indirect histories until the following scene draw finishes.</summary>
[Collection(GpuCollection.Name)]
public sealed class ExposureLifecycleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ACutAndItsFollowingFrameKeepProducingExposureSeparateFromDisplayedExposure(bool trace, bool fsr)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 1,
            exposure: AutoExposure.Default,
            screenTrace: trace ? ScreenTrace.Default : new NoScreenTrace(),
            upscaling: fsr ? new Fsr3Upscaling(new FixedRenderScale(1), 0) : new NoUpscaling(),
            warmUpFrames: 0,
            outputDither: false
        );
        try
        {
            var early = new ExposureCopy(renderer);
            var late = new ExposureCopy(renderer);
            renderer.Graph.Add(FrameGraph.Stage.Exposure, early);
            renderer.Graph.Add(FrameGraph.Stage.ExposureAdapt, late);
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 1361);
            snapshot.Ambient.Intensity = 0;
            AddRampSquare(renderer: renderer, snapshot: snapshot, position: Ahead, rotation: FacingTheCamera);
            FilmFrame source = Still(renderer: renderer, snapshot: snapshot, time: 0);
            Gpu.Star(snapshot: snapshot, irradiance: 3f);
            snapshot.CameraCuts++;

            FilmFrame cut = Still(renderer: renderer, snapshot: snapshot, time: 1.0 / 60);

            early.Values[0].Should().BeApproximately(cut.Stops, 1e-5f);
            early.Values[1].Should().BeApproximately(source.Stops, 1e-5f);
            early.Values[2].Should().BeApproximately(source.Stops, 1e-5f);
            late.Values.Should().Equal([cut.Stops, cut.Stops, source.Stops]);
            if (fsr)
                renderer.ReadFsrExposureRatio().Should().Be(1);

            Gpu.Star(snapshot: snapshot, irradiance: 6f);
            FilmFrame following = Still(renderer: renderer, snapshot: snapshot, time: 2.0 / 60);

            following.Stops.Should().BeApproximately(cut.Stops, 1e-5f);
            early.Values[2].Should().BeApproximately(source.Stops, 1e-5f);
            late.Values[1].Should().BeApproximately(cut.Stops, 1e-5f);
            late.Values[2].Should().BeApproximately(cut.Stops, 1e-5f);
            late.Values[0].Should().BeLessThan(cut.Stops, "the ordinary frame adapts only after display");
            if (fsr)
                renderer.ReadFsrExposureRatio().Should().Be(1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private sealed unsafe class ExposureCopy(VulkanRenderer renderer) : RenderPass
    {
        private GpuBuffer? readback;
        public override string Name => "test exposure lifecycle copy";
        public float[] Values => readback!.AsSpan<float>(3).ToArray();

        public override void Prepare(FrameContext context) =>
            readback ??= GpuBuffer.Create(
                device: context.Device,
                allocator: renderer.Allocator,
                size: 16,
                usage: BufferUsageFlags.TransferDstBit,
                memoryUsage: MemoryUsage.Readback,
                name: Name
            );

        public override void Record(FrameContext context)
        {
            context.Barriers.Buffer(context.Exposure.Buffer, BufferUse.TransferSource);
            context.Barriers.Buffer(readback!, BufferUse.TransferDestination);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
            var region = new BufferCopy(srcOffset: 0, dstOffset: 0, size: 16);
            context.Vk.CmdCopyBuffer(
                commandBuffer: context.CommandBuffer,
                srcBuffer: context.Exposure.Buffer.Handle,
                dstBuffer: readback!.Handle,
                regionCount: 1,
                pRegions: &region
            );
            context.Barriers.Buffer(readback, BufferUse.HostRead);
        }

        public override void Dispose()
        {
            readback?.Dispose();
            base.Dispose();
        }
    }
}
