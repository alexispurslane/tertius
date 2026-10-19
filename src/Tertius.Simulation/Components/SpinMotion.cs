using Tertius.Frames.Rails;

namespace Tertius.Simulation;

/// <summary>
/// Turns its entity in place, as the ground of a planet turns: the owner of a frame, which carries the frame, or any other
/// entity that takes a full sample (see <see cref="Self.MoveOnRails"/>). In each tick it writes the spin's motion sample for
/// the step in which the tick's intents act, at the place in its parent where the entity stands when that step starts. The
/// spin is an exact function of time, so no error builds up. The sample holds no proper acceleration: the frame tree derives
/// it from the frame's motion and the gravity at its place, so a turntable on the ground gives its cargo weight with no value
/// to copy (see <see cref="FrameTree.ProperAcceleration"/>).
/// </summary>
/// <remarks>
/// <para>
/// An entity that refuses a full sample in the next step, the one that applies the sample, gets nothing from the component,
/// and the component logs the guard's reason once (see <see cref="Self.MotionIntentProblem"/>), to <see cref="World.Log"/>,
/// never to <see cref="World.Errors"/>. With its first sample, the component teleports its entity's body to the spin's pose
/// at the start of the step when the body is kinematic in that step (see <see cref="BodyHandle.NextStepMotion"/>). An entity
/// without a body, and the owner of a root frame, need no teleport. A new component on the same entity teleports again.
/// </para>
/// <para>
/// It runs first in its entity (<see cref="Priority"/> -1000), so another component of the entity can stop it in the same
/// tick: a later motion intent wins.
/// </para>
/// </remarks>
public sealed class SpinMotion : IComponent
{
    private readonly FrameSpin spin;
    private bool started;
    private bool loggedRefusal;

    /// <summary>
    /// How the frame turns relative to its parent. Throws <see cref="ArgumentException"/> for a spin that is not valid (see
    /// <see cref="FrameSpin.IsValid"/>).
    /// </summary>
    public FrameSpin Spin
    {
        get => spin;
        init
        {
            if (!value.IsValid)
                throw new ArgumentException($"A SpinMotion needs a spin that it can follow, and {value} is not valid (see FrameSpin.IsValid).", nameof(value));
            spin = value;
        }
    }

    public int Priority => -1000;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Double3 place = PlaceAtStepStart(self);
        MotionSample sample = spin.Sample(place, self.TimeAt(self.Tick + 2), self.DeltaTime);
        if (self.MotionIntentProblem(new MotionIntent(sample)) is { } problem)
        {
            if (!loggedRefusal)
                self.Log($"SpinMotion writes no sample for {RailsLog.Mover(self)}: {self.Entity} {problem}.");
            loggedRefusal = true;
            return;
        }
        if (!started && self.Body.NextStepMotion == BodyMotion.Kinematic)
            self.Body.Teleport(new DPose(place, spin.RotationAt(self.TimeAt(self.Tick + 1))));
        started = true;
        self.MoveOnRails(sample);
    }

    /// <summary>
    /// The place in its parent where the entity stands at the start of the step that this tick's sample describes, which is
    /// the end of the step that runs beside this tick. A motion intent that the entity follows in that step puts it at its
    /// end, so the spin starts where the motion before it, such as the last sample of a component that this one replaced,
    /// leaves the entity. Without one the entity holds still in that step, at its place now. The read is public (see
    /// <see cref="Self.PoseAtNextStepStartIn"/>), so a mod's rails component can do the same.
    /// </summary>
    private static Double3 PlaceAtStepStart(Self self) => self.PoseAtNextStepStartIn(self.Frame).Position;
}
