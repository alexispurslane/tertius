using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The sectors of a slice that are blocked nearby, one bit each: bit j for sector j (docs/plans/screen-space-
/// illumination.md, "The slice and its sectors"). sectors.glsl keeps the same 32-bit words.
/// </summary>
internal readonly record struct SectorMask(uint Bits)
{
    public static SectorMask None => new(0);

    /// <summary>The number of blocked sectors, from 0 to 32.</summary>
    public int Count => BitOperations.PopCount(Bits);

    /// <summary>The sectors that this mask leaves open.</summary>
    public SectorMask Open => new(~Bits);

    /// <summary>The sectors from start to end − 1, none when end ≤ start.</summary>
    /// <remarks>No shift is by 32, which C# takes as a shift by 0 and GLSL leaves undefined.</remarks>
    public static SectorMask Run(int start, int end)
    {
        Contract.Requires(start is >= 0 and <= SliceSectors.Count && end is >= 0 and <= SliceSectors.Count);
        return end > start ? new((uint.MaxValue >> (SliceSectors.Count - (end - start))) << start) : None;
    }

    /// <summary>The sectors that either mask blocks.</summary>
    public SectorMask Union(SectorMask other) => new(Bits | other.Bits);

    /// <summary>The sectors that this mask blocks and <paramref name="other"/> does not.</summary>
    public SectorMask Except(SectorMask other) => new(Bits & ~other.Bits);

    /// <summary>The rank-th set bit, counted from bit 0. The mask has more than rank bits set.</summary>
    public int SectorOfRank(int rank)
    {
        Contract.Requires(rank >= 0 && rank < Count);
        uint bits = Bits;
        for (int i = 0; i < rank; i++)
            bits &= bits - 1;
        return BitOperations.TrailingZeroCount(bits);
    }

    // The generated ToString would print Open, whose Open it would print in turn, without end.
    public override string ToString() => $"SectorMask {{ Bits = {Bits:B32} }}";
}
