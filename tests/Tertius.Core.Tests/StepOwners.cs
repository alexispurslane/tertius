using System.Numerics;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// What the World knows about the owner of each frame at step 2 of a sync point, as a test of the frame tree gives it (see
/// <see cref="FrameTree.TakeStepRecords"/>): the records that physics released for hulls, the owners whose update the check
/// of the physics output skipped, and the transform writes of the tick, each relative to the owner's frame, the frame's
/// semantic parent. A new one holds none of them.
/// </summary>
internal readonly struct StepOwners() : IFrameOwnerFacts
{
    public Dictionary<FrameHandle, FrameStepRecord> HullRecords { get; } = [];
    public HashSet<FrameHandle> Skipped { get; } = [];
    public Dictionary<FrameHandle, DPose> Writes { get; } = [];

    public bool TryGetHullRecord(FrameHandle frame, out FrameStepRecord record) =>
        HullRecords.TryGetValue(frame, out record);

    public bool IsSkipped(FrameHandle frame) => Skipped.Contains(frame);

    public bool TryGetTransformWrite(FrameHandle frame, out DPose pose) => Writes.TryGetValue(frame, out pose);

    /// <summary>A step record that physics released for the hull of <paramref name="frame"/>, with no measured proper acceleration.</summary>
    public StepOwners WithHullRecord(FrameHandle frame, in MotionSample record, Vector3? measured = null)
    {
        HullRecords[frame] = new(frame, record, measured);
        return this;
    }
}

/// <summary>Step 2 of a sync point on a tree built in a test.</summary>
internal static class StepTwo
{
    /// <summary>
    /// Every frame of <paramref name="frames"/> takes its step record of a step of <paramref name="step"/> seconds that
    /// applied <paramref name="stepIntents"/>, with what <paramref name="owners"/> says about the owners, or with no physics
    /// records, skips or writes (see <see cref="FrameTree.TakeStepRecords"/>).
    /// </summary>
    public static void TakeStepRecords(
        this FrameTree frames,
        ReadOnlySpan<PhysicsIntent> stepIntents,
        double step,
        StepOwners? owners = null,
        bool stepFailed = false
    )
    {
        StepOwners facts = owners ?? new StepOwners();
        frames.TakeStepRecords(owners: ref facts, stepIntents: stepIntents, stepFailed: stepFailed, step: step);
    }
}
