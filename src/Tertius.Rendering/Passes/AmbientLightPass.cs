using System.Diagnostics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One fullscreen pass over all depth ranges that writes the HDR image: the direct image plus the ambient light, which
/// the estimate target dims and brightens, capped at the surfaces' share (docs/plans/screen-space-illumination.md, "The
/// ambient pass"). Sky pixels get the sky and the stars, and an impostor's pixels its baked light from the direct image.
/// Under the scatter it also adds D's ambient part to the diffuse image, with a storage write at skin pixels alone, so
/// the diffuse image is no attachment that the pass loads and stores over the whole screen (docs/plans/skin-and-hair.md,
/// decision 14).
/// </summary>
internal sealed unsafe class AmbientLightPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly ScatterImages? scatter;
    private readonly Pipeline pipeline;

    /// <param name="scatter">
    /// The scatter's images under <see cref="SeparableSubsurface"/>, whose set binds the diffuse image, else null.
    /// </param>
    public AmbientLightPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants,
        ScatterImages? scatter
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.scatter = scatter;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "ambient light",
            VertexShader = "lighting.vert",
            FragmentShader = scatter is null ? "ambient.frag" : "ambient_scatter.frag",
            Layout = scatter is null ? layouts.SceneLayout : layouts.AmbientScatterLayout,
            ColorAttachments = [new(RenderTargets.HdrFormat)],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "ambient light";

    public override void Record(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        foreach (GpuImage image in targets.GBuffer)
            barriers.Image(image, ImageUse.SampledFragment);
        // After the upsample's write, or the clear's under NoScreenSpaceLight.
        barriers.Image(targets.Estimate, ImageUse.SampledFragment);
        barriers.Image(targets.Direct, ImageUse.SampledFragment);
        barriers.Image(targets.Hdr, ImageUse.ColorAttachment, discard: true);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        if (scatter is null)
        {
            barriers.Flush(context.Vk, context.CommandBuffer);
            context.DrawSceneFullScreen(pipeline, [(targets.Hdr, AttachmentLoadOp.DontCare)]);
            return;
        }

        // Kept: the pass adds to what the direct pass wrote.
        barriers.Image(
            targets.Diffuse ?? throw new UnreachableException("The targets of the scatter hold the diffuse image."),
            ImageUse.StorageFragment
        );
        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(
            pipeline: pipeline,
            layout: layouts.AmbientScatterLayout,
            passSet: scatter.Set,
            targets: [(targets.Hdr, AttachmentLoadOp.DontCare)]
        );
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
