using System.Numerics;
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

    /// <summary>The CPU address of <see cref="Offset"/> for an Upload or a Readback, or null for a GpuOnly allocation.</summary>
    public byte* Mapped { get; }

    /// <summary>The block or dedicated allocation this came from.</summary>
    internal object Owner { get; }

    /// <summary>This allocation with a null <see cref="Mapped"/>.</summary>
    internal unsafe MemoryAllocation WithoutAddress() =>
        new(memory: Memory, offset: Offset, size: Size, mapped: null, owner: Owner);
}

/// <summary>
/// Sub-allocates 64 MiB blocks, one set per memory type, with dedicated allocations for large resources (ARCHITECTURE.md,
/// "GPU memory"). Locked, so that a misuse cannot corrupt the free lists.
/// </summary>
/// <remarks>
/// Memory is mapped exactly when its type is host-visible, which costs 64 MiB of CPU address space per block.
/// <para>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>Every block and dedicated allocation of a host-visible type has a pointer, and no other one has.</item>
/// <item>The live allocations count at least the dedicated allocations.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed unsafe class DeviceAllocator(IDeviceMemory memory) : IDisposable
{
    public const ulong BlockSize = 64ul << 20;

    private readonly IDeviceMemory memory = memory;
    private readonly Lock gate = new();
    private readonly List<Block> blocks = [];
    private readonly HashSet<Dedicated> dedicated = [];
    private int liveAllocations;
    private int? allocationsBeforeFailure;
    private bool disposed;

    /// <summary>An allocator over the memory of <paramref name="device"/>.</summary>
    public DeviceAllocator(VulkanDevice device)
        : this(new VulkanDeviceMemory(device)) { }

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

    /// <summary>
    /// An allocation for <paramref name="usage"/>, in the best memory type that has room. A driver refuses a new block
    /// when the type's heap is full, though another type's heap has room: on a discrete NVIDIA GPU without resizable BAR,
    /// the host-visible VRAM that an upload prefers lies in a heap of 256 MiB, which a few renderers in one process fill
    /// while the card's VRAM stays nearly empty (ticket ter-net6). So a type that the driver refuses as out of device
    /// memory passes the allocation to the next type, and only the last type's refusal reaches the caller.
    /// </summary>
    public MemoryAllocation Allocate(in MemoryRequirements requirements, MemoryUsage usage, bool linear, string name)
    {
        int[] types = MemoryTypesOf(requirements: requirements, usage: usage, name: name);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            CountTowardFailure(name);
            for (int candidate = 0; ; candidate++)
            {
                int type = types[candidate];
                try
                {
                    MemoryAllocation granted =
                        requirements.Size > BlockSize / 2
                            ? AllocateDedicated(size: requirements.Size, type: type, name: name)
                            : AllocateInBlock(requirements: requirements, type: type, linear: linear);
                    // A GpuOnly allocation in mapped memory hides the address, so that a CPU access fails on every driver.
                    return usage == MemoryUsage.GpuOnly ? granted.WithoutAddress() : granted;
                }
                catch (VulkanException refused)
                    when (refused.Result == Result.ErrorOutOfDeviceMemory && candidate < types.Length - 1) { }
            }
        }
    }

    /// <summary>
    /// The memory types for <paramref name="usage"/> among the types that <paramref name="requirements"/> allows, best
    /// first: those with all the required flags, by the count of preferred flags, then in the table's order.
    /// </summary>
    /// <exception cref="NotSupportedException">No memory type fits.</exception>
    private int[] MemoryTypesOf(in MemoryRequirements requirements, MemoryUsage usage, string name)
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
        int[] types = MemoryTypesWith(requirements.MemoryTypeBits, required, preferred);
        if (types.Length == 0 && usage == MemoryUsage.GpuOnly)
            types = MemoryTypesWith(requirements.MemoryTypeBits, MemoryPropertyFlags.None, MemoryPropertyFlags.None);
        if (types.Length == 0)
            throw new NotSupportedException($"No memory type for {name} ({usage}).");
        return types;
    }

    /// <summary>
    /// The memory types allowed by <paramref name="typeBits"/> with all <paramref name="required"/> flags, those with the
    /// most <paramref name="preferred"/> flags first, and in the table's order among equals.
    /// </summary>
    private int[] MemoryTypesWith(uint typeBits, MemoryPropertyFlags required, MemoryPropertyFlags preferred)
    {
        PhysicalDeviceMemoryProperties types = memory.Types;
        return
        [
            .. Enumerable
                .Range(0, (int)types.MemoryTypeCount)
                .Where(i => (typeBits & (1u << i)) != 0 && (types.MemoryTypes[i].PropertyFlags & required) == required)
                .OrderByDescending(i => BitOperations.PopCount((uint)(types.MemoryTypes[i].PropertyFlags & preferred))),
        ];
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
    private MemoryAllocation AllocateDedicated(ulong size, int type, string name)
    {
        var allocation = new Dedicated(
            memory: AllocateMemory(size: size, type: type, pointer: out byte* pointer),
            mapped: pointer,
            type: type
        );
        dedicated.Add(allocation);
        liveAllocations++;
        memory.SetName(allocation.Memory, name);
        return new(memory: allocation.Memory, offset: 0, size: size, mapped: pointer, owner: allocation);
    }

    /// <summary>
    /// An allocation in the first block of <paramref name="type"/> and <paramref name="linear"/> with room for it, or in a
    /// new block. Under the gate.
    /// </summary>
    private MemoryAllocation AllocateInBlock(in MemoryRequirements requirements, int type, bool linear)
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
            memory: AllocateMemory(size: BlockSize, type: type, pointer: out byte* blockPointer),
            mapped: blockPointer,
            type: type,
            linear: linear
        );
        memory.SetName(created.Memory, $"block {blocks.Count} (type {type}, {(linear ? "buffers" : "images")})");
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
                    memory.Free(single.Memory);
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

    /// <summary>
    /// A new <c>VkDeviceMemory</c> of <paramref name="size"/> bytes of <paramref name="type"/>, mapped exactly when the
    /// type is host-visible. A mapping that fails frees the memory before it throws.
    /// </summary>
    private DeviceMemory AllocateMemory(ulong size, int type, out byte* pointer)
    {
        DeviceMemory allocated = memory.Allocate(size, (uint)type);
        pointer = null;
        if (IsHostVisible(type))
        {
            try
            {
                pointer = memory.Map(allocated);
            }
            catch
            {
                memory.Free(allocated);
                throw;
            }
        }

        return allocated;
    }

    /// <summary>Whether memory of <paramref name="type"/> can be mapped.</summary>
    private bool IsHostVisible(int type) =>
        (memory.Types.MemoryTypes[type].PropertyFlags & MemoryPropertyFlags.HostVisibleBit) != 0;

    /// <summary>Checks the invariants in the class comment. O(blocks + dedicated allocations).</summary>
    internal void AssertInvariants()
    {
        lock (gate)
        {
            foreach (Block block in blocks)
                Contract.Invariant(block.Mapped != null == IsHostVisible(block.Type));
            foreach (Dedicated single in dedicated)
                Contract.Invariant(single.Mapped != null == IsHostVisible(single.Type));
            Contract.Invariant(liveAllocations >= dedicated.Count);
        }
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
                memory.Free(block.Memory);
            foreach (Dedicated single in dedicated)
                memory.Free(single.Memory);
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

    private sealed class Dedicated(DeviceMemory memory, byte* mapped, int type)
    {
        public DeviceMemory Memory { get; } = memory;
        public byte* Mapped { get; } = mapped;
        public int Type { get; } = type;
    }
}
