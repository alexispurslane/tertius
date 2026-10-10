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
internal sealed class SpatialIndex
{
    /// <summary>Where one entity is in the grids.</summary>
    private struct Placement
    {
        public bool InGrid;
        public int Frame;
        public CellKey Cell;
    }

    private readonly EntityTable entities;
    private readonly FrameTree frames;
    private readonly EntityTable.Column<Placement> placements;
    private SpatialGrid?[] grids = new SpatialGrid?[16];

    public SpatialIndex(EntityTable entities, FrameTree frames)
    {
        this.entities = entities;
        this.frames = frames;
        placements = entities.AddColumn<Placement>(Remove);
    }

    /// <summary>Moves each entity whose front transform changed since the last update into its current cell.</summary>
    public void Update()
    {
        foreach (Entity entity in entities.TakeMoved())
        {
            ref Placement placement = ref placements[entity];
            ref readonly TransformRow row = ref entities.FrontRow(entity);
            bool inGrid = !row.Frame.IsNull;
            int frame = inGrid ? row.Frame.Index : 0;
            CellKey cell = inGrid ? SpatialGrid.CellOf(row.Pose.Position) : default;
            if (placement.InGrid == inGrid && (!inGrid || (placement.Frame == frame && placement.Cell == cell)))
                continue;
            if (placement.InGrid)
                grids[placement.Frame]?.Remove(placement.Cell, entity);
            if (inGrid)
                GridAt(frame).Add(cell, entity);
            placement = new Placement { InGrid = inGrid, Frame = frame, Cell = cell };
        }
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

        double r2 = radius * radius;
        long x0 = SpatialGrid.Coordinate(center.X - radius), x1 = SpatialGrid.Coordinate(center.X + radius);
        long y0 = SpatialGrid.Coordinate(center.Y - radius), y1 = SpatialGrid.Coordinate(center.Y + radius);
        long z0 = SpatialGrid.Coordinate(center.Z - radius), z1 = SpatialGrid.Coordinate(center.Z + radius);
        double boxCells = (double)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        if (boxCells > grid.CellCount)
        {
            // A large radius: visiting the occupied cells is cheaper than visiting the box.
            foreach (KeyValuePair<CellKey, List<Entity>> cell in grid.Cells)
                CollectWithin(cell.Value, center, r2, results);
        }
        else
        {
            for (long x = x0; x <= x1; x++)
                for (long y = y0; y <= y1; y++)
                    for (long z = z0; z <= z1; z++)
                        if (grid.TryGetCell(new CellKey(x, y, z), out List<Entity>? cell))
                            CollectWithin(cell, center, r2, results);
        }
        results.Sort();
    }

    /// <summary>Checks every invariant in the class comment. O(capacity + entries).</summary>
    internal void AssertInvariants()
    {
        int placed = 0;
        foreach (Entity entity in entities.LiveEntities)
        {
            ref readonly Placement placement = ref placements[entity];
            ref readonly TransformRow row = ref entities.FrontRow(entity);
            Contract.Invariant(placement.InGrid == !row.Frame.IsNull);
            if (!placement.InGrid)
                continue;
            placed++;
            Contract.Invariant(placement.Frame == row.Frame.Index && placement.Cell == SpatialGrid.CellOf(row.Pose.Position));
            Contract.Invariant(grids[placement.Frame] is { } grid && grid.TryGetCell(placement.Cell, out List<Entity>? cell) && cell.Contains(entity));
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
                    ref readonly Placement placement = ref placements[entity];
                    Contract.Invariant(placement.InGrid && placement.Frame == frame && placement.Cell == cell.Key);
                    listed++;
                }
            }
        }
        Contract.Invariant(listed == placed);
    }

    /// <summary>Takes a released entity, still live while this runs, out of its cell. The entity table clears its placement next.</summary>
    private void Remove(Entity entity)
    {
        ref Placement placement = ref placements[entity];
        if (placement.InGrid)
            grids[placement.Frame]?.Remove(placement.Cell, entity);
    }

    private SpatialGrid GridAt(int frame)
    {
        if (frame >= grids.Length)
            Array.Resize(ref grids, Math.Max(frame + 1, grids.Length * 2));
        return grids[frame] ??= new SpatialGrid();
    }

    private void CollectWithin(List<Entity> cell, Double3 center, double r2, List<Entity> results)
    {
        foreach (Entity entity in cell)
            if ((entities.FrontRow(entity).Pose.Position - center).LengthSquared <= r2)
                results.Add(entity);
    }
}
