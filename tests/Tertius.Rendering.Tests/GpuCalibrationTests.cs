using System.Globalization;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The calibration probes of the GPU (docs/research/temporal-renderer/gpu-calibration.md): compute shaders of a known
/// amount of work, which give the CPU's result on any Vulkan device, and the opt-in measurements of their GPU time on
/// the development Mac. The native Metal twins are in tools/gpu-calibration/probes.swift.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class GpuCalibrationTests
{
    // The variable that turns on the measurements, which run only in a Release build under the GPU lock (tools/README.md,
    // "GPU calibration").
    private const string TimingVariable = "TERTIUS_GPU_CALIBRATION";

    // The peaks of the 24-core Apple M1 Max, the GPU of the development Mac (gpu-calibration.md, "The peaks"): 3,072 FP32
    // lanes, 192 texture units, and Apple's memory bandwidth, at the GPU's highest clock of 1,296 MHz.
    private const double PeakFlops = 3072 * 2 * 1.296e9;
    private const double PeakTexels = 192 * 1.296e9;
    private const double PeakBytes = 400e9;

    [Fact]
    public void TheFmaProbeComputesEveryStepOfEveryChainAsTheCpuDoes()
    {
        using var bench = new CalibrationBench(validation: true);
        CalibrationProbe probe = bench.Fma(invocations: 4096, iterations: 256, rolled: false);

        bench.Run(probe);

        probe.Check().Should().BeNull();
        string.Join("\n", bench.Device.Log.Snapshot()).Should().BeEmpty();
    }

    [Fact]
    public void TheRolledFmaProbeComputesEveryStepOfEveryChainAsTheCpuDoes()
    {
        using var bench = new CalibrationBench(validation: true);
        CalibrationProbe probe = bench.Fma(invocations: 4096, iterations: 256, rolled: true);

        bench.Run(probe);

        probe.Check().Should().BeNull();
    }

    [Fact]
    public void TheCachedTextureProbeSumsEveryBilinearSampleAsTheCpuDoes()
    {
        using var bench = new CalibrationBench(validation: true);
        CalibrationProbe probe = bench.Texture(TextureWalk.Cached(side: 32, gridX: 64, gridY: 16, samples: 64));

        bench.Run(probe);

        probe.Check().Should().BeNull();
        string.Join("\n", bench.Device.Log.Snapshot()).Should().BeEmpty();
    }

    [Fact]
    public void TheStreamedTextureProbeSumsEveryBilinearSampleAsTheCpuDoes()
    {
        using var bench = new CalibrationBench(validation: true);
        CalibrationProbe probe = bench.Texture(TextureWalk.Streamed(side: 256, gridY: 16));

        bench.Run(probe);

        probe.Check().Should().BeNull();
    }

    [Fact]
    public void TheBandwidthProbeSumsEveryElementAsTheCpuDoes()
    {
        using var bench = new CalibrationBench(validation: true);
        CalibrationProbe probe = bench.Bandwidth(elements: 1 << 16, invocations: 4096);

        bench.Run(probe);

        probe.Check().Should().BeNull();
        string.Join("\n", bench.Device.Log.Snapshot()).Should().BeEmpty();
    }

    [Fact]
    public void EachProbeReportsItsGpuTimeAndItsShareOfThePeak()
    {
        // A measurement, not a check: it prints each probe's times at the sizes of the native probes, then fails if a
        // result differs from the CPU's.
        SkipUnlessMeasuring();
        using var bench = new CalibrationBench(validation: false);
        (CalibrationProbe Probe, double Peak)[] probes =
        [
            (bench.Fma(invocations: 1 << 20, iterations: 1024, rolled: false), PeakFlops),
            (bench.Fma(invocations: 1 << 20, iterations: 1024, rolled: true), PeakFlops),
            (bench.Texture(TextureWalk.Cached(side: 32, gridX: 4096, gridY: 256, samples: 512)), PeakTexels),
            (bench.Texture(TextureWalk.Streamed(side: 16384, gridY: 256)), PeakTexels),
            (bench.Bandwidth(elements: 1 << 26, invocations: 1 << 20), PeakBytes),
        ];
        foreach ((CalibrationProbe probe, double peak) in probes)
        {
            for (int warm = 0; warm < 5; warm++)
                bench.Time(probe);
            Report(
                bench: bench,
                probe: probe,
                peak: peak,
                label: "",
                times: TimedSpan.Run(() => Times(bench, probe, count: 30))
            );
        }

        probes.Select(each => each.Probe.Check()).Should().AllBeEquivalentTo<string?>(null);
    }

    [Fact]
    public void TheFmaProbeReportsEachDispatchFromTheFirstAfterIdle()
    {
        // A measurement: no warm-up, so the first dispatch is the first GPU work after the caller's idle wait.
        SkipUnlessMeasuring();
        using var bench = new CalibrationBench(validation: false);
        CalibrationProbe probe = bench.Fma(invocations: 1 << 20, iterations: 1024, rolled: false);

        Report(
            bench: bench,
            probe: probe,
            peak: PeakFlops,
            label: "-cold",
            times: TimedSpan.Run(() => Times(bench, probe, count: 30))
        );

        probe.Check().Should().BeNull();
    }

    [Fact]
    public void TheFmaProbeReportsItsTimeAfterSecondsOfFullLoad()
    {
        // A measurement: 8 s of back-to-back dispatches, then the timed ones.
        SkipUnlessMeasuring();
        using var bench = new CalibrationBench(validation: false);
        CalibrationProbe probe = bench.Fma(invocations: 1 << 20, iterations: 1024, rolled: false);

        bench.Sustain(probe, seconds: 8);
        Report(
            bench: bench,
            probe: probe,
            peak: PeakFlops,
            label: "-sustained",
            times: TimedSpan.Run(() => Times(bench, probe, count: 30))
        );

        probe.Check().Should().BeNull();
    }

    [Fact]
    public void FsrReportsItsGpuTimePerFrameWhenEightFramesShareASubmission()
    {
        // A measurement: FSR's frames from 1280 x 720 to 1920 x 1080 back to back on the GPU, eight per submission, so
        // that the GPU idles between frames as little as MetalFX's frames in tools/gpu-calibration/metalfx.swift do
        // (gpu-calibration.md, "MetalFX against FSR"). A frame's time is the span of its batch over 8, which holds
        // whenever MoltenVK latches a stamp.
        SkipUnlessMeasuring();
        const int Batch = 8;
        using var bench = new Fsr3UpscalerTests.Fsr3Bench(new(1280, 720), new(1920, 1080), validation: false);
        bench.FillPattern();

        // The first 8 batches warm the caches and the driver, as the 60 frames of the timing test do. The marker lines
        // around the timed batches let tools/gpu_clock.py take gpu-clock's clock over them alone.
        BatchedFrameTimes(bench: bench, firstBatch: 0, batches: 8, batchSize: Batch);
        double[] frames = TimedSpan.Run(() =>
            BatchedFrameTimes(bench: bench, firstBatch: 8, batches: 37, batchSize: Batch)
        );
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;

        double[] sorted = [.. frames.Order()];
        CultureInfo inv = CultureInfo.InvariantCulture;
        output.WriteLine(
            string.Create(
                inv,
                $"FSR 3.1 in batches of {Batch} from 1280 x 720 to 1920 x 1080 on {bench.Device.DeviceName}: median {sorted[sorted.Length / 2]:F3} ms, 10th percentile {sorted[sorted.Length / 10]:F3} ms, 90th percentile {sorted[sorted.Length * 9 / 10]:F3} ms over {sorted.Length} batches"
            )
        );
        output.WriteLine("frame times, ms: " + string.Join(' ', frames.Select(time => time.ToString("F4", inv))));
        sorted[0].Should().BePositive();
    }

    /// <summary>
    /// The mean GPU time in ms of a frame of FSR in each of <paramref name="batches"/> submissions of
    /// <paramref name="batchSize"/> frames, by the span of the batch's timestamps. The batches continue the bench's
    /// draws after <paramref name="firstBatch"/> batches, and only the first draw of all resets the accumulation.
    /// </summary>
    private static unsafe double[] BatchedFrameTimes(
        Fsr3UpscalerTests.Fsr3Bench bench,
        int firstBatch,
        int batches,
        int batchSize
    )
    {
        Vk vk = bench.Device.Vk;
        var info = new QueryPoolCreateInfo
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Timestamp,
            QueryCount = (uint)batchSize + 1,
        };
        vk.CreateQueryPool(
                device: bench.Device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pQueryPool: out QueryPool queries
            )
            .Check("vkCreateQueryPool");
        double[] times = new double[batches];
        Span<ulong> stamps = stackalloc ulong[batchSize + 1];
        for (int batch = 0; batch < batches; batch++)
        {
            bench.Submit(commandBuffer =>
            {
                vk.CmdResetQueryPool(
                    commandBuffer: commandBuffer,
                    queryPool: queries,
                    firstQuery: 0,
                    queryCount: (uint)batchSize + 1
                );
                for (int frame = 0; frame <= batchSize; frame++)
                {
                    vk.CmdWriteTimestamp2(
                        commandBuffer: commandBuffer,
                        stage: PipelineStageFlags2.AllCommandsBit,
                        queryPool: queries,
                        query: (uint)frame
                    );
                    int draw = (firstBatch + batch) * batchSize + frame;
                    if (frame < batchSize)
                        bench.Upscaler.Record(commandBuffer, bench.Barriers, bench.Inputs(draw, reset: draw == 0));
                }
            });
            fixed (ulong* data = stamps)
                vk.GetQueryPoolResults(
                        device: bench.Device.Device,
                        queryPool: queries,
                        firstQuery: 0,
                        queryCount: (uint)batchSize + 1,
                        dataSize: (nuint)((batchSize + 1) * sizeof(ulong)),
                        pData: data,
                        stride: sizeof(ulong),
                        flags: QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit
                    )
                    .Check("vkGetQueryPoolResults");
            times[batch] = bench.Device.Timestamps.Milliseconds(stamps[0], stamps[batchSize]) / batchSize;
        }

        vk.DestroyQueryPool(bench.Device.Device, queries, null);
        return times;
    }

    // The times of count dispatches of probe, one after another.
    private static (double Gpu, double Wall)[] Times(CalibrationBench bench, CalibrationProbe probe, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => bench.Time(probe))];

    private static void SkipUnlessMeasuring() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(TimingVariable) == "1",
            $"Set {TimingVariable}=1 to measure the probes."
        );

    // One line in the format of the native probes, which tools/gpu-calibration/analyze.py reads, then every time.
    private static void Report(
        CalibrationBench bench,
        CalibrationProbe probe,
        double peak,
        string label,
        (double Gpu, double Wall)[] times
    )
    {
        static double At(double[] sorted, double fraction) =>
            sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * fraction))];
        double[] gpu = [.. times.Select(time => time.Gpu).Order()];
        double[] wall = [.. times.Select(time => time.Wall).Order()];
        double median = At(gpu, 0.5);
        double rate = probe.Work / (median * 1e-3);
        CultureInfo inv = CultureInfo.InvariantCulture;
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        output.WriteLine(
            string.Create(
                inv,
                $"probe={probe.Name}{label} api=vulkan device=\"{bench.Device.DeviceName}\" dispatches={times.Length} median={median:F4} p10={At(gpu, 0.1):F4} p90={At(gpu, 0.9):F4} rate={rate:E4} {probe.Unit}/s share={rate / peak:F4} wall_median={At(wall, 0.5):F4}"
            )
        );
        output.WriteLine("times=" + string.Join(' ', times.Select(time => time.Gpu.ToString("F4", inv))));
        output.WriteLine("wall=" + string.Join(' ', times.Select(time => time.Wall.ToString("F4", inv))));
    }
}
