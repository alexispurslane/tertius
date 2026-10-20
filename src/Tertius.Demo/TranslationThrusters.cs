using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The force that a hull's translation thrusters give for one tick, which a component that flies the hull adds to its
/// entity's events (see <see cref="EventList.Add{T}"/>), for the <see cref="TranslationThrusters"/> of the same entity,
/// which run after it.
/// </summary>
public struct Translation : IEventPayload
{
    /// <summary>The force, in newtons, in the axes of the entity's frame.</summary>
    public Double3 Force;
}

/// <summary>
/// The translation thrusters of a hull: jets along each of the hull's own axes, which push it sideways, up, down, forward
/// and back without a turn. In each tick that brings a <see cref="Translation"/>, they turn its force into the hull's
/// axes, limit each axis to ±<see cref="MaxThrust"/>, as jets along that axis do, turn the force back into the axes of
/// the entity's frame and push the hull's body with it. A tick without a translation gives no force, so the thrusters
/// keep nothing from an earlier tick. When a tick brings several, the last one counts, so a component that runs later
/// overrides one that runs earlier. A force that is not finite makes the thrusters throw
/// <see cref="ArgumentOutOfRangeException"/>, which the world logs as an error, and push nothing in that tick.
/// </summary>
/// <remarks>
/// They match the <see cref="MainEngine"/>: they read the hull's rotation in its frame from the pose at the end of the
/// last finished step, and the push of a tick acts in the physics step of the next tick, as every force does. Physics
/// moves only a dynamic body by a force, so the thrusters of a parked hull push nothing. They are an actuator of the
/// flight program, so they run after all of its layers in the tick (see <see cref="Priority"/>), and see what the layers
/// added.
/// </remarks>
/// <param name="maxThrust">The most force along each of the hull's axes, in newtons. It must be positive and finite.</param>
public sealed class TranslationThrusters(float maxThrust) : IComponent
{
    /// <summary>The most force along each of the hull's axes, in newtons.</summary>
    public float MaxThrust { get; } = maxThrust > 0 && float.IsFinite(maxThrust)
        ? maxThrust
        : throw new ArgumentOutOfRangeException(nameof(maxThrust), maxThrust, "The thrust of translation thrusters must be positive and finite.");

    // After the layers of the flight program, which add the translation in the same tick.
    public int Priority => 10;

    public EventMask Wants => EventMask.Of<Translation>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // The thrusters want only Translation, so the events hold at least one, and the last one counts.
        Double3 force = Double3.Zero;
        foreach (ref Translation each in events.Of<Translation>())
            force = each.Force;
        if (!(double.IsFinite(force.X) && double.IsFinite(force.Y) && double.IsFinite(force.Z)))
            throw new ArgumentOutOfRangeException(nameof(Translation), force, "A translation is finite.");
        DQuaternion hull = self.PoseIn(self.Frame).Rotation;
        Double3 inHull = hull.Conjugate().Rotate(force);
        Double3 limited = new(Math.Clamp(inHull.X, -MaxThrust, MaxThrust), Math.Clamp(inHull.Y, -MaxThrust, MaxThrust), Math.Clamp(inHull.Z, -MaxThrust, MaxThrust));
        if (limited != Double3.Zero)
            self.Body.AddForce(hull.Rotate(limited).ToVector3());
    }
}
