namespace Tertius;

/// <summary>
/// The poses of a frame in another frame, its parent here, at both ends of one physics step: the rule by which a chord
/// converts between the two frames (docs/plans/spin-gravity.md, "The two conversion rules"). A chord is the change of a
/// body's coordinates over the step divided by the step, so it converts by where the frame stood at both ends of the step,
/// c' = (P_end x_end − P_start (x_end − c dt)) / dt, and an angular chord by the rotations there,
/// exp(Ω' dt) = P_end.Rotation · exp(Ω dt) · P_start.Rotation⁻¹. The rule of an instant velocity, which
/// <see cref="FrameMotion"/> applies at one moment, misses a chord in a frame that turns by the turn of the step: about
/// 8 cm/s in direction at 31.3 m/s and 60 Hz on the floor of a station that spins at 0.31321 rad/s.
/// </summary>
/// <param name="Start">The frame's pose in the parent where the step starts.</param>
/// <param name="End">The frame's pose in the parent where the step ends.</param>
internal readonly record struct StepPoses(DPose Start, DPose End)
{
    /// <summary>
    /// The chord in the parent of a body that ends the step at <paramref name="pointInFrame"/> with the chord
    /// <paramref name="chord"/> in the frame, in m/s.
    /// </summary>
    /// <param name="pointInFrame">The body's place in the frame at the end of the step, in m.</param>
    /// <param name="chord">The change of the body's coordinates in the frame over the step, divided by the step, in m/s.</param>
    /// <param name="dt">The length of the step, in s.</param>
    public Double3 ChordToParent(Double3 pointInFrame, Double3 chord, double dt) =>
        // The move of the frame's origin comes first, so that a frame far from its parent's origin adds no rounding of its
        // place to the turn of the body's coordinates.
        (End.Position - Start.Position + End.Rotation.Rotate(pointInFrame) - Start.Rotation.Rotate(pointInFrame - chord * dt)) / dt;

    /// <summary>
    /// The chord in the frame of a body that ends the step at <paramref name="pointInParent"/> with the chord
    /// <paramref name="chord"/> in the parent, in m/s: the inverse of <see cref="ChordToParent"/>.
    /// </summary>
    public Double3 ChordFromParent(Double3 pointInParent, Double3 chord, double dt)
    {
        // The body's offsets from the frame's origin at both ends of the step, in the parent's axes: small numbers for a
        // body near the frame, however far the frame is from its parent's origin.
        Double3 atEnd = pointInParent - End.Position;
        Double3 atStart = atEnd + (End.Position - Start.Position) - chord * dt;
        return (End.Rotation.Conjugate().Rotate(atEnd) - Start.Rotation.Conjugate().Rotate(atStart)) / dt;
    }

    /// <summary>
    /// Jolt's angular velocity in the parent of a body that Jolt turned by <paramref name="chord"/> over the step in the
    /// frame: the rotation vector of its turn in the parent's axes over the step, divided by the step, in rad/s.
    /// </summary>
    public Double3 AngularChordToParent(Double3 chord, double dt) =>
        DQuaternion.Identity.AngularVelocityTo(End.Rotation * Turn(chord, dt) * Start.Rotation.Conjugate(), dt);

    /// <summary>The inverse of <see cref="AngularChordToParent"/>: Jolt's angular velocity in the frame of a body that turned by <paramref name="chord"/> over the step in the parent.</summary>
    public Double3 AngularChordFromParent(Double3 chord, double dt) =>
        DQuaternion.Identity.AngularVelocityTo(End.Rotation.Conjugate() * Turn(chord, dt) * Start.Rotation, dt);

    /// <summary>The turn of a body by Jolt's angular velocity <paramref name="chord"/> over a step of <paramref name="dt"/> seconds.</summary>
    private static DQuaternion Turn(Double3 chord, double dt) => DQuaternion.FromAxisAngle(chord, chord.Length * dt);
}
