using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tertius.Simulation;

/// <summary>A cell of a <see cref="SpatialGrid"/>. Coordinates are 64-bit so that a solar-system frame does not overflow.</summary>
internal readonly record struct CellKey(long X, long Y, long Z);

/// <summary>
/// A sparse hash grid of entities for one frame, with cubic cells of <see cref="CellSize"/> meters. The sync point
/// updates it from the front transforms. During a tick it is read-only, so any thread can query it. It is
/// <see cref="SpatialIndex"/>'s private storage: it holds handles that the index checked, and never reaches an
/// entity's data through them, so it has nothing to check itself. The index takes each entity out while it is still
/// live, so a grid never holds a dead entity.
/// </summary>
internal sealed class SpatialGrid
{
    public const double CellSize = 32;

    // Beyond this the cell coordinate no longer matters: such positions are broken, and they must not overflow a long.
    private const double MaxCell = 1e15;

    private readonly Stack<List<Entity>> spareLists = new();

    public int CellCount => Cells.Count;

    public Dictionary<CellKey, List<Entity>> Cells { get; } = [];

    public static CellKey CellOf(Double3 position) =>
        new(Coordinate(position.X), Coordinate(position.Y), Coordinate(position.Z));

    public static long Coordinate(double value)
    {
        double cell = Math.Floor(value / CellSize);
        return double.IsNaN(cell) ? 0 : (long)Math.Clamp(cell, -MaxCell, MaxCell);
    }

    public void Add(CellKey key, Entity entity)
    {
        // A cell that exists holds a list, so only a new cell has none.
        ref List<Entity>? list = ref CollectionsMarshal.GetValueRefOrAddDefault(Cells, key, out _);
        list ??= spareLists.Count > 0 ? spareLists.Pop() : [with(4)];
        list.Add(entity);
    }

    /// <summary>Removes an entity from a cell. Order inside a cell does not matter, because queries sort their results.</summary>
    public void Remove(CellKey key, Entity entity)
    {
        ref List<Entity> list = ref CollectionsMarshal.GetValueRefOrNullRef(Cells, key);
        if (Unsafe.IsNullRef(ref list) || !TakeOut(list, entity))
            return;
        if (list.Count == 0)
        {
            spareLists.Push(list);
            Cells.Remove(key);
        }
    }

    /// <summary>
    /// Moves an entity from the cell <paramref name="from"/> to the cell <paramref name="to"/>, and leaves the grid as
    /// <see cref="Remove"/> and then <see cref="Add"/> do, with one lookup of each cell. An entity that is alone in its
    /// cell takes the cell's list along to an empty cell, instead of handing it to the spare lists and taking it back.
    /// </summary>
    public void Move(CellKey from, CellKey to, Entity entity)
    {
        ref List<Entity> source = ref CollectionsMarshal.GetValueRefOrNullRef(Cells, from);
        if (Unsafe.IsNullRef(ref source))
        {
            Add(to, entity);
            return;
        }

        if (source.Count > 1 || source[0] != entity)
        {
            // The old cell keeps its other entities.
            TakeOut(source, entity);
            Add(to, entity);
            return;
        }

        List<Entity> list = source;
        Cells.Remove(from);
        ref List<Entity>? target = ref CollectionsMarshal.GetValueRefOrAddDefault(Cells, to, out _);
        if (target is null)
        {
            // The list holds this entity alone, which is what the new cell needs.
            target = list;
            return;
        }

        target.Add(entity);
        list.Clear();
        spareLists.Push(list);
    }

    /// <summary>The entities in the cell <paramref name="key"/>. Null if the cell is empty.</summary>
    public List<Entity>? CellAt(CellKey key) => Cells.GetValueOrDefault(key);

    /// <summary>Takes an entity out of a cell's list by moving the last entry into its place. False if the list does not hold it.</summary>
    private static bool TakeOut(List<Entity> list, Entity entity)
    {
        int index = list.IndexOf(entity);
        if (index < 0)
            return false;
        int last = list.Count - 1;
        list[index] = list[last];
        list.RemoveAt(last);
        return true;
    }
}
