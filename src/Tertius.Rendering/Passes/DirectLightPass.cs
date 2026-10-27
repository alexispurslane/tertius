using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One fullscreen pass over all depth ranges that writes the direct image: the light of each light, shadowed by its slot's
/// cascades and occluder spheres or by its atlas tiles, plus the emission, pre-exposed and capped. No ambient light
/// (docs/plans/screen-space-illumination.md, "The direct pass"). Sky pixels get 0.
/// </summary>
internal sealed unsafe class DirectLightPass : RenderPass
{
    private readonly VulkanDevice device;
    private readonly Pipeline pipeline;

    public DirectLightPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        ShaderConstants constants
    )
    {
        this.device = device;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "direct light",
            VertexShader = "lighting.vert",
            FragmentShader = "direct.frag",
            Layout = layouts.SceneLayout,
            ColorFormats = [RenderTargets.DirectFormat],
            CullMode = CullModeFlags.None,
            Constants = constants,
        }.Create(device, shaders);
    }

    public override string Name => "direct light";

    public override void Record(FrameContext context)
    {
        RenderTargets targets = context.Targets;
        BarrierBatch barriers = context.Barriers;
        foreach (GpuImage image in targets.GBuffer)
            barriers.Image(image, ImageUse.SampledFragment);
        // Always, even in a frame without shadow maps: the descriptor promises this layout whenever the shader runs.
        barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        if (context.ShadowAtlas is { } atlas)
            barriers.Image(atlas.Image, ImageUse.SampledFragment);
        // Discarded, and still after the last frame's ambient pass read it.
        barriers.Image(targets.Direct, ImageUse.ColorAttachment, discard: true);
        barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        barriers.Flush(context.Vk, context.CommandBuffer);
        context.DrawSceneFullScreen(targets.Direct, pipeline);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
