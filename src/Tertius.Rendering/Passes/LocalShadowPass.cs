using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Renders the stale tiles of the near lights into the local shadow atlas (stage <see cref="FrameGraph.Stage.ShadowMaps"/>,
/// after the cascades; docs/plans/local-light-shadows.md, "Point and spot light shadows").
/// </summary>
/// <remarks>
/// <para><see cref="Prepare"/> runs on the CPU. <see cref="ShadowTiles.Update"/> gives the near lights their tiles and
/// slots and says which views are stale, with their casters in mesh order. The pass writes every view of the frame into
/// <see cref="PreparedScene.ShadowTiles"/>, and appends each stale view's casters to the frame's instance buffer with one
/// indirect command per run of one mesh. A cached view draws nothing.</para>
/// <para><see cref="Record"/> clears each stale view's layer and draws its casters with the depth-only pipeline of the
/// cascades: depth clamping pancakes a caster nearer than the near plane onto it, and faces are not culled. Its
/// slope-scaled bias is <see cref="SlopeBias"/>, larger than the cascades', because a tile's lookup has no receiver-plane
/// term. The other layers keep what earlier frames drew.</para>
/// </remarks>
internal sealed unsafe class LocalShadowPass : RenderPass
{
    /// <summary>The casters' slope-scaled depth bias, in multiples of their largest depth change per texel.</summary>
    public const float SlopeBias = 2.5f;

    private readonly VulkanDevice device;
    private readonly ShadowAtlas atlas;
    private readonly Pipeline pipeline;
    private readonly List<TileView> views = [];
    private readonly List<TileDraw> draws = [];

    public LocalShadowPass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, ShadowAtlas atlas)
    {
        this.device = device;
        this.atlas = atlas;
        Tiles = new(layers: atlas.Tiles, tileSize: atlas.TileSize);
        pipeline = new GraphicsPipelineDesc
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
            DepthBiasSlope = -SlopeBias,
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
            casters: scene.CasterSpan,
            originFrameId: context.Snapshot.OriginFrameId,
            views: views,
            draws: draws
        );
        ReadOnlySpan<PreparedLight> lights = scene.LightSpan;
        foreach (TileView view in views)
            scene.AddShadowTile(GpuShadowTile.Of(light: lights[view.Light], view: view, tileSize: atlas.TileSize));
        ReadOnlySpan<MeshInfo> meshes = context.Geometry.Meshes;
        foreach (TileDraw draw in draws)
            scene.DrawnTiles.Add(new(View: draw.View, Commands: Emit(scene: scene, meshes: meshes, draw: draw)));
    }

    /// <summary>Appends the casters of <paramref name="draw"/>, already in mesh order, and one indirect command per run of one mesh.</summary>
    private CommandRange Emit(PreparedScene scene, ReadOnlySpan<MeshInfo> meshes, TileDraw draw)
    {
        int first = scene.CommandCount,
            currentMesh = -1;
        ReadOnlySpan<ShadowCaster> casters = scene.CasterSpan;
        for (int i = draw.First; i < draw.First + draw.Count; i++)
        {
            ref readonly ShadowCaster caster = ref casters[Tiles.CasterIndices[i]];
            // The depth-only pass reads no material.
            var instance = GpuInstance.From(
                position: caster.Position,
                rotation: caster.Rotation,
                scale: caster.Scale,
                material: MaterialStore.DefaultSlot
            );
            ShadowPass.AppendCaster(
                scene: scene,
                meshes: meshes,
                instance: instance,
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
        barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        // Not discarded: the layers of cached views keep what earlier frames drew.
        barriers.Image(atlas.Image, ImageUse.DepthAttachment);
        barriers.Flush(vk, commandBuffer);

        var extent = new Extent2D((uint)atlas.TileSize, (uint)atlas.TileSize);
        ShadowPass.BindDepthOnly(context: context, pipeline: pipeline, extent: extent);
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
                GeometryPass.DrawIndirect(context, drawn.Commands);
            }

            vk.CmdEndRendering(commandBuffer);
        }
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
