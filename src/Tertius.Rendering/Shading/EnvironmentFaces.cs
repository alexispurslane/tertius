namespace Tertius.Rendering;

/// <summary>
/// Which faces of the served maps a drawn frame draws (docs/plans/environment-map.md, "Which faces a frame draws", and
/// docs/plans/environment-maps-per-frame.md, "Which faces a frame draws"). Slot s is face s mod 6 of map ⌊s / 6⌋.
/// </summary>
internal static class EnvironmentFaces
{
    /// <summary>
    /// Clears <paramref name="faces"/>, one mask per served map, then sets the slots (<paramref name="frame"/> · F + k)
    /// mod 6M for k from 0 to F − 1, with F = <paramref name="facesPerFrame"/> from 1 to 6: a pure function of the frame
    /// number (decision 11).
    /// </summary>
    public static void InTurn(long frame, int facesPerFrame, Span<FaceMask> faces)
    {
        faces.Clear();
        long slots = (long)CubeFaces.Count * faces.Length;
        long first = frame * facesPerFrame;
        for (int k = 0; k < facesPerFrame; k++)
        {
            int slot = (int)((first + k) % slots);
            faces[slot / CubeFaces.Count] |= CubeFaces.Of((CubeFace)(slot % CubeFaces.Count));
        }
    }

    /// <summary>
    /// Whether map <paramref name="map"/> draws all six faces: it is not kept from the faces drawn for
    /// <paramref name="drawnFor"/>, or the draw takes a long step (decision 10).
    /// </summary>
    public static bool RefreshesAll(ReadOnlySpan<long> drawnFor, ReadOnlySpan<long> served, int map, bool longStep) =>
        !EnvironmentChain.Keeps(drawnFor, served, map) || longStep;
}
