using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The dispatch of an own-ray list (own_rays.glsl, <see cref="OwnRayList"/>): the clear of the list before a dispatch
/// appends to it, and the indirect dispatch of the pipeline that traces each listed pixel. The screen tracer's plan
/// records the clears, and each ray kind's pass the dispatch (<see cref="ScreenTraceKernel"/>).
/// </summary>
internal sealed unsafe class OwnRayDispatch : IDisposable
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    /// <param name="pipeline">The pipeline that traces the listed pixels, in workgroups of OwnRayGroupSize invocations.</param>
    public OwnRayDispatch(VulkanDevice device, ShaderLibrary shaders, ComputePipelineDesc pipeline)
    {
        this.device = device;
        this.pipeline = pipeline.Create(device, shaders);
    }

    /// <summary>Sets the header of <paramref name="list"/> to <see cref="OwnRayList.EmptyHeader"/>, before a dispatch appends to it.</summary>
    public static void Clear(FrameContext context, GpuBuffer list)
    {
        context.Barriers.Buffer(list, BufferUse.TransferDestination);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        fixed (uint* empty = OwnRayList.EmptyHeader)
            context.Vk.CmdUpdateBuffer(
                commandBuffer: context.CommandBuffer,
                dstBuffer: list.Handle,
                dstOffset: 0,
                dataSize: OwnRayList.HeaderBytes,
                pData: empty
            );
    }

    /// <summary>
    /// Traces the own rays listed in <paramref name="list"/>, by an indirect dispatch whose command the appends counted.
    /// The caller bound the sets and the push constants, which this pipeline's layout shares, and declared the uses of the
    /// images that the rays write, which the flush here makes visible with the list's.
    /// </summary>
    public void Trace(FrameContext context, GpuBuffer list)
    {
        context.Barriers.Buffer(list, BufferUse.ComputeIndirectRead);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        context.Vk.CmdDispatchIndirect(commandBuffer: context.CommandBuffer, buffer: list.Handle, offset: 0);
    }

    public void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
