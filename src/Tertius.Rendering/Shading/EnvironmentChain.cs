namespace Tertius.Rendering;

/// <summary>
/// The rules of the served chain, an object's map, the draw rule and kept maps, as pure functions
/// (docs/plans/environment-maps-per-frame.md, "The served chain and the map of an object"). Maps are indexed root first.
/// </summary>
internal static class EnvironmentChain
{
    /// <summary>The most maps that the renderer serves: 6 × 42 layers fit the spec's least maxImageArrayLayers (decision 19).</summary>
    public const int MaxMaps = 42;

    /// <summary>M, the number of served maps for a chain of <paramref name="chainLength"/> frames.</summary>
    public static int Maps(int chainLength) => Math.Clamp(chainLength, 1, MaxMaps);

    /// <summary>
    /// Writes the frame ids of the served chain, root first, into <paramref name="served"/> of length
    /// <see cref="Maps"/>: the M frames of <paramref name="chain"/> nearest the camera, or frame 0 for an empty chain.
    /// </summary>
    /// <param name="chain">The camera's chain, camera first, as <see cref="RenderSnapshot.Chain"/> holds it.</param>
    public static void Serve(List<RenderChainFrame> chain, Span<long> served)
    {
        if (chain.Count == 0)
        {
            served[0] = 0;
            return;
        }
        for (int map = 0; map < served.Length; map++)
            served[map] = chain[served.Length - 1 - map].FrameId;
    }

    /// <summary>The map of an object at <paramref name="chainDepth"/>: one deeper than the served chain takes map 0.</summary>
    public static int MapOf(int chainDepth, int maps) => maps - 1 - Math.Min(chainDepth, maps - 1);

    /// <summary>The draw rule: map <paramref name="map"/> draws an object of map <paramref name="objectMap"/>.</summary>
    public static bool Draws(int map, int objectMap) => objectMap <= map;

    /// <summary>
    /// Whether map <paramref name="map"/> of <paramref name="served"/> keeps the faces drawn for
    /// <paramref name="drawnFor"/>: the same frame, and the same frame below it or none (decision 9).
    /// </summary>
    public static bool Keeps(ReadOnlySpan<long> drawnFor, ReadOnlySpan<long> served, int map)
    {
        bool sameFrame = map < drawnFor.Length && drawnFor[map] == served[map];
        int below = map + 1;
        bool sameFrameBelow =
            below < served.Length
                ? below < drawnFor.Length && drawnFor[below] == served[below]
                : drawnFor.Length == served.Length;
        return sameFrame && sameFrameBelow;
    }
}
