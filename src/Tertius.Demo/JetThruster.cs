using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// One of the shuttle's jets, on its part of the hull (docs/plans/blue-danube-demo.md, "The jets as parts"): in each tick
/// that brings a <see cref="Throttle"/>, it pushes along its <see cref="Jet"/>'s push, turned by the hull's rotation in its
/// frame, with the throttle's value times the jet's thrust (see <see cref="Jet.Force"/>), and blows its cold gas,
/// <see cref="Plume"/>, at that throttle. The engine carries the push to the hull's body at the part's place. A tick
/// without a throttle gives no thrust and no puffs, so the jet keeps nothing from an earlier tick. When a tick brings
/// several, the last one counts. A throttle outside 0 to 1, or NaN, makes the jet throw
/// <see cref="ArgumentOutOfRangeException"/>, which the world logs as an error, and push nothing and blow nothing in that
/// tick.
/// </summary>
/// <remarks>
/// The <see cref="ShuttleJetControl"/> of the hull spawns the part with its -Z turned against the jet's push, so that its
/// puffs leave against the push, and sends it its pair's throttle by message. The two jets of a couple push along exact
/// negatives of one axis of the hull with one thrust, and both read the hull's rotation
/// (<see cref="Plume.PushedBodyRotation"/>), so at one throttle their forces are exact negatives. The jet reads that
/// rotation from the pose at the end of the last finished step, and its push acts in the physics step of the next tick, as
/// every force does. The emitter that it writes in the tick makes the puffs of the same step.
/// </remarks>
/// <param name="jet">The jet: its place on the hull, the hull axis that it pushes along, and its thrust.</param>
public sealed class JetThruster(Jet jet) : IComponent
{
    /// <summary>
    /// The jets' exhaust: cold gas, at three quarters of the air's temperature, that the sun and the lamps light pale gray
    /// and that lives 3 s, from 0.6 m across to 6 m. At full throttle 50 puffs leave each second at 20 m/s
    /// (docs/plans/blue-danube-demo.md, "The plumes").
    /// </summary>
    public static Plume Plume { get; } = new(
        new PuffLook(Life: 3, StartRadius: 0.3f, EndRadius: 3, TemperatureRatio: 0.75f, PuffShading.Lit(new Vector3(0.9f, 0.92f, 0.95f))),
        FullRate: 50, Speed: 20, Spread: 0.2f);

    /// <summary>The jet that this part is.</summary>
    public Jet Jet { get; } = jet;

    public int Priority => 10;

    // Tick brings the jet into every tick, so that a tick without a throttle clears its emitter.
    public EventMask Wants => EventMask.Of<Throttle>().And<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        float throttle = 0;
        foreach (ref Throttle each in events.Of<Throttle>())
            throttle = each.Value;
        bool valid = throttle is >= 0 and <= 1;
        self.Render.Emitter = valid ? Plume.At(throttle) : default;
        if (!valid)
            throw new ArgumentOutOfRangeException(nameof(Throttle), throttle, "A throttle lies from 0 to 1.");
        if (throttle > 0)
            self.Body.AddForce(Jet.Force(Plume.PushedBodyRotation(self), throttle).ToVector3());
    }
}
