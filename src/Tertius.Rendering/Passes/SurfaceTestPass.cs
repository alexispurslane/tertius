using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The surface test pass (docs/plans/temporal-renderer.md, section 8, "The surface test", and decision 24): one compute
/// dispatch, right after the G-buffer, writes the surface test image, this frame's surface history, FSR's device depth
/// and the motion of the sky (Shaders/surface_test.comp). A frame that resets (<see cref="FrameHistory.PendingFrame.Reset"/>) fails
/// every tap, because the last surface history belongs to another camera, size or no frame at all.
/// </summary>
internal sealed unsafe class SurfaceTestPass : RenderPass
{
    /// <summary>
    /// The near plane of FSR's reversed and infinite device depth, in m: the device depth is this over the view depth
    /// (docs/plans/temporal-renderer.md, "The host").
    /// </summary>
    public const float DeviceDepthNear = DepthRanges.NearPlane;

    /// <summary>The width and the height of a workgroup: the shader's local size.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly SurfaceHistory images;
    private readonly Pipeline pipeline;

    public SurfaceTestPass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, SurfaceHistory images)
    {
        this.device = device;
        this.layouts = layouts;
        this.images = images;
        pipeline = new ComputePipelineDesc
        {
            Name = "surface test",
            Shader = "surface_test.comp",
            Layout = layouts.SurfaceTestLayout,
        }.Create(device, shaders);
    }

    public override string Name => "surface test";

    public override void Record(FrameContext context)
    {
        BarrierBatch barriers = context.Barriers;
        RenderTargets targets = context.Targets;
        int parity = context.History.Parity;
        barriers.Image(targets.Distance, ImageUse.SampledCompute);
        barriers.Image(targets.Normal, ImageUse.SampledCompute);
        barriers.Image(targets.Specular, ImageUse.SampledCompute);

        // Read at every pixel, and written at the sky's.
        barriers.Image(targets.Motion, ImageUse.StorageCompute);
        barriers.Image(images.Last(parity), ImageUse.SampledCompute);
        barriers.Image(images.LastDepth(parity), ImageUse.SampledCompute);
        // Discarded: the pass writes every pixel of each.
        barriers.Image(images.Written(parity), ImageUse.StorageCompute, discard: true);
        barriers.Image(images.Test, ImageUse.StorageCompute, discard: true);
        barriers.Image(images.WrittenDepth(parity), ImageUse.StorageCompute, discard: true);
        Dispatch(context: context, set: images.Sets[parity]);
    }

    /// <summary>Flushes the barriers and dispatches one invocation per pixel of the targets, with <paramref name="set"/>.</summary>
    private void Dispatch(FrameContext context, DescriptorSet set)
    {
        context.BeginComputePass(pipeline: pipeline, layout: layouts.SurfaceTestLayout, passSet: set);
        var push = new SurfaceTestPushConstants { Reset = context.History.Reset ? 1u : 0u };
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layouts.SurfaceTestLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(SurfaceTestPushConstants),
            pValues: &push
        );
        Extent2D extent = context.Extent;
        context.Vk.CmdDispatch(
            commandBuffer: context.CommandBuffer,
            groupCountX: (extent.Width + GroupSize - 1) / GroupSize,
            groupCountY: (extent.Height + GroupSize - 1) / GroupSize,
            groupCountZ: 1
        );
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
