using System.Diagnostics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Tertius.Frames.Rails;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of the puffs in the sync point (ter-4zt6, task 3, docs/plans/particles.md, "Task 3"), on one thread, in a World
/// without physics, between ticks. Three frames:
/// <list type="bullet">
/// <item><c>earth</c>: a root frame with Earth's point field, no turn and no medium. The puffs lie 6,800 km from its origin.</item>
/// <item><c>earth-surface</c>: a frame in <c>earth</c> with the same point field, which turns with Earth at 7.29e-5 rad/s and
/// holds Earth's planet medium. The puffs lie 100 m above Earth's radius, so the medium fills their place.</item>
/// <item><c>station</c>: a frame without a field that spins at 0.31321 rad/s, with no medium. The puffs lie 8 m from its axis.</item>
/// </list>
/// <see cref="Step"/> times <see cref="PuffTable.Step"/> alone over <see cref="Puffs"/> puffs that never die. Every 600 calls it
/// puts the rows back where they started, so that the puffs stay near their places. <see cref="StepAndBirths"/> times
/// <see cref="PuffTable.Step"/> and <see cref="PuffTable.AddNewborns"/> of one sync point in steady state: an emitter makes 64
/// puffs per step, and their life holds <see cref="Puffs"/> of them alive, so each call steps them, ends 64, makes 64 and
/// rebuilds the ranges. Its time less that of <see cref="Step"/> is 64 births and one rebuild of the live puffs.
/// The cleanup prints the bytes of the table's arrays.
/// <para>
/// <see cref="Workers"/> (ter-fwsy) is the measurement behind <see cref="PuffTable.DefaultStepParallelThreshold"/>, outside
/// BenchmarkDotNet, as <see cref="RowCopyBenchmarks"/> measures the row copy: the step of the steady table of
/// <see cref="StepAndBirths"/>, whose oldest puffs die in every step, on the sync thread and on all workers, in a tick-like
/// setting.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class PuffTableBenchmarks
{
    private const double EarthMu = 3.986004418e14;
    private const int NewbornsPerStep = 64;

    private static readonly ParallelOptions AllWorkers = new() { MaxDegreeOfParallelism = -1 };

    [Params(100, 760, 1024, 1440, 4096, 16384)]
    public int Puffs;

    [Params("earth", "earth-surface", "station")]
    public string Frame = "earth";

    // The threads of the world: 1, or all of them (-1) with the step's threshold at 1, for Workers.
    private int threads = 1;

    // Whether Workers times the steady table of StepAndBirths, whose puffs die, or the table of Step, whose puffs never die.
    private bool dying;

    private World world = null!;
    private PuffTable table = null!;
    private long tick;
    private int calls;
    private Puff[] start = [];

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "rows")]
    private static extern ref Puff[] RowsOf(PuffTable table);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "spare")]
    private static extern ref Puff[] SpareOf(PuffTable table);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "newborns")]
    private static extern ref Puff[] NewbornsOf(PuffTable table);

    private static PuffLook Look(float life) =>
        new(Life: life, StartRadius: 0.2f, EndRadius: 1, TemperatureRatio: 3, Shading: PuffShading.Glow(new(4, 3, 2)));

    /// <summary>The world, its frame of the puffs and the place of the emitter in it.</summary>
    private (FrameHandle Frame, Double3 Place) Scene(int capacity)
    {
        world = new(new() { MaxDegreeOfParallelism = threads, PuffCapacity = capacity });
        switch (Frame)
        {
            case "earth":
                return (
                    world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(EarthMu))),
                    new(6.8e6, 0, 0)
                );
            case "earth-surface":
            {
                FrameHandle earth = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(EarthMu)));
                var spin = new FrameSpin(Tilt: DQuaternion.Identity, Rate: 7.2921e-5);
                return (
                    Spinning(
                        earth,
                        spin,
                        new(
                            FrameBounds.Sphere(5e7f),
                            GravityField.Point(EarthMu),
                            MediumField.Planet(radius: 6.371e6, surfaceDensity: 1.225f, scaleHeight: 8500, drag: 2)
                        )
                    ),
                    new(6.371e6 + 100, 0, 0)
                );
            }
            default:
            {
                FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
                return (
                    Spinning(space, new(Tilt: DQuaternion.Identity, Rate: 0.31321), new(FrameBounds.Sphere(150))),
                    new(8, 0, 0)
                );
            }
        }
    }

    /// <summary>A frame that turns in place at the origin of <paramref name="parent"/> by its rails, from its first step on.</summary>
    private FrameHandle Spinning(FrameHandle parent, FrameSpin spin, FrameSpec spec) =>
        world.GetOwnedFrame(
            world.Spawn(
                new()
                {
                    Frame = parent,
                    Pose = new(Double3.Zero, spin.RotationAt(world.Time)),
                    AsFrame = spec,
                    FirstMove = new(spin.Sample(Double3.Zero, world.TimeAt(world.TickNumber + 1))),
                    Components = [new SpinMotion { Spin = spin }],
                }
            )
        );

    private void SpawnEmitter(FrameHandle frame, Double3 place, float rate, float life) =>
        world.Spawn(
            new()
            {
                Frame = frame,
                Pose = new(place, DQuaternion.Identity),
                Render = new RenderData { Emitter = new(Rate: rate, Speed: 10, Spread: 1, Look: Look(life)) },
            }
        );

    [GlobalSetup(Target = nameof(Step))]
    public void SetupStep()
    {
        (FrameHandle frame, Double3 place) = Scene(Puffs);
        world.Tick();
        // One step of an emitter at Puffs puffs per step fills the table.
        SpawnEmitter(frame: frame, place: place, rate: Puffs * 60.5f, life: 1e6f);
        world.Tick();
        table = world.Puffs;
        if (table.Count != Puffs)
            throw new InvalidOperationException($"The table holds {table.Count} puffs, not {Puffs}.");
        start = table.View.Rows.ToArray();
        tick = world.TickNumber + 1;
    }

    [GlobalSetup(Target = nameof(StepAndBirths))]
    public void SetupBirths()
    {
        (FrameHandle frame, Double3 place) = Scene(capacity: 1 << 20);
        world.Tick();
        // 64 puffs per step that live Puffs / 64 steps.
        SpawnEmitter(frame: frame, place: place, rate: NewbornsPerStep * 60, life: Puffs / (float)NewbornsPerStep / 60);
        for (int i = 0; i < 3 + Puffs / NewbornsPerStep; i++)
            world.Tick();
        table = world.Puffs;
        tick = world.TickNumber + 1;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        long bytes =
            (RowsOf(table).LongLength + SpareOf(table).LongLength + NewbornsOf(table).LongLength)
            * Unsafe.SizeOf<Puff>();
        Console.WriteLine(
            $"// {Frame}, {Puffs}: {table.Count} live puffs in {table.View.Ranges.Length} range(s), {table.Looks.InUse} look(s); rows, spare and newborns {bytes} bytes"
        );
        world.Dispose();
    }

    [Benchmark]
    public void Step()
    {
        if (++calls % 600 == 0)
            start.CopyTo(RowsOf(table), 0);
        table.Step(tick++, world.DeltaTime);
    }

    [Benchmark]
    public void StepAndBirths()
    {
        long now = tick++;
        table.Step(now, world.DeltaTime);
        table.AddNewborns(now, world.DeltaTime);
    }

    /// <summary>
    /// One step as in a tick, in us: a gameplay loop on all workers, a spin of 200 us that stands for the sync steps before
    /// 2b, in which idle workers start to sleep, then the timed step. In the table of <see cref="StepAndBirths"/>, the 64
    /// oldest puffs die at the first rows of the step, so the join moves every slice after the first, and the births that
    /// keep the count steady follow, untimed. In the table of <see cref="Step"/> no puff dies, so the join moves nothing.
    /// </summary>
    private double TimedStep()
    {
        Parallel.For(fromInclusive: 0, toExclusive: 64, parallelOptions: AllWorkers, body: _ => Thread.SpinWait(200));
        long spinEnd = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5000;
        while (Stopwatch.GetTimestamp() < spinEnd) { }
        long now = tick;
        long begin = Stopwatch.GetTimestamp();
        if (dying)
            table.Step(tick++, world.DeltaTime);
        else
            Step();
        long stepEnd = Stopwatch.GetTimestamp();
        if (dying)
            table.AddNewborns(now, world.DeltaTime);
        return (stepEnd - begin) * 1e6 / Stopwatch.Frequency;
    }

    /// <summary>
    /// A set-up table of one variant of <see cref="Workers"/>: on one thread, or on all workers with the step's threshold
    /// at 1, and the steady table of <see cref="StepAndBirths"/> or the table of <see cref="Step"/>.
    /// </summary>
    private static PuffTableBenchmarks Variant(int puffs, string frame, bool onWorkers, bool dying)
    {
        var bench = new PuffTableBenchmarks
        {
            Puffs = puffs,
            Frame = frame,
            threads = onWorkers ? -1 : 1,
            dying = dying,
        };
        if (dying)
            bench.SetupBirths();
        else
            bench.SetupStep();
        if (onWorkers)
            bench.table.StepParallelThreshold = 1;
        return bench;
    }

    /// <summary>
    /// The step of <paramref name="puffs"/> live puffs in <paramref name="frame"/> on the sync thread and on all workers, in
    /// the steady table of <see cref="StepAndBirths"/>, 64 of whose puffs die in each step, and in the table of
    /// <see cref="Step"/>, in which none dies. A round times 40 steps of each of the four variants after 10 that warm up and
    /// takes their median. The variants take turns, and their order turns from round to round, so that a burst of load falls
    /// on each. Run it with <c>--puffstep &lt;puffs&gt; &lt;frame&gt; &lt;rounds&gt;</c>.
    /// </summary>
    public static void Workers(int puffs, string frame, int rounds)
    {
        (string Name, PuffTableBenchmarks Bench)[] variants =
        [
            ("sync thread, 64 die", Variant(puffs: puffs, frame: frame, onWorkers: false, dying: true)),
            ("all workers, 64 die", Variant(puffs: puffs, frame: frame, onWorkers: true, dying: true)),
            ("sync thread, none dies", Variant(puffs: puffs, frame: frame, onWorkers: false, dying: false)),
            ("all workers, none dies", Variant(puffs: puffs, frame: frame, onWorkers: true, dying: false)),
        ];
        List<double>[] medians = [.. variants.Select(_ => new List<double>())];
        List<double>[] samples = [.. variants.Select(_ => new List<double>())];
        double[] times = new double[40];
        for (int round = 0; round < rounds; round++)
        for (int k = 0; k < variants.Length; k++)
        {
            int v = (k + round) % variants.Length;
            for (int i = 0; i < 10; i++)
                variants[v].Bench.TimedStep();
            for (int i = 0; i < times.Length; i++)
                times[i] = variants[v].Bench.TimedStep();
            Array.Sort(times);
            medians[v].Add(times[times.Length / 2]);
            samples[v].AddRange(times);
        }

        for (int v = 0; v < variants.Length; v++)
        {
            List<double> m = medians[v];
            m.Sort();
            List<double> all = samples[v];
            all.Sort();
            Console.WriteLine(
                $"PUFFSTEP|{puffs} puffs|{frame}|{variants[v].Name}|median {m[m.Count / 2]:F1} us|min {m[0]:F1}|max {m[^1]:F1}|{m.Count} rounds|steps p10 {all[all.Count / 10]:F1} p50 {all[all.Count / 2]:F1}|live {variants[v].Bench.table.Count}"
            );
            variants[v].Bench.world.Dispose();
        }
    }
}
