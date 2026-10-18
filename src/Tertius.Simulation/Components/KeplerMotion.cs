using Tertius.Frames.Rails;

namespace Tertius.Simulation;

/// <summary>
/// Moves the frame that its entity owns along a Kepler orbit around the frame's parent. In each tick it writes the orbit's
/// motion sample for the step in which the tick's intents act (see <see cref="Self.MoveOnRails"/>), with the G·M of the
/// parent's point gravity. The orbit is an exact function of time, so no error builds up, and the component holds no other
/// state.
/// </summary>
/// <remarks>
/// <para>
/// It checks its own rules, so the World checks none. A parent without a point gravity whose G·M is above 0 has nothing to
/// orbit: the component writes no sample, so the frame holds still, and it logs that once. A hull that is not kinematic
/// cannot follow a sample: the component writes none and logs that once. Each log line goes to <see cref="World.Log"/>,
/// never to <see cref="World.Errors"/>, because the game chose the scene. The component reads the parent's G·M in every
/// tick, so a frame that moves to another parent takes the same orbit around it.
/// </para>
/// <para>
/// With its first sample, the component teleports its entity's hull to the orbit's pose at the start of the step, so that
/// the hull jumps onto its orbit and does not sweep through the bodies in between. A frame without a body needs no teleport,
/// because its first sample alone puts it on its orbit. A new component on the same entity teleports again.
/// </para>
/// <para>
/// It runs first in its entity (<see cref="Priority"/> -1000), so another component of the entity can stop it in the same
/// tick: a later <see cref="Self.MoveOnRails"/> wins.
/// </para>
/// </remarks>
public sealed class KeplerMotion : IComponent
{
    private readonly KeplerOrbit orbit;
    private bool started;
    private bool loggedNoGM;
    private bool loggedNotKinematic;

    /// <summary>
    /// The orbit elements and their epoch, relative to the frame's parent, in its axes. They hold no G·M. Throws
    /// <see cref="ArgumentException"/> for an orbit that is not valid (see <see cref="KeplerOrbit.IsValid"/>).
    /// </summary>
    public KeplerOrbit Orbit
    {
        get => orbit;
        init
        {
            if (!value.IsValid)
                throw new ArgumentException($"A KeplerMotion needs an orbit that it can follow, and {value} is not valid (see KeplerOrbit.IsValid).", nameof(value));
            orbit = value;
        }
    }

    public int Priority => -1000;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        double mu = self.Frame.IsNull ? 0 : KeplerOrbit.MuFrom(self.Snapshot.Frames.Gravity(self.Frame));
        if (!(mu > 0))
        {
            if (!loggedNoGM)
                self.Log($"KeplerMotion holds {self.OwnedFrame} still: its parent {self.Frame} has no point gravity with a G·M above 0 to orbit.");
            loggedNoGM = true;
            return;
        }
        if (RailsHull.IsNotKinematic(self))
        {
            if (!loggedNotKinematic)
                self.Log($"KeplerMotion writes no sample for {self.OwnedFrame}: the hull of {self.Entity} is not kinematic, so it cannot follow one.");
            loggedNotKinematic = true;
            return;
        }
        if (!started && RailsHull.Exists(self))
        {
            double stepStart = self.TimeAt(self.Tick + 1);
            self.Body.Teleport(new DPose(orbit.StateAt(stepStart, mu).Position, orbit.RotationAt(stepStart, mu)));
        }
        started = true;
        self.MoveOnRails(orbit.Sample(mu, self.TimeAt(self.Tick + 2), self.DeltaTime));
    }
}

/// <summary>The rules of a rails component about the hull of its entity: the body of the owner of a frame that has a parent.</summary>
internal static class RailsHull
{
    /// <summary>True when the running entity owns a frame with a parent and has a body: that body is the frame's hull.</summary>
    public static bool Exists(Self self) => !self.Frame.IsNull && self.Snapshot.TryGetBody(self.Entity, out _);

    /// <summary>
    /// True when the running entity has a hull that is not kinematic, which <see cref="Self.MoveOnRails"/> refuses. A root
    /// frame has no parent, so its owner's body is only the frame's static interior, not a hull.
    /// </summary>
    public static bool IsNotKinematic(Self self) =>
        !self.Frame.IsNull && self.Snapshot.TryGetBody(self.Entity, out BodyFacts body) && body.Motion != BodyMotion.Kinematic;
}
