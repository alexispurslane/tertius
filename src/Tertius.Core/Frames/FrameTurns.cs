namespace Tertius;

/// <summary>
/// The two turns of a frame that a reader in the step in flight needs (docs/plans/spin-gravity.md, "The two-turn update"):
/// the turn of the step in flight and the turn of the step that ended. A free body moves on a straight line in inertial
/// space, and in the frame's axes the same line is x_(n+1) = 2 R_n x_n − R_n R_(n−1) x_(n−1), with R_n the turn of
/// <see cref="Step"/> and R_(n−1) the turn of <see cref="Last"/>. That map from one chord to the next holds the Coriolis, the
/// centrifugal and the Euler term exactly, at any step length, and needs no rate.
/// </summary>
/// <param name="Step">The turn of the step in flight, V1 of the design.</param>
/// <param name="Last">The turn of the step that ended, V2 of the design: the turn that the chords of the frame's bodies came from.</param>
internal readonly record struct FrameTurns(FrameTurn Step, FrameTurn Last)
{
    /// <summary>
    /// Whether the frame is a turning frame: either turn is not the identity. With both turns the identity, the update's
    /// result is zero to rounding, so physics skips it, and frames that do not turn keep their results bit for bit.
    /// </summary>
    public bool Turns => !Step.IsIdentity || !Last.IsIdentity;

    /// <summary>
    /// The two-turn update: the change of a free body's chord over the step in flight that keeps the body on its inertial
    /// line, in m/s. For a steady spin at ω it is (−2 ω × c − ω × (ω × x)) dt to first order in dt, and a rate that changes
    /// between the two steps adds −α × x dt.
    /// </summary>
    /// <param name="position">The body's place at the start of the step in flight, relative to the frame's origin, in m.</param>
    /// <param name="chord">Jolt's velocity of the body, the chord of the step that ended, in m/s.</param>
    /// <param name="dt">The length of the step, in s.</param>
    public Double3 FreeChange(Double3 position, Double3 chord, double dt)
    {
        Double3 previous = position - chord * dt;
        Double3 next = Step.Turn.Rotate(position) * 2 - (Step.Turn * Last.Turn).Rotate(previous);
        return (next - position) / dt - chord;
    }

    /// <summary>
    /// The angular form of the two-turn update: Jolt's angular velocity for the step in flight of a body that keeps its
    /// inertial angular velocity, exp(Ω_J,n dt) = R_n · exp(Ω_n dt) (docs/plans/spin-gravity.md, "The angular form").
    /// </summary>
    /// <param name="chord">Jolt's angular velocity of the body over the step that ended, in the frame's axes, in rad/s.</param>
    /// <param name="change">
    /// The change of the body's inertial angular velocity that an angular impulse makes at the start of the step in flight,
    /// in the frame's axes at that moment, in rad/s, or zero.
    /// </param>
    /// <param name="dt">The length of the step, in s.</param>
    public Double3 AngularChordAhead(Double3 chord, Double3 change, double dt)
    {
        Double3 inertialAtStart = Last.InertialAngularAtEnd(chord, dt) + change;
        return Step.AngularChordBehindInertial(Step.Turn.Rotate(inertialAtStart), dt);
    }

    /// <summary>
    /// The frame's inertial angular acceleration, the mean over the step in flight, in rad/s². The time derivative of a
    /// frame's own angular velocity is the same in its axes and in inertial axes, because ω × ω = 0.
    /// </summary>
    public Double3 AngularAcceleration(double dt) => (Step.AngularVelocity - Last.AngularVelocity) / dt;

    /// <summary>The centrifugal acceleration −ω × (ω × r) at <paramref name="position"/> at the end of the step in flight, in m/s².</summary>
    public Double3 Centrifugal(Double3 position) =>
        -Double3.Cross(Step.AngularVelocity, Double3.Cross(Step.AngularVelocity, position));

    /// <summary>
    /// The Euler acceleration −α × r at <paramref name="position"/> over the step in flight of <paramref name="dt"/>
    /// seconds, in m/s².
    /// </summary>
    public Double3 Euler(Double3 position, double dt) => -Double3.Cross(AngularAcceleration(dt), position);

    /// <summary>
    /// The Coriolis acceleration −2 ω × v of a body that moves at <paramref name="velocity"/> in the frame, at the end of
    /// the step in flight, in m/s².
    /// </summary>
    public Double3 Coriolis(Double3 velocity) => -2 * Double3.Cross(Step.AngularVelocity, velocity);
}
