using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Times each pass of the frame graph, or each cost group of the render scale without <see cref="EachPass"/>, on the GPU
/// with one timestamp query pool per frame slot, and holds the timings of
/// the last frame whose GPU work finished (ARCHITECTURE.md, "GPU timings"). On a queue that writes no timestamps it
/// records no queries and holds no timings.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A slot's pool holds the stamps of the slot's last submitted frame until that slot records again.</item>
/// <item>
/// The pass names of a slot are those of its last recording. They match the stamps in the pool once that recording's
/// frame is submitted.
/// </item>
/// </list>
/// </remarks>
internal sealed unsafe class GpuTimer : IDisposable
{
    /// <summary>The most passes that one frame can time: a pool holds a stamp before them and one after each.</summary>
    public const int MaxPasses = 40;

    private const uint Queries = MaxPasses + 1;

    private readonly VulkanDevice device;
    private readonly SlotQueries[] slots;
    private readonly ulong[] results = new ulong[2 * Queries];
    private readonly PassTiming[] timings = new PassTiming[MaxPasses];

    /// <param name="eachPass">
    /// Whether the graph stamps after every pass, or only where a cost group of the render scale ends.
    /// </param>
    public GpuTimer(VulkanDevice device, int framesInFlight, bool eachPass)
    {
        this.device = device;
        EachPass = eachPass;
        int count = device.Timestamps.Supported ? framesInFlight : 0;
        var made = new List<SlotQueries>(count);
        var info = new QueryPoolCreateInfo
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Timestamp,
            QueryCount = Queries,
        };
        try
        {
            for (int slot = 0; slot < count; slot++)
            {
                device
                    .Vk.CreateQueryPool(
                        device: device.Device,
                        pCreateInfo: &info,
                        pAllocator: null,
                        pQueryPool: out QueryPool pool
                    )
                    .Check("vkCreateQueryPool");
                made.Add(new(pool));
                device.SetName(ObjectType.QueryPool, pool.Handle, $"frame {slot} pass timestamps");
            }
        }
        catch
        {
            Destroy(made);
            throw;
        }

        slots = [.. made];
    }

    /// <summary>True when the queue writes timestamps, so the timer times the passes.</summary>
    public bool Supported => slots.Length > 0;

    /// <summary>
    /// Whether <see cref="FrameGraph.Record"/> stamps after every pass (RendererOptions.PassTimestamps), or only after the
    /// last pass of each cost group of the render scale, so that each timing holds a group.
    /// </summary>
    public bool EachPass { get; }

    /// <summary>
    /// The timings of the last frame whose GPU work finished, or null before one has. The passes are this timer's storage,
    /// which a later <see cref="Collect"/> of a newer frame rewrites in place.
    /// </summary>
    public GpuTimings? Latest { get; private set; }

    /// <summary>
    /// Reads the stamps of <paramref name="frame"/>, the last frame that <paramref name="slot"/> submitted, once its
    /// fence has signaled. Waits for nothing: a result that is not available leaves the timings of an earlier frame.
    /// </summary>
    public void Collect(int slot, long frame)
    {
        if (!Supported)
            return;
        // A slot that has not submitted a frame yet holds a pool that no frame reset, and Vulkan forbids reading it
        // (VUID-vkGetQueryPoolResults-None-09401).
        if (frame < 0)
            return;
        // A frame that the output skipped leaves its slot's frame as it was, so the next render collects it again.
        if (Latest is { Frame: var latestFrame } && frame <= latestFrame)
            return;
        SlotQueries queries = slots[slot];
        int passes = queries.Passes;
        Span<ulong> data = results.AsSpan(0, 2 * (passes + 1));
        fixed (ulong* p = data)
        {
            Result read = device.Vk.GetQueryPoolResults(
                device: device.Device,
                queryPool: queries.Pool,
                firstQuery: 0,
                queryCount: (uint)(passes + 1),
                dataSize: (nuint)(data.Length * sizeof(ulong)),
                pData: p,
                stride: 2 * sizeof(ulong),
                flags: QueryResultFlags.Result64Bit | QueryResultFlags.ResultWithAvailabilityBit
            );
            if (read == Result.NotReady)
                return;
            read.Check("vkGetQueryPoolResults");
        }

        if (
            device.Timestamps.Fill(results: data, passes: queries.Descriptions.AsSpan(0, passes), timings: timings)
            is not { } span
        )
            return;
        Latest = new GpuTimings(Frame: frame, Passes: timings.AsMemory(0, passes), SpanMilliseconds: span);
    }

    /// <summary>Resets the queries of <paramref name="slot"/> and writes the stamp before the first pass.</summary>
    public void Begin(CommandBuffer commandBuffer, int slot)
    {
        if (!Supported)
            return;
        device.Vk.CmdResetQueryPool(
            commandBuffer: commandBuffer,
            queryPool: slots[slot].Pool,
            firstQuery: 0,
            queryCount: Queries
        );
        slots[slot].Passes = 0;
        Stamp(commandBuffer: commandBuffer, slot: slot, query: 0);
    }

    /// <summary>Writes the stamp after <paramref name="pass"/>, once all work recorded before it has finished.</summary>
    public void After(CommandBuffer commandBuffer, int slot, PassTiming pass)
    {
        if (!Supported)
            return;
        SlotQueries queries = slots[slot];
        int count = queries.Passes;
        queries.Descriptions[count] = pass;
        queries.Passes = count + 1;
        Stamp(commandBuffer: commandBuffer, slot: slot, query: (uint)(count + 1));
    }

    // All commands: a stamp waits for every command before it, so the stamps split the frame's GPU time between the passes.
    private void Stamp(CommandBuffer commandBuffer, int slot, uint query) =>
        device.Vk.CmdWriteTimestamp2(
            commandBuffer: commandBuffer,
            stage: PipelineStageFlags2.AllCommandsBit,
            queryPool: slots[slot].Pool,
            query: query
        );

    public void Dispose() => Destroy(slots);

    private void Destroy(IReadOnlyList<SlotQueries> made)
    {
        foreach (SlotQueries queries in made)
            device.Vk.DestroyQueryPool(device.Device, queries.Pool, null);
    }

    /// <summary>The timestamp pool of one frame slot, and the passes of the slot's last recording.</summary>
    private sealed class SlotQueries(QueryPool pool)
    {
        public QueryPool Pool { get; } = pool;

        /// <summary>The label and stage of each pass of the last recording, in recording order. The first <see cref="Passes"/> count.</summary>
        public PassTiming[] Descriptions { get; } = new PassTiming[MaxPasses];

        public int Passes { get; set; }
    }
}
