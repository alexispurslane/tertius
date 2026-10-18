namespace Tertius.Core.Tests;

/// <summary>
/// The intents of one physics step, by entity slot (<see cref="Entity.Index"/>), as <see cref="PhysicsStepContext.Intents"/>
/// holds them, for the reads of the frame tree that derive the motion of a hull in a step (see
/// <see cref="FrameTree.RunningStepHullMotion"/>).
/// </summary>
internal static class StepIntents
{
    /// <summary>
    /// The intents of a step in which the owner of <paramref name="frame"/> changes its hull's motion to
    /// <paramref name="motion"/>, and no other entity writes an intent.
    /// </summary>
    public static PhysicsIntent[] Changing(FrameHandle frame, BodyMotion motion)
    {
        var intents = new PhysicsIntent[frame.Entity.Index + 1];
        intents[frame.Entity.Index] = new PhysicsIntent { Motion = motion, Flags = IntentFlags.SetMotion };
        return intents;
    }
}
