using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// When <see cref="GeometryStore.ReserveDeformed"/> keeps the vertex buffer: for a need that the deform region already
/// holds, and for a need past the most that the window of the region allows (docs/plans/skinning-and-morphs.md, "Buffers
/// and the region"). The kept positions grow with the region, and a growth that no drawn frame followed is copied again
/// by the next frame (docs/plans/temporal-renderer.md, "Kept deformed positions"). A validating headless device; the
/// recorded commands are never submitted.
/// </summary>
public sealed class GeometryStoreRegionTests
{
    [Fact]
    public void ANeedThatTheRegionAlreadyHoldsKeepsTheVertexBufferAndTheCapacity()
    {
        using var plumbing = new Plumbing();
        using var store = new GeometryStore(plumbing.Device, plumbing.Allocator);
        Reserve(store: store, plumbing: plumbing, vertices: 1_000);
        GpuBuffer grown = store.VertexBuffer;
        int capacity = store.Region.Capacity;

        Reserve(store: store, plumbing: plumbing, vertices: 100);

        capacity.Should().Be(1_024, "the first need of 1,000 vertices rounded up to a power of two");
        store.VertexBuffer.Should().BeSameAs(grown, "100 vertices fit in the 1,024 that the region holds");
        store.Region.Capacity.Should().Be(capacity);
    }

    [Fact]
    public void ANeedPastTheMostThatTheWindowAllowsKeepsTheVertexBufferAndTheCapacity()
    {
        using var plumbing = new Plumbing();
        using var store = new GeometryStore(plumbing.Device, plumbing.Allocator);
        int most = GeometryStore.MaxRegion(plumbing.Device.Properties.Limits.MaxStorageBufferRange);
        Reserve(store: store, plumbing: plumbing, vertices: most);
        GpuBuffer grown = store.VertexBuffer;

        Reserve(store: store, plumbing: plumbing, vertices: most + 1);

        store.Region.Capacity.Should().Be(most, "the window of the region holds no more");
        store.VertexBuffer.Should().BeSameAs(grown, "a region at its most has no larger buffer to grow into");
    }

    [Fact]
    public void TheKeptPositionsHoldTheRegionThatTheReservationGrew()
    {
        using var plumbing = new Plumbing();
        using var store = new GeometryStore(plumbing.Device, plumbing.Allocator);
        using var kept = new KeptPositions(plumbing.Device, plumbing.Allocator);

        Reserve(store: store, plumbing: plumbing, vertices: 1_000, kept: kept);

        kept.Capacity.Should().Be(store.Region.Capacity);
        kept.Written(0).Should().NotBeNull();
        kept.Written(1).Should().NotBeNull();
    }

    [Fact]
    public void AGrowthThatNoDrawnFrameFollowedIsCopiedAgainByTheNextFrame()
    {
        using var plumbing = new Plumbing();
        using var store = new GeometryStore(plumbing.Device, plumbing.Allocator);
        using var kept = new KeptPositions(plumbing.Device, plumbing.Allocator);
        Reserve(store: store, plumbing: plumbing, vertices: 1_000, kept: kept);
        kept.Drew(plumbing.Releases, frame: 0);
        // Frame 1 grows the region and copies the kept positions into the new buffers, then throws before its submission.
        Reserve(store: store, plumbing: plumbing, vertices: 5_000, kept: kept, frame: 1);
        int copiesOfTheFrameThatThrew = kept.CopiesRecorded;

        // Frame 1 again, which the renderer numbers as before, because no frame was drawn since.
        Reserve(store: store, plumbing: plumbing, vertices: 5_000, kept: kept, frame: 1);

        copiesOfTheFrameThatThrew.Should().Be(2, "the growth copies both buffers");
        kept.CopiesRecorded.Should().Be(4, "the copy that frame 1 recorded never ran, so its retry records it again");
    }

    [Fact]
    public void ASecondGrowthBeforeADrawnFrameRetiresTheEmptyBuffersOfTheFirstAndKeepsTheDrawnOnesAsTheSource()
    {
        using var plumbing = new Plumbing();
        using var store = new GeometryStore(plumbing.Device, plumbing.Allocator);
        using var kept = new KeptPositions(plumbing.Device, plumbing.Allocator);
        Reserve(store: store, plumbing: plumbing, vertices: 1_000, kept: kept);
        kept.Drew(plumbing.Releases, frame: 0);
        // Frame 1 grows the region, then throws before its submission.
        Reserve(store: store, plumbing: plumbing, vertices: 5_000, kept: kept, frame: 1);
        int retiredBefore = plumbing.Releases.Count;

        // Frame 1 again grows past the first growth.
        Reserve(store: store, plumbing: plumbing, vertices: 50_000, kept: kept, frame: 1);
        int retiredBySecondGrowth = plumbing.Releases.Count - retiredBefore;
        kept.Drew(plumbing.Releases, frame: 1);

        retiredBySecondGrowth
            .Should()
            .Be(3, "the vertex buffer and the first growth's kept buffers, the targets of a copy that never ran");
        (plumbing.Releases.Count - retiredBefore)
            .Should()
            .Be(5, "the drawn frame retires frame 0's buffers, its copy's source");
        kept.CopiesRecorded.Should().Be(4, "each try of frame 1 copies frame 0's two buffers");
        plumbing
            .Device.Log.Snapshot()
            .Should()
            .BeEmpty("each copy reads only the vertices that frame 0's buffers hold");
    }

    [Fact]
    public void AGrowthThatAFrameDrewIsNotCopiedAgain()
    {
        using var plumbing = new Plumbing();
        using var store = new GeometryStore(plumbing.Device, plumbing.Allocator);
        using var kept = new KeptPositions(plumbing.Device, plumbing.Allocator);
        Reserve(store: store, plumbing: plumbing, vertices: 1_000, kept: kept);
        kept.Drew(plumbing.Releases, frame: 0);
        Reserve(store: store, plumbing: plumbing, vertices: 5_000, kept: kept, frame: 1);
        kept.Drew(plumbing.Releases, frame: 1);

        Reserve(store: store, plumbing: plumbing, vertices: 5_000, kept: kept, frame: 2);

        kept.CopiesRecorded.Should().Be(2, "frame 1's copy ran, so frame 2 reads the buffers that it filled");
    }

    [Fact]
    public void TheBuffersThatAGrowthReplacedRetireWithTheFrameThatDrewTheirCopy()
    {
        using var plumbing = new Plumbing();
        using var store = new GeometryStore(plumbing.Device, plumbing.Allocator);
        using var kept = new KeptPositions(plumbing.Device, plumbing.Allocator);
        Reserve(store: store, plumbing: plumbing, vertices: 1_000, kept: kept);
        kept.Drew(plumbing.Releases, frame: 0);
        int retiredBefore = plumbing.Releases.Count;
        Reserve(store: store, plumbing: plumbing, vertices: 5_000, kept: kept, frame: 1);
        int retiredByTheGrowth = plumbing.Releases.Count - retiredBefore;

        kept.Drew(plumbing.Releases, frame: 1);

        retiredByTheGrowth.Should().Be(1, "only the vertex buffer: the old kept buffers stay the source of the copy");
        (plumbing.Releases.Count - retiredBefore).Should().Be(3, "the drawn frame retires both old kept buffers");
    }

    private static void Reserve(
        GeometryStore store,
        Plumbing plumbing,
        int vertices,
        KeptPositions? kept = null,
        long frame = 0
    ) =>
        store.ReserveDeformed(
            vertices: vertices,
            kept: kept,
            commandBuffer: plumbing.Commands,
            barriers: plumbing.Barriers,
            releases: plumbing.Releases,
            frame: frame
        );

    /// <summary>A validating headless device with a recording command buffer that is never submitted.</summary>
    private sealed unsafe class Plumbing : IDisposable
    {
        private readonly CommandPool pool;

        public Plumbing()
        {
            Device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
            Allocator = new DeviceAllocator(Device);
            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit,
                QueueFamilyIndex = Device.QueueFamily,
            };
            Device
                .Vk.CreateCommandPool(
                    device: Device.Device,
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
            Device
                .Vk.AllocateCommandBuffers(Device.Device, &allocate, out CommandBuffer commands)
                .Should()
                .Be(Result.Success);
            var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
            Device.Vk.BeginCommandBuffer(commands, &begin).Should().Be(Result.Success);
            Commands = commands;
        }

        public VulkanDevice Device { get; }
        public DeviceAllocator Allocator { get; }
        public CommandBuffer Commands { get; }
        public BarrierBatch Barriers { get; } = new();
        public DeferredReleases Releases { get; } = new();

        public void Dispose()
        {
            Releases.Dispose();
            Device.Vk.DestroyCommandPool(Device.Device, pool, null);
            Allocator.Dispose();
            Device.Dispose();
        }
    }
}
