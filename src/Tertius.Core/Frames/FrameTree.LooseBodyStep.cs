namespace Tertius;

public sealed partial class FrameTree
{
    /// <summary>
    /// What the last finished step of <paramref name="frame"/> did to a loose body in it (see <see cref="LooseBodyStep"/>):
    /// the frame's gravity field, its proper acceleration as the step found it, its turns, the length of the step of the
    /// turns and its medium. It is valid from step 2 of a sync point, when the turns and the proper acceleration that the
    /// step found describe the step that ended, to the <see cref="BeginStep"/> of the next tick. The step of the puffs and
    /// their births read it once per frame. It requires a semantic frame, because a puff never lives in a numeric frame.
    /// Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    internal LooseBodyStep LastLooseBodyStep(FrameHandle frame)
    {
        int id = Node(frame).Id;
        Contract.Requires(numeric[id] == NumericMode.None);
        return new LooseBodyStep
        {
            Field = gravity[id],
            ProperAcceleration = properAccelerationTheStepFound[id],
            Turns = turns[id],
            TurnsStep = turnsStep,
            Medium = medium[id],
        };
    }
}
