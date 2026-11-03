using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The memory that <see cref="DeviceAllocator"/> places its blocks in: the device's table of memory types, and the calls
/// that allocate, map, name and free one <c>VkDeviceMemory</c>. <see cref="VulkanDeviceMemory"/> is a real device's. A
/// test gives the allocator the table of another driver through this interface, so it runs the allocator's rules on that
/// table without that driver.
/// </summary>
internal interface IDeviceMemory
{
    /// <summary>The memory types and heaps, as <c>vkGetPhysicalDeviceMemoryProperties</c> reports them.</summary>
    PhysicalDeviceMemoryProperties Types { get; }

    /// <summary>A new <c>VkDeviceMemory</c> of <paramref name="size"/> bytes of memory type <paramref name="type"/>.</summary>
    /// <exception cref="VulkanException">The device refused the allocation.</exception>
    DeviceMemory Allocate(ulong size, uint type);

    /// <summary>
    /// Maps the whole of <paramref name="memory"/>, which must be of a host-visible type and not mapped yet, and gives the
    /// CPU address of its first byte. The mapping lasts until <see cref="Free"/>.
    /// </summary>
    /// <exception cref="VulkanException">The device refused the mapping.</exception>
    byte* Map(DeviceMemory memory);

    /// <summary>Frees <paramref name="memory"/>, and with it any mapping of it.</summary>
    void Free(DeviceMemory memory);

    /// <summary>Gives <paramref name="memory"/> a name that validation messages and GPU debuggers show.</summary>
    void SetName(DeviceMemory memory, string name);
}

/// <summary>The memory of a real <see cref="VulkanDevice"/>.</summary>
internal sealed unsafe class VulkanDeviceMemory(VulkanDevice device) : IDeviceMemory
{
    private readonly VulkanDevice device = device;

    public PhysicalDeviceMemoryProperties Types => device.MemoryProperties;

    public DeviceMemory Allocate(ulong size, uint type)
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
        return memory;
    }

    public byte* Map(DeviceMemory memory)
    {
        void* pointer;
        device
            .Vk.MapMemory(
                device: device.Device,
                memory: memory,
                offset: 0,
                size: Vk.WholeSize,
                flags: 0,
                ppData: &pointer
            )
            .Check("vkMapMemory");
        return (byte*)pointer;
    }

    public void Free(DeviceMemory memory) => device.Vk.FreeMemory(device.Device, memory, null);

    public void SetName(DeviceMemory memory, string name) =>
        device.SetName(ObjectType.DeviceMemory, memory.Handle, name);
}
