using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The GPU plumbing of the environment map (docs/plans/environment-map.md, "Images and buffers" and "Portable Vulkan"):
/// the descriptors that its compute passes need, what a map that fails to start or to grow leaves behind, and the drawn
/// chain and the readback of a slot when a drawn frame fails before its submission. Validation is on.
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
            .Layouts.DescriptorsOf(plumbing.Layouts.EnvironmentPrefilterLayout)
            .StorageImages.Compute.Should()
            .BeLessThanOrEqualTo(
                StorageImagesOfEveryDevice,
                "the layout is made at every start, and the specification guarantees only 4 per stage"
            );
    }

    /// <summary>The uses of R16_SFLOAT that the distance cubes need: drawn, filtered and written by the downsample.</summary>
    private const FormatFeatureFlags DistanceUses =
        FormatFeatureFlags.ColorAttachmentBit
        | FormatFeatureFlags.SampledImageBit
        | FormatFeatureFlags.SampledImageFilterLinearBit
        | FormatFeatureFlags.StorageImageBit;

    [Fact]
    public void ADeviceWhoseHalfFloatCannotBeAStorageImageCannotHoldTheDistanceCubes() =>
        EnvironmentMap
            .DistanceFormatProblem(DistanceUses & ~FormatFeatureFlags.StorageImageBit)
            .Should()
            .Contain("StorageImageBit", "the downsample writes the distance cubes' levels as storage images");

    [Fact]
    public void ADeviceWhoseHalfFloatCannotBeFilteredCannotHoldTheDistanceCubes() =>
        EnvironmentMap
            .DistanceFormatProblem(DistanceUses & ~FormatFeatureFlags.SampledImageFilterLinearBit)
            .Should()
            .Contain("SampledImageFilterLinearBit", "the search reads filtered inverse distances");

    [Fact]
    public void ADeviceWhoseHalfFloatCannotBeAColorAttachmentCannotHoldTheDistanceCubes() =>
        EnvironmentMap
            .DistanceFormatProblem(DistanceUses & ~FormatFeatureFlags.ColorAttachmentBit)
            .Should()
            .Contain("ColorAttachmentBit", "the face pass draws the distance cubes' level 0");

    [Fact]
    public void ADeviceWhoseHalfFloatCannotBeSampledCannotHoldTheDistanceCubes() =>
        EnvironmentMap
            .DistanceFormatProblem(DistanceUses & ~FormatFeatureFlags.SampledImageBit)
            .Should()
            .Contain("SampledImageBit", "the shading passes sample the distance cubes through binding 16");

    [Fact]
    public void ADeviceWhoseHalfFloatHasEveryUseCanHoldTheDistanceCubes() =>
        EnvironmentMap.DistanceFormatProblem(DistanceUses).Should().BeNull();

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

    [Fact]
    public void AFrameThatWasNeverSubmittedLeavesTheNextFrameToDrawEveryFace()
    {
        using var plumbing = new Plumbing();
        using EnvironmentMap map = SmallMap(plumbing);
        // The first frame chooses its faces, then throws before its submission, so it records no drawn frame.
        map.FacesFor(frame: 0, served: Served(1), longStep: false);

        map.FacesFor(frame: 1, served: Served(1), longStep: false)
            .ToArray()
            .Should()
            .Equal(
                [FaceMask.All],
                "no frame has drawn the faces, so their level 0 holds the image's undefined content"
            );
    }

    [Fact]
    public void AFrameThatGrewTheMapsAndWasNeverSubmittedLeavesTheNextFrameToDrawEveryFaceOfEveryMap()
    {
        using var plumbing = new Plumbing();
        using EnvironmentMap map = SmallMap(plumbing);
        map.FacesFor(frame: 0, served: Served(3), longStep: false);
        map.Drew(served: Served(3), slot: 0);
        // The camera enters a child: the frame grows the maps to four, then throws before its submission.
        map.FacesFor(frame: 1, served: Served(4), longStep: false);

        map.FacesFor(frame: 1, served: Served(4), longStep: false)
            .ToArray()
            .Should()
            .Equal(
                [FaceMask.All, FaceMask.All, FaceMask.All, FaceMask.All],
                "the grown images hold no drawn face, the kept maps' included"
            );
    }

    [Fact]
    public void AfterASubmittedFrameTheNextFrameOfTheSameChainDrawsItsFacesInTurn()
    {
        using var plumbing = new Plumbing();
        using EnvironmentMap map = SmallMap(plumbing);
        map.FacesFor(frame: 0, served: Served(2), longStep: false);
        map.Drew(served: Served(2), slot: 0);

        var inTurn = new FaceMask[2];
        EnvironmentFaces.InTurn(frame: 1, facesPerFrame: 1, faces: inTurn);
        map.FacesFor(frame: 1, served: Served(2), longStep: false)
            .ToArray()
            .Should()
            .Equal(inTurn, "the submitted frame drew every face of both maps for this chain");
    }

    [Fact]
    public void ACollectReadsOnlyTheMapsThatTheSlotsLastSubmittedFrameCopied()
    {
        using var plumbing = new Plumbing();
        using EnvironmentMap map = SmallMap(plumbing);
        // Frame 0 serves one map in slot 0 and is submitted.
        map.FacesFor(frame: 0, served: Served(1), longStep: false);
        map.Drew(served: Served(1), slot: 0);
        // Frame 2, in slot 0 again, serves two maps and records its readback copy, then throws before its submission.
        map.FacesFor(frame: 2, served: Served(2), longStep: false);
        using (var recording = new Recording(plumbing.Device))
            map.RecordReadback(commandBuffer: recording.CommandBuffer, barriers: new(), slot: 0, maps: 2);

        // The wait before slot 0 records again collects its last submitted frame, frame 0.
        map.Collect(slot: 0, frame: 0);

        IrradianceReading?[] readings = map.ReadingsOf(2).ToArray();
        readings[0].Should().NotBeNull("frame 0 copied the irradiance of map 0");
        readings[1].Should().BeNull("no submitted frame copied map 1, so its readback holds no irradiance");
    }

    [Fact]
    public void AGrowthThatFailsLeavesTheOldMapsWhole()
    {
        using var plumbing = new Plumbing();
        using EnvironmentMap map = SmallMap(plumbing);
        map.FacesFor(frame: 0, served: Served(1), longStep: false);
        map.Drew(served: Served(1), slot: 0);
        int live = plumbing.Allocator.LiveAllocations;
        // The new radiance maps fit, and the new distance cubes do not.
        plumbing.Allocator.AllocationsBeforeFailure = 1;

        Action grow = () => map.FacesFor(frame: 1, served: Served(3), longStep: false);

        grow.Should().Throw<VulkanException>("the device has no memory for the second cube array");
        plumbing.Allocator.AllocationsBeforeFailure = null;
        map.Capacity.Should().Be(1);
        plumbing
            .Allocator.LiveAllocations.Should()
            .Be(live, "the old images stay, and the growth releases the new image that it made");
        map.DrawnCubes[0].FaceView(layer: 0).Handle.Should().NotBe(0, "the old radiance maps keep their views");
        map.DrawnFor.ToArray().Should().Equal(Served(1), "the old maps keep their drawn faces");
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

    /// <summary>
    /// A command buffer in the recording state that is never submitted, as a drawn frame that throws before its submission
    /// leaves it.
    /// </summary>
    private sealed unsafe class Recording : IDisposable
    {
        private readonly VulkanDevice device;
        private readonly CommandPool pool;

        public Recording(VulkanDevice device)
        {
            this.device = device;
            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit,
                QueueFamilyIndex = device.QueueFamily,
            };
            device
                .Vk.CreateCommandPool(
                    device: device.Device,
                    pCreateInfo: &poolInfo,
                    pAllocator: null,
                    pCommandPool: out pool
                )
                .Should()
                .Be(Result.Success);
            var allocate = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = pool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            device
                .Vk.AllocateCommandBuffers(device.Device, &allocate, out CommandBuffer commandBuffer)
                .Should()
                .Be(Result.Success);
            var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
            device.Vk.BeginCommandBuffer(commandBuffer, &begin).Should().Be(Result.Success);
            CommandBuffer = commandBuffer;
        }

        public CommandBuffer CommandBuffer { get; }

        /// <summary>Destroys the pool, which frees the command buffer in its recording state.</summary>
        public void Dispose() => device.Vk.DestroyCommandPool(device.Device, pool, null);
    }
}
