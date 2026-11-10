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
/// It reads only level 0 of a map's layers, never its own earlier output. A drawn map's mips are rebuilt whole, and the
/// specular levels and the irradiance of only its drawn faces, so each face's filters are a pure function of the six
/// faces as they were stored at its last turn (ARCHITECTURE.md, "The prefilter"). A map with no drawn face keeps its levels
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
        Span<FaceMask> faces = stackalloc FaceMask[map.Capacity];
        bool anyDrawn = false;
        foreach (EnvironmentFaceDraw face in scene.EnvironmentFaces)
        {
            drawn[face.Map] = true;
            faces[face.Map] |= (FaceMask)(1 << (int)face.Face);
            anyDrawn = true;
        }

        if (anyDrawn)
        {
            RecordMips(vk: vk, commandBuffer: commandBuffer, barriers: barriers, drawn: drawn);
            RecordLevels(vk: vk, commandBuffer: commandBuffer, barriers: barriers, faces: faces);
        }

        // Every layer of the radiance maps, the distance cubes and the specular maps is read-only again: the prefiltered
        // maps' just now, the others since their last one.
        foreach (DrawnCube cube in map.DrawnCubes)
            cube.ReturnToReaders();
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
    /// layers, and a storage image's layout must hold for every layer of its view, so every layer of each drawn cube is in
    /// <see cref="ImageLayout.General"/> while the downsample runs. The maps that are not drawn keep their texels through
    /// the transitions. The radiance maps' levels end read-only for the prefilter's samples, and the distance cubes' for
    /// this frame's shading passes.
    /// </summary>
    private void RecordMips(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, ReadOnlySpan<bool> drawn)
    {
        uint levels = map.Radiance.MipLevels;
        MipsToGeneral(barriers: barriers, drawn: drawn, levels: levels);
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
        foreach (DrawnCube cube in map.DrawnCubes)
        {
            ResourceState read = ResourceUses.Describe(cube.Readers);
            barriers.ImageLevels(
                image: cube.Image,
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
    }

    /// <summary>
    /// Makes every layer of the <paramref name="levels"/> levels of the radiance maps and the distance cubes storage
    /// images, after each image's last readers. Level 0 of every map keeps its texels: the drawn faces since the face
    /// pass, the others since their last readers. In levels 1 and up, a drawn map's texels are discarded, because the
    /// downsample writes them all anew, and the other maps keep theirs.
    /// </summary>
    private void MipsToGeneral(BarrierBatch barriers, ReadOnlySpan<bool> drawn, uint levels)
    {
        foreach (DrawnCube cube in map.DrawnCubes)
        {
            GpuImage image = cube.Image;
            PipelineStageFlags2 lastUse = image.State.Stages;
            ToGeneral(
                barriers: barriers,
                image: image,
                baseMip: 0,
                mipCount: 1,
                oldLayout: ImageLayout.ShaderReadOnlyOptimal,
                after: lastUse,
                layers: AllLayers
            );
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
                // Every drawn cube takes the same levels of the same map, and the push constants stay through a change of
                // pipeline with the same layout.
                foreach (DrawnCube cube in map.DrawnCubes)
                    DispatchRun(
                        vk: vk,
                        commandBuffer: commandBuffer,
                        pipeline: DownsampleOf(cube.Image.Format),
                        levelsSet: cube.DownsampleSet(dispatch),
                        size: push.LevelSize
                    );
                if (lastRun && index == lastDrawn)
                    break;
                // The next run reads the last level of this one, and the next map writes through the same views.
                foreach (DrawnCube cube in map.DrawnCubes)
                    AfterWrites(
                        barriers: barriers,
                        image: cube.Image,
                        baseMip: (uint)(source + 1),
                        mipCount: (uint)count
                    );
                barriers.Flush(vk, commandBuffer);
            }
    }

    /// <summary>
    /// The downsample whose storage images have <paramref name="format"/>: <see cref="EnvironmentMap.DistanceFormat"/> or
    /// <see cref="EnvironmentMap.RadianceFormat"/>, which the irradiance cubes share. Both include one body,
    /// environment_downsample.glsl, so each level is the same solid-angle mean of its children.
    /// </summary>
    private Pipeline DownsampleOf(Format format) =>
        format == EnvironmentMap.DistanceFormat ? distanceDownsample : downsample;

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
    /// Writes the specular levels and the irradiance of each drawn face of each map, which sample its radiance mips, in map
    /// index order (<see cref="RecordMap"/>). As for the mips, every layer of the specular maps and the cubes is in
    /// <see cref="ImageLayout.General"/> while the prefilter runs, and the faces that are not drawn keep their texels: a
    /// map's texels are discarded only when all six of its faces are drawn. Every layer of the specular maps ends read-only
    /// for this frame's readers, and the cubes stay in <see cref="ImageLayout.General"/>, which their recorded state
    /// says, for <see cref="EnvironmentMap.RecordReadback"/>.
    /// </summary>
    private void RecordLevels(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, ReadOnlySpan<FaceMask> faces)
    {
        GpuImage specular = map.Specular;
        // A map whose six faces are all drawn has its texels of every level written anew, after the readers of the frame
        // before.
        foreach (
            (GpuImage image, uint levels) in (ReadOnlySpan<(GpuImage Image, uint Levels)>)
                [(specular, (uint)map.Levels), (map.Irradiance, IrradianceCube.Levels)]
        )
        {
            PipelineStageFlags2 lastUse = image.State.Stages;
            for (int index = 0; index < faces.Length; index++)
                ToGeneral(
                    barriers: barriers,
                    image: image,
                    baseMip: 0,
                    mipCount: levels,
                    oldLayout: faces[index] == FaceMask.All ? ImageLayout.Undefined : ImageLayout.ShaderReadOnlyOptimal,
                    after: lastUse,
                    layers: new LayerRange(FirstLayer(index), CubeFaces.Count)
                );
        }
        barriers.Flush(vk, commandBuffer);
        int lastDrawn = -1;
        for (int index = 0; index < faces.Length; index++)
            if (faces[index] != FaceMask.None)
                lastDrawn = index;
        for (int index = 0; index < faces.Length; index++)
            if (faces[index] != FaceMask.None)
            {
                RecordMap(vk: vk, commandBuffer: commandBuffer, barriers: barriers, index: index, faces: faces[index]);
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
    /// Writes the drawn <paramref name="faces"/> of map <paramref name="index"/>'s irradiance cube, level 0 by the
    /// convolution and the others by the downsample, and of its specular levels. A frame of a film draws one face, so the
    /// cost of the map's filters spreads evenly over the faces' turns, and a face's filters take its neighbors' light of
    /// its own last turn (ARCHITECTURE.md, "The prefilter").
    /// </summary>
    private void RecordMap(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, int index, FaceMask faces)
    {
        // The convolution and the specular levels write different images and read only the radiance map, so they need no
        // barrier between them, and they can run at once. Each writes the run of faces in one dispatch, because two
        // dispatches through one level view would need a barrier between them.
        (uint first, uint count) = FaceRun(faces);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, irradiance);
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 1, sets: [map.IrradianceSet]);
        PushMap(vk: vk, commandBuffer: commandBuffer, index: index, firstFace: first, faceCount: count);
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: count * IrradianceCube.WorkgroupsPerFace,
            groupCountY: 1,
            groupCountZ: 1
        );
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, prefilter);
        for (int dispatch = 0; dispatch < PrefilterLevels.Dispatches(map.Levels); dispatch++)
            DispatchLevels(
                vk: vk,
                commandBuffer: commandBuffer,
                dispatch: dispatch,
                index: index,
                faces: (first, count)
            );
        // The downsample reads level 0 that the convolution wrote, and makes levels 1 to 3 in one run.
        AfterWrites(barriers: barriers, image: map.Irradiance, baseMip: 0, mipCount: 1);
        barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, DownsampleOf(map.Irradiance.Format));
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
    private void DispatchLevels(
        Vk vk,
        CommandBuffer commandBuffer,
        int dispatch,
        int index,
        (uint First, uint Count) faces
    )
    {
        (int first, int count) = PrefilterLevels.LevelsOfDispatch(dispatch: dispatch, levels: map.Levels);
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 1, sets: [map.SpecularDispatchSet(dispatch)]);
        var push = new EnvironmentPrefilterPushConstants
        {
            Samples = PrefilterLevels.SamplesPerTexel,
            LevelCount = (uint)count,
            Map = (uint)index,
            FirstFace = faces.First,
            FaceCount = faces.Count,
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
            groups += (uint)PrefilterLevels.Workgroups(level: level, faceSize: map.Setting.FaceSize, faces: (int)faces.Count);
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
    /// Pushes only <see cref="EnvironmentPrefilterPushConstants.Map"/>,
    /// <see cref="EnvironmentPrefilterPushConstants.FirstFace"/> and <see cref="EnvironmentPrefilterPushConstants.FaceCount"/>:
    /// the push constants that the convolution of the irradiance cube reads, which lie next to each other.
    /// </summary>
    private void PushMap(Vk vk, CommandBuffer commandBuffer, int index, uint firstFace, uint faceCount)
    {
        uint* values = stackalloc uint[3];
        values[0] = (uint)index;
        values[1] = firstFace;
        values[2] = faceCount;
        Push(vk: vk, commandBuffer: commandBuffer, push: values, offset: MapOffset, size: 3 * sizeof(uint));
    }

    /// <summary>
    /// The run of faces that a map's filters write for its drawn <paramref name="faces"/>: the faces themselves when they
    /// are one run, as the faces of one frame's turn are, else all six, which a refresh draws.
    /// </summary>
    internal static (uint First, uint Count) FaceRun(FaceMask faces)
    {
        uint mask = (uint)faces;
        uint first = (uint)System.Numerics.BitOperations.TrailingZeroCount(mask);
        uint count = (uint)System.Numerics.BitOperations.PopCount(mask);
        bool oneRun = mask == ((1u << (int)count) - 1) << (int)first;
        return oneRun ? (first, count) : (0, CubeFaces.Count);
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
