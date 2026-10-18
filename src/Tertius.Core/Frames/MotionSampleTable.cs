using Tertius.Frames.Rails;

namespace Tertius;

/// <summary>
/// The motion samples of the frames, by frame index, in three buffers. A sample is one step of rails motion that the owner
/// of a frame wrote with <c>Self.MoveOnRails</c> (see <see cref="MotionSample"/>), or the step record that a frame took at a
/// sync point (see <see cref="Record"/>). The tree that owns the table sizes it
/// with its own rows (see <see cref="FrameTree.Samples"/>).
/// <list type="bullet">
/// <item><see cref="Written"/>: the samples that gameplay writes during the running tick.</item>
/// <item><see cref="Applied"/>: the samples that the physics step of the running tick applies, which gameplay wrote one
/// tick before. Physics gets this buffer in its step context.</item>
/// <item><see cref="Followed"/>: the samples of the step that ended at the last sync point, which the frames follow.</item>
/// </list>
/// <para>
/// Each row holds the frame that its sample moves, so a lookup checks the frame's generation, and a frame that reuses the
/// index of a dead frame has no sample. It also holds the frame that the sample is relative to: the semantic parent of the
/// frame when its owner wrote it, which the World needs when it converts samples into another frame. And it holds what
/// made it, a <see cref="FollowKind"/>: <see cref="FollowKind.Rails"/> for a sample that the owner wrote, and for a record
/// what moved the frame in the step (see <see cref="Record"/>). So the followed buffer tells how each frame followed its
/// owner at the last sync point (see <see cref="FrameTree.Followed"/>).
/// </para>
/// <para>
/// Each buffer has a round: the number of the turn of <see cref="MoveOn"/> in which it was the written buffer. A row
/// belongs to its buffer only when it was written in the buffer's round, so a buffer that becomes the written buffer
/// again is empty at once, and a turn costs nothing per row. The rounds of the three buffers are always three numbers in
/// a row, and a row that no one wrote has round 0, which no buffer has.
/// </para>
/// <para>
/// Only the worker of a frame's owner writes the frame's row in the written buffer, during gameplay. Physics reads the
/// applied buffer during the same tick. The World moves the buffers on and converts their rows at the sync point and
/// outside a tick, when nothing else runs.
/// </para>
/// </summary>
internal sealed class MotionSampleTable
{
    private readonly FrameTree frames;
    private Row[] writtenRows;
    private Row[] appliedRows;
    private Row[] followedRows;
    // The round of the written buffer. The applied buffer's round is one less, and the followed buffer's two less.
    private long round = 3;

    /// <param name="frames">The tree that owns the table, whose frames the samples move.</param>
    /// <param name="capacity">The number of frame indexes that each buffer has a row for at first.</param>
    internal MotionSampleTable(FrameTree frames, int capacity)
    {
        this.frames = frames;
        writtenRows = new Row[capacity];
        appliedRows = new Row[capacity];
        followedRows = new Row[capacity];
    }

    /// <summary>The samples that gameplay writes during the running tick.</summary>
    internal MotionSamples Written => new(writtenRows, round);

    /// <summary>The samples that the physics step of the running tick applies, which gameplay wrote one tick before.</summary>
    internal MotionSamples Applied => new(appliedRows, round - 1);

    /// <summary>The samples of the step that ended at the last sync point, which the frames follow.</summary>
    internal MotionSamples Followed => new(followedRows, round - 2);

    /// <summary>
    /// Writes the sample of <paramref name="frame"/> for the running tick, relative to <paramref name="relativeTo"/>, the
    /// frame's semantic parent, in its axes. The last write in a tick wins. Worker-safe for the worker of the frame's owner.
    /// </summary>
    internal void Write(FrameHandle frame, FrameHandle relativeTo, in MotionSample sample)
    {
        Contract.Requires(frames.IsAlive(frame));
        writtenRows[frame.Index] = new Row(frame, relativeTo, sample, round, FollowKind.Rails);
    }

    /// <summary>
    /// Step 2 of the sync point: puts the step record that <paramref name="frame"/> took, relative to
    /// <paramref name="relativeTo"/>, into the frame's slot of the applied buffer, unless the slot holds a sample of the frame
    /// already, which is then the record (see "One motion record per frame" in docs/plans/rails-components.md). So after
    /// <see cref="MoveOn"/>, the followed buffer holds the record of every frame that moved in the step. Returns true when it
    /// put the record there.
    /// <para>
    /// The record of a frame whose hull is dynamic is the move that physics made (<see cref="FollowKind.Physics"/>). Any other
    /// record is a teleport of the frame's hull or a write of its owner's transform, which a static frame follows
    /// (<see cref="FollowKind.Static"/>). Step 4 changes the motions of the hulls after this step, so the motion here is that
    /// of the step that ended.
    /// </para>
    /// </summary>
    internal bool Record(FrameHandle frame, FrameHandle relativeTo, in MotionSample record)
    {
        Contract.Requires(frames.IsAlive(frame));
        ref Row row = ref appliedRows[frame.Index];
        if (row.Round == round - 1 && row.Frame == frame)
            return false;
        FollowKind kind = frames.HullMotion(frame) == BodyMotion.Dynamic ? FollowKind.Physics : FollowKind.Static;
        row = new Row(frame, relativeTo, record, round - 1, kind);
        return true;
    }

    /// <summary>
    /// How <paramref name="frame"/> followed its owner at the last sync point: the kind of its row in the followed buffer, or
    /// <see cref="FollowKind.Static"/> when it has none.
    /// </summary>
    internal FollowKind KindFollowed(FrameHandle frame)
    {
        ref readonly Row row = ref followedRows[frame.Index];
        return row.Round == round - 2 && row.Frame == frame ? row.Kind : FollowKind.Static;
    }

    /// <summary>
    /// Step 4 of the sync point: the applied buffer becomes the followed buffer, the written buffer becomes the applied
    /// buffer, and the written buffer starts empty, in the rows of the old followed buffer.
    /// </summary>
    internal void MoveOn()
    {
        (writtenRows, appliedRows, followedRows) = (followedRows, writtenRows, appliedRows);
        round++;
    }

    /// <summary>Drops the sample that the owner of <paramref name="frame"/> wrote in the running tick, so that the next step applies none.</summary>
    internal void DropWritten(FrameHandle frame) => Clear(writtenRows, frame);

    /// <summary>
    /// Converts the applied and the followed sample of <paramref name="frame"/>, each when it is relative to another frame
    /// than <paramref name="to"/>, into <paramref name="to"/>, with the relation of the two frames now. The pose, the chord
    /// velocity and the instant velocity at the sample's place at the end of its step, the angular velocity and the angular
    /// acceleration convert with the motion of one frame relative to the other, by the rule of a hand-off (see
    /// <see cref="FrameMotion"/>). The acceleration converts with the acceleration of one frame relative to the other (see
    /// <see cref="FrameTree.AccelerationRelative"/>). The proper acceleration is in the frame's own axes, so it stays. Both
    /// frames must be live: the World converts the samples of the frames in a frame that dies before it dies.
    /// </summary>
    internal void ConvertInto(FrameHandle frame, FrameHandle to)
    {
        Convert(appliedRows, round - 1, frame, to);
        Convert(followedRows, round - 2, frame, to);
    }

    /// <summary>Gives each buffer a row for every frame index below <paramref name="capacity"/>.</summary>
    internal void Grow(int capacity)
    {
        // FrameTree.Grow calls this only to grow, so a smaller size, which would drop samples, is a bug.
        Contract.Requires(capacity > writtenRows.Length);
        Array.Resize(ref writtenRows, capacity);
        Array.Resize(ref appliedRows, capacity);
        Array.Resize(ref followedRows, capacity);
    }

    private static void Clear(Row[] rows, FrameHandle frame)
    {
        if (rows[frame.Index].Frame == frame)
            rows[frame.Index] = default;
    }

    private void Convert(Row[] rows, long bufferRound, FrameHandle frame, FrameHandle to)
    {
        ref Row row = ref rows[frame.Index];
        if (row.Round != bufferRound || row.Frame != frame || row.RelativeTo == to)
            return;
        FrameHandle from = row.RelativeTo;
        Contract.Requires(frames.IsAlive(from) && frames.IsAlive(to));
        // The motion of the old relative frame in the new one now, by which the sample converts as a body that hands off
        // between the two frames does.
        var motion = new FrameMotion(frames.Relative(from, to), frames.ConvertVelocity(from, to, Double3.Zero, Double3.Zero),
            frames.AngularVelocityRelative(from, to));
        MotionSample sample = row.Sample;
        Double3 place = sample.Pose.Position;
        var converted = new MotionSample(
            motion.PoseToParent(sample.Pose),
            motion.VelocityToParent(place, sample.ChordVelocity),
            motion.VelocityToParent(place, sample.Velocity),
            motion.Pose.TransformDirection(sample.Acceleration) + frames.AccelerationRelative(from, to),
            motion.AngularToParent(sample.AngularVelocity),
            motion.AngularAccelerationToParent(sample.AngularAcceleration),
            sample.ProperAcceleration);
        row = row with { RelativeTo = to, Sample = converted };
    }

    /// <summary>
    /// A row of a buffer: the sample of <paramref name="Frame"/>, relative to <paramref name="RelativeTo"/>, written in the
    /// round <paramref name="Round"/>, which <paramref name="Kind"/> made.
    /// </summary>
    internal readonly record struct Row(FrameHandle Frame, FrameHandle RelativeTo, MotionSample Sample, long Round, FollowKind Kind);
}

/// <summary>
/// One buffer of the sample table, for reading: the motion samples of the frames, by frame. Each sample is relative to the
/// semantic parent of its frame when its owner wrote it, in the parent's axes, except its proper acceleration, which is in
/// the frame's own axes. The default has no sample.
/// </summary>
public readonly struct MotionSamples
{
    private readonly MotionSampleTable.Row[]? rows;
    private readonly long round;

    internal MotionSamples(MotionSampleTable.Row[] rows, long round)
    {
        this.rows = rows;
        this.round = round;
    }

    /// <summary>The sample of <paramref name="frame"/>, or false when it has none. False for a stale or null frame.</summary>
    public bool TryGet(FrameHandle frame, out MotionSample sample) => TryGet(frame, out sample, out _);

    /// <summary>
    /// The sample of <paramref name="frame"/> and the frame that it is relative to, or false when it has none. False for a
    /// stale or null frame.
    /// </summary>
    public bool TryGet(FrameHandle frame, out MotionSample sample, out FrameHandle relativeTo)
    {
        if (rows is not null && !frame.IsNull && (uint)frame.Index < (uint)rows.Length)
        {
            ref readonly MotionSampleTable.Row row = ref rows[frame.Index];
            if (row.Round == round && row.Frame == frame)
            {
                sample = row.Sample;
                relativeTo = row.RelativeTo;
                return true;
            }
        }
        sample = default;
        relativeTo = default;
        return false;
    }
}
