using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Pins which allocations of <see cref="DeviceAllocator"/> get a CPU address, on the memory-type tables of real drivers
/// (ter-gmv8). The allocator runs on <see cref="TableMemory"/>, which reports a driver's table and keeps the rules of
/// <c>vkMapMemory</c>, so no test needs that driver.
/// </summary>
public sealed unsafe class DeviceAllocatorTests
{
    private const ulong Kilobytes64 = 64ul << 10;

    /// <summary>
    /// The one memory type of KosmicKrisp, lavapipe and the integrated GPUs on RADV and ANV: device-local, host-visible,
    /// coherent and cached.
    /// </summary>
    private static readonly MemoryPropertyFlags UnifiedType =
        MemoryPropertyFlags.DeviceLocalBit
        | MemoryPropertyFlags.HostVisibleBit
        | MemoryPropertyFlags.HostCoherentBit
        | MemoryPropertyFlags.HostCachedBit;

    /// <summary>MoltenVK's three types on Apple Silicon: private, shared and memoryless.</summary>
    private static readonly MemoryPropertyFlags[] MoltenVkTypes =
    [
        MemoryPropertyFlags.DeviceLocalBit,
        UnifiedType,
        MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.LazilyAllocatedBit,
    ];

    [Fact]
    public void AnUploadAfterAGpuOnlyAllocationGetsAWritablePointerOnADriverWithOneMemoryType()
    {
        using var memory = new TableMemory([UnifiedType]);
        using var allocator = new DeviceAllocator(memory);
        MemoryRequirements requirements = Requirements(Kilobytes64, typeBits: 0b1);

        MemoryAllocation vertices = allocator.Allocate(
            requirements: requirements,
            usage: MemoryUsage.GpuOnly,
            linear: true,
            name: "vertices"
        );
        MemoryAllocation staging = allocator.Allocate(
            requirements: requirements,
            usage: MemoryUsage.Upload,
            linear: true,
            name: "staging"
        );

        staging.Memory.Handle.Should().Be(vertices.Memory.Handle, "both take the one type, so they share one block");
        ((nint)staging.Mapped).Should().NotBe(0, "an upload writes through its pointer");
        new Span<byte>(staging.Mapped, (int)Kilobytes64).Fill(0xA5);
        new Span<byte>(staging.Mapped, (int)Kilobytes64).ToArray().Should().AllBeEquivalentTo((byte)0xA5);
    }

    [Fact]
    public void AReadbackAfterAGpuOnlyAllocationGetsAReadablePointerOnADriverWithOneMemoryType()
    {
        using var memory = new TableMemory([UnifiedType]);
        using var allocator = new DeviceAllocator(memory);
        MemoryRequirements requirements = Requirements(Kilobytes64, typeBits: 0b1);

        allocator.Allocate(requirements: requirements, usage: MemoryUsage.GpuOnly, linear: true, name: "histogram");
        MemoryAllocation readback = allocator.Allocate(
            requirements: requirements,
            usage: MemoryUsage.Readback,
            linear: true,
            name: "readback"
        );

        ((nint)readback.Mapped).Should().NotBe(0, "a readback reads through its pointer");
        new Span<byte>(readback.Mapped, (int)Kilobytes64).Fill(0x5A);
        readback.Mapped[Kilobytes64 - 1].Should().Be(0x5A);
    }

    [Fact]
    public void AnUploadIntoTheBlockThatAFreedGpuOnlyAllocationLeftEmptyGetsAWritablePointer()
    {
        using var memory = new TableMemory([UnifiedType]);
        using var allocator = new DeviceAllocator(memory);
        MemoryRequirements requirements = Requirements(Kilobytes64, typeBits: 0b1);
        MemoryAllocation vertices = allocator.Allocate(
            requirements: requirements,
            usage: MemoryUsage.GpuOnly,
            linear: true,
            name: "vertices"
        );
        allocator.Free(vertices);

        MemoryAllocation staging = allocator.Allocate(
            requirements: requirements,
            usage: MemoryUsage.Upload,
            linear: true,
            name: "staging"
        );

        staging.Memory.Handle.Should().Be(vertices.Memory.Handle, "the allocator keeps an empty block and reuses it");
        ((nint)staging.Mapped).Should().NotBe(0, "an upload writes through its pointer");
        staging.Mapped[0] = 1;
    }

    [Fact]
    public void EveryBlockAndDedicatedAllocationIsMappedOnADriverWithOneMemoryType()
    {
        using var memory = new TableMemory([UnifiedType]);
        using var allocator = new DeviceAllocator(memory);

        MemoryRequirements small = Requirements(Kilobytes64, typeBits: 0b1);
        allocator.Allocate(requirements: small, usage: MemoryUsage.GpuOnly, linear: true, name: "vertices");
        allocator.Allocate(requirements: small, usage: MemoryUsage.GpuOnly, linear: false, name: "texture");
        allocator.Allocate(
            requirements: Requirements(DeviceAllocator.BlockSize, typeBits: 0b1),
            usage: MemoryUsage.GpuOnly,
            linear: false,
            name: "atlas"
        );

        memory.Live.Should().HaveCount(3, "a buffer block, an image block and a dedicated allocation");
        memory.Live.Should().OnlyContain(live => live.Mapped, "the one type is host-visible");
    }

    [Fact]
    public void AGpuOnlyAllocationOnMoltenVkTakesTheDeviceLocalOnlyTypeAndStaysUnmapped()
    {
        using var memory = new TableMemory(MoltenVkTypes);
        using var allocator = new DeviceAllocator(memory);

        MemoryAllocation vertices = allocator.Allocate(
            requirements: Requirements(Kilobytes64, typeBits: 0b011),
            usage: MemoryUsage.GpuOnly,
            linear: true,
            name: "vertices"
        );

        ((nint)vertices.Mapped).Should().Be(0, "type 0 is not host-visible");
        memory.Live.Should().ContainSingle().Which.Should().Be(new LiveMemory(Type: 0, Mapped: false));
    }

    [Fact]
    public void AnUploadAfterAGpuOnlyAllocationOnMoltenVkTakesTheHostVisibleTypeAndGetsAWritablePointer()
    {
        using var memory = new TableMemory(MoltenVkTypes);
        using var allocator = new DeviceAllocator(memory);
        MemoryRequirements requirements = Requirements(Kilobytes64, typeBits: 0b011);
        allocator.Allocate(requirements: requirements, usage: MemoryUsage.GpuOnly, linear: true, name: "vertices");

        MemoryAllocation staging = allocator.Allocate(
            requirements: requirements,
            usage: MemoryUsage.Upload,
            linear: true,
            name: "staging"
        );

        ((nint)staging.Mapped).Should().NotBe(0, "an upload writes through its pointer");
        staging.Mapped[Kilobytes64 - 1] = 1;
        memory
            .Live.Should()
            .BeEquivalentTo([new LiveMemory(Type: 0, Mapped: false), new LiveMemory(Type: 1, Mapped: true)]);
    }

    [Theory]
    [InlineData("one type")]
    [InlineData("MoltenVK")]
    public void TheMappedStateOfEveryBlockFollowsItsTypeAfterAllocationsOfEveryUsageAndFrees(string driver)
    {
        using var memory = new TableMemory(driver == "MoltenVK" ? MoltenVkTypes : [UnifiedType]);
        using var allocator = new DeviceAllocator(memory);
        uint allTypes = (1u << (int)memory.Types.MemoryTypeCount) - 1;
        MemoryRequirements small = Requirements(Kilobytes64, typeBits: allTypes);
        MemoryRequirements large = Requirements(DeviceAllocator.BlockSize, typeBits: allTypes);

        MemoryAllocation vertices = allocator.Allocate(
            requirements: small,
            usage: MemoryUsage.GpuOnly,
            linear: true,
            name: "vertices"
        );
        allocator.Allocate(requirements: small, usage: MemoryUsage.Upload, linear: true, name: "staging");
        allocator.Allocate(requirements: small, usage: MemoryUsage.Readback, linear: true, name: "readback");
        allocator.Allocate(requirements: large, usage: MemoryUsage.GpuOnly, linear: false, name: "atlas");
        MemoryAllocation upload = allocator.Allocate(
            requirements: large,
            usage: MemoryUsage.Upload,
            linear: true,
            name: "big staging"
        );
        allocator.Free(vertices);
        allocator.Free(upload);
        allocator.Allocate(requirements: small, usage: MemoryUsage.Upload, linear: true, name: "staging again");

        allocator.Invoking(it => it.AssertInvariants()).Should().NotThrow();
    }

    private static MemoryRequirements Requirements(ulong size, uint typeBits) =>
        new(size: size, alignment: 256, memoryTypeBits: typeBits);

    /// <summary>One live <c>VkDeviceMemory</c> of <see cref="TableMemory"/>: its type, and whether it is mapped.</summary>
    internal readonly record struct LiveMemory(uint Type, bool Mapped);

    /// <summary>
    /// The memory of a driver with the memory types of a given table, in host memory. It keeps the valid-usage rules of
    /// <c>vkMapMemory</c> that the allocator must keep: it throws on a mapping of memory of a type that is not
    /// host-visible (VUID-vkMapMemory-memory-00682), on a second mapping (VUID-vkMapMemory-memory-00678), and on a free
    /// of memory that is not live.
    /// </summary>
    private sealed class TableMemory : IDeviceMemory, IDisposable
    {
        private readonly Dictionary<ulong, (uint Type, ulong Size, nint Pointer)> live = [];
        private ulong nextHandle = 1;

        public TableMemory(MemoryPropertyFlags[] typeFlags)
        {
            var table = new PhysicalDeviceMemoryProperties
            {
                MemoryTypeCount = (uint)typeFlags.Length,
                MemoryHeapCount = 1,
            };
            table.MemoryHeaps[0] = new(size: 8ul << 30, flags: MemoryHeapFlags.DeviceLocalBit);
            for (int i = 0; i < typeFlags.Length; i++)
                table.MemoryTypes[i] = new(propertyFlags: typeFlags[i], heapIndex: 0);
            Types = table;
        }

        public PhysicalDeviceMemoryProperties Types { get; }

        /// <summary>Each live memory's type and whether it is mapped, in the order of allocation.</summary>
        public IReadOnlyList<LiveMemory> Live =>
            [
                .. live.OrderBy(entry => entry.Key)
                    .Select(entry => new LiveMemory(entry.Value.Type, entry.Value.Pointer != 0)),
            ];

        public DeviceMemory Allocate(ulong size, uint type)
        {
            if (type >= Types.MemoryTypeCount)
                throw new InvalidOperationException($"No memory type {type}.");
            ulong handle = nextHandle++;
            live.Add(handle, (type, size, 0));
            return new(handle);
        }

        public byte* Map(DeviceMemory memory)
        {
            (uint type, ulong size, nint pointer) = live[memory.Handle];
            if ((Types.MemoryTypes[(int)type].PropertyFlags & MemoryPropertyFlags.HostVisibleBit) == 0)
                throw new InvalidOperationException($"vkMapMemory of memory type {type}, which is not host-visible.");
            if (pointer != 0)
                throw new InvalidOperationException("vkMapMemory of memory that is mapped already.");
            pointer = (nint)NativeMemory.Alloc((nuint)size);
            live[memory.Handle] = (type, size, pointer);
            return (byte*)pointer;
        }

        public void Free(DeviceMemory memory)
        {
            if (!live.Remove(memory.Handle, out (uint Type, ulong Size, nint Pointer) freed))
                throw new InvalidOperationException("vkFreeMemory of memory that is not live.");
            NativeMemory.Free((void*)freed.Pointer);
        }

        public void SetName(DeviceMemory memory, string name) { }

        public void Dispose()
        {
            foreach ((uint Type, ulong Size, nint Pointer) left in live.Values)
                NativeMemory.Free((void*)left.Pointer);
            live.Clear();
        }
    }
}
