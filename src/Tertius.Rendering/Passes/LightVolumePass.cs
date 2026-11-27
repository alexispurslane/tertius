using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Rasterizes surface coverage into camera-centered shells, shades occupied patches and sweeps light through the grids.</summary>
internal sealed unsafe class LightVolumePass : RenderPass
{
    // The grids' shape, as volume_layout.glsl has it: each level's receivers use a reach of 16 cells per axis, and the
    // outer level also stores a guard of one cell on each face, 18 cells per axis.
    private const int Levels = 3;
    private const int Reach = 16;
    private const int OuterSide = Reach + 2;
    private const int OuterWidth = 1;
    private const int Directions = 32;
    private const int CellCount = (Levels - 1) * Reach * Reach * Reach + OuterSide * OuterSide * OuterSide;
    private const int OuterTilesPerAxis = 6;
    private const int TileCount = (Levels - 1) * 64 + OuterTilesPerAxis * OuterTilesPerAxis * OuterTilesPerAxis;
    internal const uint CounterOffset = Levels * 16;
    private const uint HeaderBytes = Levels * 16 + 16;
    private const uint SurfaceBytes = CellCount * 6 * 48;

    /// <summary>The size of the volume's buffer in bytes: 15,291,200 (14.58 MiB).</summary>
    internal const uint BufferBytes =
        HeaderBytes + SurfaceBytes + 16 * (TileCount * 6 + CellCount * (Directions + 6 + 12));
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly DescriptorSet set;
    private readonly Pipeline geometry;
    private readonly Pipeline shading;
    private readonly Pipeline restrictSurface;
    private readonly Pipeline source;
    private readonly Pipeline sweep;
    private readonly Pipeline filter;

    private struct Draw
    {
        public Vector4 Row0,
            Row1,
            Row2,
            Grid;
        public uint Level,
            Material,
            HoldsLight,
            Mirrored;
    }

    public LightVolumePass(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        ShaderLibrary shaders,
        ShaderConstants constants
    )
    {
        this.device = device;
        this.layouts = layouts;
        Buffer = GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: BufferBytes,
            usage: BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.TransferDstBit
                | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "directional light volume"
        );
        try
        {
            set = layouts.AllocateVolumeSet();
            layouts.WriteBuffer(set: set, binding: 0, type: DescriptorType.StorageBuffer, buffer: Buffer);
            geometry = Graphics("volume_surface", VertexInput.PositionAndTexCoord);
            shading = Graphics("volume_shade", VertexInput.None);
            restrictSurface = Compute("volume_restrict");
            source = Compute("volume_source");
            sweep = Compute("volume_sweep");
            filter = Compute("volume_filter");
        }
        catch
        {
            Dispose();
            throw;
        }

        Pipeline Graphics(string name, VertexInput vertices) =>
            new GraphicsPipelineDesc
            {
                Name = name,
                VertexShader = name + ".vert",
                FragmentShader = name + ".frag",
                Layout = layouts.VolumeDrawLayout,
                ColorFormats = [],
                VertexInput = vertices,
                CullMode = CullModeFlags.None,
                Constants = constants,
            }.Create(device, shaders);
        Pipeline Compute(string name) =>
            new ComputePipelineDesc
            {
                Name = name,
                Shader = name + ".comp",
                Layout = layouts.VolumeSweepLayout,
            }.Create(device, shaders);
    }

    public override string Name => "directional light volume";
    public GpuBuffer Buffer { get; }

    /// <summary>
    /// Whether a sphere, camera-relative, can reach a cell of the outer grid. The grid's center lies at most half a cell
    /// from the camera along any axis, so the grid lies within (OuterSide + 1) / 2 cells of the camera.
    /// </summary>
    public static bool Intersects(Vector3 center, float radius) =>
        MathF.Max(MathF.Abs(center.X), MathF.Max(MathF.Abs(center.Y), MathF.Abs(center.Z)))
        <= (OuterSide + 1) * 0.5f * OuterWidth + radius;

    // The cells per axis that a level stores.
    private static int SideOf(int level) => level == Levels - 1 ? OuterSide : Reach;

    private static uint WorkGroups(int cells) => (uint)(cells + 63) / 64;

    public override void Record(FrameContext context)
    {
        Span<Vector4> grids = stackalloc Vector4[Levels];
        Double3 anchor = ShadowCascades.Anchor(context.Snapshot.OriginInFrame, context.Scene.Camera.Offset);
        for (int level = 0; level < Levels; level++)
        {
            float width = 0.25f * (1 << level);
            int side = SideOf(level);
            float Origin(double x) => (float)Math.IEEERemainder(x, width) - side / 2 * width;
            grids[level] = new(x: Origin(anchor.X), y: Origin(anchor.Y), z: Origin(anchor.Z), w: width);
        }
        Use(context, BufferUse.TransferDestination);
        fixed (Vector4* data = grids)
            context.Vk.CmdUpdateBuffer(
                commandBuffer: context.CommandBuffer,
                dstBuffer: Buffer.Handle,
                dstOffset: 0,
                dataSize: Levels * 16,
                pData: data
            );
        context.Vk.CmdFillBuffer(
            commandBuffer: context.CommandBuffer,
            dstBuffer: Buffer.Handle,
            dstOffset: Levels * 16,
            size: SurfaceBytes + 16,
            data: 0
        );

        context.Barriers.Buffer(context.Geometry.VertexBuffer, BufferUse.VertexInput);
        context.Barriers.Buffer(context.Geometry.IndexBuffer, BufferUse.VertexInput);
        context.Barriers.Buffer(context.Materials.Buffer, BufferUse.ShaderRead);
        Rasterize(context, grids);
        for (uint parent = 1; parent < Levels; parent++)
        {
            int side = SideOf((int)parent);
            Dispatch(
                context: context,
                pipeline: restrictSurface,
                index: parent,
                x: WorkGroups(side * side * side),
                y: 6
            );
        }
        ShadeGroups(context);

        for (uint bounce = 0; bounce <= 1; bounce++)
        {
            Dispatch(context: context, pipeline: source, index: bounce, x: WorkGroups(CellCount), y: Directions);
            for (int level = Levels - 1; level >= 0; level--)
                Dispatch(context: context, pipeline: sweep, index: (uint)level, x: Directions, y: 1);
            Dispatch(context: context, pipeline: filter, index: bounce, x: WorkGroups(CellCount), y: 6);
        }
    }

    // Evaluates the lights once per lighting group and side (volume_shade.frag).
    private void ShadeGroups(FrameContext context)
    {
        context.Barriers.Image(context.Shadows.Image, ImageUse.SampledFragment);
        context.ShadowAtlas?.DeclareSampled(context.Barriers, ImageUse.SampledFragment);
        // One pixel per lighting group and side, one row per level: the outer level's groups set the width.
        BeginRaster(context, shading, new(OuterTilesPerAxis * OuterTilesPerAxis * OuterTilesPerAxis * 6, Levels));
        context.Vk.CmdDraw(
            commandBuffer: context.CommandBuffer,
            vertexCount: 3,
            instanceCount: 1,
            firstVertex: 0,
            firstInstance: 0
        );
        context.Vk.CmdEndRendering(context.CommandBuffer);
    }

    private void Rasterize(FrameContext context, ReadOnlySpan<Vector4> grids)
    {
        // 4 × 4 pixels per cell. Every level projects its cells into the same OuterSide × OuterSide cells
        // (volume_surface.vert), so the inner levels sample their cells as before at the lower corner.
        BeginRaster(context, geometry, new(OuterSide * 4, OuterSide * 4));
        GeometryPass.BindGeometry(context);
        foreach (ref readonly PreparedInstance p in context.Scene.PlacedSpan)
        {
            if ((p.Flags & RenderFlags.Visible) == 0 || p.Alpha is GlassAlpha || !Intersects(p.Position, p.Radius))
                continue;
            var draw = new Draw
            {
                Material = p.Material,
                HoldsLight = p.HoldsLight ? 1u : 0u,
                Mirrored = p.Scale.X * p.Scale.Y * p.Scale.Z < 0 ? 1u : 0u,
            };
            (draw.Row0, draw.Row1, draw.Row2) = GpuInstance.RowsOf(p.Position, p.Rotation, p.Scale);
            MeshInfo mesh = context.Scene.Meshes[p.Mesh];
            for (int level = 0; level < Levels; level++)
            {
                Vector4 grid = grids[level];
                float half = SideOf(level) * 0.5f * grid.W;
                var offset = Vector3.Abs(p.Position - new Vector3(grid.X + half, grid.Y + half, grid.Z + half));
                float distance = MathF.Max(offset.X, MathF.Max(offset.Y, offset.Z));
                if (distance > half + p.Radius)
                    continue;
                draw.Grid = grid;
                draw.Level = (uint)level;
                context.Vk.CmdPushConstants(
                    commandBuffer: context.CommandBuffer,
                    layout: layouts.VolumeDrawLayout,
                    stageFlags: ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    offset: 0,
                    size: (uint)sizeof(Draw),
                    pValues: &draw
                );
                context.Vk.CmdDrawIndexed(
                    commandBuffer: context.CommandBuffer,
                    indexCount: mesh.IndexCount,
                    instanceCount: 3,
                    firstIndex: mesh.FirstIndex,
                    vertexOffset: mesh.VertexOffset,
                    firstInstance: 0
                );
                if (distance + p.Radius < half)
                    break;
            }
        }
        context.Vk.CmdEndRendering(context.CommandBuffer);
    }

    private void BeginRaster(FrameContext context, Pipeline pipeline, Extent2D size)
    {
        Use(context, BufferUse.FragmentWrite);
        var info = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D(default, size),
            LayerCount = 1,
        };
        context.Vk.CmdBeginRendering(context.CommandBuffer, &info);
        context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Graphics, pipeline);
        context.BindSceneSets(PipelineBindPoint.Graphics, layouts.VolumeDrawLayout);
        Bind(context: context, point: PipelineBindPoint.Graphics, layout: layouts.VolumeDrawLayout, slot: 3);
        context.SetViewport(size);
    }

    private void Dispatch(FrameContext context, Pipeline pipeline, uint index, uint x, uint y)
    {
        Use(context, BufferUse.ComputeWrite);
        PipelineLayout layout = layouts.VolumeSweepLayout;
        context.Vk.CmdBindPipeline(context.CommandBuffer, PipelineBindPoint.Compute, pipeline);
        Bind(context: context, point: PipelineBindPoint.Compute, layout: layout, slot: 0);
        context.Vk.CmdPushConstants(
            commandBuffer: context.CommandBuffer,
            layout: layout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: 4,
            pValues: &index
        );
        context.Vk.CmdDispatch(commandBuffer: context.CommandBuffer, groupCountX: x, groupCountY: y, groupCountZ: 1);
    }

    private void Bind(FrameContext context, PipelineBindPoint point, PipelineLayout layout, uint slot)
    {
        DescriptorSet bound = set;
        context.Vk.CmdBindDescriptorSets(
            commandBuffer: context.CommandBuffer,
            pipelineBindPoint: point,
            layout: layout,
            firstSet: slot,
            descriptorSetCount: 1,
            pDescriptorSets: &bound,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
    }

    private void Use(FrameContext context, BufferUse usage)
    {
        context.Barriers.Buffer(Buffer, usage);
        context.Barriers.Flush(context.Vk, context.CommandBuffer);
    }

    public override void Dispose()
    {
        device.Vk.DestroyPipeline(device.Device, filter, null);
        device.Vk.DestroyPipeline(device.Device, sweep, null);
        device.Vk.DestroyPipeline(device.Device, source, null);
        device.Vk.DestroyPipeline(device.Device, restrictSurface, null);
        device.Vk.DestroyPipeline(device.Device, shading, null);
        device.Vk.DestroyPipeline(device.Device, geometry, null);
        Buffer.Dispose();
    }
}
