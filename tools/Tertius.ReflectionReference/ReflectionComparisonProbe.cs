using Silk.NET.Vulkan;
using System.Text.Json;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal readonly record struct ProbeOutputGroup(string Shader, int First, int Count);

internal sealed unsafe class ReflectionComparisonProbe : Tertius.Rendering.RenderPass
{
    public static IReadOnlyList<ProbeOutputGroup> OutputGroups { get; } = [
        new("reflection_comparison_light.frag", 0, 3),
        new("reflection_comparison_surface.frag", 3, 3),
        new("reflection_comparison_view.frag", 6, 2),
    ];
    private readonly VulkanRenderer renderer;
    private readonly MeshData boxGeometry;
    private readonly GpuImage[] images;
    private readonly List<ProbeOutputGroup> groups = [.. OutputGroups];
    private VulkanDevice? device;
    private ShaderLibrary? shaders;
    private readonly List<Pipeline> pipelines = [];

    private ReflectionComparisonProbe(VulkanRenderer renderer, MeshData boxGeometry, bool includeReconstruction)
    {
        this.renderer = renderer;
        this.boxGeometry = boxGeometry;
        if (renderer.ShadingConstants.ReflectionSharingPrototype || includeReconstruction)
            groups.Add(new("reflection_comparison_shared.frag", 8, 1));
        images = new GpuImage[groups.Sum(group => group.Count)];
    }

    public override string Name => "research reflected light probe";
    public JsonElement? NumericMetadata { get; private set; }

    public static ReflectionComparisonProbe On(VulkanRenderer renderer, MeshData boxGeometry, bool includeReconstruction = false)
    {
        var probe = new ReflectionComparisonProbe(renderer, boxGeometry, includeReconstruction);
        renderer.Graph.Add(FrameGraph.Stage.Composite, probe);
        return probe;
    }

    public float[] Read(int attachment) => renderer.ReadImage(images[attachment]);

    public (int Width, int Height) Size => ((int)images[0].Extent.Width, (int)images[0].Extent.Height);

    public override void Prepare(FrameContext context)
    {
        if (device is not null)
            return;
        device = context.Device;
        shaders = new ShaderLibrary(device, typeof(ReflectionComparisonProbe).Assembly);
        foreach (ProbeOutputGroup group in groups)
            pipelines.Add(new GraphicsPipelineDesc
            {
                Name = Name,
                VertexShader = "lighting.vert",
                FragmentShader = group.Shader,
                Layout = context.Layouts.CompositeLayout,
                ColorFormats = [.. Enumerable.Repeat(Format.R32G32Sfloat, group.Count)],
                CullMode = CullModeFlags.None,
                Constants = renderer.ShadingConstants,
            }.Create(device, shaders));
        for (int attachment = 0; attachment < images.Length; attachment++)
            images[attachment] = GpuImage.Create(
                device: device, allocator: renderer.Allocator, extent: context.Extent, format: Format.R32G32Sfloat,
                usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
                mipLevels: 1, name: $"{Name} {attachment}"
            );
    }

    public override void Record(FrameContext context)
    {
        NumericMetadata ??= RasterDiagnostic.ReadFrame(context, boxGeometry);
        foreach (GpuImage image in context.Targets.GBuffer)
            context.Barriers.Image(image, ImageUse.SampledFragment);
        context.Barriers.Image(renderer.ScreenTraceSignals.Specular.Slow(0), ImageUse.StorageFragment);
        foreach (GpuImage image in images)
            context.Barriers.Image(image, ImageUse.ColorAttachment, discard: true);
        context.Barriers.Buffer(context.Exposure.Buffer, BufferUse.ShaderRead);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        for (int group = 0; group < groups.Count; group++)
            context.DrawSceneFullScreen(
                pipeline: pipelines[group], layout: context.Layouts.CompositeLayout, passSet: renderer.CompositeLightSet,
                targets: [.. images.Skip(groups[group].First).Take(groups[group].Count).Select(image => (image, AttachmentLoadOp.DontCare))]
            );
    }

    public override void Dispose()
    {
        foreach (GpuImage? image in images)
            image?.Dispose();
        foreach (Pipeline pipeline in pipelines)
            device?.Vk.DestroyPipeline(device.Device, pipeline, null);
        shaders?.Dispose();
        base.Dispose();
    }
}
