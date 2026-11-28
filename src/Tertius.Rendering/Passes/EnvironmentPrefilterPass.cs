using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Derives each map's mips, specular levels and irradiance cube from level 0 of its six faces, and its distance cube's
/// levels from the faces' inverse distances, for each map that this frame drew a face of (stage
/// <see cref="FrameGraph.Stage.EnvironmentPrefilter"/>, https://github.com/alexispurslane/tertius/wiki/environment-map, "The prefilter",
/// https://github.com/alexispurslane/tertius/wiki/environment-maps-per-frame, "The prefilter of each map", and https://github.com/alexispurslane/tertius/wiki/environment-map-distances,
/// "How the capture writes it"), then copies the last level of the served maps' irradiance cubes into the frame slot's
/// readback buffer. Compute dispatches on the graphics queue.
/// </summary>
/// <remarks>
/// Rebuilds the drawn maps' radiance and distance mips, then updates specular levels and diffuse contributions for
/// freshly captured faces. It combines the contributions for every served map on every draw and publishes the
/// prefiltered cubes before their readers (ARCHITECTURE.md, "The prefilter").
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
    private readonly Pipeline combine;

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
            combine = Create(name: "environment irradiance combine", shader: "environment_irradiance_combine.comp");
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
        if (scene.EnvironmentFaces.Count > 0)
        {
            Span<FaceMask> faces = stackalloc FaceMask[map.Capacity];
            faces.Clear();
            foreach (EnvironmentFaceDraw face in scene.EnvironmentFaces)
                faces[face.Map] |= CubeFaces.Of(face.Face);
            RecordMips(vk: vk, commandBuffer: commandBuffer, barriers: barriers, faces: faces);
            RecordLevels(context, faces);
        }

        RecordIrradiance(context);

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
    private void RecordMips(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers, ReadOnlySpan<FaceMask> faces)
    {
        uint levels = map.Radiance.MipLevels;
        MipsToGeneral(barriers: barriers, faces: faces, levels: levels);
        barriers.Flush(vk, commandBuffer);
        int runs = PrefilterLevels.DownsampleDispatches((int)levels);
        for (int dispatch = 0; dispatch < runs; dispatch++)
            DownsampleRun(
                vk: vk,
                commandBuffer: commandBuffer,
                barriers: barriers,
                dispatch: dispatch,
                faces: faces,
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
    private void MipsToGeneral(BarrierBatch barriers, ReadOnlySpan<FaceMask> faces, uint levels)
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
            for (int index = 0; index < faces.Length; index++)
                ToGeneral(
                    barriers: barriers,
                    image: image,
                    baseMip: 1,
                    mipCount: levels - 1,
                    oldLayout: faces[index] != FaceMask.None
                        ? ImageLayout.Undefined
                        : ImageLayout.ShaderReadOnlyOptimal,
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
        ReadOnlySpan<FaceMask> faces,
        bool lastRun
    )
    {
        int lastDrawn = faces.LastIndexOfAnyExcept(FaceMask.None);
        int levels = (int)map.Radiance.MipLevels;
        (int source, int count) = PrefilterLevels.LevelsOfDownsample(dispatch: dispatch, levels: levels);
        for (int index = 0; index < faces.Length; index++)
            if (faces[index] != FaceMask.None)
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

    /// <summary>Updates the specular levels and the diffuse contribution of each captured face.</summary>
    private void RecordLevels(FrameContext context, ReadOnlySpan<FaceMask> faces)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        GpuImage specular = map.Specular;
        uint levels = (uint)map.Levels;
        for (int index = 0; index < faces.Length; index++)
            ToGeneral(
                barriers: barriers,
                image: specular,
                baseMip: 0,
                mipCount: levels,
                oldLayout: faces[index] == FaceMask.All ? ImageLayout.Undefined : ImageLayout.ShaderReadOnlyOptimal,
                after: specular.State.Stages,
                layers: new LayerRange(FirstLayer(index), CubeFaces.Count)
            );
        int lastDrawn = faces.LastIndexOfAnyExcept(FaceMask.None);
        for (int index = 0; index <= lastDrawn; index++)
        {
            if (faces[index] == FaceMask.None)
                continue;
            barriers.Buffer(map.IrradianceContributions, BufferUse.ComputeWrite);
            barriers.Flush(vk, commandBuffer);
            RecordMap(context: context, index: index, faces: faces[index]);
            // The next map writes through the same level views. Order the writes through those views.
            if (index < lastDrawn)
                AfterWrites(barriers: barriers, image: specular, baseMip: 0, mipCount: levels);
        }

        ResourceState sampled = ResourceUses.Describe(ImageUse.SampledFragment);
        barriers.ImageLevels(
            image: specular,
            baseMip: 0,
            mipCount: levels,
            oldLayout: ImageLayout.General,
            newLayout: sampled.Layout,
            srcStages: PipelineStageFlags2.ComputeShaderBit,
            srcAccess: AccessFlags2.ShaderStorageWriteBit,
            dstStages: sampled.Stages,
            dstAccess: sampled.Access
        );
    }

    private void RecordMap(FrameContext context, int index, FaceMask faces)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, irradiance);
        var push = new EnvironmentIrradiancePushConstants
        {
            Map = (uint)index,
            Faces = (uint)faces,
            Draw = unchecked((uint)context.Number),
            BlendFrames = map.IrradianceBlendFrames(index),
        };
        Push(
            vk: vk,
            commandBuffer: commandBuffer,
            push: &push,
            offset: 0,
            size: (uint)sizeof(EnvironmentIrradiancePushConstants)
        );
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: CubeFaces.Count * IrradianceCube.WorkgroupsPerFace,
            groupCountY: 1,
            groupCountZ: (uint)BitOperations.PopCount(push.Faces)
        );
        // Both dispatches read the radiance mips and write separate resources.
        (uint First, uint Count) specularFaces = FaceRun(faces);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, prefilter);
        for (int dispatch = 0; dispatch < PrefilterLevels.Dispatches(map.Levels); dispatch++)
            DispatchLevels(
                vk: vk,
                commandBuffer: commandBuffer,
                dispatch: dispatch,
                index: index,
                faces: specularFaces
            );
    }

    /// <summary>Advances the live blends, combines them into level 0 and derives the remaining levels.</summary>
    private void RecordIrradiance(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        GpuImage cube = map.Irradiance;
        int maps = context.Scene.MapCount;

        barriers.Buffer(map.IrradianceContributions, BufferUse.ComputeWrite);
        barriers.Image(cube, ImageUse.StorageCompute, discard: true);
        barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, combine);
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 1, sets: [map.IrradianceSet]);
        uint draw = unchecked((uint)context.Number);
        Push(vk: vk, commandBuffer: commandBuffer, push: &draw, offset: 0, size: sizeof(uint));
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: IrradianceCube.Texels / 64,
            groupCountY: 1,
            groupCountZ: (uint)maps
        );

        AfterWrites(barriers: barriers, image: cube, baseMip: 0, mipCount: 1);
        barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, DownsampleOf(cube.Format));
        var push = new EnvironmentDownsamplePushConstants
        {
            LevelSize = IrradianceCube.FaceSize / 2,
            LevelCount = IrradianceCube.Levels - 1,
        };
        Push(
            vk: vk,
            commandBuffer: commandBuffer,
            push: &push,
            offset: 0,
            size: (uint)sizeof(EnvironmentDownsamplePushConstants)
        );
        Dispatch(vk: vk, commandBuffer: commandBuffer, size: push.LevelSize, maps: (uint)maps);
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
        device.Vk.DestroyPipeline(device.Device, combine, null);
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

    /// <summary>One invocation per texel of a run's first level in every face of the maps.</summary>
    private static void Dispatch(Vk vk, CommandBuffer commandBuffer, uint size, uint maps = 1)
    {
        uint groups = (size + GroupSize - 1) / GroupSize;
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: groups,
            groupCountY: groups,
            groupCountZ: CubeFaces.Count * maps
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
            groups += (uint)
                PrefilterLevels.Workgroups(level: level, faceSize: map.Setting.FaceSize, faces: (int)faces.Count);
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
    /// The run of faces that a map's filters write for its drawn <paramref name="faces"/>: the faces themselves when they
    /// are one run, as the faces of one frame's turn are, else all six, which a refresh draws.
    /// </summary>
    internal static (uint First, uint Count) FaceRun(FaceMask faces)
    {
        uint mask = (uint)faces;
        uint first = (uint)BitOperations.TrailingZeroCount(mask);
        uint count = (uint)BitOperations.PopCount(mask);
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
