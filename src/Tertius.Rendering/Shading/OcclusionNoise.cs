namespace Tertius.Rendering;

/// <summary>
/// The fixed spatial noise of GTAO: XeGTAO's Hilbert index over a 64 × 64 tile, driving the R2 sequence, at the
/// temporal index 0 (docs/plans/ambient-occlusion.md, "The noise"). In floats, as gtao.glsl computes it.
/// </summary>
internal static class OcclusionNoise
{
    /// <summary>The width of the tile in half-resolution texels: the Hilbert curve of level 6.</summary>
    public const int TileSize = 64;

    // The R2 sequence (Roberts 2018): the inverses of the plastic number and of its square.
    private const float SliceSequence = 0.75487766624669276005f;
    private const float StepSequence = 0.5698402909980532659114f;

    /// <summary>
    /// The place of the texel (<paramref name="x"/>, <paramref name="y"/>) on the Hilbert curve through its tile, 0 to
    /// 4,095 (XeGTAO.h lines 120 to 142).
    /// </summary>
    public static uint HilbertIndex(uint x, uint y)
    {
        // The curve reads only the low 6 bits, so the tile repeats. XeGTAO's lookup image reads its texel this way.
        x %= TileSize;
        y %= TileSize;
        uint index = 0;
        for (uint level = TileSize / 2; level > 0; level /= 2)
        {
            uint regionX = (x & level) > 0 ? 1u : 0u;
            uint regionY = (y & level) > 0 ? 1u : 0u;
            index += level * level * ((3 * regionX) ^ regionY);
            if (regionY != 0)
                continue;
            if (regionX == 1)
            {
                x = TileSize - 1 - x;
                y = TileSize - 1 - y;
            }
            (x, y) = (y, x);
        }
        return index;
    }

    /// <summary>ξ of the texel (<paramref name="x"/>, <paramref name="y"/>): Slice turns its slices, Step shifts its steps.</summary>
    public static (float Slice, float Step) At(uint x, uint y)
    {
        float index = HilbertIndex(x, y);
        float slice = 0.5f + index * SliceSequence;
        float step = 0.5f + index * StepSequence;
        return (slice - MathF.Floor(slice), step - MathF.Floor(step));
    }
}
