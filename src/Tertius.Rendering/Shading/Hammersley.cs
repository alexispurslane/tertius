namespace Tertius.Rendering;

/// <summary>A Hammersley point set of <see cref="Count"/> points in the unit square.</summary>
internal readonly record struct Hammersley(int Count)
{
    /// <summary>Point <paramref name="index"/>: (index / Count, the base-2 radical inverse of index).</summary>
    public (double U, double V) this[int index] => ((double)index / Count, RadicalInverse((uint)index));

    /// <summary>The bits of <paramref name="index"/> mirrored about the binary point, in [0, 1).</summary>
    private static double RadicalInverse(uint index)
    {
        uint bits = (index << 16) | (index >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits / 4294967296.0;
    }
}
