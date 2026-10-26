namespace Tertius.Rendering;

/// <summary>Which faces a drawn frame draws (docs/plans/environment-map.md, "Which faces a frame draws").</summary>
internal static class EnvironmentFaces
{
    /// <summary>
    /// The faces (<paramref name="frame"/> · F + k) mod 6 for k from 0 to F − 1, with F =
    /// <paramref name="facesPerFrame"/> from 1 to 6: a pure function of the frame number (decision 22).
    /// </summary>
    public static FaceMask InTurn(long frame, int facesPerFrame)
    {
        FaceMask faces = FaceMask.None;
        long first = frame * facesPerFrame;
        for (int k = 0; k < facesPerFrame; k++)
            faces |= CubeFaces.Of((CubeFace)((first + k) % CubeFaces.Count));
        return faces;
    }

    /// <summary>
    /// Whether a frame draws all six faces: the map holds another frame's axes, or none yet, or the draw takes a long
    /// step (decision 23).
    /// </summary>
    public static bool RefreshesAll(long? mapFrameId, long originFrameId, bool longStep) =>
        mapFrameId != originFrameId || longStep;
}
