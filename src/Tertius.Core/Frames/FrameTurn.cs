namespace Tertius;

/// <summary>
/// The turn of a frame's axes over one physics step, and the frame's inertial angular velocity at that step's end
/// (docs/plans/spin-gravity.md, "The two-turn update"). The frame tree keeps two of them per frame in one
/// <see cref="FrameTurns"/>: the turn of the step in flight and the turn of the step that ended (V1 and V2 of the design).
/// </summary>
/// <remarks>
/// It has no case tag: the identity turn means no turn. Its default is not <see cref="None"/>: the default
/// <see cref="DQuaternion"/> is (0, 0, 0, 0), which is no rotation at all, so <see cref="IsIdentity"/> is false for it and
/// <see cref="DQuaternion.Rotate(Double3)"/> gives zero vectors. Every store of a <see cref="FrameTurn"/> fills its slots with
/// <see cref="None"/>.
/// </remarks>
/// <param name="Turn">
/// How the coordinates of a direction that is fixed in inertial space change in the frame's axes over the step: a rotation
/// from the frame's axes at the step's start to its axes at the step's end. A frame that turns by ω for a step of dt
/// seconds has a turn by the angle −dt |ω| about ω.
/// </param>
/// <param name="AngularVelocity">
/// The frame's angular velocity relative to inertial space at the step's end, in the frame's axes then, in rad/s.
/// </param>
internal readonly record struct FrameTurn(DQuaternion Turn, Double3 AngularVelocity)
{
    /// <summary>No turn and no angular velocity: the turn of a frame that does not turn.</summary>
    public static FrameTurn None => new(DQuaternion.Identity, Double3.Zero);

    /// <summary>True when the turn is exactly the identity quaternion.</summary>
    public bool IsIdentity => Turn == DQuaternion.Identity;

    /// <summary>
    /// The chord rule: the chord, over this step, of the inertial line through a body's state at the step's end. Jolt keeps
    /// it as the body's velocity, and the two-turn update of the next step carries it onto the inertial line ahead.
    /// </summary>
    /// <param name="position">The body's place relative to the frame's origin at the step's end, in m.</param>
    /// <param name="instant">The body's instant velocity relative to the frame at the step's end, in m/s.</param>
    /// <param name="dt">The length of the step, in s.</param>
    /// <returns>The change of the body's coordinates over the step, divided by the step, in m/s.</returns>
    public Double3 ChordBehind(Double3 position, Double3 instant, double dt)
    {
        Double3 inertial = instant + Double3.Cross(AngularVelocity, position);
        Double3 previous = Turn.Conjugate().Rotate(position - inertial * dt);
        return (position - previous) / dt;
    }

    /// <summary>
    /// The exact inverse of <see cref="ChordBehind"/>: the instant velocity relative to the frame at this step's end of a
    /// body that moved by <paramref name="chord"/> over the step on its inertial line.
    /// </summary>
    /// <param name="position">The body's place relative to the frame's origin at the step's end, in m.</param>
    /// <param name="chord">The change of the body's coordinates over the step, divided by the step, in m/s.</param>
    /// <param name="dt">The length of the step, in s.</param>
    public Double3 InstantAtEnd(Double3 position, Double3 chord, double dt)
    {
        Double3 previousInEndAxes = Turn.Rotate(position - chord * dt);
        return (position - previousInEndAxes) / dt - Double3.Cross(AngularVelocity, position);
    }

    /// <summary>
    /// The linear part of <see cref="ChordBehind"/>: the change of this step's chord that has the same effect as a velocity
    /// change at this step's end, such as an impulse at the start of the next step, in the frame's axes at that moment. The
    /// next step's two-turn update then carries the change by the next turn alone (docs/plans/spin-gravity.md, "Impulses").
    /// </summary>
    public Double3 ChangeBehind(Double3 change) => Turn.Conjugate().Rotate(change);

    /// <summary>
    /// The angular form of <see cref="ChordBehind"/>: Jolt's angular velocity over this step, the rotation vector of the
    /// body's turn in the frame's axes divided by the step, of a body that keeps its inertial angular velocity and turns at
    /// <paramref name="instant"/> relative to the frame at the step's end, in the frame's axes then, in rad/s.
    /// </summary>
    public Double3 AngularChordBehind(Double3 instant, double dt) =>
        AngularChordBehindInertial(instant + AngularVelocity, dt);

    /// <summary>
    /// The exact inverse of <see cref="AngularChordBehind"/>: the angular velocity relative to the frame at this step's end
    /// of a body that Jolt turned by <paramref name="chord"/> over the step and that keeps its inertial angular velocity.
    /// </summary>
    public Double3 InstantAngularAtEnd(Double3 chord, double dt) => InertialAngularAtEnd(chord, dt) - AngularVelocity;

    /// <summary>
    /// Jolt's angular velocity over this step of a body whose inertial angular velocity at the step's end, in the frame's
    /// axes then, is <paramref name="inertial"/>. A free body turns by the same inertial rotation over the step, so in the
    /// frame's coordinates it turns by exp(Ω dt) · <see cref="Turn"/>, with Ω = <paramref name="inertial"/>. Jolt turns a body by
    /// the rotation of its angular velocity times the step, so the result is the rotation vector of that turn over dt.
    /// </summary>
    internal Double3 AngularChordBehindInertial(Double3 inertial, double dt) =>
        DQuaternion.Identity.AngularVelocityTo(DQuaternion.FromAxisAngle(inertial, inertial.Length * dt) * Turn, dt);

    /// <summary>
    /// The exact inverse of <see cref="AngularChordBehindInertial"/>: the inertial angular velocity at the step's end, in
    /// the frame's axes then, of a body that Jolt turned by <paramref name="chord"/> over the step.
    /// </summary>
    internal Double3 InertialAngularAtEnd(Double3 chord, double dt) =>
        DQuaternion.Identity.AngularVelocityTo(DQuaternion.FromAxisAngle(chord, chord.Length * dt) * Turn.Conjugate(),
            dt);
}
