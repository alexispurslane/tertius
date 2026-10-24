using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Renders the sun's cascaded shadow maps (stage <see cref="FrameGraph.Stage.ShadowMaps"/>).
/// </summary>
/// <remarks>
/// <para><see cref="Prepare"/> runs on the CPU. It fits the cascades for this frame's camera
/// (<see cref="ShadowCascades"/>) and selects casters from the whole snapshot, not from the view-culled scene: every
/// instance with <see cref="RenderFlags.CastsShadow"/> whose bounding sphere comes within 2 km of the camera, in any
/// depth range, in view or not, and whether or not it is <see cref="RenderFlags.Visible"/> (an invisible caster casts
/// a shadow only). Each caster is culled against every cascade's box, extruded toward the sun, and gets one GPU
/// instance per cascade it touches. The casters are put in mesh order with a counting sort, then each cascade's
/// instances are appended to the frame's instance buffer in that order, with one indirect command per run of one
/// mesh. All of this is linear in the number of casters and meshes, and allocates nothing in a steady state.</para>
/// <para><see cref="Record"/> draws each cascade into its layer of the <see cref="ShadowMap"/> with one depth-only
/// vkCmdDrawIndexedIndirect, from the same global vertex, index and instance buffers as the G-buffer pass. Depth
/// clamping pancakes casters between the cascade box and the sun onto its sun-side face, and the rasterizer adds a
/// slope-scaled depth bias. Faces are not culled, so single-sided meshes such as planes cast shadows too.</para>
/// <para>The texel grid passes through the origin of the camera's reference frame, which the snapshot gives as
/// <see cref="RenderSnapshot.OriginInFrame"/> (<see cref="ShadowCascades.Anchor"/>).</para>
/// <para>Without a sun (<see cref="SceneBatcher.HasSun"/>: a zero or negative intensity turns it off), or without
/// any caster, nothing is drawn and the lighting pass skips the shadow maps.</para>
/// </remarks>
internal sealed unsafe class ShadowPass : RenderPass
{
    /// <summary>The caster's slope-scaled depth bias, in multiples of its largest depth change per texel.</summary>
    public const float SlopeBias = 1.5f;

    /// <summary>How far the lighting pass moves its lookup along the geometric normal, in texels, times sin(angle to the sun).</summary>
    public const float NormalOffsetTexels = 1f;

    /// <summary>The lighting pass's constant depth bias toward the sun, in texels of depth.</summary>
    public const float ConstantBiasTexels = 0.5f;

    /// <summary>Receiver planes are treated as at most this steep: the cosine of their angle to the sun.</summary>
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
            // Reversed depth: a negative bias moves casters away from the sun.
            DepthBiasSlope = -SlopeBias
        }.Create(device, shaders);
    }

    public override string Name => "shadow maps";

    public override void Prepare(FrameContext context)
    {
        RenderSnapshot snapshot = context.Snapshot;
        PreparedScene scene = context.Scene;
        // Sun.Intensity <= 0 (or no direction) turns the sun off, and its shadow maps with it.
        RenderSun sun = snapshot.Sun;
        if (!SceneBatcher.HasSun(sun))
            return;
        CameraView camera = scene.Camera;
        LightBasis light = LightBasis.FromSunDirection(sun.Direction);
        // The grid passes through the origin of the camera's frame. It is worked out from this snapshot alone, so
        // nothing carries over from tick to tick.
        Double3 anchor = ShadowCascades.Anchor(snapshot.OriginInFrame, camera.Offset);
        ShadowCascades.Fit(camera: camera, light: light, mapSize: map.Size, depthRangeScale: map.DepthRangeScale,
            anchor: anchor, cascades: ref scene.Cascades);

        ReadOnlySpan<MeshInfo> meshes = context.Geometry.Meshes;
        int casterCount = SelectCasters(snapshot: snapshot, alpha: SceneBatcher.ClampAlpha(context.Alpha),
            meshes: meshes, light: light, scene: scene);
        if (casterCount == 0)
            return;
        SortByMesh(casterCount, meshes.Length);
        Emit(scene, meshes, casterCount);
        scene.ShadowCascadeCount = ShadowCascades.Count;
        WriteConstants(scene, light);
    }

    /// <summary>Finds the casters and the cascades each one touches. Returns the number of casters.</summary>
    private int SelectCasters(RenderSnapshot snapshot, float alpha, ReadOnlySpan<MeshInfo> meshes, in LightBasis light,
        PreparedScene scene)
    {
        Vector3 offset = scene.Camera.Offset;
        int casterCount = 0, instanceCount = 0;
        foreach (ref readonly RenderInstance instance in CollectionsMarshal.AsSpan(snapshot.Instances))
        {
            // CastsShadow alone decides: an instance that is not Visible still casts its shadow.
            if ((instance.Flags & RenderFlags.CastsShadow) == 0)
                continue;
            int mesh = SceneBatcher.MeshIndex(instance.Mesh, meshes.Length);
            if (mesh < 0 || !SceneBatcher.HasVolume(instance.Scale))
                continue;
            Vector3 position = Vector3.Lerp(instance.PreviousPosition, instance.Position, alpha) - offset;
            float radius = SceneBatcher.BoundingRadius(instance, meshes[mesh]);
            if (!float.IsFinite(position.X + position.Y + position.Z + radius) ||
                !ShadowCascades.TakesCaster(position.Length(), radius))
                continue;

            // The caster can shadow a cascade's receivers if its sphere overlaps the cascade box or the part of the
            // light's view in front of it, toward the sun.
            Vector3 lightPosition = light.ToLight(position);
            int mask = 0;
            for (int i = 0; i < ShadowCascades.Count; i++)
            {
                ref readonly ShadowCascade cascade = ref scene.Cascades[i];
                float reach = cascade.HalfExtent + radius;
                if (MathF.Abs(lightPosition.X - cascade.Center.X) <= reach &&
                    MathF.Abs(lightPosition.Y - cascade.Center.Y) <= reach &&
                    lightPosition.Z + radius >= cascade.Center.Z - cascade.DepthHalfRange)
                    mask |= 1 << i;
            }

            if (mask == 0)
                continue;

            if (casterCount == casters.Length)
                Array.Resize(ref casters, casters.Length * 2);
            Quaternion rotation = SceneBatcher.Interpolate(instance.PreviousRotation, instance.Rotation, alpha);
            // The depth-only pass reads no material.
            casters[casterCount++] =
                new(
                    GpuInstance.From(position: position, rotation: rotation, scale: instance.Scale,
                        material: MaterialStore.DefaultSlot), mesh, mask);
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

    /// <summary>For each cascade, appends its casters' instances in mesh order and one indirect command per mesh.</summary>
    private void Emit(PreparedScene scene, ReadOnlySpan<MeshInfo> meshes, int casterCount)
    {
        ReadOnlySpan<int> sorted = order.AsSpan(0, casterCount);
        for (int cascade = 0; cascade < ShadowCascades.Count; cascade++)
        {
            int bit = 1 << cascade, currentMesh = -1, rangeStart = scene.CommandCount;
            foreach (int index in sorted)
            {
                ref readonly Caster caster = ref casters[index];
                if ((caster.Mask & bit) == 0)
                    continue;
                int gpuIndex = scene.AppendInstance(caster.Instance);
                if (caster.Mesh == currentMesh)
                {
                    scene.Commands[scene.CommandCount - 1].InstanceCount++;
                    continue;
                }

                currentMesh = caster.Mesh;
                MeshInfo info = meshes[caster.Mesh];
                scene.AppendCommand(new()
                {
                    IndexCount = info.IndexCount,
                    InstanceCount = 1,
                    FirstIndex = info.FirstIndex,
                    VertexOffset = info.VertexOffset,
                    FirstInstance = (uint)gpuIndex
                });
            }

            scene.ShadowCommands[cascade] = new(rangeStart, scene.CommandCount - rangeStart);
        }
    }

    private void WriteConstants(PreparedScene scene, in LightBasis light)
    {
        ref GpuFrameConstants constants = ref scene.Constants;
        ref ShadowCascadeArray cascades = ref scene.Cascades;
        constants.ShadowCascadeCount = ShadowCascades.Count;
        for (int i = 0; i < ShadowCascades.Count; i++)
            constants.ShadowViewProj[i] = cascades[i].ViewProjection;
        constants.ShadowSplits = new(x: ShadowCascades.Split(0), y: ShadowCascades.Split(1), z: ShadowCascades.Split(2),
            w: ShadowCascades.Split(3));
        constants.ShadowBlendStart = new(x: ShadowCascades.BlendStart(0), y: ShadowCascades.BlendStart(1),
            z: ShadowCascades.BlendStart(2), w: ShadowCascades.BlendStart(3));
        constants.ShadowTexel = new(x: cascades[0].TexelSize, y: cascades[1].TexelSize, z: cascades[2].TexelSize,
            w: cascades[3].TexelSize);
        // Every box has the same shape: a light-space slope s moves depth by s · h / d per unit of uv.
        float depthPerUv = cascades[0].HalfExtent / cascades[0].DepthHalfRange;
        constants.ShadowAxisX = new(light.X, depthPerUv);
        constants.ShadowAxisY = new(light.Y, 0f);
        // One texel of world distance along the light is depthPerUv / size in [0, 1] depth units.
        constants.ShadowParams = new(x: 1f / map.Size, y: NormalOffsetTexels,
            z: ConstantBiasTexels * depthPerUv / map.Size,
            w: MinimumReceiverCosine);
    }

    public override void Record(FrameContext context)
    {
        PreparedScene scene = context.Scene;
        if (scene.ShadowCascadeCount == 0)
            return;
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        barriers.Image(map.Image, ImageUse.DepthAttachment, discard: true);
        barriers.Flush(vk, commandBuffer);

        var extent = new Extent2D((uint)map.Size, (uint)map.Size);
        for (int i = 0; i < scene.ShadowCascadeCount; i++)
        {
            var depth = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = map.LayerView(i),
                ImageLayout = ImageLayout.DepthAttachmentOptimal,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = AttachmentStoreOp.Store,
                // Reversed depth: 0 is the far side of the cascade, so an empty texel shadows nothing.
                ClearValue = new(depthStencil: new ClearDepthStencilValue(0f, 0))
            };
            var rendering = new RenderingInfo
            {
                SType = StructureType.RenderingInfo,
                RenderArea = new(new Offset2D(0, 0), extent),
                LayerCount = 1,
                PDepthAttachment = &depth
            };
            vk.CmdBeginRendering(commandBuffer, &rendering);
            if (i == 0)
            {
                vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
                // Only the frame set: the G-buffer set holds this very image as a sampled descriptor.
                DescriptorSet frameSet = context.Frame.FrameSet;
                vk.CmdBindDescriptorSets(commandBuffer: commandBuffer, pipelineBindPoint: PipelineBindPoint.Graphics,
                    layout: context.Layouts.SceneLayout, firstSet: 0, descriptorSetCount: 1,
                    pDescriptorSets: &frameSet, dynamicOffsetCount: 0, pDynamicOffsets: null);
                var viewport = new Viewport(x: 0, y: 0, width: map.Size, height: map.Size, minDepth: 0, maxDepth: 1);
                var scissor = new Rect2D(new Offset2D(0, 0), extent);
                vk.CmdSetViewport(commandBuffer: commandBuffer, firstViewport: 0, viewportCount: 1,
                    pViewports: &viewport);
                vk.CmdSetScissor(commandBuffer: commandBuffer, firstScissor: 0, scissorCount: 1, pScissors: &scissor);
                GeometryPass.BindGeometry(context);
            }

            CommandRange commands = scene.ShadowCommands[i];
            if (commands.Count > 0)
            {
                GeometryPass.PushDraw(context: context, viewProjection: scene.Cascades[i].ViewProjection);
                GeometryPass.DrawIndirect(context, commands);
            }

            vk.CmdEndRendering(commandBuffer);
        }
    }

    public override void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);

    /// <summary>A selected caster: its GPU instance, its mesh and a bit for each cascade it touches.</summary>
    private readonly record struct Caster(GpuInstance Instance, int Mesh, int Mask);
}
