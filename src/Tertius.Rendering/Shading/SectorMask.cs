using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// A set of a slice's sectors, one bit each: bit j for sector j (docs/plans/screen-space-illumination.md, "The slice
/// and its sectors"). A slice's blocked sectors and its open sectors are both such sets. sectors.glsl keeps the same
/// 32-bit words.
/// </summary>
internal readonly record struct SectorMask(uint Bits)
{
    public static SectorMask None => new(0);

    /// <summary>The number of sectors in the set, from 0 to 32.</summary>
    public int Count => BitOperations.PopCount(Bits);

    /// <summary>The sectors that this set leaves out.</summary>
    public SectorMask Open => new(~Bits);

    /// <summary>The sectors from start to end − 1, none when end ≤ start.</summary>
    /// <remarks>No shift is by 32, which C# takes as a shift by 0 and GLSL leaves undefined.</remarks>
    public static SectorMask Run(int start, int end)
    {
        Contract.Requires(start is >= 0 and <= SliceSectors.Count && end is >= 0 and <= SliceSectors.Count);
        return end > start ? new((uint.MaxValue >> (SliceSectors.Count - (end - start))) << start) : None;
    }

    /// <summary>
    /// The unbroken piece of this set that holds its lowest sector: from that sector up to the first sector that the set
    /// leaves out. None for an empty set. The sum Bits + lowest bit carries through the piece and clears it, and a piece
    /// that ends at sector 31 wraps the sum to 0, so the piece is what the sum leaves out. lowestPiece in sectors.glsl is
    /// the GPU copy.
    /// </summary>
    public SectorMask LowestPiece => new(unchecked(Bits & ~(Bits + (Bits & (0u - Bits)))));

    /// <summary>The sectors in either set.</summary>
    public SectorMask Union(SectorMask other) => new(Bits | other.Bits);

    /// <summary>The sectors in this set and not in <paramref name="other"/>.</summary>
    public SectorMask Except(SectorMask other) => new(Bits & ~other.Bits);

    // The generated ToString would print Open, whose Open it would print in turn, without end.
    public override string ToString() => $"SectorMask {{ Bits = {Bits:B32} }}";
}
