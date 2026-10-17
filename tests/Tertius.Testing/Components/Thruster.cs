using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Pushes its entity's dynamic body with a steady force on every tick while <see cref="On"/> is true: <paramref name="mass"/>
/// times <see cref="Acceleration"/> along <see cref="Direction"/>. The force of a tick acts in the physics step of the next
/// tick.
/// <para>
/// A test uses it to make a body thrust under the default membership rules of numeric frames, where a group with a body
/// that thrusts gets a falling frame from a lower speed (see <see cref="NumericFrameOptions.ThrustCreateSpeed"/>). The body
/// counts as one that thrusts only after <see cref="NumericFrameOptions.ThrustOnset"/> (0.5 s) of thrust, so a test starts
/// the thrust that long before the frame matters. The body keeps counting for <see cref="NumericFrameOptions.ThrustWindow"/>
/// (20 s) after its last tick of thrust.
/// </para>
/// </summary>
/// <param name="mass">The mass of the body in kg, as its <see cref="BodyDesc"/> gives it, so that the force gives the body
/// <see cref="Acceleration"/>.</param>
public sealed class Thruster(float mass) : IComponent
{
    /// <summary>
    /// A gentle thrust in m/s²: 1e-4, six times <see cref="NumericFrameOptions.MinThrustAcceleration"/>. In 20 s it changes
    /// the body's velocity by 2 mm/s and moves the body by 2 cm, so the body keeps its path, but every tick of it counts as a
    /// tick of thrust.
    /// </summary>
    public const float GentleAcceleration = 1e-4f;

    /// <summary>The acceleration that the force gives the body, in m/s². It starts at <see cref="GentleAcceleration"/>.</summary>
    public float Acceleration { get; set; } = GentleAcceleration;

    /// <summary>The direction of the force, a unit vector in the axes of the body's frame. It starts along +X.</summary>
    public Vector3 Direction { get; set; } = Vector3.UnitX;

    /// <summary>The thruster pushes on every tick while this is true, from the start.</summary>
    public bool On { get; set; } = true;

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Tick>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (On)
            self.Body.AddForce(Direction * (mass * Acceleration));
    }
}
