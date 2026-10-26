namespace Tertius.Rendering;

/// <summary>
/// The map code in the alpha of the G-buffer's base color, the C# twin of mapCode and mapOfCode in the shaders
/// (docs/plans/environment-maps-per-frame.md, decision 20). Map i owns the 8-bit codes 255 − 2i and 256 − 2i, and code 0
/// marks an unlit pixel.
/// </summary>
internal static class MapCode
{
    /// <summary>
    /// The alpha that map <paramref name="map"/> writes: between its two codes, so either rounding that Vulkan allows names
    /// it. Map 0 writes exactly 1.
    /// </summary>
    public static float Of(int map) => MathF.Min((255.5f - 2f * map) / 255f, 1f);

    /// <summary>The map that the stored code <paramref name="code"/>, from 1 to 255, names.</summary>
    public static int MapOf(int code) => (256 - code) >> 1;
}
