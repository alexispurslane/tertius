using System.Numerics;

namespace Tertius.Rendering;

/// <summary>The bounds of a cell of the depth pyramid, in inverse view depth (docs/plans/screen-space-reflections.md).
/// </summary>
/// <param name="Near">The largest inverse depth of the cell's surface pixels, in 1/m: its nearest surface. 0 without a
/// surface.</param>
/// <param name="Far">The smallest, its farthest surface. <see cref="EmptyFar"/> without a surface.</param>
internal readonly record struct CellBounds(float Near, float Far)
{
    /// <summary>Far above any surface's inverse depth. Finite, not infinity: a shader compiler's fast math may assume
    /// that no value is infinite, on any Vulkan driver.</summary>
    public const float EmptyFar = 1e30f;

    /// <summary>The bounds of a cell with no surface. Merged with any bounds, it gives those bounds.</summary>
    public static CellBounds Empty => new(0, EmptyFar);

    public static CellBounds OfSurface(float inverseDepth) => new(inverseDepth, inverseDepth);

    public CellBounds Merge(CellBounds other) => new(MathF.Max(Near, other.Near), MathF.Min(Far, other.Far));

    /// <summary>The inverse depths that the cell's surfaces fill with their thickness: [Far / (1 + share), Near].
    /// </summary>
    public (float Low, float High) Slab(float thicknessShare) => (Far / (1 + thicknessShare), Near);
}

/// <summary>
/// The depth pyramid on the CPU: the C# copy of depth_pyramid.glsl and the pyramid shaders. Cell (x, y) of level k
/// covers the pixels whose coordinates shifted right by k give (x, y) (docs/plans/screen-space-reflections.md, "The
/// depth pyramid").
/// </summary>
internal sealed class DepthPyramid
{
    /// <summary>The most levels of a pyramid: a 16,384 px side halves to 1 px in 14 levels.</summary>
    public const int MaxLevels = 14;

    /// <summary>The cells of each level from 0 to <see cref="TopLevel"/>, row by row from the top.</summary>
    private readonly CellBounds[][] levels;

    private DepthPyramid(CellBounds[][] levels, int width, int height)
    {
        this.levels = levels;
        Width = width;
        Height = height;
    }

    /// <summary>The screen's width in pixels: level 0's width in cells.</summary>
    public int Width { get; }

    /// <summary>The screen's height in pixels.</summary>
    public int Height { get; }

    /// <summary>The level whose one cell covers the screen.</summary>
    public int TopLevel => levels.Length - 1;

    /// <summary>
    /// The top level L, whose one cell covers the screen: ⌈log₂ max(width, height)⌉, at least 1. It is also the count
    /// of the depth pyramid image's mips, which hold levels 1 to L.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A side is below 1 px.</exception>
    public static int LevelCount(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        int side = Math.Max(width, height);
        return side == 1 ? 1 : BitOperations.Log2((uint)(side - 1)) + 1;
    }

    /// <summary>The size of a level in cells: each side halved and rounded up, level times.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A side is below 1 px, or the level is negative.</exception>
    public static (int Width, int Height) LevelSize(int width, int height, int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        // ⌈n / 2^k⌉ for n ≥ 1, which repeated halving with rounding up gives.
        return (((width - 1) >> level) + 1, ((height - 1) >> level) + 1);
    }

    /// <summary>A pyramid over inverse depths given row by row from the top, 0 for sky.</summary>
    /// <exception cref="ArgumentException">The span does not hold width × height values.</exception>
    public static DepthPyramid FromInverseDepths(ReadOnlySpan<float> inverseDepths, int width, int height)
    {
        int topLevel = LevelCount(width, height);
        if (inverseDepths.Length != width * height)
            throw new ArgumentException(
                $"A {width} × {height} pyramid needs {width * height} inverse depths, not {inverseDepths.Length}.",
                nameof(inverseDepths)
            );
        var levels = new CellBounds[topLevel + 1][];
        levels[0] = new CellBounds[inverseDepths.Length];
        for (int index = 0; index < inverseDepths.Length; index++)
            levels[0][index] = inverseDepths[index] > 0 ? CellBounds.OfSurface(inverseDepths[index]) : CellBounds.Empty;
        for (int level = 1; level <= topLevel; level++)
            levels[level] = MergedLevel(
                levels[level - 1],
                LevelSize(width, height, level - 1),
                LevelSize(width, height, level)
            );
        return new(levels, width, height);
    }

    /// <summary>The bounds of a cell, or <see cref="CellBounds.Empty"/> for a cell outside its level: no surface lies
    /// beyond the screen's edges.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is not from 0 to <see cref="TopLevel"/>.</exception>
    public CellBounds Bounds(int level, int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, TopLevel);
        (int levelWidth, int levelHeight) = LevelSize(Width, Height, level);
        bool outside = x < 0 || y < 0 || x >= levelWidth || y >= levelHeight;
        return outside ? CellBounds.Empty : levels[level][y * levelWidth + x];
    }

    /// <summary>The next level: each cell merges the up to four cells of the level below that it covers.</summary>
    private static CellBounds[] MergedLevel(
        CellBounds[] below,
        (int Width, int Height) belowSize,
        (int Width, int Height) size
    )
    {
        var cells = new CellBounds[size.Width * size.Height];
        for (int y = 0; y < size.Height; y++)
        for (int x = 0; x < size.Width; x++)
        {
            CellBounds merged = CellBounds.Empty;
            for (int childY = 2 * y; childY < Math.Min(2 * y + 2, belowSize.Height); childY++)
            for (int childX = 2 * x; childX < Math.Min(2 * x + 2, belowSize.Width); childX++)
                merged = merged.Merge(below[childY * belowSize.Width + childX]);
            cells[y * size.Width + x] = merged;
        }
        return cells;
    }
}
