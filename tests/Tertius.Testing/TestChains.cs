using System.Numerics;

namespace Tertius.Testing;

/// <summary>
/// The camera's chain in a hand-built render snapshot (docs/plans/environment-maps-per-frame.md). A test names each
/// object's chain depth and each chain frame, as the snapshot builder does, so that no default stands in for them.
/// </summary>
public static class TestChains
{
    /// <summary>The chain depth of an object in the camera's frame.</summary>
    public const ushort CamerasFrame = 0;

    /// <summary>
    /// The chain depth of an object in the root, whatever chain the test gives its snapshot, even one it gives after it
    /// adds the object: the deepest depth. A depth deeper than the served chain takes map 0, as the root does.
    /// </summary>
    public const ushort Root = ushort.MaxValue;

    /// <summary>
    /// Replaces the chain of <paramref name="snapshot"/> with <paramref name="frames"/>, camera first. Each frame keeps its
    /// rotation from the previous build, as in a build that does not interpolate.
    /// </summary>
    /// <param name="frames">Each frame's id, and its axes in the axes of the camera's frame.</param>
    public static void Of(RenderSnapshot snapshot, params ReadOnlySpan<(long Id, Quaternion Rotation)> frames)
    {
        snapshot.Chain.Clear();
        foreach ((long id, Quaternion rotation) in frames)
            snapshot.Chain.Add(new(FrameId: id, Rotation: rotation, PreviousRotation: rotation));
    }

    /// <summary>
    /// Gives <paramref name="snapshot"/> the chain of a camera in a frame of id 1 whose root, of id 0, has its axes turned
    /// by <paramref name="root"/> in the camera's frame's axes.
    /// </summary>
    public static void UnderTurnedRoot(RenderSnapshot snapshot, Quaternion root) =>
        Of(snapshot, (1, Quaternion.Identity), (0, root));
}
