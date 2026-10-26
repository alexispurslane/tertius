using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Everything that differs between the engine's compute pipelines: the shader, its layout and its constants.</summary>
internal sealed class ComputePipelineDesc
{
    public required string Name { get; init; }

    /// <summary>The compute shader's file name, such as <c>exposure_meter.comp</c>.</summary>
    public required string Shader { get; init; }

    public required PipelineLayout Layout { get; init; }

    /// <summary>The specialization constants of the stage, or null for a shader that declares none.</summary>
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
