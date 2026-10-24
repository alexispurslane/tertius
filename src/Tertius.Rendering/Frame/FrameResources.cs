using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Tertius.Rendering;

/// <summary>
/// What one frame in flight owns: its command buffer, fence and acquire semaphore, the host-visible buffers the CPU
/// fills each frame (frame constants, instances, indirect commands, lights, occluders, frame outlines, puffs), its descriptor
/// sets and its staging memory. The CPU touches these only after waiting on <see cref="Fence"/>, so no other
/// synchronization is needed for them.
/// </summary>
internal sealed unsafe class FrameResources : IDisposable
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private int texturesWritten;
    private int materialVersion = -1;

    public FrameResources(VulkanDevice device, DeviceAllocator allocator, DescriptorLayouts layouts,
        TextureStore textures, int index)
    {
        this.device = device;
        this.layouts = layouts;
        Index = index;
        Vk vk = device.Vk;

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = device.QueueFamily
        };
        vk.CreateCommandPool(device: device.Device, pCreateInfo: &poolInfo, pAllocator: null, pCommandPool: out CommandPool pool).Check("vkCreateCommandPool");
        CommandPool = pool;
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };
        vk.AllocateCommandBuffers(device.Device, &allocateInfo, out CommandBuffer commandBuffer)
            .Check("vkAllocateCommandBuffers");
        CommandBuffer = commandBuffer;
        device.SetName(ObjectType.CommandBuffer, (ulong)commandBuffer.Handle, $"frame {index} commands");

        var fenceInfo = new FenceCreateInfo
            { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        vk.CreateFence(device: device.Device, pCreateInfo: &fenceInfo, pAllocator: null, pFence: out Fence fence).Check("vkCreateFence");
        Fence = fence;
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        vk.CreateSemaphore(device: device.Device, pCreateInfo: &semaphoreInfo, pAllocator: null, pSemaphore: out Semaphore acquired).Check("vkCreateSemaphore");
        ImageAvailable = acquired;
        device.SetName(ObjectType.Semaphore, acquired.Handle, $"frame {index} image available");

        Constants = GpuBuffer.Create(device: device, allocator: allocator, size: (ulong)Unsafe.SizeOf<GpuFrameConstants>(),
            usage: BufferUsageFlags.UniformBufferBit,
            memoryUsage: MemoryUsage.Upload, name: $"frame {index} constants");
        Instances = new(device: device, allocator: allocator, usage: BufferUsageFlags.StorageBufferBit, name: $"frame {index} instances", capacity: 1024);
        Commands = new(device: device, allocator: allocator, usage: BufferUsageFlags.IndirectBufferBit, name: $"frame {index} indirect", capacity: 256);
        Lights = new(device: device, allocator: allocator, usage: BufferUsageFlags.StorageBufferBit, name: $"frame {index} lights", capacity: 64);
        Occluders = new(device: device, allocator: allocator, usage: BufferUsageFlags.StorageBufferBit, name: $"frame {index} occluders", capacity: 16);
        Outlines = new(device: device, allocator: allocator, usage: BufferUsageFlags.StorageBufferBit, name: $"frame {index} outlines", capacity: 16);
        Puffs = new(device: device, allocator: allocator, usage: BufferUsageFlags.StorageBufferBit, name: $"frame {index} puffs", capacity: 256);
        Staging = new(device, allocator, $"frame {index}");

        FrameSet = layouts.AllocateFrameSet();
        TextureSet = layouts.AllocateTextureSet();
        layouts.WriteBuffer(set: FrameSet, binding: 0, type: DescriptorType.UniformBuffer, buffer: Constants);
        layouts.WriteBuffer(set: FrameSet, binding: 1, type: DescriptorType.StorageBuffer, buffer: Instances.Buffer);
        layouts.WriteBuffer(set: FrameSet, binding: 3, type: DescriptorType.StorageBuffer, buffer: Lights.Buffer);
        layouts.WriteBuffer(set: FrameSet, binding: 4, type: DescriptorType.StorageBuffer, buffer: Occluders.Buffer);
        layouts.WriteSampler(FrameSet, 5, textures.MaterialSampler);
        layouts.WriteBuffer(set: FrameSet, binding: 6, type: DescriptorType.StorageBuffer, buffer: Outlines.Buffer);
        layouts.WriteBuffer(set: FrameSet, binding: 7, type: DescriptorType.StorageBuffer, buffer: Puffs.Buffer);
    }

    public int Index { get; }
    public CommandPool CommandPool { get; }
    public CommandBuffer CommandBuffer { get; }

    /// <summary>Signaled when the GPU has finished this frame's last submission.</summary>
    public Fence Fence { get; }

    /// <summary>Signaled by vkAcquireNextImageKHR for this frame's swapchain image.</summary>
    public Semaphore ImageAvailable { get; }

    public GpuBuffer Constants { get; }
    public HostArray<GpuInstance> Instances { get; }
    public HostArray<DrawIndexedIndirectCommand> Commands { get; }
    public HostArray<GpuLight> Lights { get; }
    public HostArray<Vector4> Occluders { get; }
    public HostArray<GpuOutline> Outlines { get; }
    public HostArray<GpuPuff> Puffs { get; }
    public StagingArena Staging { get; }
    public DescriptorSet FrameSet { get; }
    public DescriptorSet TextureSet { get; }

    /// <summary>The frame number this slot last recorded.</summary>
    public long Number { get; set; } = -1;

    /// <summary>Copies the prepared scene into this frame's buffers. Host writes before vkQueueSubmit need no barrier.</summary>
    public void Write(PreparedScene scene, float exposure)
    {
        GpuFrameConstants constants = scene.Constants;
        constants.Params.X = exposure;
        Constants.Write(new ReadOnlySpan<GpuFrameConstants>(in constants));
        if (Instances.Write(scene.Instances.AsSpan(0, scene.InstanceCount)))
            layouts.WriteBuffer(set: FrameSet, binding: 1, type: DescriptorType.StorageBuffer, buffer: Instances.Buffer);
        Commands.Write(scene.Commands.AsSpan(0, scene.CommandCount));
        if (Lights.Write(scene.Lights.AsSpan(0, scene.LightCount)))
            layouts.WriteBuffer(set: FrameSet, binding: 3, type: DescriptorType.StorageBuffer, buffer: Lights.Buffer);
        if (Occluders.Write(scene.Occluders.AsSpan(0, scene.OccluderCount)))
            layouts.WriteBuffer(set: FrameSet, binding: 4, type: DescriptorType.StorageBuffer, buffer: Occluders.Buffer);
        if (Outlines.Write(scene.Outlines.AsSpan(0, scene.OutlineCount)))
            layouts.WriteBuffer(set: FrameSet, binding: 6, type: DescriptorType.StorageBuffer, buffer: Outlines.Buffer);
        if (Puffs.Write(scene.Puffs.AsSpan(0, scene.PuffCount)))
            layouts.WriteBuffer(set: FrameSet, binding: 7, type: DescriptorType.StorageBuffer, buffer: Puffs.Buffer);
    }

    /// <summary>Points this frame's sets at the current material buffer and writes textures added since its last frame.</summary>
    public void UpdateSharedDescriptors(MaterialStore materials, TextureStore textures)
    {
        if (materialVersion != materials.Version)
        {
            layouts.WriteBuffer(set: FrameSet, binding: 2, type: DescriptorType.StorageBuffer, buffer: materials.Buffer);
            materialVersion = materials.Version;
        }

        for (; texturesWritten < textures.Count; texturesWritten++)
            layouts.WriteImage(set: TextureSet, binding: 0, arrayElement: (uint)texturesWritten, image: textures[texturesWritten]);
    }

    public void Dispose()
    {
        Vk vk = device.Vk;
        Instances.Dispose();
        Commands.Dispose();
        Lights.Dispose();
        Occluders.Dispose();
        Outlines.Dispose();
        Puffs.Dispose();
        Constants.Dispose();
        Staging.Dispose();
        vk.DestroySemaphore(device.Device, ImageAvailable, null);
        vk.DestroyFence(device.Device, Fence, null);
        vk.DestroyCommandPool(device.Device, CommandPool, null);
    }
}

/// <summary>
/// A host-visible buffer of <typeparamref name="T"/> that one frame in flight rewrites every frame. It grows by
/// doubling; the old buffer can be destroyed at once because only this frame used it and its fence has signaled.
/// </summary>
internal sealed class HostArray<T> : IDisposable where T : unmanaged
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly BufferUsageFlags usage;
    private readonly string name;

    public HostArray(VulkanDevice device, DeviceAllocator allocator, BufferUsageFlags usage, string name, int capacity)
    {
        this.device = device;
        this.allocator = allocator;
        this.usage = usage;
        this.name = name;
        Capacity = Math.Max(capacity, 1);
        Buffer = GpuBuffer.Create(device: device, allocator: allocator, size: (ulong)(Capacity * sizeof(T)), usage: usage, memoryUsage: MemoryUsage.Upload, name: name);
    }

    public GpuBuffer Buffer { get; private set; }
    public int Capacity { get; private set; }

    /// <summary>Copies <paramref name="data"/> to the start of the buffer. Returns true when the buffer was replaced.</summary>
    public bool Write(ReadOnlySpan<T> data)
    {
        bool replaced = false;
        if (data.Length > Capacity)
        {
            int capacity = Capacity;
            while (capacity < data.Length)
                capacity *= 2;
            Buffer.Dispose();
            Buffer = GpuBuffer.Create(device: device, allocator: allocator, size: (ulong)(capacity * sizeof(T)), usage: usage, memoryUsage: MemoryUsage.Upload,
                name: name);
            Capacity = capacity;
            replaced = true;
        }

        if (data.Length > 0)
            Buffer.Write(data);
        return replaced;
    }

    public void Dispose() => Buffer.Dispose();
}
