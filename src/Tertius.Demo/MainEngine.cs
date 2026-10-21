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
/// The main engine of a hull: in each tick that brings a <see cref="Throttle"/>, it pushes along its own -Z, the nose, with
/// the throttle's value times <see cref="MaxThrust"/>. On the shuttle it runs on the main engine's part, an attached entity
/// of the hull on the line of thrust through the center of mass (see <see cref="ShuttleJetControl"/>), and the engine
/// carries its push to the hull's body at the part's place. On an entity with a body of its own, it pushes that body at
/// its center of mass. A tick without a throttle gives no thrust, so the engine keeps nothing from an earlier tick. When a
/// tick brings several, the last one counts, so a component that runs later overrides one that runs earlier. A throttle
/// outside 0 to 1, or NaN, makes the engine throw <see cref="ArgumentOutOfRangeException"/>, which the world logs as an
/// error, and push nothing in that tick.
/// </summary>
/// <remarks>
/// The engine reads the nose from the pose at the end of the last finished step, and the push of a tick acts in the
/// physics step of the next tick, as every force does. Physics moves only a dynamic body by a force, so the engine of a
/// parked hull pushes nothing. The engine runs at priority 10, so that on an entity whose own components add the throttle,
/// such as a test's, it sees what each of them adds in the tick. On its part, the throttle comes by message, one tick after
/// the tick of the flight program that gives it.
/// </remarks>
/// <param name="maxThrust">The force at full throttle, in newtons. It must be positive and finite.</param>
public sealed class MainEngine(float maxThrust) : IComponent
{
    /// <summary>The force at full throttle, in newtons.</summary>
    public float MaxThrust { get; } = maxThrust > 0 && float.IsFinite(maxThrust)
        ? maxThrust
        : throw new ArgumentOutOfRangeException(nameof(maxThrust), maxThrust, "The thrust of an engine must be positive and finite.");

    // After the components of the same entity that add the throttle in the same tick, at priorities below 10.
    public int Priority => 10;

    public EventMask Wants => EventMask.Of<Throttle>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // The engine wants only Throttle, so the events hold at least one, and the last one counts.
        float throttle = 0;
        foreach (ref Throttle each in events.Of<Throttle>())
            throttle = each.Value;
        if (throttle is not (>= 0 and <= 1))
            throw new ArgumentOutOfRangeException(nameof(Throttle), throttle, "A throttle lies from 0 to 1.");
        if (throttle > 0)
            self.Body.AddForce((self.PoseIn(self.Frame).Rotation.Rotate(-Double3.UnitZ) * (throttle * MaxThrust)).ToVector3());
    }
}
