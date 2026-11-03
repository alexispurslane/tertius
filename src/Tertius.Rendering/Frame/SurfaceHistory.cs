using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The images of the surface test (docs/plans/temporal-renderer.md, section 8, "The surface test" and "Images"): two
/// surface histories and two device depths, which alternate between frames by the parity of the drawn frames
/// (<see cref="FrameHistory.PendingFrame.Parity"/>), and the surface test image, all at the targets' size, and the two
/// sets of the surface test, one per parity. The last frame's device depth is the one store of its view depths: a half
/// float holds at most 65,504 m, but a planet's ground seen from orbit lies farther. Every image stays in the General
/// layout but the last surface history and the last device depth, which the pass samples.
/// </summary>
internal sealed class SurfaceHistory : IDisposable
{
    /// <summary>
    /// The surface history: x unused, because the device depth holds the view depth, then the geometric normal in
    /// octahedral code in world axes, and roughness.
    /// </summary>
    public const Format HistoryFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The surface test image: bit i for tap i of the surface test, bit 4 + i of the specular test.</summary>
    public const Format TestFormat = Format.R8Uint;

    /// <summary>FSR's device depth: reversed and infinite, <see cref="SurfaceTestPass.DeviceDepthNear"/> over the view depth, 0 for sky.</summary>
    public const Format DeviceDepthFormat = Format.R32Sfloat;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly GpuImage[] histories = new GpuImage[2];
    private readonly GpuImage[] deviceDepths = new GpuImage[2];

    /// <exception cref="NotSupportedException">A format is not a storage image on the device.</exception>
    public SurfaceHistory(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        CheckDevice(device);
        Sets = [layouts.AllocateSurfaceTestSet(), layouts.AllocateSurfaceTestSet()];
        Create(targets);
    }

    /// <summary>The surface test image of the last frame.</summary>
    public GpuImage Test { get; private set; }

    /// <summary>
    /// Set 3 of <see cref="DescriptorLayouts.SurfaceTestLayout"/> for each parity: the frame of parity q reads history and
    /// device depth 1 − q and writes history and device depth q.
    /// </summary>
    public DescriptorSet[] Sets { get; }

    /// <summary>The surface history that the frame of <paramref name="parity"/> writes.</summary>
    public GpuImage Written(int parity) => histories[parity];

    /// <summary>The surface history that the frame of <paramref name="parity"/> reads: the one that the last drawn frame wrote.</summary>
    public GpuImage Last(int parity) => histories[1 - parity];

    /// <summary>The device depth that the frame of <paramref name="parity"/> writes, and FSR reads.</summary>
    public GpuImage WrittenDepth(int parity) => deviceDepths[parity];

    /// <summary>The device depth that the frame of <paramref name="parity"/> reads: the one that the last drawn frame wrote.</summary>
    public GpuImage LastDepth(int parity) => deviceDepths[1 - parity];

    /// <summary>
    /// Recreates the images at the targets' size and rewrites both sets. The device must be idle. A new size resets every
    /// history (<see cref="FrameHistory.Begin"/>), so the images' contents need not survive.
    /// </summary>
    public void Resize(RenderTargets targets)
    {
        // Always, even at the same size: the sets point at the motion target, which a resize recreates.
        DestroyImages();
        Create(targets);
    }

    [MemberNotNull(nameof(Test))]
    private void Create(RenderTargets targets)
    {
        Extent2D extent = targets.Extent;
        histories[0] = CreateImage(extent: extent, format: HistoryFormat, name: "surface history 0", sampled: true);
        histories[1] = CreateImage(extent: extent, format: HistoryFormat, name: "surface history 1", sampled: true);
        Test = CreateImage(extent: extent, format: TestFormat, name: "surface test", sampled: false);
        // Sampled, for the next frame's surface test and for FSR.
        deviceDepths[0] = CreateImage(extent: extent, format: DeviceDepthFormat, name: "device depth 0", sampled: true);
        deviceDepths[1] = CreateImage(extent: extent, format: DeviceDepthFormat, name: "device depth 1", sampled: true);
        for (int parity = 0; parity < Sets.Length; parity++)
        {
            DescriptorSet set = Sets[parity];
            layouts.WriteImage(set: set, binding: 0, arrayElement: 0, image: Last(parity));
            layouts.WriteStorageImage(set: set, binding: 1, image: targets.Motion);
            layouts.WriteStorageImage(set: set, binding: 2, image: Written(parity));
            layouts.WriteStorageImage(set: set, binding: 3, image: Test);
            layouts.WriteStorageImage(set: set, binding: 4, image: WrittenDepth(parity));
            layouts.WriteImage(set: set, binding: 5, arrayElement: 0, image: LastDepth(parity));
        }
    }

    private GpuImage CreateImage(Extent2D extent, Format format, string name, bool sampled) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            // Transfer source for the renderer's test hooks.
            usage: ImageUsageFlags.StorageBit
                | ImageUsageFlags.TransferSrcBit
                | (sampled ? ImageUsageFlags.SampledBit : 0),
            mipLevels: 1,
            name: name
        );

    private static void CheckDevice(VulkanDevice device)
    {
        foreach (Format format in (Format[])[HistoryFormat, TestFormat, DeviceDepthFormat])
            if ((device.FormatProperties(format).OptimalTilingFeatures & FormatFeatureFlags.StorageImageBit) == 0)
                throw new NotSupportedException($"{format} cannot be a storage image on {device.DeviceName}.");
    }

    private void DestroyImages()
    {
        foreach (GpuImage history in histories)
            history.Dispose();
        foreach (GpuImage depth in deviceDepths)
            depth.Dispose();
        Test.Dispose();
    }

    // The pool of the layouts frees the sets.
    public void Dispose() => DestroyImages();
}
