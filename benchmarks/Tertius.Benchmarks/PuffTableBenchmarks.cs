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
/// </summary>
[MemoryDiagnoser]
public class PuffTableBenchmarks
{
    private const double EarthMu = 3.986004418e14;
    private const int NewbornsPerStep = 64;

    [Params(100, 1024, 4096)] public int Puffs;

    [Params("earth", "earth-surface", "station")]
    public string Frame = "earth";

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

    private static PuffLook Look(float life) => new(Life: life, StartRadius: 0.2f, EndRadius: 1, TemperatureRatio: 3,
        Shading: PuffShading.Glow(new(4, 3, 2)));

    /// <summary>The world, its frame of the puffs and the place of the emitter in it.</summary>
    private (FrameHandle Frame, Double3 Place) Scene(int capacity)
    {
        world = new(new() { MaxDegreeOfParallelism = 1, PuffCapacity = capacity });
        switch (Frame)
        {
            case "earth":
                return (world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(EarthMu))),
                    new(6.8e6, 0, 0));
            case "earth-surface":
            {
                FrameHandle earth = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(EarthMu)));
                var spin = new FrameSpin(Tilt: DQuaternion.Identity, Rate: 7.2921e-5);
                return (
                    Spinning(earth, spin,
                        new(FrameBounds.Sphere(5e7f), GravityField.Point(EarthMu),
                            MediumField.Planet(radius: 6.371e6, surfaceDensity: 1.225f, scaleHeight: 8500, drag: 2))),
                    new(6.371e6 + 100, 0, 0));
            }
            default:
            {
                FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
                return (Spinning(space, new(Tilt: DQuaternion.Identity, Rate: 0.31321), new(FrameBounds.Sphere(150))),
                    new(8, 0, 0));
            }
        }
    }

    /// <summary>A frame that turns in place at the origin of <paramref name="parent"/> by its rails, from its first step on.</summary>
    private FrameHandle Spinning(FrameHandle parent, FrameSpin spin, FrameSpec spec) => world.GetOwnedFrame(world.Spawn(
        new()
        {
            Frame = parent,
            Pose = new(Double3.Zero, spin.RotationAt(world.Time)),
            AsFrame = spec,
            FirstMove = new(spin.Sample(Double3.Zero, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
            Components = [new SpinMotion { Spin = spin }]
        }));

    private void SpawnEmitter(FrameHandle frame, Double3 place, float rate, float life) => world.Spawn(new()
    {
        Frame = frame,
        Pose = new(place, DQuaternion.Identity),
        Render = new RenderData { Emitter = new(Rate: rate, Speed: 10, Spread: 1, Look: Look(life)) }
    });

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
        long bytes = (RowsOf(table).LongLength + SpareOf(table).LongLength + NewbornsOf(table).LongLength) *
                     Unsafe.SizeOf<Puff>();
        Console.WriteLine(
            $"// {Frame}, {Puffs}: {table.Count} live puffs in {table.View.Ranges.Length} range(s), {table.Looks.InUse} look(s); rows, spare and newborns {bytes} bytes");
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
}
