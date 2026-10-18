using BenchmarkDotNet.Attributes;
using Tertius.Simulation;

namespace Tertius.Benchmarks;

/// <summary>
/// The cost of step 8c of the sync point, <c>SpatialIndex.Update</c>, for a number of moved entities, on the sync
/// thread alone and with the workers, to place <c>SpatialIndex.ParallelThreshold</c>. Every entity moves in every
/// update. With <see cref="ChangeCells"/> false they stand 2 m apart, as in <see cref="WorldTickBenchmarks"/>, and move
/// 1 cm, so none changes cell and the update only finds the cells. With it true they stand alone in their cells, 40 m
/// apart, and each moves one cell, as the Kepler frames do. Each invocation also writes and copies forward every row;
/// <see cref="UpdateMode.CopyOnly"/> does only that, so the update's own cost is a mode's time minus its time.
/// <see cref="UpdateMode.TakeOnly"/> shows the part of the update that stays on the sync thread in both modes: taking
/// the moved list from the entity table.
/// </summary>
public class SpatialUpdateBenchmarks
{
    public enum UpdateMode
    {
        /// <summary>Writes and copies the rows, and leaves the index as it is.</summary>
        CopyOnly,
        /// <summary>Writes and copies the rows, and takes the moved list from the entity table, as an update does first.</summary>
        TakeOnly,
        /// <summary>Updates the index on this thread alone.</summary>
        OneThread,
        /// <summary>Updates the index with the workers, however few entities moved.</summary>
        Workers,
    }

    private EntityTable entities = null!;
    private SpatialIndex spatial = null!;
    private Entity[] movers = [];
    private ParallelOptions? workers;
    private bool shifted;

    [Params(16384, 32768, 65536, 98304)]
    public int Moved { get; set; }

    [Params(UpdateMode.CopyOnly, UpdateMode.TakeOnly, UpdateMode.OneThread, UpdateMode.Workers)]
    public UpdateMode Mode { get; set; }

    [Params(false, true)]
    public bool ChangeCells { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        entities = new EntityTable(chunkSize: 64, initialCapacity: Moved + 64);
        var frames = new FrameTree();
        spatial = new SpatialIndex(entities, frames) { ParallelThreshold = Mode == UpdateMode.Workers ? 1 : int.MaxValue };
        workers = Mode == UpdateMode.Workers ? new ParallelOptions { MaxDegreeOfParallelism = -1 } : null;
        // The owner of the root is a handle that the table never hands out, so it is in no grid.
        FrameHandle root = frames.Create(new Entity((uint)Moved + 1000, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e12f), MotionDriver.Static);
        double spacing = ChangeCells ? 40 : 2;
        movers = new Entity[Moved];
        for (int i = 0; i < Moved; i++)
        {
            movers[i] = entities.Reserve();
            var row = new TransformRow { Frame = root, Pose = new DPose(new Double3(i % 300 * spacing, i / 300 * spacing, 0), DQuaternion.Identity) };
            entities.Activate(movers[i], row, new RenderData(), [], [], null, FrameHandle.Null);
        }
        entities.CopyDirtyRows();
        spatial.Update();
    }

    [Benchmark]
    public void MoveAndUpdate()
    {
        // Back and forth, so that the scene does not drift.
        shifted = !shifted;
        double step = (shifted ? 1 : -1) * (ChangeCells ? SpatialGrid.CellSize : 0.01);
        foreach (Entity mover in movers)
        {
            TransformRow row = entities.BackRow(mover);
            row.Pose = row.Pose with { Position = row.Pose.Position + new Double3(step, 0, 0) };
            entities.SetBackTransform(mover, row);
        }
        entities.CopyDirtyRows();
        if (Mode == UpdateMode.TakeOnly)
            entities.TakeMoved();
        else if (Mode != UpdateMode.CopyOnly)
            spatial.Update(workers);
    }
}
