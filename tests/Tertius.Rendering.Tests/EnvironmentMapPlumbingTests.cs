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

    /// <summary>The specification's least maxImageArrayLayers: the most layers that every Vulkan device lets an image hold.</summary>
    private const int LayersOfEveryDevice = 256;

    /// <summary>A map of face size 32 on <paramref name="plumbing"/>'s device, which holds one map until a chain grows it.</summary>
    private static EnvironmentMap SmallMap(Plumbing plumbing) =>
        new(
            device: plumbing.Device,
            allocator: plumbing.Allocator,
            layouts: plumbing.Layouts,
            setting: SceneEnvironmentMap.Default with
            {
                FaceSize = 32,
            },
            framesInFlight: 2
        );

    /// <summary>The frame ids, root first, of a served chain of <paramref name="maps"/> frames.</summary>
    private static long[] Served(int maps) => [.. Enumerable.Range(0, maps).Select(map => (long)map)];

    [Fact]
    public void TheMapsCubeArraysFitTheLayersThatEveryVulkanDeviceOffers()
    {
        using var plumbing = new Plumbing();
        using EnvironmentMap map = SmallMap(plumbing);

        map.FacesFor(frame: 0, served: Served(EnvironmentChain.MaxMaps), longStep: false);

        map.Capacity.Should().Be(EnvironmentChain.MaxMaps);
        (CubeFaces.Count * map.Capacity)
            .Should()
            .BeLessThanOrEqualTo(LayersOfEveryDevice, "the cube arrays of the longest served chain fit every device");
    }

    [Fact]
    public void AMapThatGrowsReleasesItsOldImages()
    {
        using var plumbing = new Plumbing();
        using EnvironmentMap map = SmallMap(plumbing);
        int live = plumbing.Allocator.LiveAllocations;

        map.FacesFor(frame: 0, served: Served(3), longStep: false);

        map.Capacity.Should().Be(3);
        plumbing.Allocator.LiveAllocations.Should().Be(live, "the grown images replace the old ones");
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
