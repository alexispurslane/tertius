using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Times each pass of the frame graph on the GPU with one timestamp query pool per frame slot, and holds the timings of
/// the last frame whose GPU work finished (ARCHITECTURE.md, "GPU timings"). On a queue that writes no timestamps it
/// records no queries and holds no timings.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A slot's pool holds the stamps of the slot's last submitted frame until that slot records again.</item>
/// <item>The pass names of a slot are those of the frame that its pool holds.</item>
/// </list>
/// </remarks>
internal sealed unsafe class GpuTimer : IDisposable
{
    /// <summary>The most passes that one frame can time: a pool holds a stamp before them and one after each.</summary>
    public const int MaxPasses = 31;

    private const uint Queries = MaxPasses + 1;

    private readonly VulkanDevice device;
    private readonly QueryPool[] pools;
    private readonly string[][] passNames;
    private readonly int[] passCounts;
    private readonly ulong[] results = new ulong[2 * Queries];
    private readonly PassTiming[] timings = new PassTiming[MaxPasses];
    private int timedPasses;
    private long timedFrame = -1;

    public GpuTimer(VulkanDevice device, int framesInFlight)
    {
        this.device = device;
        int slots = device.Timestamps.Supported ? framesInFlight : 0;
        pools = new QueryPool[slots];
        passNames = new string[slots][];
        passCounts = new int[slots];
        var info = new QueryPoolCreateInfo
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Timestamp,
            QueryCount = Queries,
        };
        try
        {
            for (int slot = 0; slot < slots; slot++)
            {
                device
                    .Vk.CreateQueryPool(
                        device: device.Device,
                        pCreateInfo: &info,
                        pAllocator: null,
                        pQueryPool: out pools[slot]
                    )
                    .Check("vkCreateQueryPool");
                device.SetName(ObjectType.QueryPool, pools[slot].Handle, $"frame {slot} pass timestamps");
                passNames[slot] = new string[MaxPasses];
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>True when the queue writes timestamps, so the timer times the passes.</summary>
    public bool Supported => pools.Length > 0;

    /// <summary>
    /// The timings of the last frame whose GPU work finished, or null before one has. The passes are this timer's storage,
    /// which a later <see cref="Collect"/> of a newer frame rewrites in place.
    /// </summary>
    public GpuTimings? Latest => timedFrame < 0 ? null : new GpuTimings(timedFrame, timings.AsMemory(0, timedPasses));

    /// <summary>
    /// Reads the stamps of <paramref name="frame"/>, the last frame that <paramref name="slot"/> submitted, once its
    /// fence has signaled. Waits for nothing: a result that is not available leaves the timings of an earlier frame.
    /// </summary>
    public void Collect(int slot, long frame)
    {
        if (!Supported || frame <= timedFrame)
            return;
        int passes = passCounts[slot];
        Span<ulong> data = results.AsSpan(0, 2 * (passes + 1));
        fixed (ulong* p = data)
        {
            Result read = device.Vk.GetQueryPoolResults(
                device: device.Device,
                queryPool: pools[slot],
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

        if (!device.Timestamps.Fill(results: data, passes: passNames[slot].AsSpan(0, passes), timings: timings))
            return;
        timedPasses = passes;
        timedFrame = frame;
    }

    /// <summary>Resets the queries of <paramref name="slot"/> and writes the stamp before the first pass.</summary>
    public void Begin(CommandBuffer commandBuffer, int slot)
    {
        if (!Supported)
            return;
        device.Vk.CmdResetQueryPool(
            commandBuffer: commandBuffer,
            queryPool: pools[slot],
            firstQuery: 0,
            queryCount: Queries
        );
        passCounts[slot] = 0;
        Stamp(commandBuffer: commandBuffer, slot: slot, query: 0);
    }

    /// <summary>Writes the stamp after the pass named <paramref name="pass"/>, once all work recorded before it has finished.</summary>
    public void After(CommandBuffer commandBuffer, int slot, string pass)
    {
        if (!Supported)
            return;
        int count = passCounts[slot];
        passNames[slot][count] = pass;
        passCounts[slot] = count + 1;
        Stamp(commandBuffer: commandBuffer, slot: slot, query: (uint)(count + 1));
    }

    // All commands: a stamp waits for every command before it, so the stamps split the frame's GPU time between the passes.
    private void Stamp(CommandBuffer commandBuffer, int slot, uint query) =>
        device.Vk.CmdWriteTimestamp2(
            commandBuffer: commandBuffer,
            stage: PipelineStageFlags2.AllCommandsBit,
            queryPool: pools[slot],
            query: query
        );

    public void Dispose()
    {
        foreach (QueryPool pool in pools)
            if (pool.Handle != 0)
                device.Vk.DestroyQueryPool(device.Device, pool, null);
    }
}
