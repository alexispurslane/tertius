using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Everything that differs between the engine's compute pipelines: the shader and its layout.</summary>
internal sealed class ComputePipelineDesc
{
    public required string Name { get; init; }

    /// <summary>The compute shader's file name, such as <c>exposure_meter.comp</c>.</summary>
    public required string Shader { get; init; }

    public required PipelineLayout Layout { get; init; }

    /// <summary>Creates the pipeline. The caller destroys it.</summary>
    public unsafe Pipeline Create(VulkanDevice device, ShaderLibrary shaders)
    {
        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        var info = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit,
                Module = shaders.Get(Shader),
                PName = entry,
            },
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
