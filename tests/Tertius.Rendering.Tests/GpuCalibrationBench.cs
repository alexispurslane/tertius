using System.Diagnostics;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The walk of the texture probe: which texel corner each sample of each invocation reads
/// (docs/research/temporal-renderer/gpu-calibration.md, "The probes"). Sample s of invocation (x, y) reads the corner
/// (x * Stride + (s &amp; MaskX) * StepX, y * Stride + (s &gt;&gt; ShiftY) * StepY), wrapped at <see cref="Side"/>.
/// </summary>
internal readonly record struct TextureWalk(
    string Name,
    int Side,
    int GridX,
    int GridY,
    uint Samples,
    uint Stride,
    uint StepX,
    uint StepY,
    uint MaskX,
    uint ShiftY
)
{
    /// <summary>
    /// A walk over a small image that stays in the texture cache: 5 texels right and 3 down per sample, from the
    /// invocation's own texel.
    /// </summary>
    public static TextureWalk Cached(int side, int gridX, int gridY, uint samples) =>
        new(
            Name: "texture-cached",
            Side: side,
            GridX: gridX,
            GridY: gridY,
            Samples: samples,
            Stride: 1,
            StepX: 5,
            StepY: 3,
            MaskX: uint.MaxValue,
            ShiftY: 0
        );

    /// <summary>
    /// A walk over the corners of even coordinates of an image of <paramref name="side"/>, so that each texel feeds
    /// exactly one sample: an odd sample goes half the side right, and every two samples go 2 grid rows down.
    /// </summary>
    public static TextureWalk Streamed(int side, int gridY) =>
        new(
            Name: "texture-stream",
            Side: side,
            GridX: side / 4,
            GridY: gridY,
            Samples: (uint)(side / gridY),
            Stride: 2,
            StepX: (uint)(side / 2),
            StepY: (uint)(2 * gridY),
            MaskX: 1,
            ShiftY: 1
        );

    /// <summary>The corner that sample <paramref name="sample"/> of invocation (x, y) reads.</summary>
    public (int X, int Y) Corner(int x, int y, int sample) =>
        (
            (int)((uint)x * Stride + ((uint)sample & MaskX) * StepX) & (Side - 1),
            (int)((uint)y * Stride + ((uint)sample >> (int)ShiftY) * StepY) & (Side - 1)
        );
}

/// <summary>The probes' results computed on the CPU, by the steps that the shaders take.</summary>
internal static class CalibrationReference
{
    /// <summary>The FMA probe's a and b: the chains approach 1 by a ten-thousandth of their distance per step.</summary>
    public const float A = 0.9999f;

    public const float B = 0.0001f;

    /// <summary>The independent chains of one invocation: four vec4.</summary>
    public const int Chains = 16;

    /// <summary>
    /// The largest relative difference from <see cref="Fma"/> that the check accepts. One dropped pass of 8 steps moves a
    /// result by at least 3.7e-4 of it, and a driver that rounds each product before its add by at most 3.4e-5.
    /// </summary>
    public const float FmaTolerance = 1e-4f;

    /// <summary>The FMA probe's result for invocation <paramref name="id"/> after <paramref name="iterations"/> steps.</summary>
    public static float Fma(int id, uint iterations)
    {
        float start = 1f + (id & 1023) * (1f / 1024f);
        Span<float> components = stackalloc float[4];
        for (int chain = 0; chain < Chains; chain++)
        {
            float x = start + chain % 4 * 0.125f + chain / 4 * 0.5f;
            for (uint step = 0; step < iterations; step++)
                x = MathF.FusedMultiplyAdd(x, A, B);
            components[chain % 4] += x;
        }

        // The shader adds the four vec4 chains first, then the four components.
        return components[0] + components[1] + components[2] + components[3];
    }

    /// <summary>Channel <paramref name="channel"/> of texel (x, y): a hash from 64 to 255, so that a sample sums to at least 1.</summary>
    public static byte Texel(int x, int y, int channel)
    {
        uint h = unchecked(((uint)x * 0x9E3779B1u) ^ ((uint)y * 0x85EBCA77u));
        h ^= unchecked((uint)channel * 0xC2B2AE3Du);
        h ^= h >> 15;
        h = unchecked(h * 0x2C1B3C6Du);
        h ^= h >> 12;
        return (byte)(64 + h % 192);
    }

    /// <summary>The sum of the channels of a bilinear sample at a corner: each channel the mean of its four texels.</summary>
    public static double CornerSum(int x, int y, int side)
    {
        double sum = 0;
        for (int channel = 0; channel < 4; channel++)
        {
            int total = 0;
            for (int dy = -1; dy <= 0; dy++)
            for (int dx = -1; dx <= 0; dx++)
                total += Texel(x: (x + dx + side) % side, y: (y + dy + side) % side, channel: channel);
            sum += total / 4.0 / 255.0;
        }

        return sum;
    }

    /// <summary>The texture probe's result for invocation (x, y) of <paramref name="walk"/>.</summary>
    public static double TextureSum(TextureWalk walk, int x, int y)
    {
        double sum = 0;
        for (int sample = 0; sample < walk.Samples; sample++)
        {
            (int cornerX, int cornerY) = walk.Corner(x, y, sample);
            sum += CornerSum(cornerX, cornerY, walk.Side);
        }

        return sum;
    }

    /// <summary>
    /// The largest difference from <see cref="TextureSum"/> that the check accepts: far below the 1 that a dropped
    /// sample removes.
    /// </summary>
    public static double TextureTolerance(TextureWalk walk) => 0.25 + walk.Samples * 1e-3;

    /// <summary>The four words of element <paramref name="index"/> of the bandwidth probe's source.</summary>
    public static (uint X, uint Y, uint Z, uint W) Element(uint index) =>
        (index, unchecked(index * 0x9E3779B1u), index ^ 0xA5A5A5A5u, (index << 7) | (index >> 25));

    /// <summary>The bandwidth probe's result for invocation <paramref name="id"/> of <paramref name="invocations"/>.</summary>
    public static uint BandwidthSum(int id, int invocations, int elements)
    {
        uint sum = 0;
        for (int read = 0; read < elements / invocations; read++)
        {
            (uint x, uint y, uint z, uint w) = Element((uint)(id + read * invocations));
            sum = unchecked(sum + x + y + z + w);
        }

        return sum;
    }
}

/// <summary>
/// One dispatch of a calibration probe: its pipeline, its set and push constants, its work per dispatch in its unit,
/// and the check of its results against the CPU, which gives null or the first difference.
/// </summary>
internal sealed class CalibrationProbe
{
    public required string Name { get; init; }
    public required double Work { get; init; }
    public required string Unit { get; init; }
    public required Pipeline Pipeline { get; init; }
    public required DescriptorSet Set { get; init; }
    public required byte[] Push { get; init; }
    public required (uint X, uint Y) Groups { get; init; }
    public required GpuBuffer Results { get; init; }
    public GpuImage? Image { get; init; }
    public GpuBuffer? Source { get; init; }
    public required Func<string?> Check { get; init; }
}

/// <summary>
/// A headless device and the probes of GpuCalibrationTests, which records each dispatch alone and waits for it. It owns
/// every probe that it makes.
/// </summary>
internal sealed unsafe class CalibrationBench : IDisposable
{
    private readonly DeviceAllocator allocator;
    private readonly ShaderLibrary shaders;
    private readonly CommandPool pool;
    private readonly Fence fence;
    private readonly Sampler sampler;
    private readonly DescriptorSetLayout setLayout;
    private readonly PipelineLayout layout;
    private readonly DescriptorPool descriptors;
    private readonly QueryPool queries;
    private readonly List<CalibrationProbe> probes = [];
    private readonly BarrierBatch barriers = new();

    // Every probe's set: results at 0, the bandwidth source at 1, the image at 2. A shader declares what it reads.
    private const int MaxProbes = 8;

    public CalibrationBench(bool validation)
    {
        Device = VulkanDevice.Create(validation: validation, surfaceExtensions: [], createSurface: null);
        allocator = new DeviceAllocator(Device);
        shaders = new ShaderLibrary(Device, typeof(CalibrationBench).Assembly);
        Vk vk = Device.Vk;
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = Device.QueueFamily,
        };
        vk.CreateCommandPool(device: Device.Device, pCreateInfo: &poolInfo, pAllocator: null, pCommandPool: out pool)
            .Check("vkCreateCommandPool");
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        vk.CreateFence(device: Device.Device, pCreateInfo: &fenceInfo, pAllocator: null, pFence: out fence)
            .Check("vkCreateFence");
        sampler = CreateSampler();
        setLayout = CreateSetLayout();
        layout = CreatePipelineLayout();
        descriptors = CreateDescriptorPool();
        var queryInfo = new QueryPoolCreateInfo
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Timestamp,
            QueryCount = 2,
        };
        vk.CreateQueryPool(device: Device.Device, pCreateInfo: &queryInfo, pAllocator: null, pQueryPool: out queries)
            .Check("vkCreateQueryPool");
    }

    public VulkanDevice Device { get; }

    /// <summary>
    /// The FMA probe over <paramref name="invocations"/>, a multiple of 256, for <paramref name="iterations"/> steps, a
    /// multiple of 8, in the loop of eight steps per pass or, when <paramref name="rolled"/>, of one.
    /// </summary>
    public CalibrationProbe Fma(int invocations, uint iterations, bool rolled)
    {
        GpuBuffer results = CreateResults(invocations);
        byte[] push = Words(iterations, BitConverter.SingleToUInt32Bits(CalibrationReference.A));
        push = [.. push, .. BitConverter.GetBytes(CalibrationReference.B)];
        return Add(
            new CalibrationProbe
            {
                Name = rolled ? "fma-rolled" : "fma",
                Work = (double)invocations * iterations * CalibrationReference.Chains * 2,
                Unit = "FLOP",
                Pipeline = CreatePipeline(rolled ? "calibration_fma_rolled.comp" : "calibration_fma.comp"),
                Set = CreateSet(results, source: null, image: null),
                Push = push,
                Groups = ((uint)invocations / 256, 1),
                Results = results,
                Check = () =>
                {
                    Span<float> values = results.AsSpan<float>(invocations);
                    for (int id = 0; id < invocations; id += Math.Max(1, invocations / 1024))
                    {
                        float expected = CalibrationReference.Fma(id, iterations);
                        if (MathF.Abs(values[id] - expected) > CalibrationReference.FmaTolerance * MathF.Abs(expected))
                            return $"invocation {id}: {values[id]} against {expected} on the CPU";
                    }

                    return null;
                },
            }
        );
    }

    /// <summary>The texture probe of <paramref name="walk"/>, over an RGBA8 image of <see cref="CalibrationReference.Texel"/>.</summary>
    public CalibrationProbe Texture(TextureWalk walk)
    {
        int invocations = walk.GridX * walk.GridY;
        GpuBuffer results = CreateResults(invocations);
        GpuImage image = CreateTexture(walk.Side);
        return Add(
            new CalibrationProbe
            {
                Name = walk.Name,
                Work = (double)invocations * walk.Samples,
                Unit = "texel",
                Pipeline = CreatePipeline("calibration_texture.comp"),
                Set = CreateSet(results, source: null, image: image),
                Push = Words(walk.Samples, walk.Stride, walk.StepX, walk.StepY, walk.MaskX, walk.ShiftY),
                Groups = ((uint)walk.GridX / 16, (uint)walk.GridY / 16),
                Results = results,
                Image = image,
                Check = () =>
                {
                    Span<float> values = results.AsSpan<float>(invocations);
                    for (int id = 0; id < invocations; id += Math.Max(1, invocations / 1024))
                    {
                        double expected = CalibrationReference.TextureSum(walk, id % walk.GridX, id / walk.GridX);
                        if (Math.Abs(values[id] - expected) > CalibrationReference.TextureTolerance(walk))
                            return $"invocation {id}: {values[id]} against {expected} on the CPU";
                    }

                    return null;
                },
            }
        );
    }

    /// <summary>The bandwidth probe over a source of <paramref name="elements"/> of 16 bytes in GPU memory.</summary>
    public CalibrationProbe Bandwidth(int elements, int invocations)
    {
        GpuBuffer results = CreateResults(invocations);
        GpuBuffer source = CreateSource(elements);
        return Add(
            new CalibrationProbe
            {
                Name = "bandwidth",
                Work = elements * 16.0,
                Unit = "B",
                Pipeline = CreatePipeline("calibration_bandwidth.comp"),
                Set = CreateSet(results, source: source, image: null),
                Push = Words((uint)(elements / invocations)),
                Groups = ((uint)invocations / 256, 1),
                Results = results,
                Source = source,
                Check = () =>
                {
                    Span<uint> values = results.AsSpan<uint>(invocations);
                    for (int id = 0; id < invocations; id += Math.Max(1, invocations / 1024))
                    {
                        uint expected = CalibrationReference.BandwidthSum(id, invocations, elements);
                        if (values[id] != expected)
                            return $"invocation {id}: {values[id]} against {expected} on the CPU";
                    }

                    return null;
                },
            }
        );
    }

    /// <summary>Dispatches <paramref name="probe"/> once and waits for it.</summary>
    public void Run(CalibrationProbe probe) => Submit(commandBuffer => Record(commandBuffer, probe, stamped: false));

    /// <summary>
    /// Dispatches <paramref name="probe"/> once between two timestamps and waits for it. Gives the GPU time from stamp to
    /// stamp and the wall time from the submission to the end of the fence's wait, both in ms.
    /// </summary>
    public (double Gpu, double Wall) Time(CalibrationProbe probe)
    {
        double wall = Submit(commandBuffer => Record(commandBuffer, probe, stamped: true));
        Span<ulong> stamps = stackalloc ulong[2];
        fixed (ulong* data = stamps)
            Device
                .Vk.GetQueryPoolResults(
                    device: Device.Device,
                    queryPool: queries,
                    firstQuery: 0,
                    queryCount: 2,
                    dataSize: 2 * sizeof(ulong),
                    pData: data,
                    stride: sizeof(ulong),
                    flags: QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit
                )
                .Check("vkGetQueryPoolResults");
        return (Device.Timestamps.Milliseconds(stamps[0], stamps[1]), wall);
    }

    /// <summary>Dispatches <paramref name="probe"/> back to back, three submissions in flight, for <paramref name="seconds"/>.</summary>
    public void Sustain(CalibrationProbe probe, double seconds)
    {
        // One command buffer, recorded once and submitted again after each wait: three of them keep the queue full.
        Vk vk = Device.Vk;
        const int InFlight = 3;
        CommandBuffer* buffers = stackalloc CommandBuffer[InFlight];
        Fence* fences = stackalloc Fence[InFlight];
        var allocate = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = InFlight,
        };
        vk.AllocateCommandBuffers(Device.Device, &allocate, buffers).Check("vkAllocateCommandBuffers");
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };
        for (int slot = 0; slot < InFlight; slot++)
        {
            vk.CreateFence(device: Device.Device, pCreateInfo: &fenceInfo, pAllocator: null, pFence: out fences[slot])
                .Check("vkCreateFence");
            var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
            vk.BeginCommandBuffer(buffers[slot], &begin).Check("vkBeginCommandBuffer");
            Record(buffers[slot], probe, stamped: false);
            vk.EndCommandBuffer(buffers[slot]).Check("vkEndCommandBuffer");
        }

        long end = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
        for (int index = 0; Stopwatch.GetTimestamp() < end; index++)
        {
            int slot = index % InFlight;
            Fence wait = fences[slot];
            vk.WaitForFences(
                    device: Device.Device,
                    fenceCount: 1,
                    pFences: &wait,
                    waitAll: true,
                    timeout: ulong.MaxValue
                )
                .Check("vkWaitForFences");
            vk.ResetFences(Device.Device, 1, &wait).Check("vkResetFences");
            CommandBuffer commandBuffer = buffers[slot];
            var submit = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };
            vk.QueueSubmit(queue: Device.Queue, submitCount: 1, pSubmits: &submit, fence: wait).Check("vkQueueSubmit");
        }

        vk.WaitForFences(
                device: Device.Device,
                fenceCount: InFlight,
                pFences: fences,
                waitAll: true,
                timeout: ulong.MaxValue
            )
            .Check("vkWaitForFences");
        for (int slot = 0; slot < InFlight; slot++)
            vk.DestroyFence(Device.Device, fences[slot], null);
        vk.FreeCommandBuffers(
            device: Device.Device,
            commandPool: pool,
            commandBufferCount: InFlight,
            pCommandBuffers: buffers
        );
    }

    public void Dispose()
    {
        Device.WaitIdle();
        Vk vk = Device.Vk;
        foreach (CalibrationProbe probe in probes)
        {
            vk.DestroyPipeline(Device.Device, probe.Pipeline, null);
            probe.Results.Dispose();
            probe.Image?.Dispose();
            probe.Source?.Dispose();
        }

        vk.DestroyQueryPool(Device.Device, queries, null);
        vk.DestroyDescriptorPool(Device.Device, descriptors, null);
        vk.DestroyPipelineLayout(Device.Device, layout, null);
        vk.DestroyDescriptorSetLayout(Device.Device, setLayout, null);
        vk.DestroySampler(Device.Device, sampler, null);
        vk.DestroyFence(Device.Device, fence, null);
        vk.DestroyCommandPool(Device.Device, pool, null);
        shaders.Dispose();
        allocator.Dispose();
        Device.Dispose();
    }

    private CalibrationProbe Add(CalibrationProbe probe)
    {
        probes.Add(probe);
        return probe;
    }

    // The barriers make each dispatch wait for the last one's writes of the results, and the host read wait for both.
    private void Record(CommandBuffer commandBuffer, CalibrationProbe probe, bool stamped)
    {
        Vk vk = Device.Vk;
        if (probe.Image is { } image)
            barriers.Image(image, ImageUse.SampledCompute);
        if (probe.Source is { } source)
            barriers.Buffer(source, BufferUse.ComputeRead);
        barriers.Buffer(probe.Results, BufferUse.ComputeWrite);
        barriers.Flush(vk, commandBuffer);
        if (stamped)
        {
            vk.CmdResetQueryPool(commandBuffer: commandBuffer, queryPool: queries, firstQuery: 0, queryCount: 2);
            vk.CmdWriteTimestamp2(
                commandBuffer: commandBuffer,
                stage: PipelineStageFlags2.AllCommandsBit,
                queryPool: queries,
                query: 0
            );
        }

        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, probe.Pipeline);
        DescriptorSet set = probe.Set;
        vk.CmdBindDescriptorSets(
            commandBuffer: commandBuffer,
            pipelineBindPoint: PipelineBindPoint.Compute,
            layout: layout,
            firstSet: 0,
            descriptorSetCount: 1,
            pDescriptorSets: &set,
            dynamicOffsetCount: 0,
            pDynamicOffsets: null
        );
        fixed (byte* push = probe.Push)
            vk.CmdPushConstants(
                commandBuffer: commandBuffer,
                layout: layout,
                stageFlags: ShaderStageFlags.ComputeBit,
                offset: 0,
                size: (uint)probe.Push.Length,
                pValues: push
            );
        vk.CmdDispatch(
            commandBuffer: commandBuffer,
            groupCountX: probe.Groups.X,
            groupCountY: probe.Groups.Y,
            groupCountZ: 1
        );
        if (stamped)
            vk.CmdWriteTimestamp2(
                commandBuffer: commandBuffer,
                stage: PipelineStageFlags2.AllCommandsBit,
                queryPool: queries,
                query: 1
            );
        barriers.Buffer(probe.Results, BufferUse.HostRead);
        barriers.Flush(vk, commandBuffer);
    }

    /// <summary>Records into a new command buffer, submits it and waits for it. Gives the wall time of submit and wait in ms.</summary>
    private double Submit(Action<CommandBuffer> record)
    {
        Vk vk = Device.Vk;
        var allocate = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        vk.AllocateCommandBuffers(Device.Device, &allocate, out CommandBuffer commandBuffer)
            .Check("vkAllocateCommandBuffers");
        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        vk.BeginCommandBuffer(commandBuffer, &begin).Check("vkBeginCommandBuffer");
        record(commandBuffer);
        vk.EndCommandBuffer(commandBuffer).Check("vkEndCommandBuffer");
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
        };
        Fence wait = fence;
        long start = Stopwatch.GetTimestamp();
        vk.QueueSubmit(queue: Device.Queue, submitCount: 1, pSubmits: &submit, fence: wait).Check("vkQueueSubmit");
        vk.WaitForFences(device: Device.Device, fenceCount: 1, pFences: &wait, waitAll: true, timeout: ulong.MaxValue)
            .Check("vkWaitForFences");
        double wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        vk.ResetFences(Device.Device, 1, &wait).Check("vkResetFences");
        vk.FreeCommandBuffers(
            device: Device.Device,
            commandPool: pool,
            commandBufferCount: 1,
            pCommandBuffers: &commandBuffer
        );
        return wall;
    }

    private static byte[] Words(params uint[] words) => [.. words.SelectMany(BitConverter.GetBytes)];

    private GpuBuffer CreateResults(int invocations) =>
        GpuBuffer.Create(
            device: Device,
            allocator: allocator,
            size: (ulong)invocations * 4,
            usage: BufferUsageFlags.StorageBufferBit,
            memoryUsage: MemoryUsage.Readback,
            name: "calibration results"
        );

    /// <summary>A source in GPU memory of <see cref="CalibrationReference.Element"/>, copied from a staging buffer.</summary>
    private GpuBuffer CreateSource(int elements)
    {
        ulong bytes = (ulong)elements * 16;
        using var staging = GpuBuffer.Create(
            device: Device,
            allocator: allocator,
            size: bytes,
            usage: BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.Upload,
            name: "calibration source upload"
        );
        uint* words = (uint*)staging.Mapped;
        Parallel.For(
            0,
            elements,
            index =>
            {
                (uint x, uint y, uint z, uint w) = CalibrationReference.Element((uint)index);
                words[index * 4L] = x;
                words[index * 4L + 1] = y;
                words[index * 4L + 2] = z;
                words[index * 4L + 3] = w;
            }
        );
        var source = GpuBuffer.Create(
            device: Device,
            allocator: allocator,
            size: bytes,
            usage: BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "calibration source"
        );
        Submit(commandBuffer =>
        {
            barriers.Buffer(source, BufferUse.TransferDestination);
            barriers.Flush(Device.Vk, commandBuffer);
            var region = new BufferCopy(0, 0, bytes);
            Device.Vk.CmdCopyBuffer(
                commandBuffer: commandBuffer,
                srcBuffer: staging.Handle,
                dstBuffer: source.Handle,
                regionCount: 1,
                pRegions: &region
            );
        });
        return source;
    }

    /// <summary>A sampled RGBA8 image of <see cref="CalibrationReference.Texel"/>, copied from a staging buffer.</summary>
    private GpuImage CreateTexture(int side)
    {
        ulong bytes = (ulong)side * (ulong)side * 4;
        using var staging = GpuBuffer.Create(
            device: Device,
            allocator: allocator,
            size: bytes,
            usage: BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.Upload,
            name: "calibration texture upload"
        );
        byte* texels = staging.Mapped;
        Parallel.For(
            0,
            side,
            y =>
            {
                for (int x = 0; x < side; x++)
                for (int channel = 0; channel < 4; channel++)
                    texels[((long)y * side + x) * 4 + channel] = CalibrationReference.Texel(x, y, channel);
            }
        );
        var image = GpuImage.Create(
            device: Device,
            allocator: allocator,
            extent: new((uint)side, (uint)side),
            format: Format.R8G8B8A8Unorm,
            usage: ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            mipLevels: 1,
            name: "calibration texture"
        );
        Submit(commandBuffer =>
        {
            barriers.Image(image, ImageUse.TransferDestination, discard: true);
            barriers.Flush(Device.Vk, commandBuffer);
            var region = new BufferImageCopy
            {
                ImageSubresource = new() { AspectMask = ImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = new((uint)side, (uint)side, 1),
            };
            Buffer source = staging.Handle;
            Device.Vk.CmdCopyBufferToImage(
                commandBuffer: commandBuffer,
                srcBuffer: source,
                dstImage: image.Handle,
                dstImageLayout: ImageLayout.TransferDstOptimal,
                regionCount: 1,
                pRegions: &region
            );
        });
        return image;
    }

    private Pipeline CreatePipeline(string shader) =>
        new ComputePipelineDesc
        {
            Name = shader,
            Shader = shader,
            Layout = layout,
        }.Create(Device, shaders);

    private DescriptorSet CreateSet(GpuBuffer results, GpuBuffer? source, GpuImage? image)
    {
        Vk vk = Device.Vk;
        DescriptorSetLayout each = setLayout;
        var allocate = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptors,
            DescriptorSetCount = 1,
            PSetLayouts = &each,
        };
        vk.AllocateDescriptorSets(Device.Device, &allocate, out DescriptorSet set).Check("vkAllocateDescriptorSets");
        var resultsInfo = new DescriptorBufferInfo(results.Handle, 0, Vk.WholeSize);
        var sourceInfo = new DescriptorBufferInfo(source?.Handle ?? default, 0, Vk.WholeSize);
        var imageInfo = new DescriptorImageInfo(sampler, image?.View ?? default, ImageLayout.ShaderReadOnlyOptimal);
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[3];
        uint count = 0;
        writes[count++] = Write(set, binding: 0, DescriptorType.StorageBuffer) with { PBufferInfo = &resultsInfo };
        if (source is not null)
            writes[count++] = Write(set, binding: 1, DescriptorType.StorageBuffer) with { PBufferInfo = &sourceInfo };
        if (image is not null)
            writes[count++] = Write(set, binding: 2, DescriptorType.CombinedImageSampler) with
            {
                PImageInfo = &imageInfo,
            };
        vk.UpdateDescriptorSets(
            device: Device.Device,
            descriptorWriteCount: count,
            pDescriptorWrites: writes,
            descriptorCopyCount: 0,
            pDescriptorCopies: null
        );
        return set;
    }

    private static WriteDescriptorSet Write(DescriptorSet set, uint binding, DescriptorType type) =>
        new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DescriptorCount = 1,
            DescriptorType = type,
        };

    private Sampler CreateSampler()
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            MinLod = 0f,
            MaxLod = 0f,
        };
        Device
            .Vk.CreateSampler(device: Device.Device, pCreateInfo: &info, pAllocator: null, pSampler: out Sampler made)
            .Check("vkCreateSampler");
        return made;
    }

    private DescriptorSetLayout CreateSetLayout()
    {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[3];
        for (uint binding = 0; binding < 3; binding++)
            bindings[binding] = new()
            {
                Binding = binding,
                DescriptorType = binding == 2 ? DescriptorType.CombinedImageSampler : DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit,
            };
        var info = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings,
        };
        Device
            .Vk.CreateDescriptorSetLayout(
                device: Device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pSetLayout: out DescriptorSetLayout made
            )
            .Check("vkCreateDescriptorSetLayout");
        return made;
    }

    private PipelineLayout CreatePipelineLayout()
    {
        DescriptorSetLayout each = setLayout;
        var push = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, 32);
        var info = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &each,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };
        Device
            .Vk.CreatePipelineLayout(
                device: Device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pPipelineLayout: out PipelineLayout made
            )
            .Check("vkCreatePipelineLayout");
        return made;
    }

    private DescriptorPool CreateDescriptorPool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new(DescriptorType.StorageBuffer, 2 * MaxProbes);
        sizes[1] = new(DescriptorType.CombinedImageSampler, MaxProbes);
        var info = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = MaxProbes,
            PoolSizeCount = 2,
            PPoolSizes = sizes,
        };
        Device
            .Vk.CreateDescriptorPool(
                device: Device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pDescriptorPool: out DescriptorPool made
            )
            .Check("vkCreateDescriptorPool");
        return made;
    }
}
