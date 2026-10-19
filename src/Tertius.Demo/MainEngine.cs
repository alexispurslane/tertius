using Tertius.Simulation;

namespace Tertius.Demo;

/// <summary>
/// The main engine of a hull: on every tick it pushes its entity's body along the body's nose, its own -Z, with
/// <see cref="Throttle"/> times <see cref="MaxThrust"/>. It reads the nose from the pose at the end of the last finished
/// step, and the push of a tick acts in the physics step of the next tick, as every force does. Physics moves only a
/// dynamic body by a force, so the engine of a parked hull pushes nothing. The component that flies the hull sets the
/// throttle before the engine runs in the tick, so the engine runs after it (see <see cref="Priority"/>).
/// </summary>
/// <param name="maxThrust">The force at full throttle, in newtons. It must be positive and finite.</param>
public sealed class MainEngine(float maxThrust) : IComponent
{
    private float throttle;

    /// <summary>The force at full throttle, in newtons.</summary>
    public float MaxThrust { get; } = maxThrust > 0 && float.IsFinite(maxThrust)
        ? maxThrust
        : throw new ArgumentOutOfRangeException(nameof(maxThrust), maxThrust, "The thrust of an engine must be positive and finite.");

    /// <summary>
    /// The part of <see cref="MaxThrust"/> that the engine gives, from 0, off, to 1, full. It starts at 0. The setter
    /// throws <see cref="ArgumentOutOfRangeException"/> for a value outside that range or NaN.
    /// </summary>
    public float Throttle
    {
        get => throttle;
        set => throttle = value is >= 0 and <= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A throttle lies from 0 to 1.");
    }

    // After the autopilots, which set the throttle in the same tick.
    public int Priority => 1;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (throttle > 0)
            self.Body.AddForce((self.PoseIn(self.Frame).Rotation.Rotate(-Double3.UnitZ) * (throttle * MaxThrust)).ToVector3());
    }
}
