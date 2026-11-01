using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>How a pipeline reads vertices from binding 0.</summary>
internal enum VertexInput
{
    /// <summary>No vertex buffer: fullscreen passes generate their vertices.</summary>
    None,

    /// <summary>Every attribute of the <see cref="GpuVertex"/> layout, at locations 0 to 4.</summary>
    Mesh,

    /// <summary>Only <see cref="GpuVertex.Position"/> at location 0, with the <see cref="GpuVertex"/> stride. Depth-only passes.</summary>
    PositionOnly,

    /// <summary>
    /// <see cref="GpuVertex.Position"/> at location 0 and <see cref="GpuVertex.TexCoord"/> at location 3, with the
    /// <see cref="GpuVertex"/> stride: the depth-only passes of cutout casters, which test the base color's alpha.
    /// </summary>
    PositionAndTexCoord,
}

/// <summary>How a pipeline blends its output into one color attachment. No case carries a field, so it is an enum.</summary>
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
internal sealed record GraphicsPipelineDesc
{
    public required string Name { get; init; }
    public required string VertexShader { get; init; }

    /// <summary>The fragment stage. Null for a depth-only pipeline.</summary>
    public required string? FragmentShader { get; init; }

    public required PipelineLayout Layout { get; init; }

    /// <summary>The formats of the color attachments in the order of the fragment shader's outputs: empty for a depth-only pipeline.</summary>
    public required Format[] ColorFormats { get; init; }

    /// <summary>
    /// How the pipeline's output blends into each of its color attachments. One blend for all of them: the renderer does
    /// not require the device feature independentBlend, without which every attachment's blend state must be the same.
    /// </summary>
    public BlendMode Blend { get; init; }

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

    /// <summary>
    /// Throws if the pipeline blends into an integer format, which Vulkan cannot blend. The refusal also keeps true
    /// reports of it out of what <see cref="ValidationLog.IsFalseIntegerBlendReport"/> drops.
    /// </summary>
    private void RefuseBlendIntoAnInteger()
    {
        if (Blend == BlendMode.None)
            return;
        foreach (Format format in ColorFormats)
        {
            if (IsInteger(format))
                throw new ArgumentException($"The pipeline {Name} blends into an attachment of {format}, an integer format.");
        }
    }

    /// <summary>Whether <paramref name="format"/> holds unsigned or signed integers, which no blend can mix.</summary>
    private static bool IsInteger(Format format)
    {
        string name = format.ToString();
        return name.Contains("Uint", StringComparison.Ordinal) || name.Contains("Sint", StringComparison.Ordinal);
    }

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
    /// <exception cref="ArgumentException">The pipeline blends into a color attachment of an integer format.</exception>
    public unsafe Pipeline Create(VulkanDevice device, ShaderLibrary shaders)
    {
        RefuseBlendIntoAnInteger();
        byte* entry = stackalloc byte[] { (byte)'m', (byte)'a', (byte)'i', (byte)'n', 0 };
        SpecializationStorage storage;
        SpecializationInfo* constants = ShaderConstants.Specialization(Constants, &storage);
        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
        uint stageCount = WriteStages(shaders: shaders, entry: entry, constants: constants, stages: stages);

        var binding = new VertexInputBindingDescription(0, (uint)Unsafe.SizeOf<GpuVertex>(), VertexInputRate.Vertex);
        VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[5];
        PipelineVertexInputStateCreateInfo vertexInput = VertexInputState(binding: &binding, attributes: attributes);

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
        PipelineRasterizationStateCreateInfo rasterization = RasterizationState();
        var multisample = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit,
        };
        PipelineDepthStencilStateCreateInfo depthStencil = DepthStencilState();

        PipelineColorBlendAttachmentState* blends =
            stackalloc PipelineColorBlendAttachmentState[Math.Max(ColorFormats.Length, 1)];
        PipelineColorBlendStateCreateInfo colorBlend = ColorBlendState(blends);
        DynamicState* dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamic = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates,
        };

        var info = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
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
        return CreateForRendering(device: device, info: info);
    }

    /// <summary>
    /// Creates the pipeline that <paramref name="info"/> describes, for dynamic rendering into
    /// <see cref="ColorFormats"/> and <see cref="DepthFormat"/>, and names it.
    /// </summary>
    private unsafe Pipeline CreateForRendering(VulkanDevice device, GraphicsPipelineCreateInfo info)
    {
        Format* colorFormats = stackalloc Format[Math.Max(ColorFormats.Length, 1)];
        for (int i = 0; i < ColorFormats.Length; i++)
            colorFormats[i] = ColorFormats[i];

        var rendering = new PipelineRenderingCreateInfo
        {
            SType = StructureType.PipelineRenderingCreateInfo,
            ColorAttachmentCount = (uint)ColorFormats.Length,
            PColorAttachmentFormats = colorFormats,
            DepthAttachmentFormat = DepthFormat,
            StencilAttachmentFormat = Format.Undefined,
        };
        info.PNext = &rendering;
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

    /// <summary>
    /// Writes the vertex stage and, if there is one, the fragment stage into <paramref name="stages"/>, and returns
    /// how many it wrote.
    /// </summary>
    private unsafe uint WriteStages(
        ShaderLibrary shaders,
        byte* entry,
        SpecializationInfo* constants,
        PipelineShaderStageCreateInfo* stages
    )
    {
        stages[0] = shaders.Stage(
            stage: ShaderStageFlags.VertexBit,
            file: VertexShader,
            entry: entry,
            constants: constants
        );
        if (FragmentShader == null)
            return 1;
        stages[1] = shaders.Stage(
            stage: ShaderStageFlags.FragmentBit,
            file: FragmentShader,
            entry: entry,
            constants: constants
        );
        return 2;
    }

    /// <summary>
    /// The vertex input state of <see cref="VertexInput"/>. It writes the attribute descriptions into
    /// <paramref name="attributes"/> (room for five) and points at them and at <paramref name="binding"/>.
    /// </summary>
    private unsafe PipelineVertexInputStateCreateInfo VertexInputState(
        VertexInputBindingDescription* binding,
        VertexInputAttributeDescription* attributes
    )
    {
        WriteMeshAttributes(attributes);
        // The texture coordinate follows the position, so the first two descriptions are the two that the cutout casters read.
        if (VertexInput == VertexInput.PositionAndTexCoord)
            attributes[1] = attributes[3];
        // Declaring only what the vertex shader reads keeps validation from reporting unconsumed attributes.
        bool hasVertices = VertexInput != VertexInput.None;
        return new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = hasVertices ? 1u : 0u,
            PVertexBindingDescriptions = hasVertices ? binding : null,
            VertexAttributeDescriptionCount = VertexInput switch
            {
                VertexInput.Mesh => 5u,
                VertexInput.PositionOnly => 1u,
                VertexInput.PositionAndTexCoord => 2u,
                VertexInput.None => 0u,
            },
            PVertexAttributeDescriptions = hasVertices ? attributes : null,
        };
    }

    /// <summary>The rasterization state: filled triangles, with this pipeline's culling, depth clamp and depth bias.</summary>
    private PipelineRasterizationStateCreateInfo RasterizationState() =>
        // The projections flip y for Vulkan's y-down clip space, which keeps counterclockwise front faces.
        new()
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

    /// <summary>The depth state: no stencil, and a test that always passes when the pipeline does not test depth.</summary>
    private PipelineDepthStencilStateCreateInfo DepthStencilState() =>
        new()
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = DepthTest,
            DepthWriteEnable = DepthWrite,
            DepthCompareOp = DepthTest ? DepthCompare : CompareOp.Always,
        };

    /// <summary>
    /// The color blend state: <see cref="Blend"/> for each color attachment, written into <paramref name="blends"/> (room
    /// for at least one).
    /// </summary>
    private unsafe PipelineColorBlendStateCreateInfo ColorBlendState(PipelineColorBlendAttachmentState* blends)
    {
        for (int i = 0; i < ColorFormats.Length; i++)
            blends[i] = BlendState(Blend);
        return new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = (uint)ColorFormats.Length,
            PAttachments = blends,
        };
    }

    /// <summary>The five attributes of a <see cref="GpuVertex"/> at locations 0 to 4 of binding 0, into <paramref name="attributes"/>.</summary>
    private static unsafe void WriteMeshAttributes(VertexInputAttributeDescription* attributes)
    {
        attributes[0] = new(
            location: 0,
            binding: 0,
            format: Format.R32G32B32Sfloat,
            offset: (uint)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.Position))
        );
        attributes[1] = new(
            location: 1,
            binding: 0,
            format: Format.R32G32B32Sfloat,
            offset: (uint)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.Normal))
        );
        attributes[2] = new(
            location: 2,
            binding: 0,
            format: Format.R32G32B32A32Sfloat,
            offset: (uint)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.Tangent))
        );
        attributes[3] = new(
            location: 3,
            binding: 0,
            format: Format.R32G32Sfloat,
            offset: (uint)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.TexCoord))
        );
        attributes[4] = new(
            location: 4,
            binding: 0,
            format: Format.R32Uint,
            offset: (uint)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.Card))
        );
    }
}
