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

    /// <summary>Sampled or fetched in a compute shader, as the exposure's meter reads the HDR image.</summary>
    SampledCompute,

    /// <summary>Sampled in the fragment and the compute shaders, as the lighting pass and the reflections' trace read the BRDF tables.</summary>
    SampledFragmentCompute,

    /// <summary>
    /// Read and written as a storage image in a compute shader, as the occlusion passes use their images and the
    /// environment prefilter writes the specular map, and sampled in the same dispatch, as a pyramid's build samples the
    /// level below the one that it writes.
    /// </summary>
    StorageCompute,

    /// <summary>Sampled or fetched in a compute shader in the General layout, as the trace reads the pyramids.</summary>
    SampledComputeGeneral,
    TransferSource,
    TransferDestination,
    Present,
}

/// <summary>How a pass uses a buffer.</summary>
internal enum BufferUse
{
    /// <summary>Read as vertex or index data.</summary>
    VertexInput,

    /// <summary>Read as a storage buffer in the vertex and fragment shaders.</summary>
    ShaderRead,

    /// <summary>Read as a storage buffer in a compute shader.</summary>
    ComputeRead,

    /// <summary>Read and written as a storage buffer in a compute shader, as by atomic adds.</summary>
    ComputeWrite,
    TransferSource,
    TransferDestination,

    /// <summary>Read by the CPU after the GPU wrote it.</summary>
    HostRead,
}

internal static class ResourceUses
{
    public const AccessFlags2 WriteMask =
        AccessFlags2.ShaderWriteBit
        | AccessFlags2.ColorAttachmentWriteBit
        | AccessFlags2.DepthStencilAttachmentWriteBit
        | AccessFlags2.TransferWriteBit
        | AccessFlags2.HostWriteBit
        | AccessFlags2.MemoryWriteBit
        | AccessFlags2.ShaderStorageWriteBit;

    public static ResourceState Describe(ImageUse use) =>
        use switch
        {
            // Load operations read the attachment, so the read bit is part of every attachment use.
            ImageUse.ColorAttachment => new(
                ImageLayout.ColorAttachmentOptimal,
                PipelineStageFlags2.ColorAttachmentOutputBit,
                AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit
            ),
            ImageUse.DepthAttachment => new(
                ImageLayout.DepthAttachmentOptimal,
                PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit,
                AccessFlags2.DepthStencilAttachmentReadBit | AccessFlags2.DepthStencilAttachmentWriteBit
            ),
            ImageUse.SampledFragment => new(
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.FragmentShaderBit,
                AccessFlags2.ShaderSampledReadBit
            ),
            ImageUse.SampledVertexFragment => new(
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.VertexShaderBit | PipelineStageFlags2.FragmentShaderBit,
                AccessFlags2.ShaderSampledReadBit
            ),
            ImageUse.SampledCompute => new(
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.ComputeShaderBit,
                AccessFlags2.ShaderSampledReadBit
            ),
            ImageUse.SampledFragmentCompute => new(
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.FragmentShaderBit | PipelineStageFlags2.ComputeShaderBit,
                AccessFlags2.ShaderSampledReadBit
            ),
            // Storage images are read and written only in the General layout. Both storage bits, so that any two storage
            // uses get a barrier: each occlusion pass reads what the one before it wrote. The sampled bit, so that the
            // barrier between two levels of a pyramid makes the last write visible to the next level's sampled read.
            ImageUse.StorageCompute => new(
                ImageLayout.General,
                PipelineStageFlags2.ComputeShaderBit,
                AccessFlags2.ShaderStorageReadBit
                    | AccessFlags2.ShaderStorageWriteBit
                    | AccessFlags2.ShaderSampledReadBit
            ),
            ImageUse.SampledComputeGeneral => new(
                ImageLayout.General,
                PipelineStageFlags2.ComputeShaderBit,
                AccessFlags2.ShaderSampledReadBit
            ),
            ImageUse.TransferSource => new(
                ImageLayout.TransferSrcOptimal,
                PipelineStageFlags2.AllTransferBit,
                AccessFlags2.TransferReadBit
            ),
            ImageUse.TransferDestination => new(
                ImageLayout.TransferDstOptimal,
                PipelineStageFlags2.AllTransferBit,
                AccessFlags2.TransferWriteBit
            ),
            // The present engine waits on a semaphore, so the barrier has no second scope of its own.
            ImageUse.Present => new(ImageLayout.PresentSrcKhr, PipelineStageFlags2.None, AccessFlags2.None),
        };

    public static ResourceState Describe(BufferUse use) =>
        use switch
        {
            BufferUse.VertexInput => new(
                ImageLayout.Undefined,
                PipelineStageFlags2.VertexAttributeInputBit | PipelineStageFlags2.IndexInputBit,
                AccessFlags2.VertexAttributeReadBit | AccessFlags2.IndexReadBit
            ),
            BufferUse.ShaderRead => new(
                ImageLayout.Undefined,
                PipelineStageFlags2.VertexShaderBit | PipelineStageFlags2.FragmentShaderBit,
                AccessFlags2.ShaderStorageReadBit
            ),
            BufferUse.ComputeRead => new(
                ImageLayout.Undefined,
                PipelineStageFlags2.ComputeShaderBit,
                AccessFlags2.ShaderStorageReadBit
            ),
            BufferUse.ComputeWrite => new(
                ImageLayout.Undefined,
                PipelineStageFlags2.ComputeShaderBit,
                AccessFlags2.ShaderStorageReadBit | AccessFlags2.ShaderStorageWriteBit
            ),
            BufferUse.TransferSource => new(
                ImageLayout.Undefined,
                PipelineStageFlags2.AllTransferBit,
                AccessFlags2.TransferReadBit
            ),
            BufferUse.TransferDestination => new(
                ImageLayout.Undefined,
                PipelineStageFlags2.AllTransferBit,
                AccessFlags2.TransferWriteBit
            ),
            BufferUse.HostRead => new(ImageLayout.Undefined, PipelineStageFlags2.HostBit, AccessFlags2.HostReadBit),
        };

    public static bool Writes(AccessFlags2 access) => (access & WriteMask) != 0;
}
