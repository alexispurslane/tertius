using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The full-resolution images of the skin scatter, its kernel and its set (docs/plans/skin-and-hair.md, "The two
/// passes"). Only <see cref="SeparableSubsurface"/> makes them. The direct pass clears and writes the scatter image, the
/// ambient pass adds to it, the rows pass writes the row image, and the columns pass adds S − D to the HDR image.
/// </summary>
internal sealed unsafe class ScatterImages : IDisposable
{
    public const Format ScatterFormat = Format.R16G16B16A16Sfloat;
    public const Format RowFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The kernel buffer's taps: room for the most taps per side, so its uniform block has one size.</summary>
    public const int KernelTaps = 2 * SeparableSubsurface.MaxTapsPerSide + 1;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    // True from the creation until the first frame's uploads copy the kernel into its buffer.
    private bool kernelUploaded;

    /// <exception cref="NotSupportedException">A format cannot be drawn, blended and stored on the device.</exception>
    public ScatterImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets,
        SeparableSubsurface setting
    )
    {
        CheckDevice(device);
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        Setting = setting;
        Kernel = GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: (ulong)(KernelTaps * sizeof(Vector4)),
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

    /// <summary>D of each skin pixel in rgb, pre-exposed, with 1 in a, and 0 at every other pixel.</summary>
    public GpuImage Scatter { get; private set; }

    /// <summary>The scatter image after the pass along the rows: 0 at every pixel that is not skin.</summary>
    public GpuImage Rows { get; private set; }

    /// <summary>The kernel's 2n + 1 taps as vec4, rgb the weights and a the offset in m, then zeros to <see cref="KernelTaps"/>.</summary>
    public GpuBuffer Kernel { get; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.ScatterLayout"/>, with the targets' HDR image.</summary>
    public DescriptorSet Set { get; }

    /// <summary>Recreates the images at the targets' size and rewrites the set. The device must be idle.</summary>
    public void Resize(RenderTargets targets)
    {
        // Always: the set points at the HDR image, which a resize recreates.
        DestroyImages();
        Create(targets);
    }

    /// <summary>
    /// Copies the kernel of <see cref="SkinProfile.Human"/> at the setting's taps into its buffer, in the first frame
    /// only: the kernel never changes (docs/plans/skin-and-hair.md, decision 17).
    /// </summary>
    public void Upload(CommandBuffer commandBuffer, StagingArena staging, BarrierBatch barriers)
    {
        if (kernelUploaded)
            return;
        StagingSlice stage = staging.Allocate(Kernel.Size);
        Span<Vector4> taps = MemoryMarshal.Cast<byte, Vector4>(stage.Bytes);
        taps.Clear();
        ScatterKernel kernel = SkinProfile.Human.Kernel(Setting.TapsPerSide);
        for (int i = 0; i < kernel.Taps.Length; i++)
        {
            ScatterTap tap = kernel.Taps[i];
            taps[i] = new(x: (float)tap.Weight.X, y: (float)tap.Weight.Y, z: (float)tap.Weight.Z, w: (float)tap.Offset);
        }

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

    [MemberNotNull(nameof(Scatter), nameof(Rows))]
    private void Create(RenderTargets targets)
    {
        // The lighting passes draw the scatter image, the passes read it as a storage image, and the read hook copies it.
        Scatter = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: targets.Extent,
            format: ScatterFormat,
            usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "scatter"
        );
        Rows = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: targets.Extent,
            format: RowFormat,
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: "scatter rows"
        );
        layouts.WriteStorageImage(set: Set, binding: 0, image: Scatter);
        layouts.WriteStorageImage(set: Set, binding: 1, image: Rows);
        layouts.WriteStorageImage(set: Set, binding: 2, image: targets.Hdr);
    }

    /// <summary>
    /// The scatter image is a blended color attachment and a storage image, and the row image a storage image. The spec
    /// requires both of RGBA16F on every device (docs/plans/skin-and-hair.md, "Required device features").
    /// </summary>
    private static void CheckDevice(VulkanDevice device)
    {
        const FormatFeatureFlags scatter =
            FormatFeatureFlags.ColorAttachmentBit
            | FormatFeatureFlags.ColorAttachmentBlendBit
            | FormatFeatureFlags.StorageImageBit;
        if ((device.FormatProperties(ScatterFormat).OptimalTilingFeatures & scatter) != scatter)
            throw new NotSupportedException(
                $"{ScatterFormat} cannot be a blended color attachment and a storage image on {device.DeviceName}."
            );
    }

    private void DestroyImages()
    {
        Scatter.Dispose();
        Rows.Dispose();
    }

    // The pool of the layouts frees the set.
    public void Dispose()
    {
        DestroyImages();
        Kernel.Dispose();
    }
}
