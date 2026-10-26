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

    /// <summary>
    /// Dual-source blending: the first output is added to the target times the second output, per channel. The target's
    /// alpha stays as it is. The device feature dualSrcBlend allows it on one color attachment only.
    /// </summary>
    Transmittance,
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
    /// The winding of a front face on screen. Counterclockwise for every view whose axes are right-handed; an environment
    /// face is a mirror image, so it is clockwise (docs/plans/environment-map.md, "The faces and their axes").
    /// </summary>
    public FrontFace FrontFace { get; init; } = FrontFace.CounterClockwise;

    /// <summary>
    /// The specialization constants of both stages, or null. A stage ignores the constants that its compiled module does
    /// not hold, as a vertex shader does (<see cref="ComputePipelineDesc.Constants"/>).
    /// </summary>
    public ShaderConstants? Constants { get; init; }

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
            BlendMode.Alpha => new()
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
            BlendMode.Additive => new()
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
            // The target's alpha times One, plus the source's times Zero: it stays as it was.
            BlendMode.Transmittance => new()
            {
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.One,
                DstColorBlendFactor = BlendFactor.Src1Color,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.Zero,
                DstAlphaBlendFactor = BlendFactor.One,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = allChannels,
            },
            BlendMode.None => new() { BlendEnable = false, ColorWriteMask = allChannels },
        };
    }

    /// <summary>Creates the pipeline. The caller destroys it.</summary>
    public unsafe Pipeline Create(VulkanDevice device, ShaderLibrary shaders)
    {
        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        SpecializationStorage storage;
        SpecializationInfo* constants = ShaderConstants.Specialization(Constants, &storage);
        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = shaders.Stage(
            stage: ShaderStageFlags.VertexBit,
            file: VertexShader,
            entry: entry,
            constants: constants
        );
        uint stageCount = 1;
        if (FragmentShader != null)
            stages[stageCount++] = shaders.Stage(
                stage: ShaderStageFlags.FragmentBit,
                file: FragmentShader,
                entry: entry,
                constants: constants
            );

        var binding = new VertexInputBindingDescription(0, (uint)Unsafe.SizeOf<Vertex>(), VertexInputRate.Vertex);
        VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[4];
        WriteMeshAttributes(attributes);
        // Declaring only what the vertex shader reads keeps validation from reporting unconsumed attributes.
        bool hasVertices = VertexInput != VertexInput.None;
        var vertexInput = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = hasVertices ? 1u : 0u,
            PVertexBindingDescriptions = hasVertices ? &binding : null,
            VertexAttributeDescriptionCount = VertexInput switch
            {
                VertexInput.Mesh => 4u,
                VertexInput.PositionOnly => 1u,
                VertexInput.None => 0u,
            },
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
            FrontFace = FrontFace,
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
        PipelineColorBlendAttachmentState* blends =
            stackalloc PipelineColorBlendAttachmentState[Math.Max(colorCount, 1)];
        for (int i = 0; i < colorCount; i++)
            blends[i] = BlendState(Blend);
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
            device
                .Vk.CreateGraphicsPipelines(
                    device: device.Device,
                    pipelineCache: default,
                    createInfoCount: 1,
                    pCreateInfos: &info,
                    pAllocator: null,
                    pPipelines: out Pipeline pipeline
                )
                .Check($"vkCreateGraphicsPipelines({Name})");
            device.SetName(ObjectType.Pipeline, pipeline.Handle, Name);
            return pipeline;
        }
    }

    /// <summary>The four attributes of a <see cref="Vertex"/> at locations 0 to 3 of binding 0, into <paramref name="attributes"/>.</summary>
    private static unsafe void WriteMeshAttributes(VertexInputAttributeDescription* attributes)
    {
        attributes[0] = new(
            location: 0,
            binding: 0,
            format: Format.R32G32B32Sfloat,
            offset: (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.Position))
        );
        attributes[1] = new(
            location: 1,
            binding: 0,
            format: Format.R32G32B32Sfloat,
            offset: (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.Normal))
        );
        attributes[2] = new(
            location: 2,
            binding: 0,
            format: Format.R32G32B32A32Sfloat,
            offset: (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.Tangent))
        );
        attributes[3] = new(
            location: 3,
            binding: 0,
            format: Format.R32G32Sfloat,
            offset: (uint)Marshal.OffsetOf<Vertex>(nameof(Vertex.TexCoord))
        );
    }
}
