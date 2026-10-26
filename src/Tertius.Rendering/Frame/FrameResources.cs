using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Tertius.Rendering;

/// <summary>
/// What one frame in flight owns: its command buffer, fence and acquire semaphore, the host-visible buffers the CPU
/// fills each frame (frame constants, instances, indirect commands, lights, occluders, frame outlines, puffs, the deform
/// pass's pose rows and, with the local shadow atlas, the tile views), its descriptor
/// sets and its staging memory. The CPU touches these only after waiting on <see cref="Fence"/>, so no other
/// synchronization is needed for them.
/// </summary>
internal sealed unsafe class FrameResources : IDisposable
{
    private readonly VulkanDevice device;
    private readonly DescriptorLayouts layouts;
    private readonly EnvironmentMap? environment;
    private int texturesWritten;
    private int materialVersion = -1;

    // The GPU form of the frame's lights, before the copy into the light buffer.
    private GpuLight[] gpuLights = new GpuLight[16];

    /// <summary>The <see cref="EnvironmentMap.ImagesVersion"/> that binding 12 points at, or −1 before it is written.</summary>
    private int environmentVersion = -1;

    /// <summary>The vertex buffer whose deform region binding 0 of the deform set points at, or null before it is written.</summary>
    private GpuBuffer? windowBuffer;

    /// <summary>The <see cref="DeformStore.Version"/> that bindings 1 and 2 of the deform set point at, or −1 before.</summary>
    private int deformVersion = -1;

    public FrameResources(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        TextureStore textures,
        BrdfTableImages tables,
        GpuBuffer exposure,
        EnvironmentMap? environment,
        ShadowAtlas? shadowAtlas,
        int index
    )
    {
        this.device = device;
        this.layouts = layouts;
        this.environment = environment;
        Index = index;
        (CommandPool, CommandBuffer) = CreateCommands(index);
        Fence = CreateSignaledFence();
        ImageAvailable = CreateAcquireSemaphore(index);

        Constants = GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: (ulong)Unsafe.SizeOf<GpuFrameConstants>(),
            usage: BufferUsageFlags.UniformBufferBit,
            memoryUsage: MemoryUsage.Upload,
            name: $"frame {index} constants"
        );
        Instances = StorageArray<GpuInstance>(allocator, "instances", 1024);
        Commands = new(
            device: device,
            allocator: allocator,
            usage: BufferUsageFlags.IndirectBufferBit,
            name: $"frame {index} indirect",
            capacity: 256
        );
        Lights = StorageArray<GpuLight>(allocator, "lights", 64);
        Occluders = StorageArray<Vector4>(allocator, "occluders", 16);
        Outlines = StorageArray<GpuOutline>(allocator, "outlines", 16);
        Puffs = StorageArray<GpuPuff>(allocator, "puffs", 256);
        Poses = StorageArray<Vector4>(allocator, "pose rows", 256);
        // Without the atlas the binding stays empty: it is partially bound.
        ShadowTiles = shadowAtlas is null ? null : StorageArray<GpuShadowTile>(allocator, "tile views", 64);
        Staging = new(device, allocator, $"frame {index}");

        FrameSet = layouts.AllocateFrameSet();
        TextureSet = layouts.AllocateTextureSet();
        DeformSet = layouts.AllocateDeformSet();
        WritePoses(Poses);
        WriteFrameSet(textures: textures, tables: tables, exposure: exposure);
        // The irradiance buffer lives as long as the renderer, so its binding is written once. The specular maps grow, so
        // UpdateSharedDescriptors writes their binding. With the map off both stay empty: they are partially bound.
        if (environment is not null)
            layouts.WriteBuffer(
                set: FrameSet,
                binding: 14,
                type: DescriptorType.StorageBuffer,
                buffer: environment.Irradiance
            );
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

    /// <summary>The pose rows of the frame's deform jobs, which the deform pass reads.</summary>
    public HostArray<Vector4> Poses { get; }

    /// <summary>The tile views of the local shadows, or null without the atlas.</summary>
    public HostArray<GpuShadowTile>? ShadowTiles { get; }
    public StagingArena Staging { get; }
    public DescriptorSet FrameSet { get; }
    public DescriptorSet TextureSet { get; }

    /// <summary>The deform pass's set, which <see cref="UpdateSharedDescriptors"/> and <see cref="Write"/> keep current.</summary>
    public DescriptorSet DeformSet { get; }

    /// <summary>The frame number this slot last recorded.</summary>
    public long Number { get; set; } = -1;

    /// <summary>Copies the prepared scene into this frame's buffers. Host writes before vkQueueSubmit need no barrier.</summary>
    public void Write(PreparedScene scene)
    {
        Constants.Write(new ReadOnlySpan<GpuFrameConstants>(in scene.Constants));
        if (Instances.Write(scene.Instances.AsSpan(0, scene.InstanceCount)))
            layouts.WriteBuffer(
                set: FrameSet,
                binding: 1,
                type: DescriptorType.StorageBuffer,
                buffer: Instances.Buffer
            );
        Commands.Write(scene.Commands.AsSpan(0, scene.CommandCount));
        if (Lights.Write(GpuLightsOf(scene)))
            layouts.WriteBuffer(set: FrameSet, binding: 3, type: DescriptorType.StorageBuffer, buffer: Lights.Buffer);
        if (Occluders.Write(scene.Occluders.AsSpan(0, scene.OccluderCount)))
            layouts.WriteBuffer(
                set: FrameSet,
                binding: 4,
                type: DescriptorType.StorageBuffer,
                buffer: Occluders.Buffer
            );
        if (Outlines.Write(scene.Outlines.AsSpan(0, scene.OutlineCount)))
            layouts.WriteBuffer(set: FrameSet, binding: 6, type: DescriptorType.StorageBuffer, buffer: Outlines.Buffer);
        if (Puffs.Write(scene.Puffs.AsSpan(0, scene.PuffCount)))
            layouts.WriteBuffer(set: FrameSet, binding: 7, type: DescriptorType.StorageBuffer, buffer: Puffs.Buffer);
        if (ShadowTiles?.Write(scene.ShadowTiles.AsSpan(0, scene.ShadowTileCount)) == true)
            WriteShadowTiles(ShadowTiles);
        if (Poses.Write(scene.Poses))
            WritePoses(Poses);
    }

    private void WritePoses(HostArray<Vector4> poses) =>
        layouts.WriteBuffer(set: DeformSet, binding: 3, type: DescriptorType.StorageBuffer, buffer: poses.Buffer);

    private void WriteShadowTiles(HostArray<GpuShadowTile> tiles) =>
        layouts.WriteBuffer(
            set: FrameSet,
            binding: ShadowAtlas.TileViewsBinding,
            type: DescriptorType.StorageBuffer,
            buffer: tiles.Buffer
        );

    /// <summary>
    /// The GPU form of each light of the frame, in its order, written after every pass's Prepare, when every slot is
    /// complete (docs/plans/local-light-shadows.md, decision 37).
    /// </summary>
    private ReadOnlySpan<GpuLight> GpuLightsOf(PreparedScene scene)
    {
        if (gpuLights.Length < scene.LightCount)
            Array.Resize(ref gpuLights, Math.Max(scene.LightCount, gpuLights.Length * 2));
        for (int i = 0; i < scene.LightCount; i++)
            gpuLights[i] = GpuLight.From(scene.Lights[i]);
        return gpuLights.AsSpan(0, scene.LightCount);
    }

    /// <summary>A storage buffer of this frame that the CPU rewrites each frame, named after the frame and <paramref name="contents"/>.</summary>
    private HostArray<T> StorageArray<T>(DeviceAllocator allocator, string contents, int capacity)
        where T : unmanaged =>
        new(
            device: device,
            allocator: allocator,
            usage: BufferUsageFlags.StorageBufferBit,
            name: $"frame {Index} {contents}",
            capacity: capacity
        );

    /// <summary>The frame's command pool and its one primary command buffer.</summary>
    private (CommandPool Pool, CommandBuffer Buffer) CreateCommands(int index)
    {
        Vk vk = device.Vk;
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = device.QueueFamily,
        };
        vk.CreateCommandPool(
                device: device.Device,
                pCreateInfo: &poolInfo,
                pAllocator: null,
                pCommandPool: out CommandPool pool
            )
            .Check("vkCreateCommandPool");
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        vk.AllocateCommandBuffers(device.Device, &allocateInfo, out CommandBuffer commandBuffer)
            .Check("vkAllocateCommandBuffers");
        device.SetName(ObjectType.CommandBuffer, (ulong)commandBuffer.Handle, $"frame {index} commands");
        return (pool, commandBuffer);
    }

    // Signaled at creation, so the first wait on a slot that has submitted nothing returns at once.
    private Fence CreateSignaledFence()
    {
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };
        device
            .Vk.CreateFence(device: device.Device, pCreateInfo: &fenceInfo, pAllocator: null, pFence: out Fence fence)
            .Check("vkCreateFence");
        return fence;
    }

    private Semaphore CreateAcquireSemaphore(int index)
    {
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        device
            .Vk.CreateSemaphore(
                device: device.Device,
                pCreateInfo: &semaphoreInfo,
                pAllocator: null,
                pSemaphore: out Semaphore acquired
            )
            .Check("vkCreateSemaphore");
        device.SetName(ObjectType.Semaphore, acquired.Handle, $"frame {index} image available");
        return acquired;
    }

    /// <summary>Writes every binding of the frame set but the materials, which <see cref="UpdateSharedDescriptors"/> writes.</summary>
    private void WriteFrameSet(TextureStore textures, BrdfTableImages tables, GpuBuffer exposure)
    {
        layouts.WriteBuffer(set: FrameSet, binding: 0, type: DescriptorType.UniformBuffer, buffer: Constants);
        layouts.WriteBuffer(set: FrameSet, binding: 1, type: DescriptorType.StorageBuffer, buffer: Instances.Buffer);
        layouts.WriteBuffer(set: FrameSet, binding: 3, type: DescriptorType.StorageBuffer, buffer: Lights.Buffer);
        layouts.WriteBuffer(set: FrameSet, binding: 4, type: DescriptorType.StorageBuffer, buffer: Occluders.Buffer);
        layouts.WriteSampler(FrameSet, 5, textures.MaterialSampler);
        layouts.WriteBuffer(set: FrameSet, binding: 6, type: DescriptorType.StorageBuffer, buffer: Outlines.Buffer);
        layouts.WriteBuffer(set: FrameSet, binding: 7, type: DescriptorType.StorageBuffer, buffer: Puffs.Buffer);
        // The tables never change, so their bindings are written once.
        layouts.WriteImage(set: FrameSet, binding: 8, arrayElement: 0, image: tables.Dfg);
        layouts.WriteImage(set: FrameSet, binding: 9, arrayElement: 0, image: tables.LobeSky);
        // The renderer has one exposure buffer for its life, which every frame passes on to the next.
        layouts.WriteBuffer(set: FrameSet, binding: 11, type: DescriptorType.StorageBuffer, buffer: exposure);
        if (ShadowTiles is { } tiles)
            WriteShadowTiles(tiles);
    }

    /// <summary>
    /// Points this frame's sets at the current material buffer, the current specular maps, the deform region and the
    /// deform store, and writes textures added since its last frame. A growth of the maps waited for the device, so no
    /// submitted frame still uses the set.
    /// </summary>
    public void UpdateSharedDescriptors(
        MaterialStore materials,
        TextureStore textures,
        GeometryStore geometry,
        DeformStore deforms
    )
    {
        UpdateDeformSet(geometry, deforms);
        if (environment is not null && environmentVersion != environment.ImagesVersion)
        {
            layouts.WriteImage(set: FrameSet, binding: 12, arrayElement: 0, image: environment.Specular);
            environmentVersion = environment.ImagesVersion;
        }

        if (materialVersion != materials.Version)
        {
            layouts.WriteBuffer(
                set: FrameSet,
                binding: 2,
                type: DescriptorType.StorageBuffer,
                buffer: materials.Buffer
            );
            materialVersion = materials.Version;
        }

        for (; texturesWritten < textures.Count; texturesWritten++)
            layouts.WriteImage(
                set: TextureSet,
                binding: 0,
                arrayElement: (uint)texturesWritten,
                image: textures[texturesWritten]
            );
    }

    /// <summary>
    /// Points the deform set at the region of the current vertex buffer and at the current deform store. An empty region
    /// has no window, and no job reads it.
    /// </summary>
    private void UpdateDeformSet(GeometryStore geometry, DeformStore deforms)
    {
        DeformRegion region = geometry.Region;
        // A growth of the region or of the static capacity makes a new vertex buffer, so the buffer names the window.
        if (region.Capacity > 0 && windowBuffer != geometry.VertexBuffer)
        {
            ulong vertexSize = (ulong)Unsafe.SizeOf<Vertex>();
            layouts.WriteBuffer(
                set: DeformSet,
                binding: 0,
                type: DescriptorType.StorageBuffer,
                buffer: geometry.VertexBuffer,
                offset: (ulong)region.Base * vertexSize,
                range: (ulong)region.Capacity * vertexSize
            );
            windowBuffer = geometry.VertexBuffer;
        }

        if (deformVersion != deforms.Version)
        {
            layouts.WriteBuffer(
                set: DeformSet,
                binding: 1,
                type: DescriptorType.StorageBuffer,
                buffer: deforms.Vertices.Buffer
            );
            layouts.WriteBuffer(
                set: DeformSet,
                binding: 2,
                type: DescriptorType.StorageBuffer,
                buffer: deforms.Deltas.Buffer
            );
            deformVersion = deforms.Version;
        }
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
        Poses.Dispose();
        ShadowTiles?.Dispose();
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
internal sealed class HostArray<T> : IDisposable
    where T : unmanaged
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
        Buffer = GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: (ulong)(Capacity * sizeof(T)),
            usage: usage,
            memoryUsage: MemoryUsage.Upload,
            name: name
        );
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
            Buffer = GpuBuffer.Create(
                device: device,
                allocator: allocator,
                size: (ulong)(capacity * sizeof(T)),
                usage: usage,
                memoryUsage: MemoryUsage.Upload,
                name: name
            );
            Capacity = capacity;
            replaced = true;
        }

        if (data.Length > 0)
            Buffer.Write(data);
        return replaced;
    }

    public void Dispose() => Buffer.Dispose();
}
