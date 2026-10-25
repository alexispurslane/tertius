using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Derives the environment map's specular map and irradiance from the six faces of the radiance map (stage
/// <see cref="FrameGraph.Stage.EnvironmentPrefilter"/>, docs/plans/environment-map.md, "The prefilter"), then copies the
/// irradiance into the frame slot's readback buffer. Compute dispatches on the graphics queue.
/// </summary>
/// <remarks>
/// It reads only the radiance map, never its own earlier output, so the specular map and the irradiance are a pure
/// function of the six stored faces. The whole map is prefiltered in every drawn frame, because a rough lobe crosses into
/// the neighbors of the face that changed. After the pass the specular map is in <see cref="ImageUse.SampledFragment"/>
/// and the irradiance buffer in <see cref="BufferUse.ShaderRead"/>, the states that its readers expect.
/// </remarks>
internal sealed unsafe class EnvironmentPrefilterPass : RenderPass
{
    /// <summary>The prefilter's workgroup: 8 by 8 texels of one face.</summary>
    private const uint GroupSize = 8;

    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly EnvironmentMap map;
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
        prefilter = new ComputePipelineDesc
        {
            Name = "environment prefilter",
            Shader = "environment_prefilter.comp",
            Layout = layouts.EnvironmentPrefilterLayout,
        }.Create(device, shaders);
        try
        {
            irradiance = new ComputePipelineDesc
            {
                Name = "environment irradiance",
                Shader = "environment_irradiance.comp",
                Layout = layouts.EnvironmentPrefilterLayout,
            }.Create(device, shaders);
        }
        catch
        {
            device.Vk.DestroyPipeline(device.Device, prefilter, null);
            throw;
        }
    }

    public override string Name => "environment prefilter";

    public override void Record(FrameContext context)
    {
        Vk vk = context.Vk;
        CommandBuffer commandBuffer = context.CommandBuffer;
        BarrierBatch barriers = context.Barriers;
        barriers.Image(map.Radiance, ImageUse.SampledCompute);
        // Every texel of every level is written anew, after the readers of the frame before.
        barriers.Image(map.Specular, ImageUse.StorageCompute, discard: true);
        barriers.Buffer(map.Irradiance, BufferUse.ComputeWrite);
        barriers.Flush(vk, commandBuffer);

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
        device.Vk.DestroyPipeline(device.Device, prefilter, null);
        device.Vk.DestroyPipeline(device.Device, irradiance, null);
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
        uint groups = (size + GroupSize - 1) / GroupSize;
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: groups,
            groupCountY: groups,
            groupCountZ: CubeFaces.Count
        );
    }
}
