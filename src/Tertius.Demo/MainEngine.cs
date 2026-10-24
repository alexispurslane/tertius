using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The throttle of an engine or a jet for one tick. A layer of the flight program adds the main engine's throttle to the
/// hull's events (see <see cref="EventList.Add{T}"/>), for the <see cref="ShuttleJetControl"/> of the same entity, which
/// runs after it and sends it as a message to the main engine's part. The control sends each jet's part the throttle of its
/// pair the same way.
/// </summary>
public struct Throttle : IEventPayload
{
    /// <summary>The part of the engine's or the jet's full thrust that it gives in the tick, from 0, off, to 1, full.</summary>
    public float Value;
}

/// <summary>
/// The main engine of a hull: in each tick that brings a <see cref="Throttle"/>, it pushes along the nose, the -Z of the
/// body that it pushes, with the throttle's value times <see cref="MaxThrust"/>, and blows its hot, glowing exhaust,
/// <see cref="Plume"/>, at that throttle. On the shuttle it runs on the main engine's part, an attached entity of the hull
/// on the line of thrust through the center of mass (see <see cref="ShuttleJetControl"/>), and the engine carries its push
/// to the hull's body at the part's place. On an entity with a body of its own, it pushes that body at its center of mass.
/// A tick without a throttle gives no thrust and no puffs, so the engine keeps nothing from an earlier tick. When a tick
/// brings several, the last one counts, so a component that runs later overrides one that runs earlier. A throttle outside
/// 0 to 1, or NaN, makes the engine throw <see cref="ArgumentOutOfRangeException"/>, which the world logs as an error, and
/// push nothing and blow nothing in that tick.
/// </summary>
/// <remarks>
/// The engine reads the nose from the rotation of the body that it pushes, its part's parent or its own entity, at the end
/// of the last finished step (see <see cref="Plume.PushedBodyRotation"/>), and the push of a tick acts in the physics step
/// of the next tick, as every force does. The emitter that it writes in the tick makes the puffs of the same step. Its puffs
/// leave along its own entity's -Z, so the shuttle's part turns that -Z aft. On an entity with a body of its own, its -Z
/// is the nose, so its puffs leave forward: a hull that shows its plume carries the engine on a part. Physics moves only a
/// dynamic body by a force, so the engine of a parked hull pushes nothing. The engine runs at priority 10, so that on an
/// entity whose own components add the throttle, such as a test's, it sees what each of them adds in the tick. On its
/// part, the throttle comes by message, one tick after the tick of the flight program that gives it.
/// </remarks>
/// <param name="maxThrust">The force at full throttle, in newtons. It must be positive and finite.</param>
public sealed class MainEngine(float maxThrust) : IComponent
{
    /// <summary>
    /// The main engine's exhaust: hot gas, three times as hot as the air, that glows orange and white and lives 6 s, from
    /// 1 m across at the nozzle to 24 m. At full throttle 240 puffs leave each second at 30 m/s, tracer puffs far slower
    /// than real exhaust, which hang behind the shuttle and mark its path (docs/plans/blue-danube-demo.md, "The plumes").
    /// </summary>
    public static Plume Plume { get; } =
        new(
            Look: new(
                Life: 6,
                StartRadius: 1,
                EndRadius: 24,
                TemperatureRatio: 3,
                Shading: PuffShading.Glow(new(1.2f, 0.6f, 0.22f))
            ),
            FullRate: 240,
            Speed: 30,
            Spread: 0.1f
        );

    /// <summary>The force at full throttle, in newtons.</summary>
    public float MaxThrust { get; } =
        maxThrust > 0 && float.IsFinite(maxThrust)
            ? maxThrust
            : throw new ArgumentOutOfRangeException(
                nameof(maxThrust),
                maxThrust,
                "The thrust of an engine must be positive and finite."
            );

    // After the components of the same entity that add the throttle in the same tick, at priorities below 10.
    public int Priority => 10;

    // Tick brings the engine into every tick, so that a tick without a throttle clears its emitter.
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
            self.Body.AddForce(
                (Plume.PushedBodyRotation(self).Rotate(-Double3.UnitZ) * (throttle * MaxThrust)).ToVector3()
            );
    }
}
