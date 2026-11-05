using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The merge of one frame's instances, in the snapshot's order, with the placement history of the last drawn frame
/// (docs/plans/temporal-renderer.md, "The placement history"). It gives each placed instance its motion row, and writes the
/// frame's own placements into the pending state. The keys of both lists rise, so one walk of each joins them, with no
/// hash.
/// </summary>
/// <param name="frame">The draw's pending state, whose placement history the join reads and whose placements it writes.</param>
/// <param name="previousEye">The last frame's eye relative to this frame's, which moves an instance without an identity.</param>
internal ref struct MotionJoin(FrameHistory.PendingFrame frame, Vector3 previousEye)
{
    private readonly ReadOnlySpan<KeptPlacement> history = frame.PreviousPlacements;
    private readonly FrameHistory.PendingFrame frame = frame;
    private readonly Vector3 previousEye = previousEye;
    private int next;
    private bool any;
    private ulong highest;

    /// <summary>
    /// The key of the snapshot's next instance, or null when it has no identity: its slot and part are not above those of
    /// every instance before it. Call it once for every instance of the snapshot, in order, whether it is placed or not.
    /// </summary>
    public PlacementKey? Identify(in RenderInstance instance)
    {
        var key = new PlacementKey(instance.Entity, instance.Part);
        if (any && key.Order <= highest)
            return null;
        any = true;
        highest = key.Order;
        return key;
    }

    /// <summary>
    /// The motion row of a placed instance of <paramref name="key"/>, which this frame draws with
    /// <paramref name="rows"/>, from static mesh <paramref name="mesh"/>, kept as <paramref name="copy"/>, from vertex
    /// <paramref name="firstVertex"/> of the vertex buffer. An instance with a key is also written into the pending state.
    /// </summary>
    public GpuMotion Place(
        PlacementKey? key,
        (Vector4 Row0, Vector4 Row1, Vector4 Row2) rows,
        int mesh,
        KeptCopy? copy,
        uint firstVertex
    )
    {
        if (key is not { } identity)
            return Still(rows, firstVertex);
        frame.Place(new(Key: identity, Row0: rows.Row0, Row1: rows.Row1, Row2: rows.Row2, Mesh: mesh, Copy: copy));
        if (Find(identity) is not { } last || last.Mesh != mesh)
            return Motion(
                rows: rows,
                kind: MotionKind.New,
                previousFirstVertex: GpuMotion.NoKeptCopy,
                firstVertex: firstVertex
            );
        return Motion(
            rows: (last.Row0, last.Row1, last.Row2),
            kind: MotionKind.Moved,
            // The last frame drew the same static mesh, so a copy that it kept has this copy's vertex count and order: a
            // mesh never changes its vertices. The design's rule that a copy of another vertex count is new then never
            // applies (docs/plans/temporal-renderer.md, "The placement history").
            previousFirstVertex: last.Copy is { } kept ? (uint)kept.FirstVertex : GpuMotion.NoKeptCopy,
            firstVertex: firstVertex
        );
    }

    /// <summary>The last frame's placement of <paramref name="key"/>, or null when it drew no instance of that key.</summary>
    private KeptPlacement? Find(PlacementKey key)
    {
        // The keys of this frame rise, so every key below this one is behind the cursor for good.
        while (next < history.Length && history[next].Key.Order < key.Order)
            next++;
        // Equal records: the same slot and part, and the same generation.
        return next < history.Length && history[next].Key == key ? history[next] : null;
    }

    /// <summary>
    /// The motion row of an instance without an identity, which draws as still in the camera's frame: its rows of this
    /// frame, moved by the eye's step since the last frame.
    /// </summary>
    private readonly GpuMotion Still((Vector4 Row0, Vector4 Row1, Vector4 Row2) rows, uint firstVertex) =>
        Motion(
            rows: (
                rows.Row0 with
                {
                    W = rows.Row0.W - previousEye.X,
                },
                rows.Row1 with
                {
                    W = rows.Row1.W - previousEye.Y,
                },
                rows.Row2 with
                {
                    W = rows.Row2.W - previousEye.Z,
                }
            ),
            kind: MotionKind.Still,
            previousFirstVertex: GpuMotion.NoKeptCopy,
            firstVertex: firstVertex
        );

    private static GpuMotion Motion(
        (Vector4 Row0, Vector4 Row1, Vector4 Row2) rows,
        MotionKind kind,
        uint previousFirstVertex,
        uint firstVertex
    ) =>
        new()
        {
            PreviousRow0 = rows.Row0,
            PreviousRow1 = rows.Row1,
            PreviousRow2 = rows.Row2,
            PreviousFirstVertex = previousFirstVertex,
            Kind = (uint)kind,
            FirstVertex = firstVertex,
        };
}
