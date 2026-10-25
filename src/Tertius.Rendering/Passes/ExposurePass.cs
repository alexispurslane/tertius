using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The exposure pass, under <see cref="AutoExposure"/> (docs/plans/auto-exposure.md, "The exposure pass"): it clears
/// the histogram, meters the surface pixels of the HDR image into it, and moves the exposure buffer toward the target
/// by this draw's step. Two compute dispatches, on the graphics queue.
/// </summary>
internal sealed unsafe class ExposurePass : RenderPass
{
    /// <summary>The meter's workgroup: 16 by 16 pixels.</summary>
    private const uint MeterGroupSize = 16;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly AutoExposure setting;
    private readonly Pipeline meter;
    private readonly Pipeline adapt;

    public ExposurePass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, AutoExposure setting)
    {
        this.device = device;
        this.layouts = layouts;
        this.setting = setting;
        meter = new ComputePipelineDesc
        {
            Name = "exposure meter",
            Shader = "exposure_meter.comp",
            Layout = layouts.ExposureLayout,
        }.Create(device, shaders);
        adapt = new ComputePipelineDesc
        {
            Name = "exposure adapt",
            Shader = "exposure_adapt.comp",
            Layout = layouts.ExposureLayout,
        }.Create(device, shaders);
    }

    public override string Name => "exposure";

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        GpuBuffer histogram = context.Exposure.Histogram;

        // Cleared first, so after the frame the histogram holds this frame's counts.
        barriers.Buffer(histogram, BufferUse.TransferDestination);
        barriers.Flush(vk, commandBuffer);
        vk.CmdFillBuffer(
            commandBuffer: commandBuffer,
            dstBuffer: histogram.Handle,
            dstOffset: 0,
            size: Vk.WholeSize,
            data: 0
        );

        barriers.Image(context.Targets.Hdr, ImageUse.SampledCompute);
        barriers.Image(context.Targets.Distance, ImageUse.SampledCompute);
        barriers.Buffer(histogram, BufferUse.ComputeWrite);
        barriers.Flush(vk, commandBuffer);
        BindSets(context);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, meter);
        Extent2D extent = context.Extent;
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: (extent.Width + MeterGroupSize - 1) / MeterGroupSize,
            groupCountY: (extent.Height + MeterGroupSize - 1) / MeterGroupSize,
            groupCountZ: 1
        );

        barriers.Buffer(histogram, BufferUse.ComputeRead);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ComputeWrite);
        barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, adapt);
        ExposureAdaptPushConstants push = PushConstants(context.ExposureStep);
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.ExposureLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(ExposureAdaptPushConstants),
            pValues: &push
        );
        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: 1, groupCountY: 1, groupCountZ: 1);
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, meter, null);
        device.Vk.DestroyPipeline(device.Device, adapt, null);
    }

    /// <summary>Binds the meter set and the exposure set at the compute bind point, for both dispatches.</summary>
    private void BindSets(FrameContext context)
    {
        DescriptorSet* sets = stackalloc[] { context.Targets.MeterSet, context.Exposure.Set };
        context.Vk.CmdBindDescriptorSets(
            commandBuffer: context.CommandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.ExposureLayout,
            firstSet: 0,
            descriptorSetCount: 2,
            pDescriptorSets: sets,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
    }

    /// <summary>The setting's fields and the two approach factors of a step of <paramref name="seconds"/>.</summary>
    private ExposureAdaptPushConstants PushConstants(float seconds) =>
        new()
        {
            DarkShare = setting.DarkShare,
            BrightShare = setting.BrightShare,
            MinStops = setting.MinStops,
            MaxStops = setting.MaxStops,
            CompensationStops = setting.CompensationStops,
            IntoDarkFactor = ExposureMeter.ApproachFactor(seconds, setting.IntoDarkSeconds),
            IntoLightFactor = ExposureMeter.ApproachFactor(seconds, setting.IntoLightSeconds),
        };
}
