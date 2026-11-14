using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Tertius.Rendering;

/// <summary>
/// What one frame in flight owns: its command buffer, fence and acquire semaphore, the host-visible buffers the CPU
/// fills each frame (frame constants, instances, indirect commands, lights, occluders, frame outlines, puffs, the motion
/// rows, the deform pass's pose rows and job table and, with the local shadow atlas, the tile views), its descriptor
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

    // The frame's job table, before the copy into the job buffer.
    private GpuDeformJob[] gpuDeformJobs = new GpuDeformJob[16];

    /// <summary>The <see cref="EnvironmentMap.ImagesVersion"/> that bindings 12, 14 and 16 point at, or −1 before they are written.</summary>
    private int environmentVersion = -1;

    /// <summary>The vertex buffer whose deform region binding 0 of the deform set points at, or null before it is written.</summary>
    private GpuBuffer? windowBuffer;

    /// <summary>The <see cref="DeformStore.Version"/> that bindings 1 and 2 of the deform set point at, or −1 before.</summary>
    private int deformVersion = -1;

    /// <summary>The buffer that the frame set's kept positions point at, or null before it is written.</summary>
    private GpuBuffer? keptRead;

    /// <summary>The buffer that the deform set's kept positions point at, or null before it is written.</summary>
    private GpuBuffer? keptWritten;

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
        (CommandPool, CommandBuffer, OutputCommandBuffer) = CreateCommands(index);
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
        Motions = StorageArray<GpuMotion>(allocator, "motion rows", 1024);

        Poses = StorageArray<Vector4>(allocator, "pose rows", 256);
        DeformJobs = StorageArray<GpuDeformJob>(allocator, "deform jobs", 16);
        // Without the atlas the binding stays empty: it is partially bound.
        ShadowTiles = shadowAtlas is null ? null : StorageArray<GpuShadowTile>(allocator, "tile views", 64);
        Staging = new(device, allocator, $"frame {index}");

        FrameSet = layouts.AllocateFrameSet();
        TextureSet = layouts.AllocateTextureSet();
        DeformSet = layouts.AllocateDeformSet();
        WritePoses(Poses);
        WriteDeformJobs(DeformJobs);
        WriteFrameSet(textures: textures, tables: tables, exposure: exposure);
    }

    public int Index { get; }
    public CommandPool CommandPool { get; }
    public CommandBuffer CommandBuffer { get; }
    public CommandBuffer OutputCommandBuffer { get; }

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

    /// <summary>The motion rows of the frame's placed instances, which the G-buffer reads (<see cref="PreparedScene.Motions"/>).</summary>
    public HostArray<GpuMotion> Motions { get; }

    /// <summary>The pose rows of the frame's deform jobs, which the deform pass reads.</summary>
    public HostArray<Vector4> Poses { get; }

    /// <summary>The job table of the frame's deform jobs, which the deform pass reads.</summary>
    public HostArray<GpuDeformJob> DeformJobs { get; }

    /// <summary>The workgroups of the deform pass's dispatch, which the records in <see cref="DeformJobs"/> divide among them.</summary>
    public uint DeformGroups { get; private set; }

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
        if (Motions.Write(scene.MotionSpan))
            WriteMotions(Motions);
        if (ShadowTiles?.Write(scene.ShadowTiles.AsSpan(0, scene.ShadowTileCount)) == true)
            WriteShadowTiles(ShadowTiles);
        if (Poses.Write(scene.Poses))
            WritePoses(Poses);
        if (DeformJobs.Write(GpuDeformJobsOf(scene)))
            WriteDeformJobs(DeformJobs);
    }

    private void WriteMotions(HostArray<GpuMotion> motions) =>
        layouts.WriteBuffer(
            set: FrameSet,
            binding: DescriptorLayouts.MotionsBinding,
            type: DescriptorType.StorageBuffer,
            buffer: motions.Buffer
        );

    /// <summary>
    /// Points the frame set at the kept positions that the frame of <paramref name="parity"/> reads and the deform set at
    /// the ones that it writes (<see cref="KeptPositions"/>). Both stay empty before the first deformed copy, when no
    /// motion row names a kept copy and no job runs.
    /// </summary>
    public void UpdateKeptPositions(KeptPositions kept, int parity)
    {
        if (kept.Read(parity) is { } read && read != keptRead)
        {
            layouts.WriteBuffer(
                set: FrameSet,
                binding: DescriptorLayouts.KeptPositionsBinding,
                type: DescriptorType.StorageBuffer,
                buffer: read
            );
            keptRead = read;
        }

        if (kept.Written(parity) is { } written && written != keptWritten)
        {
            layouts.WriteBuffer(
                set: DeformSet,
                binding: DescriptorLayouts.DeformKeptPositionsBinding,
                type: DescriptorType.StorageBuffer,
                buffer: written
            );
            keptWritten = written;
        }
    }

    private void WritePoses(HostArray<Vector4> poses) =>
        layouts.WriteBuffer(set: DeformSet, binding: 3, type: DescriptorType.StorageBuffer, buffer: poses.Buffer);

    private void WriteDeformJobs(HostArray<GpuDeformJob> jobs) =>
        layouts.WriteBuffer(set: DeformSet, binding: 4, type: DescriptorType.StorageBuffer, buffer: jobs.Buffer);

    /// <summary>The job table of the frame's deform jobs, in job order. Sets <see cref="DeformGroups"/> to its workgroups.</summary>
    private ReadOnlySpan<GpuDeformJob> GpuDeformJobsOf(PreparedScene scene)
    {
        ReadOnlySpan<DeformJob> jobs = scene.Deforms;
        if (gpuDeformJobs.Length < jobs.Length)
            Array.Resize(ref gpuDeformJobs, Math.Max(jobs.Length, gpuDeformJobs.Length * 2));
        Span<GpuDeformJob> records = gpuDeformJobs.AsSpan(0, jobs.Length);
        DeformGroups = GpuDeformJob.FromJobs(jobs: jobs, meshes: scene.Meshes, records: records);
        return records;
    }

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
    private (CommandPool Pool, CommandBuffer Buffer, CommandBuffer Output) CreateCommands(int index)
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
            CommandBufferCount = 2,
        };
        CommandBuffer* commands = stackalloc CommandBuffer[2];
        vk.AllocateCommandBuffers(device.Device, &allocateInfo, commands).Check("vkAllocateCommandBuffers");
        device.SetName(ObjectType.CommandBuffer, (ulong)commands[0].Handle, $"frame {index} commands");
        device.SetName(ObjectType.CommandBuffer, (ulong)commands[1].Handle, $"frame {index} output commands");
        return (pool, commands[0], commands[1]);
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
        WriteMotions(Motions);

        // The tables and the mask never change, so their bindings are written once.
        layouts.WriteImage(set: FrameSet, binding: 8, arrayElement: 0, image: tables.Dfg);
        layouts.WriteImage(set: FrameSet, binding: 9, arrayElement: 0, image: tables.LobeSky);
        layouts.WriteImage(
            set: FrameSet,
            binding: DescriptorLayouts.LtcTableBinding,
            arrayElement: 0,
            image: tables.Ltc
        );
        layouts.WriteImage(
            set: FrameSet,
            binding: DescriptorLayouts.BlueNoiseBinding,
            arrayElement: 0,
            image: tables.BlueNoise
        );
        // The renderer has one exposure buffer for its life, which every frame passes on to the next.
        layouts.WriteBuffer(set: FrameSet, binding: 11, type: DescriptorType.StorageBuffer, buffer: exposure);
        if (ShadowTiles is { } tiles)
            WriteShadowTiles(tiles);
    }

    /// <summary>
    /// Points this frame's sets at the current material buffer, the current images of the environment maps, the deform
    /// region and the deform store, and writes textures added since its last frame. A growth of the maps waited for the device, so no
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
            layouts.WriteImage(set: FrameSet, binding: 14, arrayElement: 0, image: environment.Irradiance);
            layouts.WriteImage(set: FrameSet, binding: 16, arrayElement: 0, image: environment.Distance);
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
            ulong vertexSize = (ulong)Unsafe.SizeOf<GpuVertex>();
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
        Motions.Dispose();

        Poses.Dispose();
        DeformJobs.Dispose();
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

    /// <summary>
    /// The most elements that a growth gives the buffer: for a storage buffer, which binds whole, as many as fit
    /// maxStorageBufferRange (docs/plans/skinning-and-morphs.md, "Portable Vulkan").
    /// </summary>
    private readonly int maxCapacity;

    public HostArray(VulkanDevice device, DeviceAllocator allocator, BufferUsageFlags usage, string name, int capacity)
    {
        this.device = device;
        this.allocator = allocator;
        this.usage = usage;
        this.name = name;
        maxCapacity =
            (usage & BufferUsageFlags.StorageBufferBit) != 0
                ? (int)Math.Min(device.Properties.Limits.MaxStorageBufferRange / (uint)sizeof(T), int.MaxValue)
                : int.MaxValue;
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
            int capacity = GrownCapacity(capacity: Capacity, needed: data.Length, maxCapacity: maxCapacity);
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

    /// <summary>
    /// The capacity that holds <paramref name="needed"/> elements: <paramref name="capacity"/> doubled until it does, but
    /// at most <paramref name="maxCapacity"/>, unless <paramref name="needed"/> passes it.
    /// </summary>
    internal static int GrownCapacity(int capacity, int needed, int maxCapacity)
    {
        long doubled = capacity;
        while (doubled < needed)
            doubled *= 2;
        return (int)Math.Max(needed, Math.Min(doubled, maxCapacity));
    }
}
