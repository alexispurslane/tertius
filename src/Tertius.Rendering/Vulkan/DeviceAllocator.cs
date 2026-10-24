using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>Where a resource lives and who touches it.</summary>
internal enum MemoryUsage
{
    /// <summary>Only the GPU reads and writes it.</summary>
    GpuOnly,

    /// <summary>The CPU writes it through a persistent mapping, the GPU reads it.</summary>
    Upload,

    /// <summary>The GPU writes it, the CPU reads it through a persistent mapping.</summary>
    Readback,
}

/// <summary>A piece of device memory handed out by <see cref="DeviceAllocator"/>.</summary>
internal readonly struct MemoryAllocation
{
    internal MemoryAllocation(DeviceMemory memory, ulong offset, ulong size, byte* mapped, object owner)
    {
        Memory = memory;
        Offset = offset;
        Size = size;
        Mapped = mapped;
        Owner = owner;
    }

    public DeviceMemory Memory { get; }
    public ulong Offset { get; }
    public ulong Size { get; }

    /// <summary>The CPU address of <see cref="Offset"/>, or null for memory that is not host-visible.</summary>
    public byte* Mapped { get; }

    /// <summary>The block or dedicated allocation this came from.</summary>
    internal object Owner { get; }
}

/// <summary>
/// Sub-allocates resources from large blocks of device memory, one set of blocks per memory type. Buffers and
/// optimal-tiling images use separate blocks, so bufferImageGranularity never applies. Resources larger than half a
/// block get a dedicated allocation. Host-visible blocks stay mapped for their whole life.
/// Only the render thread allocates, but the methods are locked so that a misuse cannot corrupt the free lists.
/// </summary>
internal sealed unsafe class DeviceAllocator(VulkanDevice device) : IDisposable
{
    public const ulong BlockSize = 64ul << 20;

    private readonly VulkanDevice device = device;
    private readonly Lock gate = new();
    private readonly List<Block> blocks = [];
    private readonly HashSet<Dedicated> dedicated = [];
    private int liveAllocations;
    private bool disposed;

    /// <summary>The number of allocations not yet freed. Zero after a clean shutdown.</summary>
    public int LiveAllocations
    {
        get
        {
            lock (gate)
            {
                return liveAllocations;
            }
        }
    }

    /// <summary>The number of <c>VkDeviceMemory</c> objects, blocks and dedicated allocations together.</summary>
    public int DeviceMemoryCount
    {
        get
        {
            lock (gate)
            {
                return blocks.Count + dedicated.Count;
            }
        }
    }

    public MemoryAllocation Allocate(in MemoryRequirements requirements, MemoryUsage usage, bool linear, string name)
    {
        (MemoryPropertyFlags required, MemoryPropertyFlags preferred) = usage switch
        {
            MemoryUsage.GpuOnly => (MemoryPropertyFlags.DeviceLocalBit, MemoryPropertyFlags.None),
            MemoryUsage.Upload => (
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                MemoryPropertyFlags.DeviceLocalBit
            ),
            MemoryUsage.Readback => (
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                MemoryPropertyFlags.HostCachedBit
            ),
        };
        int type = device.FindMemoryType(requirements.MemoryTypeBits, required, preferred);
        if (type < 0 && usage == MemoryUsage.GpuOnly)
            type = device.FindMemoryType(
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.None,
                MemoryPropertyFlags.None
            );
        if (type < 0)
            throw new NotSupportedException($"No memory type for {name} ({usage}).");
        bool mapped = usage != MemoryUsage.GpuOnly;

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (requirements.Size > BlockSize / 2)
            {
                var allocation = new Dedicated(
                    AllocateMemory(size: requirements.Size, type: (uint)type, map: mapped, pointer: out byte* pointer),
                    pointer
                );
                dedicated.Add(allocation);
                liveAllocations++;
                device.SetName(ObjectType.DeviceMemory, allocation.Memory.Handle, name);
                return new(
                    memory: allocation.Memory,
                    offset: 0,
                    size: requirements.Size,
                    mapped: pointer,
                    owner: allocation
                );
            }

            ulong alignment = Math.Max(requirements.Alignment, 16);
            foreach (Block block in blocks)
                if (
                    block.Type == type
                    && block.Linear == linear
                    && block.Space.Allocate(requirements.Size, alignment) is { } offset
                )
                {
                    liveAllocations++;
                    return new(
                        memory: block.Memory,
                        offset: offset,
                        size: requirements.Size,
                        mapped: block.Mapped == null ? null : block.Mapped + offset,
                        owner: block
                    );
                }

            var created = new Block(
                memory: AllocateMemory(size: BlockSize, type: (uint)type, map: mapped, pointer: out byte* blockPointer),
                mapped: blockPointer,
                type: type,
                linear: linear
            );
            device.SetName(
                ObjectType.DeviceMemory,
                created.Memory.Handle,
                $"block {blocks.Count} (type {type}, {(linear ? "buffers" : "images")})"
            );
            blocks.Add(created);
            ulong first =
                created.Space.Allocate(requirements.Size, alignment)
                ?? throw new InvalidOperationException("A new block cannot hold an allocation smaller than half a block.");
            liveAllocations++;
            return new(
                memory: created.Memory,
                offset: first,
                size: requirements.Size,
                mapped: blockPointer == null ? null : blockPointer + first,
                owner: created
            );
        }
    }

    public void Free(in MemoryAllocation allocation)
    {
        lock (gate)
        {
            switch (allocation.Owner)
            {
                case Block block:
                    block.Space.Free(allocation.Offset, allocation.Size);
                    // Empty blocks are kept: the next resource of the same kind reuses them without a new
                    // vkAllocateMemory. All blocks are released in Dispose.
                    break;
                case Dedicated single:
                    if (!dedicated.Remove(single))
                        throw new InvalidOperationException("Double free of a dedicated allocation.");
                    device.Vk.FreeMemory(device.Device, single.Memory, null);
                    break;
                default:
                    throw new ArgumentException(
                        "The allocation does not come from this allocator.",
                        nameof(allocation)
                    );
            }

            liveAllocations--;
        }
    }

    private DeviceMemory AllocateMemory(ulong size, uint type, bool map, out byte* pointer)
    {
        var info = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = type,
        };
        device
            .Vk.AllocateMemory(
                device: device.Device,
                pAllocateInfo: &info,
                pAllocator: null,
                pMemory: out DeviceMemory memory
            )
            .Check("vkAllocateMemory");
        pointer = null;
        if (map)
        {
            void* p;
            Result result = device.Vk.MapMemory(
                device: device.Device,
                memory: memory,
                offset: 0,
                size: Vk.WholeSize,
                flags: 0,
                ppData: &p
            );
            if (result != Result.Success)
            {
                device.Vk.FreeMemory(device.Device, memory, null);
                result.Check("vkMapMemory");
            }

            pointer = (byte*)p;
        }

        return memory;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            // Unmapping is implicit in vkFreeMemory.
            foreach (Block block in blocks)
                device.Vk.FreeMemory(device.Device, block.Memory, null);
            foreach (Dedicated single in dedicated)
                device.Vk.FreeMemory(device.Device, single.Memory, null);
            blocks.Clear();
            dedicated.Clear();
        }
    }

    private sealed class Block(DeviceMemory memory, byte* mapped, int type, bool linear)
    {
        public DeviceMemory Memory { get; } = memory;
        public byte* Mapped { get; } = mapped;
        public int Type { get; } = type;
        public bool Linear { get; } = linear;
        public FreeList Space { get; } = new(BlockSize);
    }

    private sealed class Dedicated(DeviceMemory memory, byte* mapped)
    {
        public DeviceMemory Memory { get; } = memory;
        public byte* Mapped { get; } = mapped;
    }
}
