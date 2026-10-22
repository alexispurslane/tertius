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
        vk.CreateCommandPool(device.Device, &poolInfo, null, out CommandPool pool).Check("vkCreateCommandPool");
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
        vk.CreateFence(device.Device, &fenceInfo, null, out Fence fence).Check("vkCreateFence");
        Fence = fence;
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        vk.CreateSemaphore(device.Device, &semaphoreInfo, null, out Semaphore acquired).Check("vkCreateSemaphore");
        ImageAvailable = acquired;
        device.SetName(ObjectType.Semaphore, acquired.Handle, $"frame {index} image available");

        Constants = GpuBuffer.Create(device, allocator, (ulong)Unsafe.SizeOf<GpuFrameConstants>(),
            BufferUsageFlags.UniformBufferBit,
            MemoryUsage.Upload, $"frame {index} constants");
        Instances = new(device, allocator, BufferUsageFlags.StorageBufferBit, $"frame {index} instances", 1024);
        Commands = new(device, allocator, BufferUsageFlags.IndirectBufferBit, $"frame {index} indirect", 256);
        Lights = new(device, allocator, BufferUsageFlags.StorageBufferBit, $"frame {index} lights", 64);
        Occluders = new(device, allocator, BufferUsageFlags.StorageBufferBit, $"frame {index} occluders", 16);
        Outlines = new(device, allocator, BufferUsageFlags.StorageBufferBit, $"frame {index} outlines", 16);
        Puffs = new(device, allocator, BufferUsageFlags.StorageBufferBit, $"frame {index} puffs", 256);
        Staging = new(device, allocator, $"frame {index}");

        FrameSet = layouts.AllocateFrameSet();
        TextureSet = layouts.AllocateTextureSet();
        layouts.WriteBuffer(FrameSet, 0, DescriptorType.UniformBuffer, Constants);
        layouts.WriteBuffer(FrameSet, 1, DescriptorType.StorageBuffer, Instances.Buffer);
        layouts.WriteBuffer(FrameSet, 3, DescriptorType.StorageBuffer, Lights.Buffer);
        layouts.WriteBuffer(FrameSet, 4, DescriptorType.StorageBuffer, Occluders.Buffer);
        layouts.WriteSampler(FrameSet, 5, textures.MaterialSampler);
        layouts.WriteBuffer(FrameSet, 6, DescriptorType.StorageBuffer, Outlines.Buffer);
        layouts.WriteBuffer(FrameSet, 7, DescriptorType.StorageBuffer, Puffs.Buffer);
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
            layouts.WriteBuffer(FrameSet, 1, DescriptorType.StorageBuffer, Instances.Buffer);
        Commands.Write(scene.Commands.AsSpan(0, scene.CommandCount));
        if (Lights.Write(scene.Lights.AsSpan(0, scene.LightCount)))
            layouts.WriteBuffer(FrameSet, 3, DescriptorType.StorageBuffer, Lights.Buffer);
        if (Occluders.Write(scene.Occluders.AsSpan(0, scene.OccluderCount)))
            layouts.WriteBuffer(FrameSet, 4, DescriptorType.StorageBuffer, Occluders.Buffer);
        if (Outlines.Write(scene.Outlines.AsSpan(0, scene.OutlineCount)))
            layouts.WriteBuffer(FrameSet, 6, DescriptorType.StorageBuffer, Outlines.Buffer);
        if (Puffs.Write(scene.Puffs.AsSpan(0, scene.PuffCount)))
            layouts.WriteBuffer(FrameSet, 7, DescriptorType.StorageBuffer, Puffs.Buffer);
    }

    /// <summary>Points this frame's sets at the current material buffer and writes textures added since its last frame.</summary>
    public void UpdateSharedDescriptors(MaterialStore materials, TextureStore textures)
    {
        if (materialVersion != materials.Version)
        {
            layouts.WriteBuffer(FrameSet, 2, DescriptorType.StorageBuffer, materials.Buffer);
            materialVersion = materials.Version;
        }

        for (; texturesWritten < textures.Count; texturesWritten++)
            layouts.WriteImage(TextureSet, 0, (uint)texturesWritten, textures[texturesWritten]);
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
        Buffer = GpuBuffer.Create(device, allocator, (ulong)(Capacity * sizeof(T)), usage, MemoryUsage.Upload, name);
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
            Buffer = GpuBuffer.Create(device, allocator, (ulong)(capacity * sizeof(T)), usage, MemoryUsage.Upload,
                name);
            Capacity = capacity;
            replaced = true;
        }

        if (data.Length > 0)
            Buffer.Write(data);
        return replaced;
    }

    public void Dispose() => Buffer.Dispose();
}
