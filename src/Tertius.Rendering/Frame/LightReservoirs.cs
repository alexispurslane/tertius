using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// ReSTIR's reservoirs (docs/plans/temporal-renderer.md, section 7, "The reservoirs") under the renderer's
/// <see cref="ResampledLights"/>, which they hold: two RG32UI images at the targets' size, R0 and R1, and the reservoir set
/// of each of the two passes. A reservoir holds the chosen light's index in the low
/// 16 bits of x and M in its high 16 bits, and the bits of W in y. "Light candidates" reads R1 and writes R0, and "direct
/// light" reads R0 and writes R1, which the next frame's candidates read. So the images need no swap.
/// </summary>
/// <remarks>
/// Invariants, checked by <see cref="AssertInvariants"/>: both images have the size of the targets that the reservoirs
/// bind, and the reservoir format.
/// </remarks>
internal sealed class LightReservoirs : IDisposable
{
    /// <summary>A reservoir's texel: x the chosen light in the low 16 bits and M in the high 16, y the bits of W (restir.glsl).</summary>
    public const Format ReservoirFormat = Format.R32G32Uint;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly RenderTargets targets;
    private readonly SurfaceHistory surfaces;

    /// <param name="setting">The setting of ReSTIR that the two passes take.</param>
    /// <param name="targets">The render targets, whose size the images take, and whose motion target the sets bind.</param>
    /// <param name="surfaces">The surface history, whose test image the sets bind.</param>
    /// <exception cref="NotSupportedException">The format is not a sampled color attachment on the device.</exception>
    public LightReservoirs(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        ResampledLights setting,
        RenderTargets targets,
        SurfaceHistory surfaces
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.targets = targets;
        this.surfaces = surfaces;
        Setting = setting;
        const FormatFeatureFlags needed = FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit;
        if ((device.FormatProperties(ReservoirFormat).OptimalTilingFeatures & needed) != needed)
            throw new NotSupportedException(
                $"{ReservoirFormat} cannot be a sampled color attachment on {device.DeviceName}."
            );
        CandidatesSet = layouts.AllocateReservoirSet();
        DirectSet = layouts.AllocateReservoirSet();
        (R0, R1) = Create();
    }

    /// <summary>The setting of ReSTIR: the candidates of each pixel and the neighbors whose reservoirs it reuses.</summary>
    public ResampledLights Setting { get; }

    /// <summary>The reservoirs that "light candidates" writes and "direct light" reads.</summary>
    public GpuImage R0 { get; private set; }

    /// <summary>The reservoirs that "direct light" writes and the next frame's "light candidates" reads.</summary>
    public GpuImage R1 { get; private set; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.ReservoirLayout"/> for "light candidates": R1, the surface test image and the motion target.</summary>
    public DescriptorSet CandidatesSet { get; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.ReservoirLayout"/> for "direct light": R0, the surface test image and the motion target.</summary>
    public DescriptorSet DirectSet { get; }

    /// <summary>
    /// Recreates both images at the size of the targets that the reservoirs bind, and rewrites both sets. The device must
    /// be idle, and the surface history resized first. A new size resets every history, and a reset frame's candidates
    /// read no reservoir.
    /// </summary>
    public void Resize()
    {
        Contract.Requires(Fits(surfaces.Test));
        R0.Dispose();
        R1.Dispose();
        (R0, R1) = Create();
        Contract.Ensures(Fits(R0) && Fits(R1));
    }

    /// <summary>Checks the invariants in the class remarks.</summary>
    internal void AssertInvariants()
    {
        Contract.Invariant(Fits(R0) && Fits(R1));
        Contract.Invariant(R0.Format == ReservoirFormat && R1.Format == ReservoirFormat);
    }

    /// <summary>Whether <paramref name="image"/> has the targets' size.</summary>
    private bool Fits(GpuImage image) =>
        image.Extent.Width == targets.Extent.Width && image.Extent.Height == targets.Extent.Height;

    private (GpuImage R0, GpuImage R1) Create()
    {
        GpuImage r0 = CreateImage(targets.Extent, "reservoirs 0");
        GpuImage r1 = CreateImage(targets.Extent, "reservoirs 1");
        WriteSet(set: CandidatesSet, read: r1);
        WriteSet(set: DirectSet, read: r0);
        return (r0, r1);
    }

    private void WriteSet(DescriptorSet set, GpuImage read)
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
