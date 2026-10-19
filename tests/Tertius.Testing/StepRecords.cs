using System.Numerics;
using Tertius.Frames.Rails;

namespace Tertius.Testing;

/// <summary>Step records of frames that a test writes into the output of a step.</summary>
public static class StepRecords
{
    /// <summary>
    /// The step record of <paramref name="frame"/> at <paramref name="pose"/> in its tree parent, with
    /// <paramref name="velocity"/> as both its chord and its instant velocity, <paramref name="angularVelocity"/>, no
    /// acceleration, and <paramref name="measured"/> as the proper acceleration that physics measured, which only a dynamic
    /// hull has.
    /// </summary>
    public static FrameStepRecord Of(FrameHandle frame, in DPose pose, Double3 velocity = default, Vector3 angularVelocity = default,
        Vector3? measured = null) =>
        new(frame, new MotionSample(pose, velocity, velocity, Double3.Zero, angularVelocity, Double3.Zero), measured);
}
