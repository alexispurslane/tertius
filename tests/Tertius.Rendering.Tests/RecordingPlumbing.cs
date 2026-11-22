using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>A validating headless device with a recording command buffer that is never submitted.</summary>
internal sealed unsafe class RecordingPlumbing : IDisposable
{
    private readonly CommandPool pool;

    public RecordingPlumbing()
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
