using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Tertius.Rendering;

/// <summary>A buffer and its memory. Host-visible buffers stay mapped.</summary>
internal sealed unsafe class GpuBuffer : IDisposable
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly MemoryAllocation memory;
    private bool disposed;

    private GpuBuffer(VulkanDevice device, DeviceAllocator allocator, Buffer handle, MemoryAllocation memory, ulong size, string name)
    {
        this.device = device;
        this.allocator = allocator;
        this.memory = memory;
        Handle = handle;
        Size = size;
        Name = name;
    }

    public Buffer Handle { get; }
    public ulong Size { get; }
    public string Name { get; }
    /// <summary>The CPU address of the buffer's first byte, or null when it is not host-visible.</summary>
    public byte* Mapped => memory.Mapped;
    /// <summary>The last synchronized GPU use. See <see cref="BarrierBatch"/>.</summary>
    public ResourceState State;

    public static GpuBuffer Create(VulkanDevice device, DeviceAllocator allocator, ulong size, BufferUsageFlags usage, MemoryUsage memoryUsage, string name)
    {
        ArgumentOutOfRangeException.ThrowIfZero(size);
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };
        device.Vk.CreateBuffer(device.Device, &info, null, out Buffer buffer).Check("vkCreateBuffer");
        try
        {
            device.Vk.GetBufferMemoryRequirements(device.Device, buffer, out MemoryRequirements requirements);
            MemoryAllocation memory = allocator.Allocate(requirements, memoryUsage, linear: true, name);
            Result bind = device.Vk.BindBufferMemory(device.Device, buffer, memory.Memory, memory.Offset);
            if (bind != Result.Success)
            {
                allocator.Free(memory);
                bind.Check("vkBindBufferMemory");
            }
            device.SetName(ObjectType.Buffer, buffer.Handle, name);
            return new GpuBuffer(device, allocator, buffer, memory, size, name) { State = new ResourceState(ImageLayout.Undefined, PipelineStageFlags2.None, AccessFlags2.None) };
        }
        catch
        {
            device.Vk.DestroyBuffer(device.Device, buffer, null);
            throw;
        }
    }

    /// <summary>The mapped contents as <paramref name="count"/> elements of <typeparamref name="T"/>.</summary>
    public Span<T> AsSpan<T>(int count) where T : unmanaged
    {
        if (memory.Mapped == null)
            throw new InvalidOperationException($"{Name} is not host-visible.");
        if ((ulong)count * (ulong)sizeof(T) > Size)
            throw new ArgumentOutOfRangeException(nameof(count));
        return new Span<T>(memory.Mapped, count);
    }

    /// <summary>Copies <paramref name="data"/> into the mapped buffer at <paramref name="byteOffset"/>.</summary>
    public void Write<T>(ReadOnlySpan<T> data, ulong byteOffset = 0) where T : unmanaged
    {
        ulong bytes = (ulong)data.Length * (ulong)sizeof(T);
        if (memory.Mapped == null)
            throw new InvalidOperationException($"{Name} is not host-visible.");
        if (byteOffset + bytes > Size)
            throw new ArgumentOutOfRangeException(nameof(data));
        fixed (T* source = data)
            Unsafe.CopyBlockUnaligned(memory.Mapped + byteOffset, source, (uint)bytes);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        device.Vk.DestroyBuffer(device.Device, Handle, null);
        allocator.Free(memory);
    }
}
