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
    private int? allocationsBeforeFailure;
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

    /// <summary>
    /// A test hook: when set, <see cref="Allocate"/> grants this many more allocations, then refuses each one with the
    /// error of a device that is out of memory. Null, the default, refuses none.
    /// </summary>
    public int? AllocationsBeforeFailure
    {
        get
        {
            lock (gate)
            {
                return allocationsBeforeFailure;
            }
        }
        set
        {
            lock (gate)
            {
                allocationsBeforeFailure = value;
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
        int type = MemoryTypeOf(requirements: requirements, usage: usage, name: name);
        bool mapped = usage != MemoryUsage.GpuOnly;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            CountTowardFailure(name);
            return requirements.Size > BlockSize / 2
                ? AllocateDedicated(size: requirements.Size, type: type, mapped: mapped, name: name)
                : AllocateInBlock(requirements: requirements, type: type, mapped: mapped, linear: linear);
        }
    }

    /// <summary>The memory type for <paramref name="usage"/> among the types that <paramref name="requirements"/> allows.</summary>
    /// <exception cref="NotSupportedException">No memory type fits.</exception>
    private int MemoryTypeOf(in MemoryRequirements requirements, MemoryUsage usage, string name)
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
        return type;
    }

    /// <summary>
    /// Counts one allocation toward <see cref="AllocationsBeforeFailure"/>, and throws as a device out of memory would
    /// when none is left. Under the gate.
    /// </summary>
    private void CountTowardFailure(string name)
    {
        if (allocationsBeforeFailure is not { } granted)
            return;
        if (granted == 0)
            throw new VulkanException($"vkAllocateMemory for {name}", Result.ErrorOutOfDeviceMemory);
        allocationsBeforeFailure = granted - 1;
    }

    /// <summary>An allocation of its own <c>VkDeviceMemory</c>, for a resource of more than half a block. Under the gate.</summary>
    private MemoryAllocation AllocateDedicated(ulong size, int type, bool mapped, string name)
    {
        var allocation = new Dedicated(
            AllocateMemory(size: size, type: (uint)type, map: mapped, pointer: out byte* pointer),
            pointer
        );
        dedicated.Add(allocation);
        liveAllocations++;
        device.SetName(ObjectType.DeviceMemory, allocation.Memory.Handle, name);
        return new(memory: allocation.Memory, offset: 0, size: size, mapped: pointer, owner: allocation);
    }

    /// <summary>
    /// An allocation in the first block of <paramref name="type"/> and <paramref name="linear"/> with room for it, or in a
    /// new block. Under the gate.
    /// </summary>
    private MemoryAllocation AllocateInBlock(in MemoryRequirements requirements, int type, bool mapped, bool linear)
    {
        ulong alignment = Math.Max(requirements.Alignment, 16);
        foreach (Block block in blocks)
            if (
                block.Type == type
                && block.Linear == linear
                && block.Space.Allocate(requirements.Size, alignment) is { } offset
            )
                return InBlock(block: block, offset: offset, size: requirements.Size);

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
        return InBlock(block: created, offset: first, size: requirements.Size);
    }

    /// <summary>The allocation of <paramref name="size"/> bytes at <paramref name="offset"/> of <paramref name="block"/>. Under the gate.</summary>
    private MemoryAllocation InBlock(Block block, ulong offset, ulong size)
    {
        liveAllocations++;
        return new(
            memory: block.Memory,
            offset: offset,
            size: size,
            mapped: block.Mapped == null ? null : block.Mapped + offset,
            owner: block
        );
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
