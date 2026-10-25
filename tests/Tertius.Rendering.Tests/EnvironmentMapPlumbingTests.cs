namespace Tertius.Rendering.Tests;

/// <summary>
/// The GPU plumbing of the environment map (docs/plans/environment-map.md, "Images and buffers" and "Portable Vulkan"):
/// the descriptors that its compute passes need, and what a map that fails to start leaves behind. Validation is on.
/// </summary>
public sealed class EnvironmentMapPlumbingTests
{
    /// <summary>The specification's least maxPerStageDescriptorStorageImages: what every Vulkan device offers.</summary>
    private const uint StorageImagesOfEveryDevice = 4;

    [Fact]
    public void TheMapsComputeLayoutNeedsNoMoreStorageImagesThanEveryVulkanDeviceOffers()
    {
        using var plumbing = new Plumbing();

        plumbing
            .Layouts.StorageImagesOf(plumbing.Layouts.EnvironmentPrefilterLayout)
            .Should()
            .BeLessThanOrEqualTo(
                StorageImagesOfEveryDevice,
                "the layout is made at every start, and the specification guarantees only 4 per stage"
            );
    }

    [Fact]
    public void AMapThatCannotGetItsDescriptorSetsReleasesTheImagesAndBuffersItMade()
    {
        using var plumbing = new Plumbing();
        // Fill the pool, so that the map's first set does not fit.
        int filled = 0;
        while (filled < 100 && plumbing.FitsAnotherPrefilterSet())
            filled++;

        Action make = () =>
            new EnvironmentMap(
                device: plumbing.Device,
                allocator: plumbing.Allocator,
                layouts: plumbing.Layouts,
                setting: SceneEnvironmentMap.Default with
                {
                    FaceSize = 32,
                },
                framesInFlight: 2
            ).Dispose();

        make.Should().Throw<VulkanException>("the pool holds no more sets");
        plumbing.Allocator.LiveAllocations.Should().Be(0, "the map releases its images and buffers when it fails");
    }

    /// <summary>A validating headless device with the descriptor layouts.</summary>
    private sealed class Plumbing : IDisposable
    {
        public Plumbing()
        {
            Device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
            Allocator = new DeviceAllocator(Device);
            Layouts = new DescriptorLayouts(device: Device, framesInFlight: 2, glowLevels: 0);
        }

        public VulkanDevice Device { get; }
        public DeviceAllocator Allocator { get; }
        public DescriptorLayouts Layouts { get; }

        /// <summary>Allocates one more prefilter set from the pool, and says whether it fit.</summary>
        public bool FitsAnotherPrefilterSet()
        {
            try
            {
                Layouts.AllocateEnvironmentPrefilterSet();
                return true;
            }
            catch (VulkanException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            Layouts.Dispose();
            Allocator.Dispose();
            Device.Dispose();
        }
    }
}
