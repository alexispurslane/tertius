using System.Diagnostics.CodeAnalysis;

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

    private readonly Dictionary<CellKey, List<Entity>> cells = new();
    private readonly Stack<List<Entity>> spareLists = new();

    public int CellCount => cells.Count;

    public Dictionary<CellKey, List<Entity>> Cells => cells;

    public static CellKey CellOf(Double3 position) => new(Coordinate(position.X), Coordinate(position.Y), Coordinate(position.Z));

    public static long Coordinate(double value)
    {
        double cell = Math.Floor(value / CellSize);
        return double.IsNaN(cell) ? 0 : (long)Math.Clamp(cell, -MaxCell, MaxCell);
    }

    public void Add(CellKey key, Entity entity)
    {
        if (!cells.TryGetValue(key, out List<Entity>? list))
        {
            list = spareLists.Count > 0 ? spareLists.Pop() : new List<Entity>(4);
            cells.Add(key, list);
        }
        list.Add(entity);
    }

    /// <summary>Removes an entity from a cell. Order inside a cell does not matter, because queries sort their results.</summary>
    public void Remove(CellKey key, Entity entity)
    {
        if (!cells.TryGetValue(key, out List<Entity>? list))
            return;
        int index = list.IndexOf(entity);
        if (index < 0)
            return;
        int last = list.Count - 1;
        list[index] = list[last];
        list.RemoveAt(last);
        if (list.Count == 0)
        {
            cells.Remove(key);
            spareLists.Push(list);
        }
    }

    public bool TryGetCell(CellKey key, [MaybeNullWhen(false)] out List<Entity> entities) => cells.TryGetValue(key, out entities);
}
