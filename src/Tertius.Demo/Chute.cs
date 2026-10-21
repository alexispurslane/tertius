namespace Tertius.Demo;

/// <summary>
/// A chute down a spoke of a spinning station: a trough on a back plate, with a rail on each side, along -X in the
/// station's axes at θ = 180 degrees (docs/plans/blue-danube-demo.md, "The exit", "The chute" and "The slide"). The
/// trough is open toward +Z, the spoke's middle. Every place is in the station's axes, in meters.
/// </summary>
/// <param name="BackFace">The z of the back plate's face toward the trough.</param>
/// <param name="HalfWidth">Half the width between the rails' inner faces, along Y.</param>
/// <param name="Depth">How far the chute's top reaches from the back plate's face toward +Z, the hole in the deck.</param>
/// <param name="Top">The x of the chute's top, where its faces start: the deck.</param>
/// <param name="BrakeFrom">The x below which the brake run starts.</param>
/// <param name="End">The x of the trough's lower end.</param>
/// <param name="Coefficient">The friction coefficient of the trough above the brake run.</param>
/// <param name="BrakeCoefficient">The friction coefficient of the brake run.</param>
public readonly record struct Chute(double BackFace, double HalfWidth, double Depth, double Top, double BrakeFrom, double End, double Coefficient, double BrakeCoefficient)
{
    /// <summary>
    /// True when the chute holds a person's center at <paramref name="position"/>: between the rails, within
    /// <see cref="Depth"/> of the back plate's face, and from the trough's end to 1 m above the deck. So it holds the center
    /// of a person who stands over the chute's top, 0.9 m up, as well as one in the trough.
    /// </summary>
    public bool Holds(Double3 position) =>
        Math.Abs(position.Y) <= HalfWidth && position.Z >= BackFace && position.Z <= BackFace + Depth && position.X <= Top + 1 && position.X >= End;

    /// <summary>
    /// How far a person's center lies beyond its radius from the back plate's face, at most, in meters, while it touches
    /// the plate: 0.1 m. A character keeps 2 cm of padding from a wall. Its up also leans toward the felt gravity, which
    /// points away from the plane z = 0, so the capsule touches the plate with its lower end, and its center lies
    /// 0.6 m × sin(lean) farther off. At the top of the spoke the lean is 4.4 degrees, and the center lies 0.37 m off the face.
    /// </summary>
    public const double ContactMargin = 0.1;

    /// <summary>
    /// The acceleration, in m/s² in the station's axes, that the chute's friction gives a person whose center is at
    /// <paramref name="position"/> with <paramref name="velocity"/> in the station's frame (docs/plans/blue-danube-demo.md,
    /// "The exit", "The slide"). The station turns at <paramref name="omega"/> relative to the virtual root, in its own axes.
    /// The free acceleration of the person, the centrifugal and the Coriolis terms, presses it into the back plate. The
    /// friction is the coefficient times that press, against the slide, which is the velocity along the plate. It is at most
    /// the slide divided by <paramref name="dt"/>, so that one step of it never turns the slide around. It is zero unless the
    /// person touches the plate: its center lies within <paramref name="radius"/> plus <see cref="ContactMargin"/> of the
    /// plate's face, between the rails, and from the trough's end to the deck. It is also zero for a free acceleration
    /// away from the plate and for a person who does not slide.
    /// </summary>
    /// <param name="position">The person's center, in meters.</param>
    /// <param name="velocity">The person's velocity, in m/s.</param>
    /// <param name="omega">The station's angular velocity, in rad/s.</param>
    /// <param name="radius">The person's radius, in meters.</param>
    /// <param name="dt">The length of one step, in seconds.</param>
    public Double3 Friction(Double3 position, Double3 velocity, Double3 omega, double radius, double dt)
    {
        bool touches = position.Z - BackFace <= radius + ContactMargin && Math.Abs(position.Y) <= HalfWidth && position.X <= Top && position.X >= End;
        if (!touches)
            return Double3.Zero;
        // The back plate's normal toward the person is +Z, so the press is the free acceleration's part along -Z.
        Double3 free = -Double3.Cross(omega, Double3.Cross(omega, position)) - 2 * Double3.Cross(omega, velocity);
        double press = Math.Max(0, -free.Z);
        Double3 slide = new(velocity.X, velocity.Y, 0);
        double speed = slide.Length;
        if (press == 0 || speed == 0)
            return Double3.Zero;
        double coefficient = position.X < BrakeFrom ? BrakeCoefficient : Coefficient;
        return -Math.Min(coefficient * press, speed / dt) / speed * slide;
    }
}
