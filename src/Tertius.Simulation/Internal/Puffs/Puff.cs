namespace Tertius.Simulation;

/// <summary>
/// One puff in the puff table: a loose body without a shape, in the semantic frame of its range. A row holds only what
/// differs from puff to puff. Its frame is the frame of the range that holds it, and what the puffs of one emitter share
/// lies once in the look table. It is 56 bytes with no padding.
/// </summary>
/// <remarks>
/// The row keeps the place and the chord, not the last two places. They hold the same information, with the place one
/// step back at <c>Place − Chord · step</c>. The chord form keeps more digits far from the frame's origin, where a step's
/// gravity times the square of the step is smaller than the step of a double at the place.
/// </remarks>
internal struct Puff
{
    /// <summary>The puff's place at the end of the last step, in m, in its frame's axes.</summary>
    public Double3 Place;
    /// <summary>The puff's move over the last step divided by the step, in m/s, in its frame's axes.</summary>
    public Double3 Chord;
    /// <summary>The puff's age at the end of the last step, in s, from 0 up to but not including its look's life.</summary>
    public float Age;
    /// <summary>The index of the puff's look in the look table.</summary>
    public int Look;
}

/// <summary>The rows of one frame in the puff table: they lie together, from <see cref="Start"/> for <see cref="Count"/> rows.</summary>
/// <param name="Frame">The semantic frame that every row of the range lives in.</param>
/// <param name="Start">The index of the range's first row in the table.</param>
/// <param name="Count">The number of rows in the range, above 0 for a range that the table holds.</param>
internal readonly record struct PuffRange(FrameHandle Frame, int Start, int Count);

/// <summary>
/// A read-only view of the puff table for the render snapshot: the live rows, their ranges and the look table. It is valid
/// until the table next changes, at the next sync point.
/// </summary>
/// <param name="rows">The live rows, packed from index 0.</param>
/// <param name="ranges">One range per frame that holds puffs, in the order of the rows.</param>
/// <param name="looks">The look table by index, with its free entries.</param>
/// <param name="step">The length of the step that the rows describe, in s. A row's place one step back is <c>Place − Chord · step</c>.</param>
internal readonly ref struct PuffView(ReadOnlySpan<Puff> rows, ReadOnlySpan<PuffRange> ranges, ReadOnlySpan<PuffLook> looks, double step)
{
    /// <summary>The live rows, packed from index 0.</summary>
    public ReadOnlySpan<Puff> Rows { get; } = rows;
    /// <summary>One range per frame that holds puffs, in the order of the rows.</summary>
    public ReadOnlySpan<PuffRange> Ranges { get; } = ranges;
    /// <summary>The look table by index, with its free entries.</summary>
    public ReadOnlySpan<PuffLook> Looks { get; } = looks;
    /// <summary>The length of the step that the rows describe, in s.</summary>
    public double Step { get; } = step;
}
