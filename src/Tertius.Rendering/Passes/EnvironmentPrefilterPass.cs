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
        DescriptorSet set = map.PrefilterSet;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layouts.EnvironmentPrefilterLayout,
            firstSet: 0,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        // The mips first, because the prefilter's samples read them.
        RecordMips(vk: vk, commandBuffer: commandBuffer, barriers: barriers);
        // Every texel of every level is written anew, after the readers of the frame before.
        barriers.Image(map.Specular, ImageUse.StorageCompute, discard: true);
        barriers.Buffer(map.Irradiance, BufferUse.ComputeWrite);
        barriers.Flush(vk, commandBuffer);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, prefilter);
        // The levels write different images and read only the radiance map, so they need no barrier between them.
        for (int level = 0; level < map.Levels; level++)
            DispatchLevel(vk: vk, commandBuffer: commandBuffer, level: level);
        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, irradiance);
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
    }

    /// <summary>
    /// Rebuilds the radiance map's levels 1 and up from level 0, level by level, each from the one above it. Level 0 and
    /// the written levels are storage images in <see cref="ImageLayout.General"/>, and the whole map ends read-only for the
    /// prefilter's samples.
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
        for (uint level = 1; level < levels; level++)
        {
            uint size = (uint)Math.Max(1, map.Setting.FaceSize >> (int)level);
            var push = new EnvironmentDownsamplePushConstants { Level = level, LevelSize = size };
            vk.CmdPushConstants(
                commandBuffer: commandBuffer,
                layout: layouts.EnvironmentPrefilterLayout,
                stageFlags: ShaderStageFlags.ComputeBit,
                offset: 0,
                size: (uint)sizeof(EnvironmentDownsamplePushConstants),
                pValues: &push
            );
            Dispatch(vk: vk, commandBuffer: commandBuffer, size: size);
            // The next level reads the one just written.
            barriers.ImageLevels(
                image: radiance,
                baseMip: level,
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

    /// <summary>One invocation per texel of a level's six faces of <paramref name="size"/> texels.</summary>
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

    /// <summary>Dispatches one level of the specular map: one invocation per texel of its six faces.</summary>
    private void DispatchLevel(Vk vk, CommandBuffer commandBuffer, int level)
    {
        uint size = (uint)(map.Setting.FaceSize >> level);
        var push = new EnvironmentPrefilterPushConstants
        {
            Level = (uint)level,
            LevelSize = size,
            Roughness = PrefilterLevels.Roughness(level, map.Levels),
            Samples = PrefilterLevels.SamplesPerTexel,
        };
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: layouts.EnvironmentPrefilterLayout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: (uint)sizeof(EnvironmentPrefilterPushConstants),
            pValues: &push
        );
        Dispatch(vk: vk, commandBuffer: commandBuffer, size: size);
    }
}
