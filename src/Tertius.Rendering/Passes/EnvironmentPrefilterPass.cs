using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Derives each map's mips, specular levels and irradiance cube from level 0 of its six faces, and its distance cube's
/// levels from the faces' inverse distances, for each map that this frame drew a face of (stage
/// <see cref="FrameGraph.Stage.EnvironmentPrefilter"/>, docs/plans/environment-map.md, "The prefilter",
/// docs/plans/environment-maps-per-frame.md, "The prefilter of each map", and docs/plans/environment-map-distances.md,
/// "How the capture writes it"), then copies the last level of the served maps' irradiance cubes into the frame slot's
/// readback buffer. Compute dispatches on the graphics queue.
/// </summary>
/// <remarks>
/// It reads only level 0 of a map's layers, never its own earlier output, so all it writes of a map is a pure function of
/// the map's six stored faces. A map is prefiltered whole when one of its faces is drawn, because a rough lobe and the
/// cosine of the irradiance cross into the neighbors of the face that changed. A map with no drawn face keeps its levels
/// and its cube (decision 12). The maps go in index order, each writing only its own layers (decision 25), with a barrier
/// between two maps (see <see cref="AfterWrites"/>): first every drawn map's mips, then every drawn map's specular levels
/// and irradiance cube, because a level view spans every map's layers and so holds one layout for all of them while it is
/// bound. The distance cubes' mips run beside the radiance maps' mips, by the same rule and under the same barriers. After
/// the pass the specular maps are in <see cref="ImageUse.SampledFragment"/>, the irradiance cubes in
/// <see cref="ImageUse.SampledVertexFragment"/> and the distance cubes in <see cref="ImageUse.SampledFragmentCompute"/>,
/// the states that their readers expect.
/// </remarks>
internal sealed unsafe class EnvironmentPrefilterPass : RenderPass
{
    /// <summary>The downsample's workgroup: 8 by 8 texels of one face of the first level of its run.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly EnvironmentMap map;
    private readonly Pipeline downsample;
    private readonly Pipeline distanceDownsample;
    private readonly Pipeline prefilter;
    private readonly Pipeline irradiance;

    /// <summary>The byte offset of <see cref="EnvironmentPrefilterPushConstants.Map"/>, 76.</summary>
    private static readonly uint MapOffset = (uint)
        Marshal.OffsetOf<EnvironmentPrefilterPushConstants>(nameof(EnvironmentPrefilterPushConstants.Map));

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
            distanceDownsample = Create(
                name: "environment distance downsample",
                shader: "environment_distance_downsample.comp"
            );
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

        // Every layer of the radiance maps, the distance cubes and the specular maps is read-only again: the prefiltered
        // maps' just now, the others since their last one.
        map.Radiance.State = ResourceUses.Describe(ImageUse.SampledCompute);
        map.Distance.State = ResourceUses.Describe(ImageUse.SampledFragmentCompute);
        map.Specular.State = ResourceUses.Describe(ImageUse.SampledFragment);
        // Right after the irradiance, so a reading is the irradiance that this frame's passes use.
        map.RecordReadback(
            commandBuffer: commandBuffer,
            barriers: barriers,
            slot: context.Frame.Index,
            maps: scene.MapCount
        );
    }

    /// <summary>
    /// Rebuilds levels 1 and up of the radiance maps and of the distance cubes of each drawn map from its level 0, in runs
    /// of up to three levels, each run from the last level of the run before
    /// (<see cref="PrefilterLevels.LevelsOfDownsample"/>), in map index order within a run. A level view spans every map's
    /// layers, and a storage image's layout must hold for every layer of its view, so every layer of both images is in
    /// <see cref="ImageLayout.General"/> while the downsample runs. The maps that are not drawn keep their texels through
    /// the transitions. The radiance maps' levels end read-only for the prefilter's samples, and the distance cubes' for
    /// this frame's shading passes.
    /// </summary>
    private void RecordMips(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, ReadOnlySpan<bool> drawn)
    {
        GpuImage radiance = map.Radiance;
        uint levels = radiance.MipLevels;
        foreach (GpuImage image in (ReadOnlySpan<GpuImage>)[radiance, map.Distance])
        {
            PipelineStageFlags2 lastUse = image.State.Stages;
            // Level 0 of every map: the drawn faces since the face pass, the others since their last readers.
            ToGeneral(
                barriers: barriers,
                image: image,
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
                    image: image,
                    baseMip: 1,
                    mipCount: levels - 1,
                    oldLayout: drawn[index] ? ImageLayout.Undefined : ImageLayout.ShaderReadOnlyOptimal,
                    after: lastUse,
                    layers: new LayerRange(FirstLayer(index), CubeFaces.Count)
                );
        }

        barriers.Flush(vk, commandBuffer);
        int runs = PrefilterLevels.DownsampleDispatches((int)levels);
        for (int dispatch = 0; dispatch < runs; dispatch++)
            DownsampleRun(
                vk: vk,
                commandBuffer: commandBuffer,
                barriers: barriers,
                dispatch: dispatch,
                drawn: drawn,
                lastRun: dispatch == runs - 1
            );
        // The levels' first barrier makes these levels read-only, which orders the last run's writes.
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
        ResourceState read = ResourceUses.Describe(ImageUse.SampledFragmentCompute);
        barriers.ImageLevels(
            image: map.Distance,
            baseMip: 0,
            mipCount: levels,
            oldLayout: ImageLayout.General,
            newLayout: read.Layout,
            srcStages: PipelineStageFlags2.ComputeShaderBit,
            srcAccess: AccessFlags2.ShaderStorageWriteBit,
            dstStages: read.Stages,
            dstAccess: read.Access
        );
    }

    /// <summary>
    /// Writes the run of levels of downsample dispatch <paramref name="dispatch"/> of each drawn map's radiance map and
    /// distance cube from the level above the run. After the <paramref name="lastRun"/> of the last drawn map, the
    /// barriers that make the mips read-only order the writes, so none follows.
    /// </summary>
    private void DownsampleRun(
        Vk vk,
        CommandBuffer commandBuffer,
        BarrierBatch barriers,
        int dispatch,
        ReadOnlySpan<bool> drawn,
        bool lastRun
    )
    {
        int lastDrawn = drawn.LastIndexOf(true);
        int levels = (int)map.Radiance.MipLevels;
        (int source, int count) = PrefilterLevels.LevelsOfDownsample(dispatch: dispatch, levels: levels);
        for (int index = 0; index < drawn.Length; index++)
            if (drawn[index])
            {
                var push = new EnvironmentDownsamplePushConstants
                {
                    LevelSize = (uint)Math.Max(1, map.Setting.FaceSize >> (source + 1)),
                    LevelCount = (uint)count,
                    Map = (uint)index,
                };
                Push(
                    vk: vk,
                    commandBuffer: commandBuffer,
                    push: &push,
                    offset: 0,
                    size: (uint)sizeof(EnvironmentDownsamplePushConstants)
                );
                // Both images take the same levels of the same map, and the push constants stay through a change of
                // pipeline with the same layout.
                DispatchRun(
                    vk: vk,
                    commandBuffer: commandBuffer,
                    pipeline: downsample,
                    levelsSet: map.DownsampleSet(dispatch),
                    size: push.LevelSize
                );
                DispatchRun(
                    vk: vk,
                    commandBuffer: commandBuffer,
                    pipeline: distanceDownsample,
                    levelsSet: map.DistanceDownsampleSet(dispatch),
                    size: push.LevelSize
                );
                if (lastRun && index == lastDrawn)
                    break;
                // The next run reads the last level of this one, and the next map writes through the same views.
                foreach (GpuImage image in (ReadOnlySpan<GpuImage>)[map.Radiance, map.Distance])
                    AfterWrites(barriers: barriers, image: image, baseMip: (uint)(source + 1), mipCount: (uint)count);
                barriers.Flush(vk, commandBuffer);
            }
    }

    /// <summary>
    /// Binds <paramref name="pipeline"/>, a downsample, with <paramref name="levelsSet"/> as set 1, and dispatches one run
    /// of one map whose first level has <paramref name="size"/> texels on a side, by the push constants already pushed.
    /// </summary>
    private void DispatchRun(Vk vk, CommandBuffer commandBuffer, Pipeline pipeline, DescriptorSet levelsSet, uint size)
    {
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 1, sets: [levelsSet]);
        Dispatch(vk: vk, commandBuffer: commandBuffer, size: size);
    }

    /// <summary>
    /// Writes each drawn map's specular levels and irradiance cube, which sample its radiance mips, in map index order. As
    /// for the mips, every layer of the specular maps and the cubes is in <see cref="ImageLayout.General"/> while the
    /// prefilter runs, and the maps that are not drawn keep their texels. Every layer of the specular maps ends read-only
    /// for this frame's readers, and the cubes stay in <see cref="ImageLayout.General"/>, which their recorded state
    /// says, for <see cref="EnvironmentMap.RecordReadback"/>.
    /// </summary>
    private void RecordLevels(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, ReadOnlySpan<bool> drawn)
    {
        GpuImage specular = map.Specular;
        // A drawn map's texels of every level are all written anew, after the readers of the frame before.
        foreach (
            (GpuImage image, uint levels) in (ReadOnlySpan<(GpuImage Image, uint Levels)>)
                [(specular, (uint)map.Levels), (map.Irradiance, IrradianceCube.Levels)]
        )
        {
            PipelineStageFlags2 lastUse = image.State.Stages;
            for (int index = 0; index < drawn.Length; index++)
                ToGeneral(
                    barriers: barriers,
                    image: image,
                    baseMip: 0,
                    mipCount: levels,
                    oldLayout: drawn[index] ? ImageLayout.Undefined : ImageLayout.ShaderReadOnlyOptimal,
                    after: lastUse,
                    layers: new LayerRange(FirstLayer(index), CubeFaces.Count)
                );
        }
        barriers.Flush(vk, commandBuffer);
        int lastDrawn = drawn.LastIndexOf(true);
        for (int index = 0; index < drawn.Length; index++)
            if (drawn[index])
            {
                RecordMap(vk: vk, commandBuffer: commandBuffer, barriers: barriers, index: index);
                // After the last map, the specular map's read-only barrier and the readback's barriers order the writes.
                if (index == lastDrawn)
                    break;
                // The next map writes through the same level views.
                AfterWrites(barriers: barriers, image: specular, baseMip: 0, mipCount: (uint)map.Levels);
                AfterWrites(barriers: barriers, image: map.Irradiance, baseMip: 0, mipCount: IrradianceCube.Levels);
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
        map.Irradiance.State = ResourceUses.Describe(ImageUse.StorageCompute);
    }

    /// <summary>
    /// Writes map <paramref name="index"/>'s irradiance cube, level 0 by the convolution and the others by the downsample,
    /// and its specular levels.
    /// </summary>
    private void RecordMap(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, int index)
    {
        // The convolution and the specular levels write different images and read only the radiance map, so they need no
        // barrier between them, and they can run at once.
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, irradiance);
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 1, sets: [map.IrradianceSet]);
        PushMap(vk: vk, commandBuffer: commandBuffer, index: index);
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: IrradianceCube.Workgroups,
            groupCountY: 1,
            groupCountZ: 1
        );
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, prefilter);
        for (int dispatch = 0; dispatch < PrefilterLevels.Dispatches(map.Levels); dispatch++)
            DispatchLevels(vk: vk, commandBuffer: commandBuffer, dispatch: dispatch, index: index);
        // The downsample reads level 0 that the convolution wrote, and makes levels 1 to 3 in one run.
        AfterWrites(barriers: barriers, image: map.Irradiance, baseMip: 0, mipCount: 1);
        barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, downsample);
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 1, sets: [map.IrradianceSet]);
        var push = new EnvironmentDownsamplePushConstants
        {
            LevelSize = IrradianceCube.FaceSize / 2,
            LevelCount = IrradianceCube.Levels - 1,
            Map = (uint)index,
        };
        Push(
            vk: vk,
            commandBuffer: commandBuffer,
            push: &push,
            offset: 0,
            size: (uint)sizeof(EnvironmentDownsamplePushConstants)
        );
        Dispatch(vk: vk, commandBuffer: commandBuffer, size: push.LevelSize);
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
        device.Vk.DestroyPipeline(device.Device, distanceDownsample, null);
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

    /// <summary>One invocation per texel of the six faces of one map, of <paramref name="size"/> texels, of a run's first level.</summary>
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

    /// <summary>
    /// Dispatches a run of levels of map <paramref name="index"/>'s specular map
    /// (<see cref="PrefilterLevels.LevelsOfDispatch"/>): the workgroups of each level follow those of the level before,
    /// with <see cref="PrefilterLevels.Lanes"/> invocations per texel of its six faces, in rows of at most 65,535
    /// workgroups (<see cref="ComputeLimits.Grid"/>).
    /// </summary>
    private void DispatchLevels(Vk vk, CommandBuffer commandBuffer, int dispatch, int index)
    {
        (int first, int count) = PrefilterLevels.LevelsOfDispatch(dispatch: dispatch, levels: map.Levels);
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 1, sets: [map.SpecularDispatchSet(dispatch)]);
        var push = new EnvironmentPrefilterPushConstants
        {
            Samples = PrefilterLevels.SamplesPerTexel,
            LevelCount = (uint)count,
            Map = (uint)index,
        };
        uint groups = 0;
        for (int binding = 0; binding < count; binding++)
        {
            int level = first + binding;
            push.Levels[binding] = new EnvironmentPrefilterLevel
            {
                FirstGroup = groups,
                LevelSize = (uint)(map.Setting.FaceSize >> level),
                Roughness = PrefilterLevels.Roughness(level, map.Levels),
                Lanes = (uint)PrefilterLevels.Lanes(level),
            };
            groups += (uint)PrefilterLevels.Workgroups(level: level, faceSize: map.Setting.FaceSize);
        }

        push.GroupCount = groups;
        Push(
            vk: vk,
            commandBuffer: commandBuffer,
            push: &push,
            offset: 0,
            size: (uint)sizeof(EnvironmentPrefilterPushConstants)
        );
        // The rows keep each count within the limit of every device.
        (uint x, uint y) = ComputeLimits.Grid(groups);
        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: x, groupCountY: y, groupCountZ: 1);
    }

    /// <summary>
    /// Pushes only <see cref="EnvironmentPrefilterPushConstants.Map"/>, as <paramref name="index"/>: the one push constant
    /// that the convolution of the irradiance cube reads.
    /// </summary>
    private void PushMap(Vk vk, CommandBuffer commandBuffer, int index)
    {
        uint value = (uint)index;
        Push(vk: vk, commandBuffer: commandBuffer, push: &value, offset: MapOffset, size: sizeof(uint));
    }

    /// <summary>
    /// Pushes <paramref name="size"/> bytes at <paramref name="push"/> to the prefilter layout's compute range, from byte
    /// <paramref name="offset"/>.
    /// </summary>
    private void Push(Vk vk, CommandBuffer commandBuffer, void* push, uint offset, uint size) =>
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.EnvironmentPrefilterLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: offset,
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
