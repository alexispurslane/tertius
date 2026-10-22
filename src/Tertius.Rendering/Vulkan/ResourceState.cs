using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The last synchronized use of an image or buffer: its layout (images only), the pipeline stages that touched it
/// and how. <see cref="BarrierBatch"/> reads it to build the next barrier and updates it.
/// </summary>
internal struct ResourceState(ImageLayout layout, PipelineStageFlags2 stages, AccessFlags2 access)
{
    public static ResourceState Undefined => new(ImageLayout.Undefined, PipelineStageFlags2.None, AccessFlags2.None);
    public ImageLayout Layout = layout;
    public PipelineStageFlags2 Stages = stages;
    public AccessFlags2 Access = access;
}

/// <summary>How a pass uses an image. Each use maps to one layout, stage set and access set.</summary>
internal enum ImageUse
{
    ColorAttachment,
    DepthAttachment,

    /// <summary>Sampled or fetched in a fragment shader.</summary>
    SampledFragment,

    /// <summary>Sampled or fetched in the vertex and the fragment shaders, as the lit puffs sample the shadow cascades.</summary>
    SampledVertexFragment,
    TransferSource,
    TransferDestination,
    Present
}

/// <summary>How a pass uses a buffer.</summary>
internal enum BufferUse
{
    /// <summary>Read as vertex or index data.</summary>
    VertexInput,

    /// <summary>Read as a storage buffer in the vertex and fragment shaders.</summary>
    ShaderRead,
    TransferSource,
    TransferDestination,

    /// <summary>Read by the CPU after the GPU wrote it.</summary>
    HostRead
}

internal static class ResourceUses
{
    public const AccessFlags2 WriteMask =
        AccessFlags2.ShaderWriteBit | AccessFlags2.ColorAttachmentWriteBit |
        AccessFlags2.DepthStencilAttachmentWriteBit |
        AccessFlags2.TransferWriteBit | AccessFlags2.HostWriteBit | AccessFlags2.MemoryWriteBit |
        AccessFlags2.ShaderStorageWriteBit;

    public static ResourceState Describe(ImageUse use) => use switch
    {
        // Load operations read the attachment, so the read bit is part of every attachment use.
        ImageUse.ColorAttachment => new(ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlags2.ColorAttachmentOutputBit,
            AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit),
        ImageUse.DepthAttachment => new(ImageLayout.DepthAttachmentOptimal,
            PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit,
            AccessFlags2.DepthStencilAttachmentReadBit | AccessFlags2.DepthStencilAttachmentWriteBit),
        ImageUse.SampledFragment => new(ImageLayout.ShaderReadOnlyOptimal, PipelineStageFlags2.FragmentShaderBit,
            AccessFlags2.ShaderSampledReadBit),
        ImageUse.SampledVertexFragment => new(ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlags2.VertexShaderBit | PipelineStageFlags2.FragmentShaderBit,
            AccessFlags2.ShaderSampledReadBit),
        ImageUse.TransferSource => new(ImageLayout.TransferSrcOptimal, PipelineStageFlags2.AllTransferBit,
            AccessFlags2.TransferReadBit),
        ImageUse.TransferDestination => new(ImageLayout.TransferDstOptimal, PipelineStageFlags2.AllTransferBit,
            AccessFlags2.TransferWriteBit),
        // The present engine waits on a semaphore, so the barrier has no second scope of its own.
        ImageUse.Present => new(ImageLayout.PresentSrcKhr, PipelineStageFlags2.None, AccessFlags2.None),
        _ => throw new ArgumentOutOfRangeException(nameof(use))
    };

    public static ResourceState Describe(BufferUse use) => use switch
    {
        BufferUse.VertexInput => new(ImageLayout.Undefined,
            PipelineStageFlags2.VertexAttributeInputBit | PipelineStageFlags2.IndexInputBit,
            AccessFlags2.VertexAttributeReadBit | AccessFlags2.IndexReadBit),
        BufferUse.ShaderRead => new(ImageLayout.Undefined,
            PipelineStageFlags2.VertexShaderBit | PipelineStageFlags2.FragmentShaderBit,
            AccessFlags2.ShaderStorageReadBit),
        BufferUse.TransferSource => new(ImageLayout.Undefined, PipelineStageFlags2.AllTransferBit,
            AccessFlags2.TransferReadBit),
        BufferUse.TransferDestination => new(ImageLayout.Undefined, PipelineStageFlags2.AllTransferBit,
            AccessFlags2.TransferWriteBit),
        BufferUse.HostRead => new(ImageLayout.Undefined, PipelineStageFlags2.HostBit, AccessFlags2.HostReadBit),
        _ => throw new ArgumentOutOfRangeException(nameof(use))
    };

    public static bool Writes(AccessFlags2 access) => (access & WriteMask) != 0;
}
