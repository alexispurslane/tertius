using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// ReSTIR's reservoirs (docs/plans/temporal-renderer.md, section 7, "The reservoirs"): two RG32UI images at the targets'
/// size, R0 and R1, and the reservoir set of each of the two passes. A reservoir holds the chosen light's index in the low
/// 16 bits of x and M in its high 16 bits, and the bits of W in y. "Light candidates" reads R1 and writes R0, and "direct
/// light" reads R0 and writes R1, which the next frame's candidates read. So the images need no swap.
/// </summary>
/// <remarks>
/// Invariants, checked by <see cref="AssertInvariants"/>: both images have the targets' size and the reservoir format.
/// </remarks>
internal sealed class LightReservoirs : IDisposable
{
    /// <summary>The format of a reservoir.</summary>
    public const Format ReservoirFormat = Format.R32G32Uint;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    /// <exception cref="NotSupportedException">The format is not a sampled color attachment on the device.</exception>
    public LightReservoirs(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets,
        SurfaceHistory surfaces
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        const FormatFeatureFlags needed = FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit;
        if ((device.FormatProperties(ReservoirFormat).OptimalTilingFeatures & needed) != needed)
            throw new NotSupportedException($"{ReservoirFormat} cannot be a sampled color attachment on {device.DeviceName}.");
        CandidatesSet = layouts.AllocateReservoirSet();
        DirectSet = layouts.AllocateReservoirSet();
        (R0, R1) = Create(targets, surfaces);
    }

    /// <summary>The reservoirs that "light candidates" writes and "direct light" reads.</summary>
    public GpuImage R0 { get; private set; }

    /// <summary>The reservoirs that "direct light" writes and the next frame's "light candidates" reads.</summary>
    public GpuImage R1 { get; private set; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.ReservoirLayout"/> for "light candidates": R1, the surface test image and the motion target.</summary>
    public DescriptorSet CandidatesSet { get; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.ReservoirLayout"/> for "direct light": R0, the surface test image and the motion target.</summary>
    public DescriptorSet DirectSet { get; }

    /// <summary>
    /// Recreates both images at the targets' size and rewrites both sets. The device must be idle, and the surface history
    /// resized first. A new size resets every history, and a reset frame's candidates read no reservoir.
    /// </summary>
    public void Resize(RenderTargets targets, SurfaceHistory surfaces)
    {
        R0.Dispose();
        R1.Dispose();
        (R0, R1) = Create(targets, surfaces);
    }

    /// <summary>Checks the invariants in the class remarks.</summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(R0.Extent.Width == R1.Extent.Width && R0.Extent.Height == R1.Extent.Height);
        Contract.Invariant(R0.Format == ReservoirFormat && R1.Format == ReservoirFormat);
    }

    private (GpuImage R0, GpuImage R1) Create(RenderTargets targets, SurfaceHistory surfaces)
    {
        GpuImage r0 = CreateImage(targets.Extent, "reservoirs 0");
        GpuImage r1 = CreateImage(targets.Extent, "reservoirs 1");
        WriteSet(set: CandidatesSet, read: r1, targets: targets, surfaces: surfaces);
        WriteSet(set: DirectSet, read: r0, targets: targets, surfaces: surfaces);
        return (r0, r1);
    }

    private void WriteSet(DescriptorSet set, GpuImage read, RenderTargets targets, SurfaceHistory surfaces)
    {
        layouts.WriteImage(set: set, binding: 0, arrayElement: 0, image: read);
        layouts.WriteStorageImage(set: set, binding: 1, image: surfaces.Test);
        layouts.WriteStorageImage(set: set, binding: 2, image: targets.Motion);
    }

    private GpuImage CreateImage(Extent2D extent, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: ReservoirFormat,
            usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: name
        );

    // The pool of the layouts frees the sets.
    public void Dispose()
    {
        R0.Dispose();
        R1.Dispose();
    }
}
