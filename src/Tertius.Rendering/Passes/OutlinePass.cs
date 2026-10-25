using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The fixed color of each kind of frame outline, as the shader unpacks it (<c>unpackUnorm4x8</c>: red in the lowest
/// byte), with the sRGB encoding of a display color and alpha 1. They are the five frame bubble colors of the editor
/// palette (docs/plans/editor.md), one for each kind, chosen to stand out against black space and gray boxes.
/// </summary>
internal static class OutlineColors
{
    /// <summary>A semantic frame's bubble: #7AA7FA, blue.</summary>
    public static readonly uint Semantic = Pack(0x7AA7FA);

    /// <summary>A falling numeric frame: #E58FB4, pink.</summary>
    public static readonly uint Falling = Pack(0xE58FB4);

    /// <summary>A resting numeric frame: #4FC1B6, teal.</summary>
    public static readonly uint Resting = Pack(0x4FC1B6);

    /// <summary>A nesting numeric frame: #B58CF0, purple.</summary>
    public static readonly uint Nesting = Pack(0xB58CF0);

    /// <summary>A following frame: #C9B458, gold.</summary>
    public static readonly uint Following = Pack(0xC9B458);

    /// <summary>A display color written as 0xRRGGBB, packed as the bytes R, G, B and 255 from the lowest byte up.</summary>
    private static uint Pack(uint rgb) => 0xFF000000u | ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
}

/// <summary>
/// The overlay pass (stage <see cref="FrameGraph.Stage.Overlay"/>): draws the frame outlines of the snapshot over the
/// output image, before the capture readback, so a capture includes them.
/// </summary>
/// <remarks>
/// <para><see cref="Prepare"/>, on the CPU, turns each <see cref="FrameOutline"/> into one <see cref="GpuOutline"/> (see
/// <see cref="Collect"/>). <see cref="Record"/> makes one instanced draw of <see cref="VerticesPerOutline"/> vertices for
/// each record. Shaders/outline.vert makes the rings of an outline from the vertex index: the three ellipses in the
/// principal planes of the frame, with <see cref="SegmentsPerRing"/> segments each, and for a sphere seen from outside,
/// its outline as seen from the eye. Each segment is a quad two pixels wide on the screen, made of two triangles, so the
/// lines do not depend on the line widths that the device offers. Two quads that meet at a corner share one mitered edge,
/// so no pixel blends twice there. Each segment is cut at the near plane, so a camera inside a bubble sees the parts of
/// its rings in front of it. Shaders/outline.frag draws a fragment behind geometry, by the G-buffer distance, at 30 %
/// alpha, and any other fragment at full alpha, blended into the output.</para>
/// <para>With no outline in the snapshot, the pass costs one empty loop: <see cref="Prepare"/> writes no record and
/// <see cref="Record"/> returns at once.</para>
/// </remarks>
internal sealed class OutlinePass : RenderPass
{
    /// <summary>The segments of each ring.</summary>
    public const int SegmentsPerRing = 64;

    /// <summary>The rings of each outline: three in the principal planes, and the outline of a sphere seen from the eye.</summary>
    public const int Rings = 4;

    /// <summary>
    /// The vertices of one outline: 6 for each segment of each ring. An ellipsoid has no outline as seen from the eye,
    /// so the 384 vertices of its fourth ring collapse to nothing.
    /// </summary>
    public const int VerticesPerOutline = Rings * SegmentsPerRing * 6;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly Pipeline pipeline;

    public OutlinePass(VulkanDevice device, ShaderLibrary shaders, DescriptorLayouts layouts, Format outputFormat)
    {
        this.device = device;
        this.layouts = layouts;
        pipeline = new GraphicsPipelineDesc
        {
            Name = "outlines",
            VertexShader = "outline.vert",
            FragmentShader = "outline.frag",
            Layout = layouts.OverlayLayout,
            ColorFormats = [outputFormat],
            VertexInput = VertexInput.None,
            // A quad faces either way, by the direction of its segment on the screen.
            CullMode = CullModeFlags.None,
            // The vertex shader cuts each segment at the near plane, so no clipping is wanted there. No depth buffer is bound.
            DepthClamp = true,
            Blend = BlendMode.Alpha,
        }.Create(device, shaders);
    }

    public override string Name => "outlines";

    public override void Prepare(FrameContext context) => Collect(context.Snapshot, context.Scene);

    /// <summary>
    /// Writes one <see cref="GpuOutline"/> into <paramref name="scene"/> for each outline of <paramref name="snapshot"/> that
    /// can show, in the order of the snapshot, and nothing for the others. An outline shows when its outline sphere has a
    /// radius above 0, lies within <see cref="ShaderInput.PositionLimit"/> of the eye and is not outside the view frustum:
    /// the largest radius for an ellipsoid, the radius for a sphere.
    /// A sphere, which gets <see cref="GpuOutline.SphereFlag"/>, is the contents sphere of a numeric frame, or a bubble
    /// with three equal radii (<see cref="FrameBounds.IsSphere"/>). The center becomes relative to the interpolated eye
    /// of <see cref="PreparedScene.Camera"/>, as a light's does. The batcher has reset <paramref name="scene"/> for the frame, so the records of the frame before are gone.
    /// </summary>
    internal static void Collect(RenderSnapshot snapshot, PreparedScene scene)
    {
        foreach (ref readonly FrameOutline outline in CollectionsMarshal.AsSpan(snapshot.Outlines))
            switch (outline)
            {
                case SemanticOutline { Center: var center, Rotation: var rotation, Radii: var radii }:
                    // A bubble with three equal radii is a sphere, by the rule of FrameBounds, so it also gets its rim.
                    Add(
                        scene: scene,
                        center: center,
                        rotation: new(x: rotation.X, y: rotation.Y, z: rotation.Z, w: rotation.W),
                        radii: radii,
                        color: OutlineColors.Semantic,
                        flags: new FrameBounds(radii).IsSphere ? GpuOutline.SphereFlag : 0
                    );
                    break;
                case FallingOutline { Center: var center, Radius: var radius }:
                    AddSphere(scene: scene, center: center, radius: radius, color: OutlineColors.Falling);
                    break;
                case RestingOutline { Center: var center, Radius: var radius }:
                    AddSphere(scene: scene, center: center, radius: radius, color: OutlineColors.Resting);
                    break;
                case NestingOutline { Center: var center, Radius: var radius }:
                    AddSphere(scene: scene, center: center, radius: radius, color: OutlineColors.Nesting);
                    break;
                case FollowingOutline { Center: var center, Radius: var radius }:
                    AddSphere(scene: scene, center: center, radius: radius, color: OutlineColors.Following);
                    break;
                case null:
                    // The default outline holds no case, so it draws nothing.
                    break;
                default:
                    throw new UnreachableException($"The outline {outline} has a case that the pass does not draw.");
            }
    }

    /// <summary>A contents sphere has no axes of its own, so its rings lie in the axes of the camera's frame.</summary>
    private static void AddSphere(PreparedScene scene, Vector3 center, float radius, uint color) =>
        Add(
            scene: scene,
            center: center,
            rotation: new(x: 0, y: 0, z: 0, w: 1),
            radii: new(radius),
            color: color,
            flags: GpuOutline.SphereFlag
        );

    private static void Add(
        PreparedScene scene,
        Vector3 center,
        Vector4 rotation,
        Vector3 radii,
        uint color,
        uint flags
    )
    {
        float outlineRadius = MathF.Max(radii.X, MathF.Max(radii.Y, radii.Z));
        Vector3 position = center - scene.Camera.Offset;
        // One sum is non-finite when any term is NaN or infinite. The rotation's parts are at most 1 in a unit quaternion,
        // so their sum overflows only for a rotation that is not.
        bool finite = float.IsFinite(radii.X + radii.Y + radii.Z + rotation.X + rotation.Y + rotation.Z + rotation.W);
        // MathF.Max passes a NaN on, so a NaN radius fails the first test.
        bool shows = outlineRadius > 0f && ShaderInput.InPositionRange(position, outlineRadius);
        if (!finite || !shows || scene.Camera.Culls(position, outlineRadius))
            return;
        scene.AppendOutline(
            new()
            {
                Rotation = rotation,
                Center = position,
                Color = color,
                Radii = radii,
                Flags = flags,
            }
        );
    }

    public override unsafe void Record(FrameContext context)
    {
        int count = context.Scene.OutlineCount;
        if (count == 0)
            return;

        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        barriers.Image(context.Targets.Distance, ImageUse.SampledFragment);
        barriers.Image(context.Output, ImageUse.ColorAttachment);
        barriers.Flush(vk, commandBuffer);

        var color = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = context.Output.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new Offset2D(0, 0), context.Output.Extent),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &color,
        };
        vk.CmdBeginRendering(commandBuffer, &rendering);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        DescriptorSet* sets = stackalloc[] {
            context.Frame.FrameSet,
            context.Frame.TextureSet,
            context.Targets.GBufferSet,
        };
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Graphics,
            layout: layouts.OverlayLayout,
            firstSet: 0,
            descriptorSetCount: 3,
            pDescriptorSets: sets,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        context.SetFullViewport();
        var push = new OutlinePushConstants { EncodeSrgb = context.EncodeSrgb ? 1u : 0u };
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.OverlayLayout,
            stageFlags: ShaderStageFlags.FragmentBit,
            offset: 0,
            size: (uint)sizeof(OutlinePushConstants),
            pValues: &push
        );
        vk.CmdDraw(
            commandBuffer: commandBuffer,
            vertexCount: VerticesPerOutline,
            instanceCount: (uint)count,
            firstVertex: 0,
            firstInstance: 0
        );
        vk.CmdEndRendering(commandBuffer);
    }

    public override unsafe void Dispose() => device.Vk.DestroyPipeline(device.Device, pipeline, null);
}
