namespace Tertius.Simulation;

/// <summary>
/// The four additive intents of one entity in one tick, in doubles, in the axes of the frame of the intents
/// (docs/plans/parts.md, "How a part's push converts into the root's push"): a force in N, a torque in N·m, an impulse in
/// N·s and an angular impulse in N·m·s. The flow of the parts' pushes (see <see cref="Attachments.CarryPushes"/>) widens
/// each part's intent into a push, moves it to the center of mass of its root's body, sums the pushes of each root in
/// doubles, and narrows each sum once, into the root's intent.
/// </summary>
internal readonly record struct Push(Double3 Force, Double3 Torque, Double3 Impulse, Double3 AngularImpulse)
{
    /// <summary>The four additive fields of <paramref name="intent"/>, widened. Every float is exact as a double.</summary>
    public static Push Of(in PhysicsIntent intent) =>
        new(intent.Force, intent.Torque, intent.Impulse, intent.AngularImpulse);

    /// <summary>
    /// The lever of a part: <paramref name="placeInBody"/> less <paramref name="centerOfMass"/>, both in meters in the root
    /// body's own axes, turned by <paramref name="rotation"/>, the root body's axes in the axes of the frame of the intents.
    /// It is the arm, in the axes of that frame, of the part's force about the center of mass.
    /// </summary>
    public static Double3 Lever(DQuaternion rotation, Double3 placeInBody, Double3 centerOfMass) =>
        rotation.Rotate(placeInBody - centerOfMass);

    /// <summary>
    /// The push that this push, acting at the end of <paramref name="lever"/>, gives a body about its center of mass: the
    /// same force with the torque of its lever, and the same impulse with the angular impulse of its lever. A torque and an
    /// angular impulse are free vectors, so the lever does not change them.
    /// </summary>
    public Push At(Double3 lever) =>
        new(Force, Torque + Double3.Cross(lever, Force), Impulse, AngularImpulse + Double3.Cross(lever, Impulse));

    /// <summary>The sum of this push and <paramref name="other"/>, field by field, in doubles.</summary>
    public Push Plus(in Push other) =>
        new(Force + other.Force, Torque + other.Torque, Impulse + other.Impulse, AngularImpulse + other.AngularImpulse);

    /// <summary>
    /// Narrows the four fields once, into the force, the torque, the impulse and the angular impulse of
    /// <paramref name="intent"/>. Every other field of the intent stays as it is.
    /// </summary>
    public void WriteInto(ref PhysicsIntent intent)
    {
        intent.Force = Force.ToVector3();
        intent.Torque = Torque.ToVector3();
        intent.Impulse = Impulse.ToVector3();
        intent.AngularImpulse = AngularImpulse.ToVector3();
    }
}
