using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// Whether a mesh bounds a volume: after a weld of its positions, every directed edge has as many triangles as its
/// reverse edge, and the mesh has at least one triangle that is not degenerate (https://github.com/alexispurslane/tertius/wiki/back-face-depth-layer,
/// decision 5).
/// </summary>
internal static class MeshClosure
{
    /// <summary>
    /// The weld tolerance over the largest coordinate: 2^−16, 128 float steps at that coordinate. It holds the rounding of
    /// a generator's sines and an exporter's transforms, and is 1.5 mm on a mesh 100 m from its origin.
    /// </summary>
    private const float ToleranceShare = 1f / 65_536;

    /// <summary>
    /// How many tolerances wide a cell of the weld's grid is. A position looks into a neighbor cell only within a
    /// tolerance of that cell, so most positions look into one cell.
    /// </summary>
    private const int CellTolerances = 4;

    /// <summary>
    /// Whether the mesh of <paramref name="indices"/>, three per triangle, over <paramref name="positions"/> is closed. A
    /// triangle with two corners at one welded position covers no area and does not count.
    /// </summary>
    public static bool IsClosed(ReadOnlySpan<uint> indices, ReadOnlySpan<Vector3> positions)
    {
        int[] welded = Weld(positions);
        // Each undirected edge, smaller vertex first, sums +1 for each triangle that runs it upward and −1 for each that
        // runs it downward, so the pairing rule holds where every sum is 0.
        var balance = new Dictionary<(int Low, int High), int>();
        bool counted = false;
        for (int corner = 0; corner + 2 < indices.Length; corner += 3)
        {
            int a = welded[indices[corner]],
                b = welded[indices[corner + 1]],
                c = welded[indices[corner + 2]];
            if (a == b || b == c || c == a)
                continue;
            counted = true;
            Run(balance, a, b);
            Run(balance, b, c);
            Run(balance, c, a);
        }

        return counted && balance.Values.All(sum => sum == 0);
    }

    /// <summary>
    /// The distance within which two positions of <paramref name="positions"/> are one vertex, along every axis: the
    /// largest coordinate times <see cref="ToleranceShare"/>, because a float's rounding grows with its size.
    /// </summary>
    public static float WeldTolerance(ReadOnlySpan<Vector3> positions)
    {
        Vector3 largest = Vector3.Zero;
        foreach (Vector3 position in positions)
            largest = Vector3.Max(largest, Vector3.Abs(position));
        return MathF.Max(largest.X, MathF.Max(largest.Y, largest.Z)) * ToleranceShare;
    }

    /// <summary>
    /// The welded vertex of each position: an earlier position within <see cref="WeldTolerance"/> along every axis, or
    /// the position itself.
    /// </summary>
    private static int[] Weld(ReadOnlySpan<Vector3> positions)
    {
        var grid = new WeldGrid(positions, WeldTolerance(positions));
        int[] welded = new int[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            welded[i] = grid.Near(positions[i]) ?? i;
            if (welded[i] == i)
                grid.Add(i);
        }

        return welded;
    }

    /// <summary>
    /// Adds 1 to <paramref name="balance"/> of the undirected edge of <paramref name="from"/> and <paramref name="to"/> for a
    /// triangle that runs it from the smaller vertex up, and −1 for one that runs it down.
    /// </summary>
    private static void Run(Dictionary<(int Low, int High), int> balance, int from, int to)
    {
        (int Low, int High) edge = from < to ? (from, to) : (to, from);
        balance[edge] = balance.GetValueOrDefault(edge) + (from < to ? 1 : -1);
    }

    /// <summary>
    /// The welded positions so far, in a grid of cells <see cref="CellTolerances"/> tolerances wide. A position looks into
    /// every cell that its tolerance reaches, so two near positions weld across a grid line.
    /// </summary>
    private readonly ref struct WeldGrid
    {
        private readonly ReadOnlySpan<Vector3> positions;
        private readonly float tolerance;
        private readonly double cell;
        private readonly Dictionary<(long X, long Y, long Z), int> firstOfCell = [];

        // The next welded position of the same cell, or −1 for the cell's last.
        private readonly int[] nextInCell;

        public WeldGrid(ReadOnlySpan<Vector3> positions, float tolerance)
        {
            this.positions = positions;
            this.tolerance = tolerance;
            // A mesh at one point welds only equal positions, and any cell size serves it.
            cell = tolerance > 0 ? (double)tolerance * CellTolerances : 1;
            nextInCell = new int[positions.Length];
        }

        /// <summary>A welded position within the tolerance of <paramref name="position"/> along every axis, or null for none.</summary>
        public int? Near(Vector3 position)
        {
            var reach = new Vector3(tolerance);
            (long X, long Y, long Z) low = CellOf(position - reach),
                high = CellOf(position + reach);
            for (long x = low.X; x <= high.X; x++)
            for (long y = low.Y; y <= high.Y; y++)
            for (long z = low.Z; z <= high.Z; z++)
            for (int j = firstOfCell.GetValueOrDefault((x, y, z), -1); j >= 0; j = nextInCell[j])
                if (Vector3.LessThanOrEqualAll(Vector3.Abs(positions[j] - position), reach))
                    return j;
            return null;
        }

        /// <summary>Adds position <paramref name="index"/> as a welded position.</summary>
        public void Add(int index)
        {
            (long X, long Y, long Z) home = CellOf(positions[index]);
            nextInCell[index] = firstOfCell.GetValueOrDefault(home, -1);
            firstOfCell[home] = index;
        }

        private (long X, long Y, long Z) CellOf(Vector3 position) =>
            (
                (long)Math.Floor(position.X / cell),
                (long)Math.Floor(position.Y / cell),
                (long)Math.Floor(position.Z / cell)
            );
    }
}
