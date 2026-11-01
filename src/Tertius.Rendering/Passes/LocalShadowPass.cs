using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Renders the stale tiles of the near lights into the local shadow atlas, after the cascades in
/// <see cref="FrameGraph.Stage.ShadowMaps"/>, then the bounds of each tile that it drew, and writes every tile view of the
/// frame. A cached tile draws nothing and keeps its bounds. The pipeline, the bias and the casters' commands:
/// ARCHITECTURE.md, "Local shadows".
/// </summary>
internal sealed unsafe class LocalShadowPass : RenderPass
{
    /// <summary>The width and the height of a workgroup of shadow_bounds.comp, its local size.</summary>
    private const uint BoundsGroupSize = 8;

    private readonly VulkanDevice device;
    private readonly ShadowAtlas atlas;
    private readonly Pipeline pipeline;
    private readonly Pipeline cutoutPipeline;
    private readonly Pipeline boundsPipeline;
    private readonly DescriptorLayouts layouts;
    private readonly List<TileView> views = [];
    private readonly List<TileDraw> draws = [];

    public LocalShadowPass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, ShadowAtlas atlas)
    {
        this.device = device;
        this.atlas = atlas;
        this.layouts = layouts;
        Tiles = new(layers: atlas.Tiles, tileSize: atlas.TileSize);
        (pipeline, cutoutPipeline) = ShadowPass.CreatePipelines(
            device: device,
            shaders: shaders,
            desc: new()
            {
                Name = "local shadow tiles",
                VertexShader = "shadow.vert",
                FragmentShader = null,
                Layout = layouts.SceneLayout,
                ColorFormats = [],
                DepthFormat = atlas.Format,
                VertexInput = VertexInput.PositionOnly,
                DepthTest = true,
                DepthWrite = true,
                DepthCompare = CompareOp.Greater,
                DepthClamp = true,
                CullMode = CullModeFlags.None,
                // Reversed depth: a negative bias moves casters away from the light.
                DepthBiasSlope = -ShadowBias.SlopeBias,
            }
        );
        boundsPipeline = new ComputePipelineDesc
        {
            Name = "local shadow bounds",
            Shader = "shadow_bounds.comp",
            Layout = layouts.ShadowBoundsLayout,
        }.Create(device, shaders);
    }

    public override string Name => "local shadows";

    /// <summary>Which light holds which layer of the atlas, and what each layer holds.</summary>
    public ShadowTiles Tiles { get; }

    public override void Prepare(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        Tiles.Update(
            lights: scene.LightSpan,
            camera: scene.Camera,
            placed: scene.PlacedSpan,
            deformed: scene.Deformed,
            originFrameId: context.Snapshot.OriginFrameId,
            views: views,
            draws: draws
        );
        ReadOnlySpan<PreparedLight> lights = scene.LightSpan;
        foreach (TileView view in views)
            scene.AddShadowTile(GpuShadowTile.Of(light: lights[view.Light], view: view, tileSize: atlas.TileSize));
        ReadOnlySpan<MeshInfo> meshes = context.Scene.Meshes;
        foreach (TileDraw draw in draws)
            scene.DrawnTiles.Add(new(View: draw.View, Commands: Emit(scene: scene, meshes: meshes, draw: draw)));
    }

    /// <summary>
    /// Appends the casters of <paramref name="draw"/>, already in mesh order: the solid run, then the cutout run, as every
    /// opaque pass draws them (docs/plans/skin-and-hair.md, decision 25), with one indirect command per stretch of one mesh.
    /// </summary>
    private OpaqueRuns Emit(PreparedScene scene, ReadOnlySpan<MeshInfo> meshes, TileDraw draw) =>
        new(
            Solid: EmitRun(scene: scene, meshes: meshes, draw: draw, cutout: false),
            Cutout: EmitRun(scene: scene, meshes: meshes, draw: draw, cutout: true)
        );

    /// <summary>Appends the casters of <paramref name="draw"/> that draw in the cutout run, or those that do not, and their commands.</summary>
    private CommandRange EmitRun(PreparedScene scene, ReadOnlySpan<MeshInfo> meshes, TileDraw draw, bool cutout)
    {
        int first = scene.CommandCount,
            currentMesh = -1;
        ReadOnlySpan<PreparedInstance> placed = scene.PlacedSpan;
        for (int i = draw.First; i < draw.First + draw.Count; i++)
        {
            ref readonly PreparedInstance caster = ref placed[Tiles.CasterIndices[i]];
            if (OpaqueRuns.InCutoutRun(caster.Alpha) != cutout)
                continue;
            ShadowPass.AppendCaster(
                scene: scene,
                meshes: meshes,
                instance: ShadowPass.CasterInstance(caster),
                mesh: caster.Mesh,
                currentMesh: ref currentMesh
            );
        }

        return new(first, scene.CommandCount - first);
    }

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        if (scene.DrawnTiles.Count == 0)
            return;
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        ShadowPass.BarrierCasterInputs(context);
        // Not discarded: the layers of cached views keep what earlier frames drew.
        barriers.Image(atlas.Image, ImageUse.DepthAttachment);
        barriers.Flush(vk, commandBuffer);

        var extent = new Extent2D((uint)atlas.TileSize, (uint)atlas.TileSize);
        ShadowPass.BindDepthOnly(context: context, extent: extent);
        foreach (TileCommands drawn in scene.DrawnTiles)
        {
            ref readonly GpuShadowTile tile = ref scene.ShadowTiles[drawn.View];
            var depth = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = atlas.LayerView((int)tile.Layer),
                ImageLayout = ImageLayout.DepthAttachmentOptimal,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                // Reversed depth: 0 is infinitely far from the light, so an empty texel shadows nothing.
                ClearValue = new(depthStencil: new ClearDepthStencilValue(0f, 0)),
            };
            var rendering = new RenderingInfo
            {
                SType = StructureType.RenderingInfo,
                RenderArea = new(new Offset2D(0, 0), extent),
                LayerCount = 1,
                PDepthAttachment = &depth,
            };
            vk.CmdBeginRendering(commandBuffer, &rendering);
            if (drawn.Commands.Count > 0)
            {
                GeometryPass.PushDraw(context: context, viewProjection: tile.ViewProjection);
                GeometryPass.DrawRuns(context: context, runs: drawn.Commands, solid: pipeline, cutout: cutoutPipeline);
            }

            vk.CmdEndRendering(commandBuffer);
        }

        RecordBounds(context);
    }

    /// <summary>
    /// Writes the bounds of each tile that <see cref="Record"/> drew into its own layer, after its depth writes, in one
    /// dispatch per <see cref="ShadowBoundsPushConstants.MaxLayers"/> tiles. The other layers keep the bounds of the tiles
    /// that they hold. Each dispatch waits for the one before it: they write disjoint layers, but through one view of every
    /// layer, which the barriers track as one image.
    /// </summary>
    private void RecordBounds(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        barriers.Image(atlas.Image, ImageUse.SampledCompute);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, boundsPipeline);
        DescriptorSet set = atlas.BoundsSet;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.ShadowBoundsLayout,
            firstSet: 0,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        uint groups = ((uint)atlas.BoundsSize + BoundsGroupSize - 1) / BoundsGroupSize;
        PreparedScene scene = context.Scene;
        List<TileCommands> drawn = scene.DrawnTiles;
        for (int first = 0; first < drawn.Count; first += ShadowBoundsPushConstants.MaxLayers)
        {
            int count = Math.Min(ShadowBoundsPushConstants.MaxLayers, drawn.Count - first);
            var push = new ShadowBoundsPushConstants();
            for (int i = 0; i < count; i++)
                push.Layers[i] = scene.ShadowTiles[drawn[first + i].View].Layer;
            barriers.Image(atlas.Bounds, ImageUse.StorageCompute);
            barriers.Flush(vk, commandBuffer);
            vk.CmdPushConstants(
                commandBuffer: commandBuffer,
                layout: layouts.ShadowBoundsLayout,
                stageFlags: ShaderStageFlags.ComputeBit,
                offset: 0,
                size: (uint)sizeof(ShadowBoundsPushConstants),
                pValues: &push
            );
            vk.CmdDispatch(
                commandBuffer: commandBuffer,
                groupCountX: groups,
                groupCountY: groups,
                groupCountZ: (uint)count
            );
        }
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, pipeline, null);
        device.Vk.DestroyPipeline(device.Device, cutoutPipeline, null);
        device.Vk.DestroyPipeline(device.Device, boundsPipeline, null);
    }
}
