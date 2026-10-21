using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>How a pipeline reads vertices from binding 0.</summary>
internal enum VertexInput
{
    /// <summary>No vertex buffer: fullscreen passes generate their vertices.</summary>
    None,
    /// <summary>Every attribute of the <see cref="Vertex"/> layout, at locations 0 to 3.</summary>
    Mesh,
    /// <summary>Only <see cref="Vertex.Position"/> at location 0, with the <see cref="Vertex"/> stride. Depth-only passes.</summary>
    PositionOnly,
}

/// <summary>How a pipeline blends its output into every color attachment. No case carries a field, so it is an enum.</summary>
internal enum BlendMode
{
    /// <summary>The output replaces the target.</summary>
    None,
    /// <summary>Straight alpha blending: the color is mixed by the source alpha, and the alpha builds up.</summary>
    Alpha,
    /// <summary>The source color is added to the target, with the factors One and One. The target's alpha stays as it is.</summary>
    Additive,
}

/// <summary>
/// Everything that differs between the engine's graphics pipelines. All of them use dynamic rendering, a dynamic
/// viewport and scissor, and read vertices as <see cref="VertexInput"/> says.
/// </summary>
internal sealed class GraphicsPipelineDesc
{
    public required string Name { get; init; }
    public required string VertexShader { get; init; }
    /// <summary>The fragment stage. Null for a depth-only pipeline.</summary>
    public required string? FragmentShader { get; init; }
    public required PipelineLayout Layout { get; init; }
    public required Format[] ColorFormats { get; init; }
    public Format DepthFormat { get; init; } = Format.Undefined;
    public VertexInput VertexInput { get; init; }
    public bool DepthTest { get; init; }
    public bool DepthWrite { get; init; }
    /// <summary>Reversed-Z: nearer is greater.</summary>
    public CompareOp DepthCompare { get; init; } = CompareOp.Greater;
    /// <summary>Clamp depth instead of clipping at the near and far planes.</summary>
    public bool DepthClamp { get; init; }
    public CullModeFlags CullMode { get; init; } = CullModeFlags.BackBit;
    /// <summary>
    /// The rasterizer's slope-scaled depth bias: this many times the primitive's largest depth change per pixel is
    /// added to its depth. 0 disables depth bias.
    /// </summary>
    public float DepthBiasSlope { get; init; }
    /// <summary>How the output blends into every color attachment.</summary>
    public BlendMode Blend { get; init; }

    /// <summary>The blend state of one color attachment for <paramref name="mode"/>.</summary>
    internal static PipelineColorBlendAttachmentState BlendState(BlendMode mode)
    {
        const ColorComponentFlags allChannels =
            ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit;
        return mode switch
        {
            BlendMode.Alpha => new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = allChannels,
            },
            // The target's alpha times One, plus the source's times Zero: it stays as it was.
            BlendMode.Additive => new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.One,
                DstColorBlendFactor = BlendFactor.One,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.Zero,
                DstAlphaBlendFactor = BlendFactor.One,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = allChannels,
            },
            _ => new PipelineColorBlendAttachmentState { BlendEnable = false, ColorWriteMask = allChannels },
        };
    }

    /// <summary>Creates the pipeline. The caller destroys it.</summary>
    public unsafe Pipeline Create(VulkanDevice device, ShaderLibrary shaders)
    {
        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = shaders.Get(VertexShader),
            PName = entry,
        };
        uint stageCount = 1;
        if (FragmentShader != null)
        {
            stages[stageCount++] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = shaders.Get(FragmentShader),
                PName = entry,
            };
        }

        var binding = new VertexInputBindingDescription(0, (uint)Unsafe.SizeOf<Vertex>(), VertexInputRate.Vertex);
        VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[4];
        attributes[0] = new(0, 0, Format.R32G32B32Sfloat, (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.Position)));
        attributes[1] = new(1, 0, Format.R32G32B32Sfloat, (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.Normal)));
        attributes[2] = new(2, 0, Format.R32G32B32A32Sfloat, (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.Tangent)));
        attributes[3] = new(3, 0, Format.R32G32Sfloat, (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.TexCoord)));
        // Declaring only what the vertex shader reads keeps validation from reporting unconsumed attributes.
        bool hasVertices = VertexInput != VertexInput.None;
        var vertexInput = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = hasVertices ? 1u : 0u,
            PVertexBindingDescriptions = hasVertices ? &binding : null,
            VertexAttributeDescriptionCount = VertexInput switch { VertexInput.Mesh => 4u, VertexInput.PositionOnly => 1u, _ => 0u },
            PVertexAttributeDescriptions = hasVertices ? attributes : null,
        };
        var inputAssembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
        };
        var viewport = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1,
        };
        // The projections flip y for Vulkan's y-down clip space, which keeps counterclockwise front faces.
        var rasterization = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            DepthClampEnable = DepthClamp,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullMode,
            FrontFace = FrontFace.CounterClockwise,
            DepthBiasEnable = DepthBiasSlope != 0f,
            DepthBiasSlopeFactor = DepthBiasSlope,
            LineWidth = 1f,
        };
        var multisample = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit,
        };
        var depthStencil = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = DepthTest,
            DepthWriteEnable = DepthWrite,
            DepthCompareOp = DepthTest ? DepthCompare : CompareOp.Always,
        };

        int colorCount = ColorFormats.Length;
        PipelineColorBlendAttachmentState* blends = stackalloc PipelineColorBlendAttachmentState[Math.Max(colorCount, 1)];
        for (int i = 0; i < colorCount; i++)
        {
            blends[i] = BlendState(Blend);
        }
        var colorBlend = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = (uint)colorCount,
            PAttachments = blends,
        };
        DynamicState* dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamic = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates,
        };

        fixed (Format* colorFormats = ColorFormats)
        {
            var rendering = new PipelineRenderingCreateInfo
            {
                SType = StructureType.PipelineRenderingCreateInfo,
                ColorAttachmentCount = (uint)colorCount,
                PColorAttachmentFormats = colorFormats,
                DepthAttachmentFormat = DepthFormat,
                StencilAttachmentFormat = Format.Undefined,
            };
            var info = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                PNext = &rendering,
                StageCount = stageCount,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewport,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamic,
                Layout = Layout,
            };
            device.Vk.CreateGraphicsPipelines(device.Device, default, 1, &info, null, out Pipeline pipeline).Check($"vkCreateGraphicsPipelines({Name})");
            device.SetName(ObjectType.Pipeline, pipeline.Handle, Name);
            return pipeline;
        }
    }
}
