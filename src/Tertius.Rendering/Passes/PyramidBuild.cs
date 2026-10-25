using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The two compute pipelines of one pyramid's build, and its dispatches: the first level from a G-buffer image, then each
/// level from the one below (docs/plans/screen-space-reflections.md, "The passes"). Each dispatch writes one mip, one
/// invocation per texel in 8 × 8 workgroups, after a barrier on the image.
/// </summary>
internal sealed unsafe class PyramidBuild : IDisposable
{
    /// <summary>The width and the height of a workgroup, the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly Pipeline first;
    private readonly Pipeline down;

    /// <param name="shaders">The library that holds <c>{name}_first.comp</c> and <c>{name}_down.comp</c>.</param>
    /// <param name="name">The shaders' common name, such as <c>depth_pyramid</c>.</param>
    public PyramidBuild(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, string name)
    {
        this.device = device;
        this.layouts = layouts;
        first = new ComputePipelineDesc
        {
            Name = name + " first",
            Shader = name + "_first.comp",
            Layout = layouts.PyramidLayout,
        }.Create(device, shaders);
        down = new ComputePipelineDesc
        {
            Name = name + " down",
            Shader = name + "_down.comp",
            Layout = layouts.PyramidLayout,
        }.Create(device, shaders);
    }

    /// <summary>
    /// Records the dispatch that writes <paramref name="mip"/> of <paramref name="image"/> through
    /// <paramref name="levelSet"/>, with the first pipeline for mip 0 and the down pipeline above. The caller binds sets 0
    /// to 2 at the compute bind point with <see cref="DescriptorLayouts.PyramidLayout"/> and declares the build's other
    /// images before the first dispatch, whose barrier this flushes.
    /// </summary>
    public void Dispatch(FrameContext context, GpuImage image, int mip, DescriptorSet levelSet)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        // Mip 0 discards the last frame's pyramid, after the last frame's reads of it. Each later mip waits for the write
        // of the mip below, which it samples.
        context.Barriers.Image(image, ImageUse.StorageCompute, discard: mip == 0);
        context.Barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, mip == 0 ? first : down);
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.PyramidLayout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &levelSet,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        uint width = Math.Max(image.Extent.Width >> mip, 1),
            height = Math.Max(image.Extent.Height >> mip, 1);
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: (width + GroupSize - 1) / GroupSize,
            groupCountY: (height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
    }

    public void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, first, null);
        device.Vk.DestroyPipeline(device.Device, down, null);
    }
}
