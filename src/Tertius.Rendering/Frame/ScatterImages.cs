using System.Diagnostics.CodeAnalysis;
using System.Numerics;
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

    // The kernel's 2n + 1 taps as the buffer holds them, computed in doubles when the images are made.
    private readonly Vector4[] kernelTaps;

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
        kernelTaps = TapsOf(SkinProfile.Human.Kernel(setting.TapsPerSide));
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

    /// <summary>The diffuse image after the pass along the rows: 0 at every pixel that is not skin.</summary>
    public GpuImage Rows { get; private set; }

    /// <summary>The kernel's 2n + 1 taps as vec4, rgb the weights and a the offset in m, then zeros to <see cref="KernelTaps"/>.</summary>
    public GpuBuffer Kernel { get; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.ScatterLayout"/>, with the targets' HDR image.</summary>
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
        Span<Vector4> taps = MemoryMarshal.Cast<byte, Vector4>(stage.Bytes);
        taps.Clear();
        kernelTaps.CopyTo(taps);

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

    /// <summary>The taps of <paramref name="kernel"/> as <c>vec4</c>: rgb the weights and a the offset in m.</summary>
    private static Vector4[] TapsOf(ScatterKernel kernel) =>
        [
            .. kernel.Taps.Select(tap => new Vector4(
                x: (float)tap.Weight.X,
                y: (float)tap.Weight.Y,
                z: (float)tap.Weight.Z,
                w: (float)tap.Offset
            )),
        ];

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
