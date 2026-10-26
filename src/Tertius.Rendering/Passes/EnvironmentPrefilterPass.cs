using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Derives each map's mips, specular levels and irradiance from level 0 of its six faces, for each map that this frame drew
/// a face of (stage <see cref="FrameGraph.Stage.EnvironmentPrefilter"/>, docs/plans/environment-map.md, "The prefilter",
/// and docs/plans/environment-maps-per-frame.md, "The prefilter of each map"), then copies the irradiance of the served
/// maps into the frame slot's readback buffer. Compute dispatches on the graphics queue.
/// </summary>
/// <remarks>
/// It reads only level 0 of a map's layers, never its own earlier output, so all it writes of a map is a pure function of
/// the map's six stored faces. A map is prefiltered whole when one of its faces is drawn, because a rough lobe crosses into
/// the neighbors of the face that changed. A map with no drawn face keeps its levels and its coefficients (decision 12). The
/// maps go in index order, each writing only its own layers and coefficients (decision 25), with a barrier between two maps
/// (see <see cref="AfterWrites"/>): first every drawn map's mips,
/// then every drawn map's specular levels and irradiance, because a level view spans every map's layers and so holds one
/// layout for all of them while it is bound. After the pass the specular
/// maps are in <see cref="ImageUse.SampledFragment"/> and the irradiance buffer in <see cref="BufferUse.ShaderRead"/>, the
/// states that their readers expect.
/// </remarks>
internal sealed unsafe class EnvironmentPrefilterPass : RenderPass
{
    /// <summary>The prefilter's workgroup: 8 by 8 texels of one face.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly EnvironmentMap map;
    private readonly Pipeline downsample;
    private readonly Pipeline prefilter;
    private readonly Pipeline irradiance;

    public EnvironmentPrefilterPass(
        VulkanDevice device,
        ShaderLibrary shaders,
        DescriptorLayouts layouts,
        EnvironmentMap map
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.map = map;
        try
        {
            downsample = Create(name: "environment downsample", shader: "environment_downsample.comp");
            prefilter = Create(name: "environment prefilter", shader: "environment_prefilter.comp");
            irradiance = Create(name: "environment irradiance", shader: "environment_irradiance.comp");
        }
        catch
        {
            Dispose();
            throw;
        }

        Pipeline Create(string name, string shader) =>
            new ComputePipelineDesc
            {
                Name = name,
                Shader = shader,
                Layout = layouts.EnvironmentPrefilterLayout,
            }.Create(device, shaders);
    }

    public override string Name => "environment prefilter";

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        PreparedScene scene = context.Scene;
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 0, sets: [map.PrefilterSet]);
        barriers.Buffer(map.Irradiance, BufferUse.ComputeWrite);
        barriers.Flush(vk, commandBuffer);
        Span<bool> drawn = stackalloc bool[map.Capacity];
        bool anyDrawn = false;
        foreach (EnvironmentFaceDraw face in scene.EnvironmentFaces)
        {
            drawn[face.Map] = true;
            anyDrawn = true;
        }

        if (anyDrawn)
        {
            RecordMips(vk: vk, commandBuffer: commandBuffer, barriers: barriers, drawn: drawn);
            RecordLevels(vk: vk, commandBuffer: commandBuffer, barriers: barriers, drawn: drawn);
        }

        // Every layer of both images is read-only again: the prefiltered maps' just now, the others since their last one.
        map.Radiance.State = ResourceUses.Describe(ImageUse.SampledCompute);
        map.Specular.State = ResourceUses.Describe(ImageUse.SampledFragment);
        // Right after the irradiance, so a reading is the irradiance that this frame's passes use.
        map.RecordReadback(
            commandBuffer: commandBuffer,
            barriers: barriers,
            slot: context.Frame.Index,
            maps: scene.MapCount
        );
        barriers.Buffer(map.Irradiance, BufferUse.ShaderRead);
        barriers.Flush(vk, commandBuffer);
    }

    /// <summary>
    /// Rebuilds levels 1 and up of the radiance maps of each drawn map from its level 0, level by level, each from the one
    /// above it, in map index order within a level. A level view spans every map's layers, and a storage image's layout
    /// must hold for every layer of its view, so every layer of the radiance maps is in <see cref="ImageLayout.General"/>
    /// while the downsample runs. The maps that are not drawn keep their texels through the transitions. All levels end
    /// read-only for the prefilter's samples.
    /// </summary>
    private void RecordMips(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, ReadOnlySpan<bool> drawn)
    {
        GpuImage radiance = map.Radiance;
        uint levels = radiance.MipLevels;
        PipelineStageFlags2 lastUse = radiance.State.Stages;
        // Level 0 of every map: the drawn faces since the face pass, the others since their last prefilter.
        ToGeneral(
            barriers: barriers,
            image: radiance,
            baseMip: 0,
            mipCount: 1,
            oldLayout: ImageLayout.ShaderReadOnlyOptimal,
            after: lastUse,
            layers: AllLayers
        );
        // Levels 1 and up: a drawn map's texels are all written anew, and the others keep theirs.
        for (int index = 0; index < drawn.Length; index++)
            ToGeneral(
                barriers: barriers,
                image: radiance,
                baseMip: 1,
                mipCount: levels - 1,
                oldLayout: drawn[index] ? ImageLayout.Undefined : ImageLayout.ShaderReadOnlyOptimal,
                after: lastUse,
                layers: new LayerRange(FirstLayer(index), CubeFaces.Count)
            );
        barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, downsample);
        for (uint level = 1; level < levels; level++)
            DownsampleLevel(vk: vk, commandBuffer: commandBuffer, barriers: barriers, level: level, drawn: drawn);
        barriers.ImageLevels(
            image: radiance,
            baseMip: 0,
            mipCount: levels,
            oldLayout: ImageLayout.General,
            newLayout: ImageLayout.ShaderReadOnlyOptimal,
            srcStages: PipelineStageFlags2.ComputeShaderBit,
            srcAccess: AccessFlags2.ShaderStorageWriteBit,
            dstStages: PipelineStageFlags2.ComputeShaderBit,
            dstAccess: AccessFlags2.ShaderSampledReadBit
        );
    }

    /// <summary>Writes radiance level <paramref name="level"/> of each drawn map from the level above it.</summary>
    private void DownsampleLevel(
        Vk vk,
        CommandBuffer commandBuffer,
        BarrierBatch barriers,
        uint level,
        ReadOnlySpan<bool> drawn
    )
    {
        uint size = (uint)Math.Max(1, map.Setting.FaceSize >> (int)level);
        BindSets(
            vk: vk,
            commandBuffer: commandBuffer,
            firstSet: 1,
            sets: [map.RadianceLevelSet((int)level - 1), map.RadianceLevelSet((int)level)]
        );
        for (int index = 0; index < drawn.Length; index++)
            if (drawn[index])
            {
                var push = new EnvironmentDownsamplePushConstants { LevelSize = size, Map = (uint)index };
                Push(
                    vk: vk,
                    commandBuffer: commandBuffer,
                    push: &push,
                    size: (uint)sizeof(EnvironmentDownsamplePushConstants)
                );
                Dispatch(vk: vk, commandBuffer: commandBuffer, size: size);
                // The next level reads the one just written, and the next map writes through the same view.
                AfterWrites(barriers: barriers, image: map.Radiance, baseMip: level, mipCount: 1);
                barriers.Flush(vk, commandBuffer);
            }
    }

    /// <summary>
    /// Writes each drawn map's specular levels, which sample its radiance mips, then its irradiance, in map index order. As
    /// for the mips, every layer of the specular maps is in <see cref="ImageLayout.General"/> while the prefilter runs, the
    /// maps that are not drawn keep their texels, and every layer ends read-only for this frame's readers.
    /// </summary>
    private void RecordLevels(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, ReadOnlySpan<bool> drawn)
    {
        GpuImage specular = map.Specular;
        PipelineStageFlags2 lastUse = specular.State.Stages;
        // A drawn map's texels of every level are all written anew, after the readers of the frame before.
        for (int index = 0; index < drawn.Length; index++)
            ToGeneral(
                barriers: barriers,
                image: specular,
                baseMip: 0,
                mipCount: (uint)map.Levels,
                oldLayout: drawn[index] ? ImageLayout.Undefined : ImageLayout.ShaderReadOnlyOptimal,
                after: lastUse,
                layers: new LayerRange(FirstLayer(index), CubeFaces.Count)
            );
        barriers.Flush(vk, commandBuffer);
        for (int index = 0; index < drawn.Length; index++)
            if (drawn[index])
            {
                RecordMap(vk: vk, commandBuffer: commandBuffer, index: index);
                // The next map writes through the same level views and the same buffer binding.
                AfterWrites(barriers: barriers, image: specular, baseMip: 0, mipCount: (uint)map.Levels);
                barriers.Buffer(map.Irradiance, BufferUse.ComputeWrite);
                barriers.Flush(vk, commandBuffer);
            }
        ResourceState sampled = ResourceUses.Describe(ImageUse.SampledFragment);
        barriers.ImageLevels(
            image: specular,
            baseMip: 0,
            mipCount: (uint)map.Levels,
            oldLayout: ImageLayout.General,
            newLayout: sampled.Layout,
            srcStages: PipelineStageFlags2.ComputeShaderBit,
            srcAccess: AccessFlags2.ShaderStorageWriteBit,
            dstStages: sampled.Stages,
            dstAccess: sampled.Access
        );
    }

    /// <summary>Writes map <paramref name="index"/>'s specular levels, then its irradiance coefficients.</summary>
    private void RecordMap(Vk vk, CommandBuffer commandBuffer, int index)
    {
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, prefilter);
        // The levels write different images and read only the radiance map, so they need no barrier between them.
        for (int level = 0; level < map.Levels; level++)
            DispatchLevel(vk: vk, commandBuffer: commandBuffer, level: level, index: index);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, irradiance);
        var push = new EnvironmentPrefilterPushConstants { Map = (uint)index };
        Push(vk: vk, commandBuffer: commandBuffer, push: &push, size: (uint)sizeof(EnvironmentPrefilterPushConstants));
        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: 1, groupCountY: 1, groupCountZ: 1);
    }

    /// <summary>
    /// Orders the compute writes to some levels of every layer of <paramref name="image"/> before the next compute reads
    /// and writes. Two maps write different layers, which do not overlap, but a level view spans every map's layers, and
    /// the synchronization validation tracks the whole view: so the maps are ordered too, at the cost of one barrier each.
    /// </summary>
    private static void AfterWrites(BarrierBatch barriers, GpuImage image, uint baseMip, uint mipCount) =>
        barriers.ImageLevels(
            image: image,
            baseMip: baseMip,
            mipCount: mipCount,
            oldLayout: ImageLayout.General,
            newLayout: ImageLayout.General,
            srcStages: PipelineStageFlags2.ComputeShaderBit,
            srcAccess: AccessFlags2.ShaderStorageWriteBit,
            dstStages: PipelineStageFlags2.ComputeShaderBit,
            dstAccess: AccessFlags2.ShaderStorageReadBit | AccessFlags2.ShaderStorageWriteBit
        );

    /// <summary>Some layers of an image: all of them, or one map's six.</summary>
    private readonly record struct LayerRange(uint First, uint Count);

    /// <summary>Every layer, in <see cref="BarrierBatch.ImageLevels"/>'s sense of a layer count of 0.</summary>
    private static readonly LayerRange AllLayers = new(First: 0, Count: 0);

    /// <summary>The first layer of map <paramref name="index"/> in both cube arrays.</summary>
    private static uint FirstLayer(int index) => (uint)(CubeFaces.Count * index);

    public override void Dispose()
    {
        // A pipeline that the constructor did not make is a null handle, which vkDestroyPipeline ignores.
        device.Vk.DestroyPipeline(device.Device, downsample, null);
        device.Vk.DestroyPipeline(device.Device, prefilter, null);
        device.Vk.DestroyPipeline(device.Device, irradiance, null);
    }

    /// <summary>
    /// Makes some levels of some layers of <paramref name="image"/> storage images, after their last use in
    /// <paramref name="after"/>. An old layout of <see cref="ImageLayout.Undefined"/> discards the texels.
    /// </summary>
    private static void ToGeneral(
        BarrierBatch barriers,
        GpuImage image,
        uint baseMip,
        uint mipCount,
        ImageLayout oldLayout,
        PipelineStageFlags2 after,
        LayerRange layers
    ) =>
        barriers.ImageLevels(
            image: image,
            baseMip: baseMip,
            mipCount: mipCount,
            oldLayout: oldLayout,
            newLayout: ImageLayout.General,
            srcStages: after,
            srcAccess: AccessFlags2.None,
            dstStages: PipelineStageFlags2.ComputeShaderBit,
            dstAccess: AccessFlags2.ShaderStorageReadBit | AccessFlags2.ShaderStorageWriteBit,
            baseLayer: layers.First,
            layerCount: layers.Count
        );

    /// <summary>One invocation per texel of a level's six faces of one map, of <paramref name="size"/> texels.</summary>
    private static void Dispatch(Vk vk, CommandBuffer commandBuffer, uint size)
    {
        uint groups = (size + GroupSize - 1) / GroupSize;
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: groups,
            groupCountY: groups,
            groupCountZ: CubeFaces.Count
        );
    }

    /// <summary>Dispatches one level of map <paramref name="index"/>'s specular map: one invocation per texel of its six faces.</summary>
    private void DispatchLevel(Vk vk, CommandBuffer commandBuffer, int level, int index)
    {
        uint size = (uint)(map.Setting.FaceSize >> level);
        // The prefilter writes set 2 and reads no level set, so set 1 keeps what the mips left there.
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 2, sets: [map.SpecularLevelSet(level)]);
        var push = new EnvironmentPrefilterPushConstants
        {
            LevelSize = size,
            Roughness = PrefilterLevels.Roughness(level, map.Levels),
            Samples = PrefilterLevels.SamplesPerTexel,
            Map = (uint)index,
        };
        Push(vk: vk, commandBuffer: commandBuffer, push: &push, size: (uint)sizeof(EnvironmentPrefilterPushConstants));
        Dispatch(vk: vk, commandBuffer: commandBuffer, size: size);
    }

    /// <summary>Pushes <paramref name="size"/> bytes at <paramref name="push"/> to the prefilter layout's compute range.</summary>
    private void Push(Vk vk, CommandBuffer commandBuffer, void* push, uint size) =>
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.EnvironmentPrefilterLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: size,
            pValues: push
        );

    /// <summary>Binds <paramref name="sets"/> to the prefilter layout from set <paramref name="firstSet"/> on.</summary>
    private void BindSets(Vk vk, CommandBuffer commandBuffer, uint firstSet, ReadOnlySpan<DescriptorSet> sets)
    {
        fixed (DescriptorSet* bound = sets)
            vk.CmdBindDescriptorSets(
                commandBuffer: commandBuffer,
                pipelineBindPoint: PipelineBindPoint.Compute,
                layout: layouts.EnvironmentPrefilterLayout,
                firstSet: firstSet,
                descriptorSetCount: (uint)sets.Length,
                pDescriptorSets: bound,
                dynamicOffsetCount: 0,
                pDynamicOffsets: null
            );
    }
}
