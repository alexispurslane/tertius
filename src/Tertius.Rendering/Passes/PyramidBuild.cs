using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The compute pipelines of one pyramid's build, and its dispatches (docs/plans/screen-space-reflections.md, "The
/// passes"): the finest level, the first level from a G-buffer image or the finest level, each level from the one below,
/// and the tail (<see cref="PyramidTail"/>). Each dispatch writes one mip, one invocation per texel in 8 × 8 workgroups,
/// after a barrier on the image.
/// </summary>
internal sealed unsafe class PyramidBuild : IDisposable
{
    /// <summary>The width and the height of a workgroup, the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly Pipeline finest;
    private readonly Pipeline first;
    private readonly Pipeline down;
    private readonly PyramidTail tail;

    /// <param name="shaders">The library that holds <paramref name="firstShader"/> and <c>{name}_finest.comp</c>,
    /// <c>{name}_down.comp</c> and <c>{name}_tail.comp</c>.</param>
    /// <param name="name">The shaders' common name, such as <c>depth_pyramid</c>.</param>
    /// <param name="firstShader">The shader of level 1, such as <c>depth_pyramid_first.comp</c>.</param>
    public PyramidBuild(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        string name,
        string firstShader
    )
    {
        this.device = device;
        this.layouts = layouts;
        finest = Create(shaders, name: name + " finest", shader: name + "_finest.comp");
        first = Create(shaders, name: name + " first", shader: firstShader);
        down = Create(shaders, name: name + " down", shader: name + "_down.comp");
        tail = new(device: device, shaders: shaders, layouts: layouts, name: name);
    }

    /// <summary>
    /// Records the dispatches that write levels 1 to L of <paramref name="pyramid"/>: one per level below the tail, then
    /// the tail. The caller binds sets 0 to 2 at the compute bind point with <see cref="DescriptorLayouts.PyramidLayout"/>
    /// and declares the build's other images before, whose barrier the first dispatch flushes.
    /// </summary>
    public void DispatchLevels(FrameContext context, PyramidImages pyramid)
    {
        // Mip m holds level m + 1, so the dispatches write the mips below the tail's first.
        int tailStart = PyramidTail.FirstMip(pyramid.Image);
        for (int level = 1; level <= tailStart; level++)
            Dispatch(context: context, image: pyramid.Image, mip: level - 1, levelSet: pyramid.LevelSet(level));
        tail.Dispatch(context: context, image: pyramid.Image, tailSet: pyramid.TailSet);
    }

    /// <summary>
    /// Records the dispatch that writes the finest level of <paramref name="pyramid"/>, after the last frame's reads of
    /// it. The caller binds sets 0 to 2 as for <see cref="DispatchLevels"/>.
    /// </summary>
    public void DispatchFinest(FrameContext context, PyramidImages pyramid)
    {
        GpuImage image = pyramid.Finest;
        context.Barriers.Image(image, ImageUse.StorageCompute, discard: true);
        Dispatch(context: context, pipeline: finest, set: pyramid.FinestSet, extent: image.Extent);
    }

    public void Dispose()
    {
        tail.Dispose();
        device.Vk.DestroyPipeline(device.Device, finest, null);
        device.Vk.DestroyPipeline(device.Device, first, null);
        device.Vk.DestroyPipeline(device.Device, down, null);
    }

    private Pipeline Create(ShaderLibrary shaders, string name, string shader) =>
        new ComputePipelineDesc
        {
            Name = name,
            Shader = shader,
            Layout = layouts.PyramidLayout,
        }.Create(device, shaders);

    /// <summary>
    /// Records the dispatch that writes <paramref name="mip"/> of <paramref name="image"/> through
    /// <paramref name="levelSet"/>, with the first pipeline for mip 0 and the down pipeline above.
    /// </summary>
    private void Dispatch(FrameContext context, GpuImage image, int mip, DescriptorSet levelSet)
    {
        // Mip 0 discards the last frame's pyramid, after the last frame's reads of it. Each later mip waits for the write
        // of the mip below, which it samples.
        context.Barriers.Image(image, ImageUse.StorageCompute, discard: mip == 0);
        Dispatch(
            context: context,
            pipeline: mip == 0 ? first : down,
            set: levelSet,
            extent: new(Math.Max(image.Extent.Width >> mip, 1), Math.Max(image.Extent.Height >> mip, 1))
        );
    }

    /// <summary>Flushes the barriers and records one dispatch of <paramref name="pipeline"/> over <paramref name="extent"/>.</summary>
    private void Dispatch(FrameContext context, Pipeline pipeline, DescriptorSet set, Extent2D extent)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        context.Barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.PyramidLayout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: (extent.Width + GroupSize - 1) / GroupSize,
            groupCountY: (extent.Height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
    }
}
