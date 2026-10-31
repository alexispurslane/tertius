using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The exposure buffer, the one store of the exposure in stops that the passes use, the histogram of the exposure's
/// meter, and one readback buffer per frame slot, through which the CPU reports the exposure of a finished frame
/// (docs/plans/auto-exposure.md, "The exposure on the GPU") and bounds the exposure of the frames after it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The exposure buffer holds the seed, or what the last adapt step wrote. The CPU never maps it.</item>
/// <item>The histogram holds the counts of the last metered frame.</item>
/// <item>A slot's readback buffer holds the exposure that the slot's last drawn frame used, once its fence signals.</item>
/// <item>A slot's step is the step of the adapt step of the slot's last recorded frame.</item>
/// </list>
/// </remarks>
internal sealed unsafe class ExposureResources : IDisposable
{
    /// <summary>The size of the exposure buffer: <c>exposureStops</c> at offset 0, and padding to 16 bytes.</summary>
    private const ulong ExposureBytes = 16;

    /// <summary>The size of the histogram: one <c>uint</c> count per bin.</summary>
    private const ulong HistogramBytes = ExposureMeter.Bins * sizeof(uint);

    private readonly VulkanDevice device;
    private readonly ExposureSetting setting;
    private readonly GpuBuffer[] readbacks;

    // The last frame that each slot recorded, and the step in s by which that frame's adapt step moves the exposure.
    private readonly (long Frame, float Step)[] steps;

    public ExposureResources(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        ExposureSetting setting,
        int framesInFlight
    )
    {
        this.device = device;
        this.setting = setting;
        steps = new (long Frame, float Step)[framesInFlight];
        Array.Fill(steps, (Frame: -1, Step: 0));
        const BufferUsageFlags storage =
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit;
        Buffer = GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: ExposureBytes,
            usage: storage,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "exposure"
        );
        readbacks = new GpuBuffer[framesInFlight];
        try
        {
            Histogram = GpuBuffer.Create(
                device: device,
                allocator: allocator,
                size: HistogramBytes,
                usage: storage,
                memoryUsage: MemoryUsage.GpuOnly,
                name: "exposure histogram"
            );
            for (int slot = 0; slot < framesInFlight; slot++)
                readbacks[slot] = GpuBuffer.Create(
                    device: device,
                    allocator: allocator,
                    size: ExposureBytes,
                    usage: BufferUsageFlags.TransferDstBit,
                    memoryUsage: MemoryUsage.Readback,
                    name: $"frame {slot} exposure readback"
                );
        }
        catch
        {
            Dispose();
            throw;
        }

        Set = layouts.AllocateExposureSet();
        layouts.WriteBuffer(set: Set, binding: 0, type: DescriptorType.StorageBuffer, buffer: Histogram);
        layouts.WriteBuffer(set: Set, binding: 1, type: DescriptorType.StorageBuffer, buffer: Buffer);
    }

    /// <summary>The exposure buffer: <c>exposureStops</c> of frame.glsl, at binding 11 of every frame set.</summary>
    public GpuBuffer Buffer { get; }

    /// <summary>The histogram: 256 <c>uint</c> counts of the surface pixels of the last metered frame.</summary>
    public GpuBuffer Histogram { get; }

    /// <summary>Set 1 of <see cref="DescriptorLayouts.ExposureLayout"/>: the histogram at binding 0 and the exposure buffer at binding 1.</summary>
    public DescriptorSet Set { get; }

    /// <summary>The exposure of the last frame whose GPU work finished, or null before one has.</summary>
    public ExposureReading? Latest { get; private set; }

    /// <summary>Writes <paramref name="stops"/> into the exposure buffer, in the first drawn frame.</summary>
    public void RecordSeed(CommandBuffer commandBuffer, BarrierBatch barriers, float stops)
    {
        barriers.Buffer(Buffer, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        device.Vk.CmdFillBuffer(
            commandBuffer: commandBuffer,
            dstBuffer: Buffer.Handle,
            dstOffset: 0,
            size: ExposureBytes,
            data: BitConverter.SingleToUInt32Bits(stops)
        );
    }

    /// <summary>
    /// The highest exposure, in stops, that <paramref name="frame"/> uses or its adapt step writes after a step of
    /// <paramref name="step"/> s: <see cref="Latest"/>, or the seed before any reading, raised as far as the adapt steps
    /// of the frames since and of this frame can raise it (<see cref="ExposureMeter.HighestStops"/>).
    /// </summary>
    public float HighestStops(long frame, float step)
    {
        ExposureReading known = Latest ?? new(Frame: 0, Stops: setting.InitialStops);
        // Every frame in flight holds a slot, so at most a slot's worth of frames lies between the reading and this one.
        Span<float> since = stackalloc float[steps.Length + 1];
        int count = 0;
        for (long earlier = known.Frame; earlier < frame; earlier++)
        {
            (long Frame, float Step) recorded = steps[earlier % steps.Length];
            Contract.Assert(recorded.Frame == earlier, "a frame after the reading still holds its slot");
            since[count++] = recorded.Step;
        }

        since[count++] = step;
        return ExposureMeter.HighestStops(setting: setting, knownStops: known.Stops, steps: since[..count]);
    }

    /// <summary>
    /// Copies the exposure that this frame's passes use into the readback buffer of <paramref name="slot"/>, and keeps the
    /// <paramref name="step"/> in s of the frame's adapt step for <see cref="HighestStops"/>.
    /// </summary>
    public void RecordReadback(CommandBuffer commandBuffer, BarrierBatch barriers, int slot, long frame, float step)
    {
        steps[slot] = (frame, step);
        GpuBuffer readback = readbacks[slot];
        barriers.Buffer(Buffer, BufferUse.TransferSource);
        barriers.Buffer(readback, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var region = new BufferCopy(srcOffset: 0, dstOffset: 0, size: ExposureBytes);
        device.Vk.CmdCopyBuffer(
            commandBuffer: commandBuffer,
            srcBuffer: Buffer.Handle,
            dstBuffer: readback.Handle,
            regionCount: 1,
            pRegions: &region
        );

        // Left in the batch: the first pass's flush records it with its own barriers, before the frame's fence.
        barriers.Buffer(readback, BufferUse.HostRead);
    }

    /// <summary>
    /// Reads the exposure of <paramref name="frame"/>, the last frame that <paramref name="slot"/> drew, once its fence
    /// has signaled. A slot that has drawn no frame, or whose frame is not newer than <see cref="Latest"/>'s, leaves
    /// <see cref="Latest"/> as it is.
    /// </summary>
    public void Collect(int slot, long frame)
    {
        if (frame < 0)
            return;
        // A capture collects its own frame, so the wait on an older slot after it must not take the reading back.
        if (Latest is { Frame: var latestFrame } && frame <= latestFrame)
            return;
        Latest = new(Frame: frame, Stops: readbacks[slot].AsSpan<float>(1)[0]);
    }

    public void Dispose()
    {
        foreach (GpuBuffer? readback in readbacks)
            readback?.Dispose();
        Histogram?.Dispose();
        Buffer.Dispose();
    }
}
