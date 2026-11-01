using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The skin scatter's row image, its kernel and its set, which binds the targets' diffuse image as its source
/// (docs/plans/skin-and-hair.md, "The two passes"). Only <see cref="SeparableSubsurface"/> makes them. The rows pass
/// writes the row image, and the columns pass adds S − D to the HDR image.
/// </summary>
internal sealed unsafe class ScatterImages : IDisposable
{
    public const Format RowFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The kernel buffer's taps: room for the most taps per side, so its uniform block has one size.</summary>
    public const int KernelTaps = 2 * SeparableSubsurface.MaxTapsPerSide + 1;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    // The kernel buffer's contents, computed in doubles when the images are made.
    private readonly GpuScatterKernel kernelBlock;

    // False from the creation until the first frame's uploads copy the kernel into its buffer, then true.
    private bool kernelUploaded;

    /// <exception cref="ArgumentException">The targets have no diffuse image.</exception>
    public ScatterImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets,
        SeparableSubsurface setting
    )
    {
        // Before anything is made, so that a refused call leaks nothing.
        _ = DiffuseOf(targets);
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        Setting = setting;
        kernelBlock = BlockOf(SkinProfile.Human.Kernel(setting.TapsPerSide));
        Kernel = GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: (ulong)Unsafe.SizeOf<GpuScatterKernel>(),
            usage: BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "scatter kernel"
        );
        Set = layouts.AllocateScatterSet();
        layouts.WriteBuffer(set: Set, binding: 3, type: DescriptorType.UniformBuffer, buffer: Kernel);
        Create(targets);
    }

    /// <summary>The setting that made the images, whose taps per side the kernel holds.</summary>
    public SeparableSubsurface Setting { get; }

    /// <summary>The diffuse image after the pass along the rows: 0 at every pixel that is not skin.</summary>
    public GpuImage Rows { get; private set; }

    /// <summary>The kernel's uniform block, a <see cref="GpuScatterKernel"/>.</summary>
    public GpuBuffer Kernel { get; }

    /// <summary>
    /// Set 3 of <see cref="DescriptorLayouts.ScatterLayout"/> and of <see cref="DescriptorLayouts.AmbientScatterLayout"/>,
    /// with the targets' HDR image. The ambient pass binds it while the HDR image is its color attachment, in the
    /// <c>ColorAttachmentOptimal</c> layout. That is legal only because ambient_scatter.frag uses binding 0 alone: a
    /// shader that statically uses binding 1 or 2 there needs those images in the General layout.
    /// </summary>
    public DescriptorSet Set { get; }

    /// <summary>Recreates the images at the targets' size and rewrites the set. The device must be idle.</summary>
    public void Resize(RenderTargets targets)
    {
        // Always: the set points at the diffuse and the HDR image, which a resize recreates.
        DestroyImages();
        Create(targets);
    }

    /// <summary>
    /// Copies the kernel of <see cref="SkinProfile.Human"/> at the setting's taps into its buffer, in the first frame
    /// only: the kernel never changes after the images are made (docs/plans/skin-and-hair.md, decision 17).
    /// </summary>
    public void Upload(CommandBuffer commandBuffer, StagingArena staging, BarrierBatch barriers)
    {
        if (kernelUploaded)
            return;
        StagingSlice stage = staging.Allocate(Kernel.Size);
        MemoryMarshal.Write(stage.Bytes, in kernelBlock);

        barriers.Buffer(Kernel, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var region = new BufferCopy(stage.Offset, 0, stage.Size);
        device.Vk.CmdCopyBuffer(
            commandBuffer: commandBuffer,
            srcBuffer: stage.Buffer.Handle,
            dstBuffer: Kernel.Handle,
            regionCount: 1,
            pRegions: &region
        );
        kernelUploaded = true;
    }

    /// <summary><paramref name="kernel"/> as the GPU holds it, with zeros in the tap slots past its taps.</summary>
    private static GpuScatterKernel BlockOf(ScatterKernel kernel)
    {
        var block = new GpuScatterKernel { WidthScale = (float)kernel.WidthScale };
        for (int i = 0; i < kernel.Taps.Length; i++)
        {
            ScatterTap tap = kernel.Taps[i];
            block.Taps[i] = new(
                x: (float)tap.Weight.X,
                y: (float)tap.Weight.Y,
                z: (float)tap.Weight.Z,
                w: (float)tap.Offset
            );
        }
        return block;
    }

    [MemberNotNull(nameof(Rows))]
    private void Create(RenderTargets targets)
    {
        Rows = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: targets.Extent,
            format: RowFormat,
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "scatter rows"
        );
        layouts.WriteStorageImage(set: Set, binding: 0, image: DiffuseOf(targets));
        layouts.WriteStorageImage(set: Set, binding: 1, image: Rows);
        layouts.WriteStorageImage(set: Set, binding: 2, image: targets.Hdr);
    }

    /// <exception cref="ArgumentException">The targets have no diffuse image.</exception>
    private static GpuImage DiffuseOf(RenderTargets targets) =>
        targets.Diffuse ?? throw new ArgumentException("The targets have no diffuse image.", nameof(targets));

    private void DestroyImages() => Rows.Dispose();

    // The pool of the layouts frees the set.
    public void Dispose()
    {
        DestroyImages();
        Kernel.Dispose();
    }
}
