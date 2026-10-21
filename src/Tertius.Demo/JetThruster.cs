using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// One of the shuttle's jets, on its part of the hull (docs/plans/blue-danube-demo.md, "The jets as parts"): in each tick
/// that brings a <see cref="Throttle"/>, it pushes along its <see cref="Jet"/>'s push, turned by its own rotation in its
/// frame, with the throttle's value times the jet's thrust (see <see cref="Jet.Force"/>). The engine carries the push to
/// the hull's body at the part's place. A tick without a throttle gives no thrust, so the jet keeps nothing from an earlier
/// tick. When a tick brings several, the last one counts. A throttle outside 0 to 1, or NaN, makes the jet throw
/// <see cref="ArgumentOutOfRangeException"/>, which the world logs as an error, and push nothing in that tick.
/// </summary>
/// <remarks>
/// The <see cref="ShuttleJetControl"/> of the hull spawns the part with the hull's rotation and sends it its pair's
/// throttle by message. The two jets of a couple push along exact negatives of one axis with one thrust, so at one
/// throttle and one rotation their forces are exact negatives. The jet reads its rotation from the pose at the end of the
/// last finished step, and its push acts in the physics step of the next tick, as every force does.
/// </remarks>
/// <param name="jet">The jet: its place on the hull, the hull axis that it pushes along, and its thrust.</param>
public sealed class JetThruster(Jet jet) : IComponent
{
    /// <summary>The jet that this part is.</summary>
    public Jet Jet { get; } = jet;

    public int Priority => 10;

    public EventMask Wants => EventMask.Of<Throttle>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // The jet wants only Throttle, so the events hold at least one, and the last one counts.
        float throttle = 0;
        foreach (ref Throttle each in events.Of<Throttle>())
            throttle = each.Value;
        if (throttle is not (>= 0 and <= 1))
            throw new ArgumentOutOfRangeException(nameof(Throttle), throttle, "A throttle lies from 0 to 1.");
        if (throttle > 0)
            self.Body.AddForce(Jet.Force(self.PoseIn(self.Frame).Rotation, throttle).ToVector3());
    }
}
