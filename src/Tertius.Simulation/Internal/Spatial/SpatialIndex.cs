namespace Tertius.Simulation;

/// <summary>
/// One sparse grid per frame over the front transforms, for radius queries. The sync point brings it up to date with
/// <see cref="Update"/>, after the dirty rows have been copied forward. During a tick it does not change, so any
/// thread can query it.
/// </summary>
/// <remarks>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>After <see cref="Update"/>, every live entity that is in a frame is in exactly one cell, the cell of its
/// front position, in the grid of its front frame. The owner of a root frame is in no grid.</item>
/// <item>Only live entities are in a cell, and each entity's placement names the one cell that lists it. Releasing an
/// entity takes it out of its cell.</item>
/// </list>
/// Query results are entity handles and never name a dead entity.
/// </remarks>
internal sealed partial class SpatialIndex
{
    /// <summary>The number of work items of an update on the workers, which share the moved entities in equal ranges.</summary>
    private const int ParallelItems = 32;

    /// <summary>
    /// The default of <see cref="ParallelThreshold"/>. Measured with SpatialUpdateBenchmarks on an M1 Max: while the rows
    /// that an update reads fit in the caches of the sync thread, up to about 50,000 moved entities, the workers save
    /// nothing. From 65,536 on they save 8 to 10 percent there, and a third to a half of step 8c in the 100,000-entity
    /// tick benchmark.
    /// </summary>
    internal const int DefaultParallelThreshold = 65_536;

    private readonly EntityTable entities;
    private readonly FrameTree frames;
    private readonly ParallelOptions? workers;
    private readonly EntityTable.Column<Placement> placements;
    private SpatialGrid?[] grids = new SpatialGrid?[16];

    // The moves that the last update found. In an update on the workers, each work item writes the moves of its range of
    // the moved entities into the same range here, and records how many it found.
    private Move[] moves = [];
    private readonly int[] movesPerItem = new int[ParallelItems];

    // The moved entities of an update on the workers, copied out of the entity table so that the workers can read them.
    private Entity[] movedForWorkers = [];
    private int movedCount;
    private Action<int>? findItemMoves;

    private int parallelThreshold = DefaultParallelThreshold;

    /// <param name="entities">The entity table whose front rows the grids index.</param>
    /// <param name="frames">The frame tree whose frames have the grids.</param>
    /// <param name="workers">
    /// The workers on which a large <see cref="Update"/> finds the new cells, or null to run every update on the calling
    /// thread.
    /// </param>
    public SpatialIndex(EntityTable entities, FrameTree frames, ParallelOptions? workers = null)
    {
        this.entities = entities;
        this.frames = frames;
        this.workers = workers;
        placements = entities.AddColumn<Placement>(Remove);
    }

    /// <summary>
    /// The fewest moved entities for which <see cref="Update"/> finds their new cells on the workers. 1 or more. Both
    /// paths end in the same grids, so only tests change it: at 1, every update with a moved entity takes the workers.
    /// </summary>
    internal int ParallelThreshold
    {
        get => parallelThreshold;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            parallelThreshold = value;
        }
    }

    /// <summary>The case of a <see cref="Placement"/> of an entity that a grid lists.</summary>
    /// <param name="Frame">The index of the frame whose grid lists the entity.</param>
    /// <param name="Cell">The cell of that grid that lists the entity.</param>
    private readonly record struct InCell(int Frame, CellKey Cell);

    /// <summary>
    /// Where one entity is in the grids: <see cref="InCell"/>, or in no grid. The empty value is <see cref="NotInGrid"/>,
    /// the all-zero default, so the row that the entity table clears for a released slot is in no grid.
    /// This is a generated union (docs/plans/union-tooling.md).
    /// </summary>
    [GeneratedUnion(typeof(InCell))]
    private readonly partial struct Placement
    {
        /// <summary>In no grid.</summary>
        public static Placement NotInGrid => default;
    }

    /// <summary>A moved entity and the placement that <see cref="Update"/> found for it, before the grids change.</summary>
    private readonly record struct Move(Entity Entity, Placement To);

    /// <summary>
    /// Moves each entity whose front transform changed since the last update into its current cell. With workers and at
    /// least <see cref="ParallelThreshold"/> moved entities, the workers find the new cells; the grids end in the same
    /// state either way.
    /// </summary>
    public void Update()
    {
        // An update finds the moves first and applies them after, in the order of the moved entities. Finding the move of
        // an entity reads only that entity's placement and front row, and applying a move writes only that entity's
        // placement and the grids. The moved list names each entity once, so no entity's move depends on a move applied
        // before it, and the grids end as if each entity were moved as soon as its move was found.
        ReadOnlySpan<Entity> moved = entities.TakeMoved();
        if (moves.Length < moved.Length)
            moves = new Move[Math.Max(moved.Length, moves.Length * 2)];
        if (workers == null || moved.Length < parallelThreshold)
        {
            Apply(moves.AsSpan(0, FindMoves(moved, moves)));
            return;
        }

        // Each work item finds the moves of one range of the moved entities, into the same range of the moves. This
        // thread applies the ranges in order, so it applies every move in the order of the moved entities, as above.
        if (movedForWorkers.Length < moved.Length)
            movedForWorkers = new Entity[moves.Length];
        moved.CopyTo(movedForWorkers);
        movedCount = moved.Length;
        int items = Math.Min(movedCount, ParallelItems);
        Parallel.For(fromInclusive: 0, toExclusive: items, parallelOptions: workers,
            body: findItemMoves ??= FindItemMoves);
        for (int item = 0; item < items; item++)
            Apply(moves.AsSpan(ItemStart(item, items), movesPerItem[item]));
    }

    /// <summary>
    /// Clears <paramref name="results"/>, then adds every live entity in <paramref name="frame"/> whose front position is
    /// within <paramref name="radius"/> of <paramref name="center"/>, sorted by entity index. The distance test is
    /// exact; the grid only narrows the search. Thread-safe during a tick.
    /// </summary>
    public void QueryRadius(FrameHandle frame, Double3 center, double radius, List<Entity> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        results.Clear();
        if (!frames.IsAlive(frame))
            throw new InvalidOperationException($"{frame} is stale or null.");
        if (!(radius >= 0))
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "The radius must be zero or more.");
        if (frame.Index >= grids.Length || grids[frame.Index] is not { } grid)
            return;

        CellBox box = new(center, radius);
        if (box.CellCount > grid.CellCount)
            // A large radius: visiting the occupied cells is cheaper than visiting the box.
            foreach (KeyValuePair<CellKey, List<Entity>> cell in grid.Cells)
                CollectWithin(cell: cell.Value, center: center, r2: radius * radius, results: results);
        else
            for (long x = box.X0; x <= box.X1; x++)
            for (long y = box.Y0; y <= box.Y1; y++)
            for (long z = box.Z0; z <= box.Z1; z++)
                if (grid.TryGetCell(new(x, y, z), out List<Entity>? cell))
                    CollectWithin(cell: cell, center: center, r2: radius * radius, results: results);
        results.Sort();
    }

    /// <summary>
    /// How many entities the cells of the grid of <paramref name="frame"/> that a sphere around <paramref name="center"/>
    /// touches hold: an upper bound of the entities within <paramref name="radius"/>. -1 when the box around the sphere
    /// has more cells than the grid has occupied ones: then the frame's members are the cheaper index. The frame must be
    /// alive. Thread-safe during a tick.
    /// </summary>
    public int CountNear(FrameHandle frame, Double3 center, double radius)
    {
        if (frame.Index >= grids.Length || grids[frame.Index] is not { } grid)
            return 0;
        CellBox box = new(center, radius);
        if (box.CellCount > grid.CellCount)
            return -1;
        int count = 0;
        for (long x = box.X0; x <= box.X1; x++)
        for (long y = box.Y0; y <= box.Y1; y++)
        for (long z = box.Z0; z <= box.Z1; z++)
            if (grid.TryGetCell(new(x, y, z), out List<Entity>? cell))
                count += cell.Count;
        return count;
    }

    /// <summary>
    /// Writes every entity of the grid of <paramref name="frame"/> whose front position is within
    /// <paramref name="radius"/> of <paramref name="center"/> into <paramref name="into"/>, in no order, and returns how
    /// many. <paramref name="into"/> must hold at least <see cref="CountNear"/> entries, and that count must not be -1.
    /// Thread-safe during a tick.
    /// </summary>
    public int CollectNear(FrameHandle frame, Double3 center, double radius, Span<Entity> into)
    {
        if (frame.Index >= grids.Length || grids[frame.Index] is not { } grid)
            return 0;
        double r2 = radius * radius;
        CellBox box = new(center, radius);
        int count = 0;
        for (long x = box.X0; x <= box.X1; x++)
        for (long y = box.Y0; y <= box.Y1; y++)
        for (long z = box.Z0; z <= box.Z1; z++)
            if (grid.TryGetCell(new(x, y, z), out List<Entity>? cell))
                foreach (Entity entity in cell)
                    if ((entities.FrontRow(entity).Pose.Position - center).LengthSquared <= r2)
                        into[count++] = entity;
        return count;
    }

    /// <summary>Checks every invariant in the class comment. O(capacity + entries).</summary>
    internal void AssertInvariants()
    {
        int placed = 0;
        foreach (Entity entity in entities.LiveEntities)
        {
            ref readonly Placement placement = ref placements[entity];
            ref readonly TransformRow row = ref entities.FrontRow(entity);
            Contract.Invariant(placement.HasValue == !row.Frame.IsNull);
            if (placement is not InCell at)
                continue;
            placed++;
            Contract.Invariant(at.Frame == row.Frame.Index && at.Cell == SpatialGrid.CellOf(row.Pose.Position));
            Contract.Invariant(grids[at.Frame] is { } grid && grid.TryGetCell(at.Cell, out List<Entity>? cell) &&
                               cell.Contains(entity));
        }

        int listed = 0;
        for (int frame = 0; frame < grids.Length; frame++)
        {
            if (grids[frame] is not { } grid)
                continue;
            foreach (KeyValuePair<CellKey, List<Entity>> cell in grid.Cells)
            {
                Contract.Invariant(cell.Value.Count > 0);
                foreach (Entity entity in cell.Value)
                {
                    Contract.Invariant(entities.IsAlive(entity));
                    Contract.Invariant(placements[entity] == new InCell(frame, cell.Key));
                    listed++;
                }
            }
        }

        Contract.Invariant(listed == placed);
    }

    /// <summary>
    /// Writes a <see cref="Move"/> into <paramref name="into"/> for each entity of <paramref name="moved"/> whose placement
    /// changed, in the order of <paramref name="moved"/>, and returns how many. It reads only those entities'
    /// placements and front rows and writes only <paramref name="into"/>, so workers can run it on disjoint ranges.
    /// </summary>
    private int FindMoves(ReadOnlySpan<Entity> moved, Span<Move> into)
    {
        int count = 0;
        foreach (Entity entity in moved)
        {
            ref readonly TransformRow row = ref entities.FrontRow(entity);
            Placement to = row.Frame.IsNull
                ? Placement.NotInGrid
                : new InCell(row.Frame.Index, SpatialGrid.CellOf(row.Pose.Position));
            if (placements[entity] != to)
                into[count++] = new(entity, to);
        }

        return count;
    }

    /// <summary>The moves of one work item of an update on the workers, on a worker.</summary>
    private void FindItemMoves(int item)
    {
        int items = Math.Min(movedCount, ParallelItems);
        int start = ItemStart(item, items), length = ItemStart(item + 1, items) - start;
        movesPerItem[item] = FindMoves(movedForWorkers.AsSpan(start, length), moves.AsSpan(start, length));
    }

    /// <summary>The index of the first moved entity of a work item: the items share the moved entities in equal ranges.</summary>
    private int ItemStart(int item, int items) => (int)((long)movedCount * item / items);

    /// <summary>Takes each entity of <paramref name="found"/> out of its old cell, puts it into its new one, and records where it is now.</summary>
    private void Apply(ReadOnlySpan<Move> found)
    {
        foreach (ref readonly Move move in found)
        {
            ref Placement placement = ref placements[move.Entity];
            if (placement is InCell from && move.To is InCell to && from.Frame == to.Frame)
            {
                // Within one grid: one lookup per cell.
                GridAt(to.Frame).Move(from.Cell, to.Cell, move.Entity);
            }
            else
            {
                // Into the grid of another frame, out of every grid, or into a grid from none.
                if (placement is InCell left)
                    grids[left.Frame]?.Remove(left.Cell, move.Entity);
                if (move.To is InCell entered)
                    GridAt(entered.Frame).Add(entered.Cell, move.Entity);
            }

            placement = move.To;
        }
    }

    /// <summary>Takes a released entity, still live while this runs, out of its cell. The entity table clears its placement next.</summary>
    private void Remove(Entity entity)
    {
        if (placements[entity] is InCell at)
            grids[at.Frame]?.Remove(at.Cell, entity);
    }

    private SpatialGrid GridAt(int frame)
    {
        if (frame >= grids.Length)
            Array.Resize(ref grids, Math.Max(frame + 1, grids.Length * 2));
        return grids[frame] ??= new();
    }

    private void CollectWithin(List<Entity> cell, Double3 center, double r2, List<Entity> results)
    {
        foreach (Entity entity in cell)
            if ((entities.FrontRow(entity).Pose.Position - center).LengthSquared <= r2)
                results.Add(entity);
    }
}

/// <summary>The cells of a <see cref="SpatialGrid"/> that the axis-aligned box around a sphere covers, from corner to corner.</summary>
internal readonly struct CellBox(Double3 center, double radius)
{
    public long X0 { get; } = SpatialGrid.Coordinate(center.X - radius);
    public long X1 { get; } = SpatialGrid.Coordinate(center.X + radius);
    public long Y0 { get; } = SpatialGrid.Coordinate(center.Y - radius);
    public long Y1 { get; } = SpatialGrid.Coordinate(center.Y + radius);
    public long Z0 { get; } = SpatialGrid.Coordinate(center.Z - radius);
    public long Z1 { get; } = SpatialGrid.Coordinate(center.Z + radius);

    /// <summary>The number of cells, as a double, because a large box has more than a long can count.</summary>
    public double CellCount => (double)(X1 - X0 + 1) * (Y1 - Y0 + 1) * (Z1 - Z0 + 1);
}
