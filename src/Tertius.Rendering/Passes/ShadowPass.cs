using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Renders the cascade sets of the far lights (stage <see cref="FrameGraph.Stage.ShadowMaps"/>).
/// </summary>
/// <remarks>
/// <para><see cref="Prepare"/> runs on the CPU. For each far light whose slot is a <see cref="CascadeSet"/>, it fits the
/// four cascades of that set to this frame's camera along the light's own direction (<see cref="ShadowCascades"/>). It
/// takes its casters from the frame's casters, which the batcher interpolated once (<see cref="PreparedScene.Casters"/>):
/// every caster whose bounding sphere comes within 2 km of the camera, in view or not. Each caster is culled against every
/// cascade box of every set, extruded toward that set's light, and gets one GPU instance per cascade that it touches. The
/// casters are put in mesh order with a counting sort, then each cascade's instances are appended to the frame's instance
/// buffer in that order, with one indirect command per run of one mesh. All of this is linear in the number of casters,
/// sets and meshes, and allocates nothing in a steady state.</para>
/// <para><see cref="Record"/> draws each cascade into its layer of the <see cref="ShadowMap"/> with one depth-only
/// vkCmdDrawIndexedIndirect, from the same global vertex, index and instance buffers as the G-buffer pass. It clears the
/// layers of a set without casters too, because the shading passes sample every set that a light names. Depth clamping
/// pancakes casters between a cascade box and its light onto the box's light side, and the rasterizer adds a slope-scaled
/// depth bias. Faces are not culled, so single-sided meshes such as planes cast shadows too.</para>
/// <para>The texel grid passes through the origin of the camera's reference frame, which the snapshot gives as
/// <see cref="RenderSnapshot.OriginInFrame"/> (<see cref="ShadowCascades.Anchor"/>).</para>
/// </remarks>
internal sealed unsafe class ShadowPass : RenderPass
{
    /// <summary>The caster's slope-scaled depth bias, in multiples of its largest depth change per texel.</summary>
    public const float SlopeBias = 1.5f;

    /// <summary>How far the lighting pass moves its lookup along the geometric normal, in texels, times sin(angle to the light).</summary>
    public const float NormalOffsetTexels = 1f;

    /// <summary>The lighting pass's constant depth bias toward the light, in texels of depth.</summary>
    public const float ConstantBiasTexels = 0.5f;

    /// <summary>Receiver planes are treated as at most this steep: the cosine of their angle to the light.</summary>
    public const float MinimumReceiverCosine = 0.1f;

    private readonly VulkanDevice device;
    private readonly ShadowMap map;
    private readonly Pipeline pipeline;
    private Caster[] casters = new Caster[64];
    private int[] order = new int[64];
    private int[] meshStarts = new int[64];

    public ShadowPass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, ShadowMap map)
    {
        this.device = device;
        this.map = map;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "shadow cascades",
            VertexShader = "shadow.vert",
            FragmentShader = null,
            Layout = layouts.SceneLayout,
            ColorFormats = [],
            DepthFormat = map.Format,
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

    public override string Name => "shadow maps";

    public override void Prepare(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        // The grid passes through the origin of the camera's frame. It is worked out from this snapshot alone, so
        // nothing carries over from tick to tick.
        FitSets(
            scene: scene,
            anchor: ShadowCascades.Anchor(context.Snapshot.OriginInFrame, scene.Camera.Offset),
            mapSize: map.Size,
            depthRangeScale: map.DepthRangeScale
        );
        if (scene.CascadeSetCount == 0)
            return;
        ReadOnlySpan<MeshInfo> meshes = context.Geometry.Meshes;
        int casterCount = SelectCasters(scene);
        // Without a caster the maps would shadow nothing, so none is drawn and the shaders skip them: the frame
        // constants keep 0 cascades per set.
        if (casterCount == 0)
        {
            scene.CascadeSetCount = 0;
            return;
        }

        SortByMesh(casterCount, meshes.Length);
        Emit(scene, meshes, casterCount);
        WriteConstants(scene);
    }

    /// <summary>
    /// Fits the four cascades of each far light whose slot is a <see cref="CascadeSet"/> to the camera, along the light's
    /// own direction, with the texel grid through <paramref name="anchor"/>, and writes them into the frame constants.
    /// Leaves <see cref="PreparedScene.CascadeSetCount"/> at one more than the highest set, or 0 without one.
    /// </summary>
    internal static void FitSets(PreparedScene scene, Double3 anchor, int mapSize, float depthRangeScale)
    {
        foreach (ref readonly PreparedLight light in scene.LightSpan)
        {
            if (light.Shadow is not CascadeSet { Set: var set })
                continue;
            // A far light is at least FarDistance from the eye, so its direction is not zero.
            var basis = LightBasis.FromDirection(light.Position);
            ShadowCascades.Fit(
                camera: scene.Camera,
                light: basis,
                mapSize: mapSize,
                depthRangeScale: depthRangeScale,
                anchor: anchor,
                cascades: ref scene.CascadeSets[set]
            );
            scene.CascadeBases[set] = basis;
            WriteSet(scene: scene, set: set, light: basis);
            scene.CascadeSetCount = Math.Max(scene.CascadeSetCount, set + 1);
        }
    }

    /// <summary>
    /// The cascades of every set that a caster touches, as a mask with the bit <see cref="ShadowMap.Layer"/> for each:
    /// its sphere overlaps the cascade box or the part of the set's light view in front of it, toward the light.
    /// </summary>
    internal static int CascadeMask(PreparedScene scene, in ShadowCaster caster)
    {
        int mask = 0;
        for (int set = 0; set < scene.CascadeSetCount; set++)
        {
            Vector3 lightPosition = scene.CascadeBases[set].ToLight(caster.Position);
            for (int i = 0; i < ShadowCascades.Count; i++)
            {
                ref readonly ShadowCascade cascade = ref scene.CascadeSets[set][i];
                float reach = cascade.HalfExtent + caster.Radius;
                if (
                    MathF.Abs(lightPosition.X - cascade.Center.X) <= reach
                    && MathF.Abs(lightPosition.Y - cascade.Center.Y) <= reach
                    && lightPosition.Z + caster.Radius >= cascade.Center.Z - cascade.DepthHalfRange
                )
                    mask |= 1 << ShadowMap.Layer(set, i);
            }
        }

        return mask;
    }

    /// <summary>Finds the casters within 2 km and the cascades each one touches. Returns the number of casters.</summary>
    private int SelectCasters(PreparedScene scene)
    {
        int casterCount = 0,
            instanceCount = 0;
        foreach (ref readonly ShadowCaster caster in scene.CasterSpan)
        {
            if (!ShadowCascades.TakesCaster(caster.Distance, caster.Radius))
                continue;
            int mask = CascadeMask(scene, caster);
            if (mask == 0)
                continue;
            if (casterCount == casters.Length)
                Array.Resize(ref casters, casters.Length * 2);
            // The depth-only pass reads no material.
            casters[casterCount++] = new(
                GpuInstance.From(
                    position: caster.Position,
                    rotation: caster.Rotation,
                    scale: caster.Scale,
                    material: MaterialStore.DefaultSlot
                ),
                caster.Mesh,
                mask
            );
            instanceCount += BitOperations.PopCount((uint)mask);
        }

        scene.ShadowCasters = casterCount;
        scene.ShadowInstances = instanceCount;
        return casterCount;
    }

    /// <summary>Fills <see cref="order"/> with the caster indices in mesh order, keeping snapshot order within a mesh.</summary>
    private void SortByMesh(int casterCount, int meshCount)
    {
        if (order.Length < casterCount)
            Array.Resize(ref order, Math.Max(casterCount, order.Length * 2));
        if (meshStarts.Length < meshCount + 1)
            Array.Resize(ref meshStarts, Math.Max(meshCount + 1, meshStarts.Length * 2));
        Span<int> starts = meshStarts.AsSpan(0, meshCount + 1);
        starts.Clear();
        for (int i = 0; i < casterCount; i++)
            starts[casters[i].Mesh + 1]++;
        for (int m = 1; m <= meshCount; m++)
            starts[m] += starts[m - 1];
        for (int i = 0; i < casterCount; i++)
            order[starts[casters[i].Mesh]++] = i;
    }

    /// <summary>For each cascade of each set, appends its casters' instances in mesh order and one indirect command per mesh.</summary>
    private void Emit(PreparedScene scene, ReadOnlySpan<MeshInfo> meshes, int casterCount)
    {
        ReadOnlySpan<int> sorted = order.AsSpan(0, casterCount);
        for (int layer = 0; layer < scene.CascadeSetCount * ShadowCascades.Count; layer++)
        {
            int bit = 1 << layer,
                currentMesh = -1,
                rangeStart = scene.CommandCount;
            foreach (int index in sorted)
            {
                ref readonly Caster caster = ref casters[index];
                if ((caster.Mask & bit) == 0)
                    continue;
                AppendCaster(
                    scene: scene,
                    meshes: meshes,
                    instance: caster.Instance,
                    mesh: caster.Mesh,
                    currentMesh: ref currentMesh
                );
            }

            scene.ShadowCommands[layer] = new(rangeStart, scene.CommandCount - rangeStart);
        }
    }

    /// <summary>
    /// Appends one caster of a run in mesh order: its instance, and either one more instance on the run's last indirect
    /// command, when its mesh is <paramref name="currentMesh"/>, or a new command for its mesh, which becomes the current one.
    /// </summary>
    internal static void AppendCaster(
        PreparedScene scene,
        ReadOnlySpan<MeshInfo> meshes,
        in GpuInstance instance,
        int mesh,
        ref int currentMesh
    )
    {
        int gpuIndex = scene.AppendInstance(instance);
        if (mesh == currentMesh)
        {
            scene.Commands[scene.CommandCount - 1].InstanceCount++;
            return;
        }

        currentMesh = mesh;
        MeshInfo info = meshes[mesh];
        scene.AppendCommand(
            new()
            {
                IndexCount = info.IndexCount,
                InstanceCount = 1,
                FirstIndex = info.FirstIndex,
                VertexOffset = info.VertexOffset,
                FirstInstance = (uint)gpuIndex,
            }
        );
    }

    /// <summary>Writes the matrices and the light-space axes of one cascade set into the frame constants.</summary>
    private static void WriteSet(PreparedScene scene, int set, in LightBasis light)
    {
        ref ShadowCascadeArray cascades = ref scene.CascadeSets[set];
        ref GpuCascadeSet gpu = ref scene.Constants.CascadeSets[set];
        for (int i = 0; i < ShadowCascades.Count; i++)
            gpu.ViewProj[i] = cascades[i].ViewProjection;
        // Every box has the same shape: a light-space slope s moves depth by s · h / d per unit of uv.
        gpu.AxisX = new(light.X, cascades[0].HalfExtent / cascades[0].DepthHalfRange);
        gpu.AxisY = new(light.Y, 0f);
    }

    /// <summary>Writes what every cascade set shares into the frame constants: the splits, the texels and the biases.</summary>
    private void WriteConstants(PreparedScene scene)
    {
        ref GpuFrameConstants constants = ref scene.Constants;
        // Every set is fitted to the same camera with the same map, so the slices and the texels are the same in each.
        ref ShadowCascadeArray cascades = ref scene.CascadeSets[0];
        constants.Counts = constants.Counts with { Z = ShadowCascades.Count };
        constants.ShadowSplits = new(
            x: ShadowCascades.Split(0),
            y: ShadowCascades.Split(1),
            z: ShadowCascades.Split(2),
            w: ShadowCascades.Split(3)
        );
        constants.ShadowBlendStart = new(
            x: ShadowCascades.BlendStart(0),
            y: ShadowCascades.BlendStart(1),
            z: ShadowCascades.BlendStart(2),
            w: ShadowCascades.BlendStart(3)
        );
        constants.ShadowTexel = new(
            x: cascades[0].TexelSize,
            y: cascades[1].TexelSize,
            z: cascades[2].TexelSize,
            w: cascades[3].TexelSize
        );
        float depthPerUv = cascades[0].HalfExtent / cascades[0].DepthHalfRange;
        // One texel of world distance along the light is depthPerUv / size in [0, 1] depth units.
        constants.ShadowParams = new(
            x: 1f / map.Size,
            y: NormalOffsetTexels,
            z: ConstantBiasTexels * depthPerUv / map.Size,
            w: MinimumReceiverCosine
        );
    }

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        if (scene.CascadeSetCount == 0)
            return;
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        barriers.Image(map.Image, ImageUse.DepthAttachment, discard: true);
        barriers.Flush(vk, commandBuffer);

        var extent = new Extent2D((uint)map.Size, (uint)map.Size);
        for (int layer = 0; layer < scene.CascadeSetCount * ShadowCascades.Count; layer++)
        {
            (int set, int cascade) = Math.DivRem(layer, ShadowCascades.Count);
            var depth = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = map.LayerView(set, cascade),
                ImageLayout = ImageLayout.DepthAttachmentOptimal,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                // Reversed depth: 0 is the far side of the cascade, so an empty texel shadows nothing. A set without
                // casters is cleared too, because its light's slot names it.
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
            // Every layer shares the pipeline, the frame set, the viewport and the geometry, which stay bound.
            if (layer == 0)
                BindDepthOnly(context: context, pipeline: pipeline, extent: extent);

            CommandRange commands = scene.ShadowCommands[layer];
            if (commands.Count > 0)
            {
                GeometryPass.PushDraw(context: context, viewProjection: scene.CascadeSets[set][cascade].ViewProjection);
                GeometryPass.DrawIndirect(context, commands);
            }

            vk.CmdEndRendering(commandBuffer);
        }
    }

    /// <summary>
    /// Binds what every layer of a depth-only shadow image shares: <paramref name="pipeline"/>, the frame set, a viewport
    /// of <paramref name="extent"/> and the geometry. The cascades and the local shadow atlas both use it.
    /// </summary>
    internal static void BindDepthOnly(FrameContext context, Pipeline pipeline, Extent2D extent)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        // Only the frame set: the G-buffer set holds this very image as a sampled descriptor.
        DescriptorSet frameSet = context.Frame.FrameSet;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Graphics,
            layout: context.Layouts.SceneLayout,
            firstSet: 0,
            descriptorSetCount: 1,
            pDescriptorSets: &frameSet,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        context.SetViewport(extent);
        GeometryPass.BindGeometry(context);
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);

    /// <summary>A selected caster: its GPU instance, its mesh and a bit for each cascade of each set that it touches.</summary>
    private readonly record struct Caster(GpuInstance Instance, int Mesh, int Mask);
}
