using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One pass of the skin scatter under <see cref="SeparableSubsurface"/> (docs/plans/skin-and-hair.md, "The two passes"):
/// "scatter rows" sums D along the rows into the row image, and "scatter columns" sums the row image along the columns
/// and adds S − D to the HDR image at each skin pixel. One invocation per pixel, in 8 × 8 workgroups, over sets 0 to 2 of
/// the scene layout and the scatter set.
/// </summary>
internal sealed unsafe class SkinScatterPass : RenderPass
{
    /// <summary>The width and the height of a workgroup, the shaders' local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly ScatterImages images;
    private readonly ScatterAxis axis;
    private readonly Pipeline pipeline;

    public SkinScatterPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ScatterImages images,
        ShaderConstants constants,
        ScatterAxis axis
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.images = images;
        this.axis = axis;
        (Name, string shader) = axis switch
        {
            ScatterAxis.Rows => (Name: "scatter rows", Shader: "skin_scatter_rows.comp"),
            ScatterAxis.Columns => (Name: "scatter columns", Shader: "skin_scatter_columns.comp"),
        };
        pipeline = new ComputePipelineDesc
        {
            Name = Name,
            Shader = shader,
            Layout = layouts.ScatterLayout,
            // The taps per side; the shaders hold no other constant.
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name { get; }

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        DeclareUses(context);
        context.Barriers.Flush(vk, commandBuffer);

        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);
        context.BindSceneSets(PipelineBindPoint.Compute, layouts.ScatterLayout);
        DescriptorSet set = images.Set;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.ScatterLayout,
            firstSet: 3,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        Extent2D extent = context.Targets.Extent;
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: (extent.Width + GroupSize - 1) / GroupSize,
            groupCountY: (extent.Height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);

    /// <summary>The images and the buffer that the pass reads and writes, and how.</summary>
    private void DeclareUses(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        // The view depth of each tap comes from the distance target.
        barriers.Image(context.Targets.Distance, ImageUse.SampledCompute);
        barriers.Image(images.Scatter, ImageUse.StorageCompute);
        barriers.Buffer(images.Kernel, BufferUse.ComputeUniformRead);
        switch (axis)
        {
            case ScatterAxis.Rows:
                // Every pixel is written in each frame, after the last frame's columns pass read it.
                barriers.Image(images.Rows, ImageUse.StorageCompute, discard: true);
                break;
            case ScatterAxis.Columns:
                barriers.Image(images.Rows, ImageUse.StorageCompute);
                barriers.Image(context.Targets.Hdr, ImageUse.StorageCompute);
                break;
            default:
                throw new System.Diagnostics.UnreachableException();
        }
    }
}
