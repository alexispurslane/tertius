using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Everything that differs between the engine's compute pipelines: the shader, its layout and its constants.</summary>
internal sealed class ComputePipelineDesc
{
    public required string Name { get; init; }

    /// <summary>The compute shader's file name, such as <c>exposure_meter.comp</c>.</summary>
    public required string Shader { get; init; }

    public required PipelineLayout Layout { get; init; }

    /// <summary>
    /// The specialization constants of the stage, or null. A stage ignores the constants that its compiled module does
    /// not hold, so a pass may give the same constants to each of its shaders: Vulkan ignores a map entry whose
    /// constantID the module does not use (VkSpecializationMapEntry).
    /// </summary>
    public ShaderConstants? Constants { get; init; }

    /// <summary>Creates the pipeline. The caller destroys it.</summary>
    public unsafe Pipeline Create(VulkanDevice device, ShaderLibrary shaders)
    {
        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        SpecializationStorage storage;
        var info = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = shaders.Stage(
                stage: ShaderStageFlags.ComputeBit,
                file: Shader,
                entry: entry,
                constants: ShaderConstants.Specialization(Constants, &storage)
            ),
            Layout = Layout,
        };
        device
            .Vk.CreateComputePipelines(
                device: device.Device,
                pipelineCache: default,
                createInfoCount: 1,
                pCreateInfos: &info,
                pAllocator: null,
                pPipelines: out Pipeline pipeline
            )
            .Check($"vkCreateComputePipelines({Name})");
        device.SetName(ObjectType.Pipeline, pipeline.Handle, Name);
        return pipeline;
    }
}
