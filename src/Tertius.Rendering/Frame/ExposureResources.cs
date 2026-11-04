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
/// <item>The steps hold the step of the adapt step of each of the last FramesInFlight + 1 drawn frames.</item>
/// </list>
/// Invariants of <see cref="Latest"/> and the steps, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>An entry of the steps holds no frame, or a frame whose number modulo the entry count is the entry's index.</item>
/// <item><see cref="Latest"/> is of a drawn frame, no newer than the newest drawn frame.</item>
/// <item>Every frame from <see cref="Latest"/>'s, or frame 0 before a reading, to the newest drawn frame holds its entry.</item>
/// </list>
/// </remarks>
internal sealed unsafe class ExposureResources : IDisposable
{
    /// <summary>
    /// The size of the exposure buffer: <c>exposureStops</c> at offset 0, <c>previousStops</c> at offset 4, the stops that
    /// the frame before used, and padding to 16 bytes.
    /// </summary>
    private const ulong ExposureBytes = 16;

    /// <summary>The size of the histogram: one <c>uint</c> count per bin.</summary>
    private const ulong HistogramBytes = ExposureMeter.Bins * sizeof(uint);

    private readonly VulkanDevice device;
    private readonly ExposureSetting setting;
    private readonly GpuBuffer[] readbacks;

    // The step in s by which a drawn frame's adapt step moves the exposure, at the entry of its number modulo the entry
    // count. Frame N is prepared after the slot of frame N − FramesInFlight finished, so the reading is of that frame or a
    // newer one, and FramesInFlight + 1 entries hold every frame from the reading to frame N once frame N is drawn.
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
        steps = new (long Frame, float Step)[framesInFlight + 1];
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

    /// <summary>Writes the setting's initial stops into the exposure buffer, in the first drawn frame.</summary>
    public void RecordSeed(CommandBuffer commandBuffer, BarrierBatch barriers)
    {
        barriers.Buffer(Buffer, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        device.Vk.CmdFillBuffer(
            commandBuffer: commandBuffer,
            dstBuffer: Buffer.Handle,
            dstOffset: 0,
            size: ExposureBytes,
            data: BitConverter.SingleToUInt32Bits(setting.InitialStops)
        );
    }

    /// <summary>
    /// The highest exposure, in stops, that <paramref name="frame"/>, the frame after the newest drawn one, uses or its
    /// adapt step writes after a step of <paramref name="step"/> s: <see cref="Latest"/>, or the seed before any reading,
    /// raised as far as the adapt steps of the frames since and of this frame can raise it
    /// (<see cref="ExposureMeter.HighestStops"/>).
    /// </summary>
    public float HighestStops(long frame, float step)
    {
        ExposureReading known = Known;
        Contract.Requires(frame == NewestDrawn + 1 && frame - known.Frame < steps.Length);
        Span<float> since = stackalloc float[steps.Length];
        int count = 0;
        for (long earlier = known.Frame; earlier < frame; earlier++)
        {
            (long Frame, float Step) recorded = steps[Entry(earlier)];
            Contract.Assert(recorded.Frame == earlier, "a frame after the reading still holds its entry");
            since[count++] = recorded.Step;
        }

        since[count++] = step;
        return ExposureMeter.HighestStops(setting: setting, knownStops: known.Stops, steps: since[..count]);
    }

    /// <summary>Copies the exposure that this frame's passes use into the readback buffer of <paramref name="slot"/>.</summary>
    public void RecordReadback(CommandBuffer commandBuffer, BarrierBatch barriers, int slot)
    {
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
    /// Keeps the <paramref name="step"/> in s of the adapt step of <paramref name="frame"/>, the frame after the newest
    /// drawn one, for <see cref="HighestStops"/>, once the frame is submitted.
    /// </summary>
    public void Drew(long frame, float step)
    {
        Contract.Requires(frame == NewestDrawn + 1 && frame - Known.Frame < steps.Length);
        steps[Entry(frame)] = (frame, step);
        Contract.Ensures(NewestDrawn == frame);
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
        Contract.Requires(steps[Entry(frame)].Frame == frame, "the frame still holds its entry");
        Latest = new(Frame: frame, Stops: readbacks[slot].AsSpan<float>(1)[0]);
    }

    /// <summary>Checks the invariants of <see cref="Latest"/> and the steps that the remarks list.</summary>
    internal void AssertInvariants()
    {
        for (int entry = 0; entry < steps.Length; entry++)
            Contract.Invariant(steps[entry].Frame < 0 || Entry(steps[entry].Frame) == entry);

        long newest = NewestDrawn;
        if (Latest is { Frame: var latestFrame })
            Contract.Invariant(latestFrame >= 0 && latestFrame <= newest);
        for (long frame = Known.Frame; frame <= newest; frame++)
            Contract.Invariant(steps[Entry(frame)].Frame == frame);
    }

    /// <summary>The exposure from which <see cref="HighestStops"/> starts: <see cref="Latest"/>, or the seed at frame 0.</summary>
    private ExposureReading Known => Latest ?? new(Frame: 0, Stops: setting.InitialStops);

    /// <summary>The newest frame that <see cref="Drew"/> kept, or −1 before any.</summary>
    private long NewestDrawn
    {
        get
        {
            long newest = -1;
            foreach ((long frame, _) in steps)
                newest = Math.Max(newest, frame);
            return newest;
        }
    }

    /// <summary>The entry of the steps that holds <paramref name="frame"/>.</summary>
    private int Entry(long frame) => (int)(frame % steps.Length);

    public void Dispose()
    {
        foreach (GpuBuffer? readback in readbacks)
            readback?.Dispose();
        Histogram?.Dispose();
        Buffer.Dispose();
    }
}
