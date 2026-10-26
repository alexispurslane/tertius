using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The tail of the depth pyramid's build (docs/plans/screen-space-reflections.md, "The passes"): one workgroup writes
/// every mip from the first that its shared memory holds to 1 × 1, with a barrier of the workgroup between two mips, so
/// the small mips, whose work is less than the drain of a barrier between dispatches, share one dispatch.
/// </summary>
internal sealed unsafe class DepthPyramidTail : IDisposable
{
    /// <summary>The most texels of the tail's first mip, 4 KB of shared memory: depth_pyramid_tail.comp's TailTexels.</summary>
    public const int Texels = 512;

    /// <summary>The most mips of a tail, from 512 texels in a row to 1 × 1: the storage images that its set binds.</summary>
    public const int Mips = 10;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly Pipeline pipeline;

    public DepthPyramidTail(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts)
    {
        this.device = device;
        this.layouts = layouts;
        pipeline = new ComputePipelineDesc
        {
            Name = "depth_pyramid tail",
            Shader = "depth_pyramid_tail.comp",
            Layout = layouts.DepthTailLayout,
        }.Create(device, shaders);
    }

    /// <summary>The first mip of <paramref name="image"/> that the tail builds, or its mip count for none.</summary>
    internal static int FirstMip(GpuImage image) => FirstMip(image.Extent, image.MipLevels);

    /// <summary>The first mip of an image of <paramref name="mip0"/> that the tail builds, or <paramref name="mipCount"/> for none.</summary>
    internal static int FirstMip(Extent2D mip0, uint mipCount)
    {
        // Mip 0 reads the distance target, so the tail starts at mip 1 at the earliest.
        int mip = 1;
        while (mip < mipCount && (long)Math.Max(mip0.Width >> mip, 1) * Math.Max(mip0.Height >> mip, 1) > Texels)
            mip++;
        return mip;
    }

    /// <summary>
    /// Records the tail's dispatch of one workgroup through <paramref name="tailSet"/>, which writes the mips of
    /// <paramref name="image"/> from <see cref="FirstMip(GpuImage)"/> to the last, after the dispatches of the mips
    /// below, or nothing when the image has no tail. The caller binds sets 0 to 2 as for <see cref="PyramidBuild.Dispatch"/>.
    /// </summary>
    public void Dispatch(FrameContext context, GpuImage image, DescriptorSet tailSet)
    {
        int first = FirstMip(image);
        if (first >= image.MipLevels)
            return;
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        // The tail reads the mip that the dispatch before it wrote.
        context.Barriers.Image(image, ImageUse.StorageCompute);
        context.Barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.DepthTailLayout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &tailSet,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        var push = new DepthTailPushConstants { MipCount = image.MipLevels - (uint)first };
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.DepthTailLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(DepthTailPushConstants),
            pValues: &push
        );
        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: 1, groupCountY: 1, groupCountZ: 1);
    }

    public void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
