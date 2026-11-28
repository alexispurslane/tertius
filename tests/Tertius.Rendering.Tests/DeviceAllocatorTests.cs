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

    /// <summary>The one memory type of KosmicKrisp and lavapipe (ARCHITECTURE.md, "GPU memory").</summary>
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

    /// <summary>
    /// RADV's seven types on a discrete GPU (Mesa 26.2.4, radv_physical_device.c): VRAM, GTT, host-visible VRAM and cached
    /// GTT, each twice, the second for 32-bit addresses. Its upload type, 3, is not host-cached.
    /// </summary>
    private static readonly MemoryPropertyFlags[] RadvDiscreteTypes =
    [
        MemoryPropertyFlags.DeviceLocalBit,
        MemoryPropertyFlags.DeviceLocalBit,
        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
        MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
        MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit | MemoryPropertyFlags.HostCachedBit,
        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit | MemoryPropertyFlags.HostCachedBit,
    ];

    /// <summary>The types that RADV allows a buffer: all but the 32-bit types 1, 4 and 6.</summary>
    private const uint RadvBufferTypes = 0b0101101;

    /// <summary>
    /// The four types that NVIDIA's driver gives a buffer on a discrete GPU without resizable BAR: VRAM, system memory
    /// coherent and cached, and the host-visible VRAM of the BAR window, whose heap holds only 256 MiB (ticket ter-net6).
    /// </summary>
    private static readonly MemoryPropertyFlags[] NvidiaDiscreteTypes =
    [
        MemoryPropertyFlags.DeviceLocalBit,
        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
        MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit | MemoryPropertyFlags.HostCachedBit,
        MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
    ];

    /// <summary>The types that NVIDIA's driver allows a buffer: all four.</summary>
    private const uint NvidiaBufferTypes = 0b1111;

    /// <summary>
    /// ANV's two types on an Intel GPU without a shared last-level cache under i915 (Mesa 26.2.4, i915/anv_device.c).
    /// Both are device-local and host-visible. Type 0 is coherent, and type 1 is cached but not coherent.
    /// </summary>
    private static readonly MemoryPropertyFlags[] AnvNonLlcTypes =
    [
        MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
        MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit,
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
    public void AGpuOnlyAllocationGetsNoPointerOnADriverWithOneMemoryTypeAlthoughItsMemoryIsMapped()
    {
        using var memory = new TableMemory([UnifiedType]);
        using var allocator = new DeviceAllocator(memory);

        MemoryAllocation vertices = allocator.Allocate(
            requirements: Requirements(Kilobytes64, typeBits: 0b1),
            usage: MemoryUsage.GpuOnly,
            linear: true,
            name: "vertices"
        );
        MemoryAllocation atlas = allocator.Allocate(
            requirements: Requirements(DeviceAllocator.BlockSize, typeBits: 0b1),
            usage: MemoryUsage.GpuOnly,
            linear: false,
            name: "atlas"
        );

        ((nint)vertices.Mapped).Should().Be(0, "a CPU write into GpuOnly memory fails on every driver");
        ((nint)atlas.Mapped).Should().Be(0, "a dedicated GpuOnly allocation follows the same rule");
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

    [Fact]
    public void AGpuOnlyAllocationOnADiscreteRadeonTakesTheDeviceLocalOnlyTypeAndStaysUnmapped()
    {
        using var memory = new TableMemory(RadvDiscreteTypes);
        using var allocator = new DeviceAllocator(memory);

        MemoryAllocation vertices = allocator.Allocate(
            requirements: Requirements(Kilobytes64, typeBits: RadvBufferTypes),
            usage: MemoryUsage.GpuOnly,
            linear: true,
            name: "vertices"
        );

        ((nint)vertices.Mapped).Should().Be(0, "type 0 is not host-visible");
        memory.Live.Should().ContainSingle().Which.Should().Be(new LiveMemory(Type: 0, Mapped: false));
    }

    [Fact]
    public void AnUploadOnADiscreteRadeonTakesTheHostVisibleVramTypeThatIsNotCachedAndGetsAWritablePointer()
    {
        using var memory = new TableMemory(RadvDiscreteTypes);
        using var allocator = new DeviceAllocator(memory);

        MemoryAllocation staging = allocator.Allocate(
            requirements: Requirements(Kilobytes64, typeBits: RadvBufferTypes),
            usage: MemoryUsage.Upload,
            linear: true,
            name: "staging"
        );

        ((nint)staging.Mapped).Should().NotBe(0, "an upload writes through its pointer");
        staging.Mapped[Kilobytes64 - 1] = 1;
        memory.Live.Should().ContainSingle().Which.Should().Be(new LiveMemory(Type: 3, Mapped: true));
    }

    [Fact]
    public void AReadbackOnAnIntelGpuWithoutASharedCacheTakesTheCoherentTypeAndNotTheCachedOne()
    {
        using var memory = new TableMemory(AnvNonLlcTypes);
        using var allocator = new DeviceAllocator(memory);

        MemoryAllocation readback = allocator.Allocate(
            requirements: Requirements(Kilobytes64, typeBits: 0b11),
            usage: MemoryUsage.Readback,
            linear: true,
            name: "readback"
        );

        ((nint)readback.Mapped).Should().NotBe(0, "a readback reads through its pointer");
        memory
            .Live.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new LiveMemory(Type: 0, Mapped: true),
                "the CPU sees GPU writes without an invalidate only in coherent memory"
            );
    }

    [Fact]
    public void AGpuOnlyResourceThatNoDeviceLocalTypeAllowsTakesTheFirstTypeThatItAllows()
    {
        using var memory = new TableMemory(RadvDiscreteTypes);
        using var allocator = new DeviceAllocator(memory);

        MemoryAllocation vertices = allocator.Allocate(
            requirements: Requirements(Kilobytes64, typeBits: 0b0100100),
            usage: MemoryUsage.GpuOnly,
            linear: true,
            name: "vertices"
        );

        ((nint)vertices.Mapped).Should().Be(0, "a GpuOnly allocation gets no address, also in host-visible memory");
        memory.Live.Should().ContainSingle().Which.Should().Be(new LiveMemory(Type: 2, Mapped: true));
    }

    [Fact]
    public void AnUploadThatNoHostVisibleTypeAllowsIsRefused()
    {
        using var memory = new TableMemory(MoltenVkTypes);
        using var allocator = new DeviceAllocator(memory);

        allocator
            .Invoking(it =>
                it.Allocate(
                    requirements: Requirements(Kilobytes64, typeBits: 0b001),
                    usage: MemoryUsage.Upload,
                    linear: true,
                    name: "staging"
                )
            )
            .Should()
            .Throw<NotSupportedException>();
        memory.Live.Should().BeEmpty();
    }

    [Fact]
    public void AnUploadOnNvidiaTakesTheHostVisibleVramWhileItsHeapHasRoom()
    {
        using var memory = new TableMemory(NvidiaDiscreteTypes);
        using var allocator = new DeviceAllocator(memory);

        allocator.Allocate(
            requirements: Requirements(Kilobytes64, typeBits: NvidiaBufferTypes),
            usage: MemoryUsage.Upload,
            linear: true,
            name: "staging"
        );

        memory.Live.Should().ContainSingle().Which.Should().Be(new LiveMemory(Type: 3, Mapped: true));
    }

    [Theory]
    [InlineData(Kilobytes64)]
    [InlineData(DeviceAllocator.BlockSize)]
    public void AnUploadWhoseHostVisibleVramIsFullTakesTheNextHostVisibleType(ulong size)
    {
        // Several renderers in one process fill the BAR window's 256 MiB while the card's VRAM stays nearly empty. The
        // driver refuses the allocation, and the allocator takes the next type that the usage allows.
        using var memory = new TableMemory(NvidiaDiscreteTypes) { FullTypes = [3] };
        using var allocator = new DeviceAllocator(memory);

        MemoryAllocation staging = allocator.Allocate(
            requirements: Requirements(size, typeBits: NvidiaBufferTypes),
            usage: MemoryUsage.Upload,
            linear: true,
            name: "staging"
        );

        ((nint)staging.Mapped).Should().NotBe(0, "an upload writes through its pointer");
        memory.Live.Should().ContainSingle().Which.Should().Be(new LiveMemory(Type: 1, Mapped: true));
    }

    [Fact]
    public void AnUploadThatEveryAllowedTypeRefusesFailsAsTheDeviceOutOfMemory()
    {
        using var memory = new TableMemory(NvidiaDiscreteTypes) { FullTypes = [1, 2, 3] };
        using var allocator = new DeviceAllocator(memory);

        Action allocate = () =>
            allocator.Allocate(
                requirements: Requirements(Kilobytes64, typeBits: NvidiaBufferTypes),
                usage: MemoryUsage.Upload,
                linear: true,
                name: "staging"
            );

        allocate.Should().Throw<VulkanException>().Which.Result.Should().Be(Result.ErrorOutOfDeviceMemory);
        memory.Live.Should().BeEmpty();
    }

    [Fact]
    public void AGpuOnlyAllocationWhoseVramIsFullTakesTheOtherDeviceLocalTypeAndNoSystemMemory()
    {
        using var memory = new TableMemory(NvidiaDiscreteTypes) { FullTypes = [0] };
        using var allocator = new DeviceAllocator(memory);

        allocator.Allocate(
            requirements: Requirements(Kilobytes64, typeBits: NvidiaBufferTypes),
            usage: MemoryUsage.GpuOnly,
            linear: false,
            name: "target"
        );

        memory.Live.Should().ContainSingle().Which.Type.Should().Be(3, "the only other device-local type");
    }

    [Fact]
    public void AnAllocationWhoseMappingFailsFreesItsMemoryBeforeItThrows()
    {
        using var memory = new TableMemory([UnifiedType]) { RefusesMaps = true };
        using var allocator = new DeviceAllocator(memory);

        allocator
            .Invoking(it =>
                it.Allocate(
                    requirements: Requirements(Kilobytes64, typeBits: 0b1),
                    usage: MemoryUsage.Upload,
                    linear: true,
                    name: "staging"
                )
            )
            .Should()
            .Throw<VulkanException>();
        memory.Live.Should().BeEmpty("the memory whose mapping failed is freed");
        allocator.LiveAllocations.Should().Be(0);
    }

    [Fact]
    public void FreeingADedicatedAllocationReleasesItsMemoryAtOnce()
    {
        using var memory = new TableMemory(MoltenVkTypes);
        using var allocator = new DeviceAllocator(memory);
        MemoryAllocation atlas = allocator.Allocate(
            requirements: Requirements(DeviceAllocator.BlockSize, typeBits: 0b011),
            usage: MemoryUsage.GpuOnly,
            linear: false,
            name: "atlas"
        );

        allocator.Free(atlas);

        memory.Live.Should().BeEmpty("a dedicated allocation owns its memory alone");
    }

    [Fact]
    public void DisposingTheAllocatorReleasesEveryBlockAndDedicatedAllocation()
    {
        using var memory = new TableMemory(MoltenVkTypes);
        using var allocator = new DeviceAllocator(memory);
        MemoryRequirements small = Requirements(Kilobytes64, typeBits: 0b011);
        allocator.Allocate(requirements: small, usage: MemoryUsage.GpuOnly, linear: true, name: "vertices");
        allocator.Allocate(requirements: small, usage: MemoryUsage.Upload, linear: true, name: "staging");
        allocator.Allocate(
            requirements: Requirements(DeviceAllocator.BlockSize, typeBits: 0b011),
            usage: MemoryUsage.GpuOnly,
            linear: false,
            name: "atlas"
        );

        allocator.Dispose();

        memory.Live.Should().BeEmpty();
    }

    [Fact]
    public void FreeingAnAllocationThatTheAllocatorDidNotMakeIsRefused()
    {
        using var memory = new TableMemory([UnifiedType]);
        using var allocator = new DeviceAllocator(memory);

        allocator.Invoking(it => it.Free(default)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("one type")]
    [InlineData("MoltenVK")]
    [InlineData("RADV discrete")]
    [InlineData("ANV without a shared cache")]
    public void TheMappedStateOfEveryBlockFollowsItsTypeAfterAllocationsOfEveryUsageAndFrees(string driver)
    {
        (MemoryPropertyFlags[] types, uint typeBits) = driver switch
        {
            "MoltenVK" => (MoltenVkTypes, 0b111u),
            "RADV discrete" => (RadvDiscreteTypes, RadvBufferTypes),
            "ANV without a shared cache" => (AnvNonLlcTypes, 0b11u),
            _ => ([UnifiedType], 0b1u),
        };
        using var memory = new TableMemory(types);
        using var allocator = new DeviceAllocator(memory);
        MemoryRequirements small = Requirements(Kilobytes64, typeBits: typeBits);
        MemoryRequirements large = Requirements(DeviceAllocator.BlockSize, typeBits: typeBits);

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
        allocator.Allocate(requirements: large, usage: MemoryUsage.Readback, linear: true, name: "big readback");
        allocator.Free(vertices);
        allocator.Free(upload);
        allocator.Allocate(requirements: small, usage: MemoryUsage.Upload, linear: true, name: "staging again");

        allocator.Invoking(it => it.AssertInvariants()).Should().NotThrow();
    }

    [Theory]
    [InlineData(65536ul, 67108863ul, 67108864ul)]
    [InlineData(33554433ul, 33554432ul, 33554433ul)]
    [InlineData(268435456ul, 257949696ul, 268435456ul)]
    [InlineData(536870912ul, 257949696ul, 536870912ul)]
    [InlineData(1069547520ul, 257949696ul, 1069547520ul)]
    public void AnUploadSkipsAPreferredHeapSmallerThanTheActualDeviceAllocation(
        ulong resourceBytes,
        ulong preferredHeapBytes,
        ulong deviceAllocationBytes
    )
    {
        using var memory = new TableMemory(
            types: [(UnifiedType, 1u), (MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, 0u)],
            heapSizes: [2ul << 30, preferredHeapBytes]
        );
        using var allocator = new DeviceAllocator(memory);

        MemoryAllocation upload = allocator.Allocate(
            requirements: Requirements(resourceBytes, typeBits: 0b11),
            usage: MemoryUsage.Upload,
            linear: true,
            name: "upload"
        );

        memory.Calls.Should().Equal((deviceAllocationBytes, 1u));
        upload.Size.Should().Be(resourceBytes);
        ((nint)upload.Mapped).Should().NotBe(0);
        allocator.LiveAllocations.Should().Be(1);
        allocator.DeviceMemoryCount.Should().Be(1);
        allocator.AssertInvariants();
        allocator.Free(upload);
        allocator.LiveAllocations.Should().Be(0);
        allocator.AssertInvariants();
        allocator.Dispose();
        memory.Live.Should().BeEmpty();
    }

    [Theory]
    [InlineData(65536ul, 67108864ul)]
    [InlineData(67108864ul, 67108864ul)]
    [InlineData(257949696ul, 257949696ul)]
    public void ADeviceAllocationCanEqualItsHeapSize(ulong resourceBytes, ulong heapBytes)
    {
        using var memory = new TableMemory(types: [(UnifiedType, 0u)], heapSizes: [heapBytes]);
        using var allocator = new DeviceAllocator(memory);

        MemoryAllocation upload = allocator.Allocate(
            requirements: Requirements(resourceBytes, typeBits: 0b1),
            usage: MemoryUsage.Upload,
            linear: true,
            name: "upload"
        );

        memory.Calls.Should().Equal((heapBytes, 0u));
        upload.Size.Should().Be(resourceBytes);
        allocator.Free(upload);
        allocator.AssertInvariants();
    }

    [Theory]
    [InlineData(65536ul, 67108863ul)]
    [InlineData(268435456ul, 257949696ul)]
    public void CompatibleTypesWhoseSharedHeapIsTooSmallFailAsOutOfDeviceMemoryWithoutCallingTheDriver(
        ulong resourceBytes,
        ulong heapBytes
    )
    {
        using var memory = new TableMemory(
            types: [(UnifiedType, 1u), (UnifiedType, 1u)],
            heapSizes: [2ul << 30, heapBytes]
        );
        using var allocator = new DeviceAllocator(memory);

        Action allocate = () =>
            allocator.Allocate(
                requirements: Requirements(resourceBytes, typeBits: 0b11),
                usage: MemoryUsage.Upload,
                linear: true,
                name: "upload"
            );

        allocate.Should().Throw<VulkanException>().Which.Result.Should().Be(Result.ErrorOutOfDeviceMemory);
        memory.Calls.Should().BeEmpty();
        memory.Live.Should().BeEmpty();
        allocator.LiveAllocations.Should().Be(0);
        allocator.DeviceMemoryCount.Should().Be(0);
        allocator.AssertInvariants();
    }

    [Theory]
    [InlineData(65536ul)]
    [InlineData(268435456ul)]
    public void ALargerHeapDoesNotOverrideTheResourcesAllowedTypesOrTheUploadsRequiredFlags(ulong resourceBytes)
    {
        using var memory = new TableMemory(
            types: [(UnifiedType, 0u), (UnifiedType, 1u), (MemoryPropertyFlags.HostVisibleBit, 1u)],
            heapSizes: [1ul << 20, 2ul << 30]
        );
        using var allocator = new DeviceAllocator(memory);

        Action allocate = () =>
            allocator.Allocate(
                requirements: Requirements(resourceBytes, typeBits: 0b101),
                usage: MemoryUsage.Upload,
                linear: true,
                name: "upload"
            );

        allocate.Should().Throw<VulkanException>().Which.Result.Should().Be(Result.ErrorOutOfDeviceMemory);
        memory.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(65536ul)]
    [InlineData(268435456ul)]
    public void AGpuOnlyAllocationDoesNotUseSystemMemoryWhenAnAllowedDeviceLocalHeapIsTooSmall(ulong resourceBytes)
    {
        using var memory = new TableMemory(
            types:
            [
                (MemoryPropertyFlags.DeviceLocalBit, 0u),
                (MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, 1u),
            ],
            heapSizes: [1ul << 20, 2ul << 30]
        );
        using var allocator = new DeviceAllocator(memory);

        Action allocate = () =>
            allocator.Allocate(
                requirements: Requirements(resourceBytes, typeBits: 0b11),
                usage: MemoryUsage.GpuOnly,
                linear: false,
                name: "image"
            );

        allocate.Should().Throw<VulkanException>().Which.Result.Should().Be(Result.ErrorOutOfDeviceMemory);
        memory.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(65536ul, 67108864ul)]
    [InlineData(268435456ul, 268435456ul)]
    public void ADriverRefusalTriesTheNextLegalTypeInPreferenceOrder(ulong resourceBytes, ulong deviceAllocationBytes)
    {
        using var memory = new TableMemory(
            types:
            [
                (UnifiedType, 1u),
                (UnifiedType, 0u),
                (MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, 0u),
                (UnifiedType, 0u),
            ],
            heapSizes: [2ul << 30, 1ul << 20]
        )
        {
            FullTypes = [1],
        };
        using var allocator = new DeviceAllocator(memory);

        allocator.Allocate(
            requirements: Requirements(resourceBytes, typeBits: 0b1111),
            usage: MemoryUsage.Upload,
            linear: true,
            name: "upload"
        );

        memory.Calls.Should().Equal((deviceAllocationBytes, 1u), (deviceAllocationBytes, 3u));
        memory.Live.Should().ContainSingle().Which.Type.Should().Be(3);
        allocator.AssertInvariants();
    }

    [Fact]
    public void AReadbackSkipsAnUndersizedCachedHeap()
    {
        using var memory = new TableMemory(
            types: [(UnifiedType, 1u), (MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, 0u)],
            heapSizes: [2ul << 30, 1ul << 20]
        );
        using var allocator = new DeviceAllocator(memory);

        allocator.Allocate(
            requirements: Requirements(Kilobytes64, typeBits: 0b11),
            usage: MemoryUsage.Readback,
            linear: true,
            name: "readback"
        );

        memory.Calls.Should().Equal((67108864ul, 1u));
        memory.Live.Should().ContainSingle().Which.Should().Be(new LiveMemory(Type: 1, Mapped: true));
    }

    [Fact]
    public void AnUploadReusesItsBlockAfterSkippingAnUndersizedPreferredHeap()
    {
        using var memory = new TableMemory(
            types: [(UnifiedType, 1u), (MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, 0u)],
            heapSizes: [67108864ul, 1ul << 20]
        );
        using var allocator = new DeviceAllocator(memory);
        MemoryRequirements requirements = Requirements(Kilobytes64, typeBits: 0b11);
        MemoryAllocation first = allocator.Allocate(
            requirements: requirements,
            usage: MemoryUsage.Upload,
            linear: true,
            name: "first"
        );
        allocator.Free(first);

        MemoryAllocation second = allocator.Allocate(
            requirements: requirements,
            usage: MemoryUsage.Upload,
            linear: true,
            name: "second"
        );

        second.Memory.Should().Be(first.Memory);
        second.Offset.Should().Be(first.Offset);
        memory.Calls.Should().Equal((67108864ul, 1u));
        allocator.LiveAllocations.Should().Be(1);
        allocator.AssertInvariants();
        allocator.Free(second);
    }

    private static MemoryRequirements Requirements(ulong size, uint typeBits) =>
        new(size: size, alignment: 256, memoryTypeBits: typeBits);

    /// <summary>One live <c>VkDeviceMemory</c> of <see cref="TableMemory"/>: its type, and whether it is mapped.</summary>
    internal readonly record struct LiveMemory(uint Type, bool Mapped);

    /// <summary>
    /// The memory of a driver with the memory types of a given table, in host memory. It keeps the valid-usage rules of
    /// <c>vkMapMemory</c> that the allocator must keep: it throws on a mapping of memory of a type that is not
    /// host-visible (VUID-vkMapMemory-memory-00682), on a second mapping (VUID-vkMapMemory-memory-00678), and on a free
    /// of memory that is not live. It also rejects allocations larger than the selected heap
    /// (VUID-vkAllocateMemory-pAllocateInfo-01713). Calls record actual requests before any refusal.
    /// </summary>
    private sealed class TableMemory : IDeviceMemory, IDisposable
    {
        private readonly Dictionary<ulong, (uint Type, ulong Size, nint Pointer)> live = [];
        private ulong nextHandle = 1;

        public TableMemory(MemoryPropertyFlags[] typeFlags)
            : this([.. typeFlags.Select(flags => (Flags: flags, Heap: 0u))], [8ul << 30]) { }

        public TableMemory((MemoryPropertyFlags Flags, uint Heap)[] types, ulong[] heapSizes)
        {
            var table = new PhysicalDeviceMemoryProperties
            {
                MemoryTypeCount = (uint)types.Length,
                MemoryHeapCount = (uint)heapSizes.Length,
            };
            for (int i = 0; i < heapSizes.Length; i++)
                table.MemoryHeaps[i] = new(size: heapSizes[i], flags: MemoryHeapFlags.DeviceLocalBit);
            for (int i = 0; i < types.Length; i++)
                table.MemoryTypes[i] = new(propertyFlags: types[i].Flags, heapIndex: types[i].Heap);
            Types = table;
        }

        public List<(ulong Size, uint Type)> Calls { get; } = [];

        public PhysicalDeviceMemoryProperties Types { get; }

        /// <summary>Whether <see cref="Map"/> fails as a driver out of host memory does.</summary>
        public bool RefusesMaps { get; init; }

        /// <summary>The types whose heap is full: <see cref="Allocate"/> of them fails as a driver out of device memory does.</summary>
        public uint[] FullTypes { get; init; } = [];

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
            Calls.Add((size, type));
            uint heap = Types.MemoryTypes[(int)type].HeapIndex;
            if (size > Types.MemoryHeaps[(int)heap].Size)
                throw new InvalidOperationException(
                    $"VUID-vkAllocateMemory-pAllocateInfo-01713: {size} bytes exceed heap {heap}."
                );
            if (FullTypes.Contains(type))
                throw new VulkanException("vkAllocateMemory", Result.ErrorOutOfDeviceMemory);
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
            if (RefusesMaps)
                throw new VulkanException("vkMapMemory", Result.ErrorMemoryMapFailed);
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
