using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// When <see cref="GeometryStore.ReserveDeformed"/> keeps the vertex buffer: for a need that the deform region already
/// holds, and for a need past the most that the window of the region allows (docs/plans/skinning-and-morphs.md, "Buffers
/// and the region"). A validating headless device; the recorded commands are never submitted.
/// </summary>
public sealed class GeometryStoreRegionTests
{
    [Fact]
    public void ANeedThatTheRegionAlreadyHoldsKeepsTheVertexBufferAndTheCapacity()
    {
        using var plumbing = new Plumbing();
        using var store = new GeometryStore(plumbing.Device, plumbing.Allocator);
        Reserve(store, plumbing, 1_000);
        GpuBuffer grown = store.VertexBuffer;
        int capacity = store.Region.Capacity;

        Reserve(store, plumbing, 100);

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
        Reserve(store, plumbing, most);
        GpuBuffer grown = store.VertexBuffer;

        Reserve(store, plumbing, most + 1);

        store.Region.Capacity.Should().Be(most, "the window of the region holds no more");
        store.VertexBuffer.Should().BeSameAs(grown, "a region at its most has no larger buffer to grow into");
    }

    private static void Reserve(GeometryStore store, Plumbing plumbing, int vertices) =>
        store.ReserveDeformed(
            vertices: vertices,
            commandBuffer: plumbing.Commands,
            barriers: plumbing.Barriers,
            releases: plumbing.Releases,
            frame: 0
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
