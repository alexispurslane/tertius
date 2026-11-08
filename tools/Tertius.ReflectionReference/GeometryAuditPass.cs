using System.Security.Cryptography;
using System.Text.Json;
using Silk.NET.Vulkan;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal sealed unsafe class GeometryAuditPass : Rendering.RenderPass
{
    private readonly VulkanRenderer renderer;
    private readonly uint plateMaterial;
    private readonly uint emitterMaterial;
    private readonly GpuImage[] colors = new GpuImage[4];
    private readonly GpuImage[] originals = new GpuImage[3];
    private readonly (GpuBuffer Before, GpuBuffer After)[] snapshots = new (GpuBuffer Before, GpuBuffer After)[3];
    private VulkanDevice? device;
    private GpuImage? depth;
    private ShaderLibrary? shaders;
    private SidedPipeline solid;
    private SidedPipeline cutout;
    private string meshHash = "";
    private string fragmentHash = "";
    private JsonElement metadata;

    private GeometryAuditPass(VulkanRenderer renderer, uint plateMaterial, uint emitterMaterial)
    {
        this.renderer = renderer;
        this.plateMaterial = plateMaterial;
        this.emitterMaterial = emitterMaterial;
    }

    public override string Name => "research direct geometry audit";

    public static GeometryAuditPass On(VulkanRenderer renderer, uint plateMaterial, uint emitterMaterial)
    {
        var audit = new GeometryAuditPass(renderer, plateMaterial, emitterMaterial);
        renderer.Graph.Add(FrameGraph.Stage.Composite, audit);
        return audit;
    }

    public override void Prepare(FrameContext context)
    {
        if (device is not null)
            return;
        byte[] toolMesh = ShaderLibrary.LoadSpirv(typeof(GeometryAuditPass).Assembly, "mesh.vert");
        byte[] productionMesh = ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, "mesh.vert");
        if (!toolMesh.AsSpan().SequenceEqual(productionMesh))
            throw new InvalidOperationException(
                "Audit mesh.vert SPIR-V differs from the frozen production shader; no audit draw is allowed."
            );
        meshHash = Convert.ToHexStringLower(SHA256.HashData(toolMesh));
        fragmentHash = Convert.ToHexStringLower(
            SHA256.HashData(ShaderLibrary.LoadSpirv(typeof(GeometryAuditPass).Assembly, "geometry_audit.frag"))
        );
        device = context.Device;
        shaders = new ShaderLibrary(device, typeof(GeometryAuditPass).Assembly);
        ShaderConstants constants = renderer.ShadingConstants with { WritesMotion = true, MainView = true };
        var desc = new GraphicsPipelineDesc
        {
            Name = Name,
            VertexShader = "mesh.vert",
            FragmentShader = "geometry_audit.frag",
            Layout = context.Layouts.SceneLayout,
            ColorFormats = [.. Enumerable.Repeat(Format.R32G32Sfloat, 4)],
            DepthFormat = RenderTargets.DepthFormat,
            VertexInput = VertexInput.Mesh,
            DepthTest = true,
            DepthWrite = true,
            DepthCompare = CompareOp.Greater,
            Constants = constants,
        };
        solid = SidedPipeline.Create(device: device, shaders: shaders, desc: desc);
        cutout = SidedPipeline.Create(
            device: device,
            shaders: shaders,
            desc: desc with
            {
                Name = Name + " cutout",
                Constants = constants with { CutoutAlpha = true },
            }
        );
        for (int attachment = 0; attachment < colors.Length; attachment++)
            colors[attachment] = GpuImage.Create(
                device: device,
                allocator: renderer.Allocator,
                extent: context.Extent,
                format: Format.R32G32Sfloat,
                usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
                mipLevels: 1,
                name: $"{Name} {attachment}"
            );
        depth = GpuImage.Create(
            device: device,
            allocator: renderer.Allocator,
            extent: context.Extent,
            format: RenderTargets.DepthFormat,
            usage: ImageUsageFlags.DepthStencilAttachmentBit,
            mipLevels: 1,
            name: Name + " owned depth"
        );
        originals[0] = context.Targets.Distance;
        originals[1] = renderer.ScreenTraceSignals.Specular.Slow(0);
        originals[2] = context.Targets.Hdr;
        for (int index = 0; index < originals.Length; index++)
        {
            ulong bytesPerPixel = index == 0 ? 4UL : 8UL;
            ulong size = context.Extent.Width * (ulong)context.Extent.Height * bytesPerPixel;
            GpuBuffer Create(string suffix) =>
                GpuBuffer.Create(
                    device: device,
                    allocator: renderer.Allocator,
                    size: size,
                    usage: BufferUsageFlags.TransferDstBit,
                    memoryUsage: MemoryUsage.Readback,
                    name: $"{Name} {originals[index].Name} {suffix}"
                );
            snapshots[index] = (Create("before"), Create("after"));
        }
    }

    public override void Record(FrameContext context)
    {
        GpuImage ownedDepth = depth ?? throw new InvalidOperationException("Audit attachments are not prepared.");
        GpuImage[] owned = [.. colors, ownedDepth];
        GpuImage[] forbidden = [.. context.Targets.GBuffer, context.Targets.Depth, originals[1], originals[2]];
        if (
            owned.Select(image => image.Handle.Handle).Distinct().Count() != owned.Length
            || owned.Any(image => forbidden.Any(original => original.Handle.Handle == image.Handle.Handle))
        )
            throw new InvalidOperationException("An audit attachment aliases a live attachment.");
        ResourceState[] before = [.. originals.Select(image => image.State)];
        for (int index = 0; index < originals.Length; index++)
            Copy(context: context, image: originals[index], destination: snapshots[index].Before);
        int callsBefore = context.IndirectDrawCalls;
        context.Barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        context.Barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        context.Barriers.Buffer(context.Materials.Buffer, BufferUse.ShaderRead);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        context.BindSceneSets();
        context.SetFullViewport();
        GeometryPass.BindGeometry(context);
        for (int range = DepthRanges.Far; range <= DepthRanges.Near; range++)
        {
            BeginOwnedRange(context, range, ownedDepth);
            SidedRuns runs = context.Scene.Opaque[range];
            if (runs.Count > 0)
            {
                GeometryPass.PushDraw(context: context, viewProjection: context.Scene.RangeViewProjection[range]);
                GeometryPass.DrawRuns(context: context, runs: runs, solid: solid, cutout: cutout);
            }
            context.Vk.CmdEndRendering(context.CommandBuffer);
        }
        int extraDrawCalls = context.IndirectDrawCalls - callsBefore;
        for (int index = 0; index < originals.Length; index++)
            Copy(context: context, image: originals[index], destination: snapshots[index].After);
        metadata = JsonSerializer.SerializeToElement(
            new
            {
                Scope = "Same-frame direct geometry measurement, after the read-only probe. No original-frame advancement or light integration.",
                MeshSpirvSha256 = meshHash,
                ProductionMeshSpirvSha256 = meshHash,
                MeshBytesEqual = true,
                FragmentSpirvSha256 = fragmentHash,
                OriginalRendererFrames = 1,
                AuditIndirectDrawCalls = extraDrawCalls,
                AuditRangeRenderingInstances = DepthRanges.Count,
                VertexInput = "Mesh",
                Depth = "owned D32Sfloat, Greater reversed-Z, depth test/write; original per-range clears",
                Constants = new
                {
                    WritesMotion = true,
                    MainView = true,
                    SolidCutoutAlpha = false,
                    CutoutCutoutAlpha = true,
                    OneSidedDropsFibers = true,
                },
                Culling = "Original SidedPipeline variants: one-sided BackBit/CounterClockwise, two-sided None/CounterClockwise; solid then cutout DrawRuns order",
                RangeOrder = new[] { DepthRanges.Far, DepthRanges.Middle, DepthRanges.Near },
                OwnedAttachments = owned.Select(image => new
                {
                    image.Name,
                    Image = image.Handle.Handle,
                    View = image.View.Handle,
                    Format = image.Format.ToString(),
                }),
                NeverAttached = forbidden.Select(image => new
                {
                    image.Name,
                    Image = image.Handle.Handle,
                    View = image.View.Handle,
                }),
                DescriptorAudit = "SceneLayout sets0..2 only. Unchanged mesh.vert and geometry_audit.frag include readonly frame/material/motion buffers and sampled textures. No storage image, writable buffer, CompositeLightSet or original output attachment is used by the audit shaders.",
                Snapshots = originals.Select(
                    (image, index) =>
                        new
                        {
                            image.Name,
                            Format = image.Format.ToString(),
                            image.Extent.Width,
                            image.Extent.Height,
                            BeforeState = Describe(before[index]),
                            AfterState = Describe(image.State),
                            ExactTrackedStateRestored = before[index].Layout == image.State.Layout
                                && before[index].Stages == image.State.Stages
                                && before[index].Access == image.State.Access,
                        }
                ),
                SnapshotMethod = "Whole-image raw vkCmdCopyImageToBuffer before and after the audit, in this renderer command buffer; transfer-to-host barriers and completed-GPU wait before mapped bytes are read. Original layouts/stage/access are restored after each copy.",
            }
        );
    }

    private void BeginOwnedRange(FrameContext context, int range, GpuImage ownedDepth)
    {
        bool first = range == DepthRanges.Far;
        foreach (GpuImage image in colors)
            context.Barriers.Image(image, ImageUse.ColorAttachment, discard: first);
        context.Barriers.Image(ownedDepth, ImageUse.DepthAttachment, discard: true);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        RenderingAttachmentInfo* attachments = stackalloc RenderingAttachmentInfo[4];
        for (int index = 0; index < colors.Length; index++)
            attachments[index] = new()
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = colors[index].View,
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = first ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                StoreOp = AttachmentStoreOp.Store,
                ClearValue = new(new ClearColorValue(float32_0: 0, float32_1: 0, float32_2: 0, float32_3: 0)),
            };
        var depthAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = ownedDepth.View,
            ImageLayout = ImageLayout.DepthAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            ClearValue = new(depthStencil: new ClearDepthStencilValue(DepthRanges.ClearDepth(range), 0)),
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), context.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 4,
            PColorAttachments = attachments,
            PDepthAttachment = &depthAttachment,
        };
        context.Vk.CmdBeginRendering(context.CommandBuffer, &rendering);
    }

    private static void Copy(FrameContext context, GpuImage image, GpuBuffer destination)
    {
        ResourceState state = image.State;
        context.Barriers.Image(image, ImageUse.TransferSource);
        context.Barriers.Buffer(destination, BufferUse.TransferDestination);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
        var region = new BufferImageCopy
        {
            ImageSubresource = new(aspectMask: image.Aspect, mipLevel: 0, baseArrayLayer: 0, layerCount: 1),
            ImageExtent = new(image.Extent.Width, image.Extent.Height, 1),
        };
        context.Vk.CmdCopyImageToBuffer(
            commandBuffer: context.CommandBuffer,
            srcImage: image.Handle,
            srcImageLayout: ImageLayout.TransferSrcOptimal,
            dstBuffer: destination.Handle,
            regionCount: 1,
            pRegions: &region
        );
        context.Barriers.Buffer(destination, BufferUse.HostRead);
        context.Barriers.ImageLevels(
            image: image,
            baseMip: 0,
            mipCount: image.MipLevels,
            oldLayout: ImageLayout.TransferSrcOptimal,
            newLayout: state.Layout,
            srcStages: PipelineStageFlags2.AllTransferBit,
            srcAccess: AccessFlags2.TransferReadBit,
            dstStages: state.Stages,
            dstAccess: state.Access
        );
        image.State = state;
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
    }

    public GeometryAuditFrame ReadCompleted(ReferenceCrop crop)
    {
        VulkanDevice active = device ?? throw new InvalidOperationException("No geometry audit was recorded.");
        active.WaitIdle();
        float[][] fields = [.. colors.Select(renderer.ReadImage)];
        float[] values = new float[crop.Width * crop.Height * 8];
        for (int pixel = 0; pixel < crop.Width * crop.Height; pixel++)
        {
            int source =
                ((crop.Top + pixel / crop.Width) * (int)colors[0].Extent.Width + crop.Left + pixel % crop.Width) * 2;
            for (int field = 0; field < 4; field++)
            {
                values[pixel * 8 + field * 2] = fields[field][source];
                values[pixel * 8 + field * 2 + 1] = fields[field][source + 1];
            }
        }
        string[] names = ["distance", "history", "composition"];
        static byte[] Bytes(GpuBuffer buffer) =>
            new ReadOnlySpan<byte>(buffer.Mapped, checked((int)buffer.Size)).ToArray();
        GeometrySnapshot[] retained =
        [
            .. snapshots.Select(
                (snapshot, index) => new GeometrySnapshot(names[index], Bytes(snapshot.Before), Bytes(snapshot.After))
            ),
        ];
        return new(Values: values, Snapshots: retained, Metadata: metadata)
        {
            PlateMaterial = plateMaterial,
            EmitterMaterial = emitterMaterial,
        };
    }

    private static object Describe(ResourceState state) =>
        new
        {
            Layout = state.Layout.ToString(),
            Stages = state.Stages.ToString(),
            Access = state.Access.ToString(),
        };

    public override void Dispose()
    {
        foreach (GpuImage? image in colors)
            image?.Dispose();
        depth?.Dispose();
        foreach ((GpuBuffer? before, GpuBuffer? after) in snapshots)
        {
            before?.Dispose();
            after?.Dispose();
        }
        if (device is { } active)
        {
            solid.Destroy(active);
            cutout.Destroy(active);
        }
        shaders?.Dispose();
        base.Dispose();
    }
}
