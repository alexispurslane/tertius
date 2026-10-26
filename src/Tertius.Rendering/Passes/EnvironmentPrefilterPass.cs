using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Derives the radiance map's mips, the specular map and the irradiance from level 0 of the six faces (stage
/// <see cref="FrameGraph.Stage.EnvironmentPrefilter"/>, docs/plans/environment-map.md, "The prefilter"), then copies the
/// irradiance into the frame slot's readback buffer. Compute dispatches on the graphics queue.
/// </summary>
/// <remarks>
/// It reads only level 0 of the radiance map, never its own earlier output, so all it writes is a pure function of the
/// six stored faces. The whole map is prefiltered in every drawn frame, because a rough lobe crosses into the neighbors
/// of the face that changed. After the pass the specular map is in <see cref="ImageUse.SampledFragment"/> and the
/// irradiance buffer in <see cref="BufferUse.ShaderRead"/>, the states that its readers expect.
/// </remarks>
internal sealed unsafe class EnvironmentPrefilterPass : RenderPass
{
    /// <summary>The downsample's workgroup: 8 by 8 texels of one face of the first level of its run.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly EnvironmentMap map;
    private readonly Pipeline downsample;
    private readonly Pipeline prefilter;
    private readonly Pipeline irradiance;
    private readonly Pipeline irradianceSum;

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
            irradianceSum = Create(name: "environment irradiance sum", shader: "environment_irradiance_sum.comp");
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
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 0, sets: [map.PrefilterSet]);
        // The mips first, because the prefilter's samples read them.
        RecordMips(vk: vk, commandBuffer: commandBuffer, barriers: barriers);
        // Every texel of every level is written anew, after the readers of the frame before.
        barriers.Image(map.Specular, ImageUse.StorageCompute, discard: true);
        barriers.Buffer(map.Irradiance, BufferUse.ComputeWrite);
        barriers.Buffer(map.IrradiancePartials, BufferUse.ComputeWrite);
        barriers.Flush(vk, commandBuffer);
        // The irradiance's blocks and the specular levels write different resources and read only the radiance map, so
        // they need no barrier between them, and they can run at once.
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, irradiance);
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: (uint)Irradiance.Blocks(map.Setting.FaceSize),
            groupCountY: 1,
            groupCountZ: 1
        );
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, prefilter);
        for (int dispatch = 0; dispatch < PrefilterLevels.Dispatches(map.Levels); dispatch++)
            DispatchLevels(vk: vk, commandBuffer: commandBuffer, dispatch: dispatch);
        barriers.Buffer(map.IrradiancePartials, BufferUse.ComputeRead);
        barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, irradianceSum);
        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: 1, groupCountY: 1, groupCountZ: 1);

        // Right after the irradiance, so a reading is the irradiance that this frame's passes use.
        map.RecordReadback(commandBuffer: commandBuffer, barriers: barriers, slot: context.Frame.Index);
        barriers.Buffer(map.Irradiance, BufferUse.ShaderRead);
        barriers.Image(map.Specular, ImageUse.SampledFragment);
        barriers.Flush(vk, commandBuffer);
    }

    public override void Dispose()
    {
        // A pipeline that the constructor did not make is a null handle, which vkDestroyPipeline ignores.
        device.Vk.DestroyPipeline(device.Device, downsample, null);
        device.Vk.DestroyPipeline(device.Device, prefilter, null);
        device.Vk.DestroyPipeline(device.Device, irradiance, null);
        device.Vk.DestroyPipeline(device.Device, irradianceSum, null);
    }

    /// <summary>
    /// Rebuilds the radiance map's levels 1 and up from level 0, in runs of up to three levels, each run from the last
    /// level of the run before (<see cref="PrefilterLevels.LevelsOfDownsample"/>). Level 0 and the written levels are
    /// storage images in <see cref="ImageLayout.General"/>, and the whole map ends read-only for the prefilter's samples.
    /// </summary>
    private void RecordMips(Vk vk, CommandBuffer commandBuffer, BarrierBatch barriers)
    {
        GpuImage radiance = map.Radiance;
        uint levels = radiance.MipLevels;
        PipelineStageFlags2 lastUse = radiance.State.Stages;
        ToGeneral(barriers: barriers, baseMip: 0, mipCount: 1, discard: false, after: lastUse);
        // Every texel of levels 1 and up is written anew.
        ToGeneral(barriers: barriers, baseMip: 1, mipCount: levels - 1, discard: true, after: lastUse);
        barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, downsample);
        int dispatches = PrefilterLevels.DownsampleDispatches((int)levels);
        for (int dispatch = 0; dispatch < dispatches; dispatch++)
        {
            (int source, int count) = PrefilterLevels.LevelsOfDownsample(dispatch: dispatch, levels: (int)levels);
            BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 1, sets: [map.DownsampleSet(dispatch)]);
            var push = new EnvironmentDownsamplePushConstants
            {
                LevelSize = (uint)Math.Max(1, map.Setting.FaceSize >> (source + 1)),
                LevelCount = (uint)count,
            };
            vk.CmdPushConstants(
                commandBuffer: commandBuffer,
                layout: layouts.EnvironmentPrefilterLayout,
                stageFlags: ShaderStageFlags.ComputeBit,
                offset: 0,
                size: (uint)sizeof(EnvironmentDownsamplePushConstants),
                pValues: &push
            );
            Dispatch(vk: vk, commandBuffer: commandBuffer, size: push.LevelSize);
            if (dispatch == dispatches - 1)
                break;
            // The next run reads the last level of this one.
            barriers.ImageLevels(
                image: radiance,
                baseMip: (uint)(source + count),
                mipCount: 1,
                oldLayout: ImageLayout.General,
                newLayout: ImageLayout.General,
                srcStages: PipelineStageFlags2.ComputeShaderBit,
                srcAccess: AccessFlags2.ShaderStorageWriteBit,
                dstStages: PipelineStageFlags2.ComputeShaderBit,
                dstAccess: AccessFlags2.ShaderStorageReadBit
            );
            barriers.Flush(vk, commandBuffer);
        }

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
        radiance.State = ResourceUses.Describe(ImageUse.SampledCompute);
    }

    /// <summary>Makes some levels of the radiance map storage images, after its last use in <paramref name="after"/>.</summary>
    private void ToGeneral(
        BarrierBatch barriers,
        uint baseMip,
        uint mipCount,
        bool discard,
        PipelineStageFlags2 after
    ) =>
        barriers.ImageLevels(
            image: map.Radiance,
            baseMip: baseMip,
            mipCount: mipCount,
            oldLayout: discard ? ImageLayout.Undefined : ImageLayout.ShaderReadOnlyOptimal,
            newLayout: ImageLayout.General,
            srcStages: after,
            srcAccess: AccessFlags2.None,
            dstStages: PipelineStageFlags2.ComputeShaderBit,
            dstAccess: AccessFlags2.ShaderStorageReadBit | AccessFlags2.ShaderStorageWriteBit
        );

    /// <summary>One invocation per texel of the six faces of <paramref name="size"/> texels of a run's first level.</summary>
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
    /// Dispatches a run of levels of the specular map (<see cref="PrefilterLevels.LevelsOfDispatch"/>): the workgroups
    /// of each level follow those of the level before, with <see cref="PrefilterLevels.Lanes"/> invocations per texel of
    /// its six faces, in rows of at most 65,535 workgroups (<see cref="PrefilterLevels.Grid"/>).
    /// </summary>
    private void DispatchLevels(Vk vk, CommandBuffer commandBuffer, int dispatch)
    {
        (int first, int count) = PrefilterLevels.LevelsOfDispatch(dispatch: dispatch, levels: map.Levels);
        BindSets(vk: vk, commandBuffer: commandBuffer, firstSet: 1, sets: [map.SpecularDispatchSet(dispatch)]);
        var push = new EnvironmentPrefilterPushConstants
        {
            Samples = PrefilterLevels.SamplesPerTexel,
            LevelCount = (uint)count,
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
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.EnvironmentPrefilterLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(EnvironmentPrefilterPushConstants),
            pValues: &push
        );
        // The rows keep each count within the limit of every device.
        (int x, int y) = PrefilterLevels.Grid((int)groups);
        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: (uint)x, groupCountY: (uint)y, groupCountZ: 1);
    }

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
