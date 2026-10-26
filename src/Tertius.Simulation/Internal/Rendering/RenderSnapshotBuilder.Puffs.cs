using System.Runtime.InteropServices;

namespace Tertius.Simulation;

internal sealed partial class RenderSnapshotBuilder
{
    /// <summary>
    /// Places the puffs of <paramref name="puffs"/> into <paramref name="snapshot"/>, which <see cref="Build"/> filled in
    /// the same build, with the frame poses of that build. Each range is placed through the pose of its frame relative to
    /// the camera's physics home, less the render origin, as an instance is, so a puff near the camera is exact however far
    /// it is from its frame's origin. The snapshot's puffs keep the order of the rows, and its looks are the view's look
    /// table, with its free entries, by index.
    /// </summary>
    /// <remarks>
    /// A puff needs no memory of the last build. Its place at the end of the step before is its place one chord back,
    /// <c>Place − Chord · Step</c>, in the same frame. That place, through the frame's pose of the previous build, is the
    /// puff's previous position when this build interpolates and the previous build was of the tick before. Otherwise the
    /// previous position is the position, and the age step is 0: in the first build, after a camera cut, in a second build
    /// at the same tick (a headless capture) and after a build that skipped ticks. A snapshot without a camera gets no
    /// puffs and no looks. The call fills the snapshot's puffs, looks and age step anew, so a second call in the same build
    /// gives the same snapshot.
    /// </remarks>
    internal void BuildPuffs(RenderSnapshot snapshot, in PuffView puffs)
    {
        // Build cleared the snapshot, so without a camera it keeps no puffs, no looks and an age step of 0.
        if (!renderHasCamera)
            return;

        bool followsTick = renderFollowsTick;
        double step = puffs.Step;
        snapshot.PuffAgeStep = followsTick ? (float)step : 0;
        CollectionsMarshal.SetCount(snapshot.PuffLooks, puffs.Looks.Length);
        puffs.Looks.CopyTo(CollectionsMarshal.AsSpan(snapshot.PuffLooks));

        ReadOnlySpan<Puff> rows = puffs.Rows;
        CollectionsMarshal.SetCount(snapshot.Puffs, rows.Length);
        Span<RenderPuff> placed = CollectionsMarshal.AsSpan(snapshot.Puffs);
        Double3 origin = renderOrigin;
        int count = 0;
        foreach (PuffRange range in puffs.Ranges)
        {
            // After a sync point every range is in a live frame, which this build placed. So that only guards against a
            // broken table, as TryGetRenderPoses does for an entity.
            if (!FrameSeen(range.Frame))
                continue;
            // The frame's poses relative to the camera's home are the identity or results of DPose.Compose, so their
            // rotations are unit length, as TransformPoint needs.
            int dense = denseOfFrame[range.Frame.Index];
            DPose current = relCurrent[dense];
            DPose previous = relPrevious[dense];
            ushort depth = (ushort)chainDepth[dense];
            foreach (ref readonly Puff row in rows.Slice(range.Start, range.Count))
            {
                var position = (current.TransformPoint(row.Place) - origin).ToVector3();
                ref RenderPuff puff = ref placed[count++];
                puff.Position = position;
                puff.PreviousPosition = followsTick
                    ? (previous.TransformPoint(row.Place - row.Chord * step) - origin).ToVector3()
                    : position;
                puff.Age = row.Age;
                puff.Look = row.Look;
                puff.ChainDepth = depth;
            }
        }

        CollectionsMarshal.SetCount(snapshot.Puffs, count);
    }
}
