using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The own rays of the half-resolution upsample (docs/plans/screen-space-reflections.md, "Rays at outlines"): the clear of
/// the own-ray list before the upsample appends to it, and the indirect dispatch of reflection_own_rays.comp after it,
/// which traces each listed pixel. The "reflections" pass at <see cref="TraceResolution.Half"/> records both.
/// </summary>
internal sealed unsafe class OwnRayDispatch : IDisposable
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    /// <param name="constants">The renderer's constants: the own rays add their deltas, whose fallback reads the map while it is on.</param>
    public OwnRayDispatch(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, ShaderConstants constants)
    {
        this.device = device;
        pipeline = new ComputePipelineDesc
        {
            Name = "reflection own rays",
            Shader = "reflection_own_rays.comp",
            Layout = layouts.ReflectionLayout,
            Constants = constants,
        }.Create(device, shaders);
    }

    /// <summary>Sets the header of <paramref name="list"/> to <see cref="OwnRayList.EmptyHeader"/>, before the upsample appends to it.</summary>
    public void Clear(FrameContext context, GpuBuffer list)
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
    /// Traces the own rays that the upsample listed in <paramref name="list"/>, by an indirect dispatch whose command the
    /// upsample counted. The caller bound the upsample's sets and push constants, which this pipeline's layout shares.
    /// </summary>
    public void Trace(FrameContext context, GpuBuffer list)
    {
        context.Barriers.Buffer(list, BufferUse.ComputeIndirectRead);
        // The own rays write only pixels that the upsample left, but the validation layers' hazard tracking sees one image,
        // so the HDR image takes a barrier in the same flush, which drains nothing more than the list's barrier.
        context.Barriers.Image(context.Targets.Hdr, ImageUse.StorageCompute);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        context.Vk.CmdDispatchIndirect(commandBuffer: context.CommandBuffer, buffer: list.Handle, offset: 0);
    }

    public void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
