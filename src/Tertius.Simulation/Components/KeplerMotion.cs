using Tertius.Frames.Rails;

namespace Tertius.Simulation;

/// <summary>
/// Moves its entity along a Kepler orbit around the frame that the entity is in: the owner of a frame, which carries the
/// frame, or any other entity that takes a full sample (see <see cref="Self.MoveOnRails"/>). In each tick it writes the
/// orbit's motion sample for the step in which the tick's intents act, with the G·M of the parent's point gravity. The orbit
/// is an exact function of time, so no error builds up, and the component holds no other state.
/// </summary>
/// <remarks>
/// <para>
/// It checks its own rules, so the World checks none. A component without a valid orbit, which a blueprint that names no
/// orbit makes, because the property's setter never runs, has nothing to follow: it writes no sample, so the entity holds
/// still, and it logs that once. A parent without a point gravity whose G·M is above 0 has nothing to orbit: the component
/// writes no sample, so the entity holds still, and it logs that once. An entity that refuses a full sample in the next
/// step, the one that applies the sample, gets nothing from the component, and the component logs the guard's reason once
/// (see <see cref="Self.MotionIntentProblem"/>). Each log line goes to <see cref="World.Log"/>, never to
/// <see cref="World.Errors"/>, because the game chose the scene. The component reads the parent's G·M in every tick, so an
/// entity that moves to another parent takes the same orbit around it.
/// </para>
/// <para>
/// With its first sample, the component teleports its entity's body to the orbit's pose at the start of the step when the
/// body is kinematic in that step (see <see cref="BodyHandle.NextStepMotion"/>), so that the body jumps onto its orbit and
/// does not sweep through the bodies in between. An entity without a body, and the owner of a root frame, whose body is only
/// the frame's static interior, need no teleport, because the first sample alone puts them on the orbit. A new component on
/// the same entity teleports again.
/// </para>
/// <para>
/// It runs first in its entity (<see cref="Priority"/> -1000), so another component of the entity can stop it in the same
/// tick: a later motion intent wins.
/// </para>
/// </remarks>
public sealed class KeplerMotion : IComponent
{
    private bool started;
    private bool loggedNoOrbit;
    private bool loggedNoGM;
    private bool loggedRefusal;

    /// <summary>
    /// The orbit elements and their epoch, relative to the frame's parent, in its axes. They hold no G·M. Throws
    /// <see cref="ArgumentException"/> for an orbit that is not valid (see <see cref="KeplerOrbit.IsValid"/>).
    /// </summary>
    public KeplerOrbit Orbit
    {
        get;
        init
        {
            if (!value.IsValid)
                throw new ArgumentException(
                    $"A KeplerMotion needs an orbit that it can follow, and {value} is not valid (see KeplerOrbit.IsValid).",
                    nameof(value)
                );
            field = value;
        }
    }

    public int Priority => -1000;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // A blueprint that names no orbit makes the component without the setter, so it keeps the default orbit.
        if (!Orbit.IsValid)
        {
            if (!loggedNoOrbit)
                self.Log(
                    $"KeplerMotion holds {RailsLog.Mover(self)} still: it has no orbit that it can follow (see KeplerOrbit.IsValid)."
                );
            loggedNoOrbit = true;
            return;
        }

        double mu = self.Frame.IsNull ? 0 : KeplerOrbit.MuFrom(self.Snapshot.Frames.Gravity(self.Frame));
        if (!(mu > 0))
        {
            if (!loggedNoGM)
                self.Log(
                    $"KeplerMotion holds {RailsLog.Mover(self)} still: its parent {self.Frame} has no point gravity with a G·M above 0 to orbit."
                );
            loggedNoGM = true;
            return;
        }

        MotionSample sample = Orbit.Sample(mu, self.TimeAt(self.Tick + 2), self.DeltaTime);
        var move = new MotionIntent(sample);
        if (!self.TakesMotionIntent(move))
        {
            // The reason allocates, so the component words it only for its one log line.
            if (!loggedRefusal)
                self.Log(
                    $"KeplerMotion writes no sample for {RailsLog.Mover(self)}: {self.Entity} {self.MotionIntentProblem(move)}."
                );
            loggedRefusal = true;
            return;
        }

        if (!started && self.Body.NextStepMotion == BodyMotion.Kinematic)
        {
            double stepStart = self.TimeAt(self.Tick + 1);
            self.Body.Teleport(new(Orbit.StateAt(stepStart, mu).Position, Orbit.RotationAt(stepStart, mu)));
        }

        started = true;
        self.MoveOnRails(sample);
    }
}

/// <summary>What the log lines of a rails component name.</summary>
internal static class RailsLog
{
    /// <summary>What the rails component of <paramref name="self"/> moves: the frame that the entity owns, or else the entity.</summary>
    public static string Mover(Self self) =>
        self.OwnedFrame.IsNull ? self.Entity.ToString() : self.OwnedFrame.ToString();
}
