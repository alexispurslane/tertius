namespace Tertius;

/// <summary>
/// What the last finished step of a frame does to a loose body in it (docs/plans/particles.md, "The rule of motion"): the
/// frame's field, its proper acceleration as the step found it, its turns, the length of the step of the turns, and its
/// medium. <see cref="FrameTree.LastLooseBodyStep"/> fills one per frame, and the step of the puffs reads it once per range
/// of rows. The rule of a loose body in a frame belongs to the frame's step, so it lives here: a body that the medium does
/// not lift takes the expression of physics for a free body with no numeric home, to the bit.
/// </summary>
internal readonly struct LooseBodyStep
{
    /// <summary>The frame's gravity field.</summary>
    public GravityField Field { get; init; }

    /// <summary>
    /// The frame's proper acceleration as the last finished step found it, widened to doubles, in m/s², in the frame's
    /// axes: the value that physics subtracted for the frame's bodies during that step.
    /// </summary>
    public Double3 ProperAcceleration { get; init; }

    /// <summary>The frame's V1 and V2 (see <see cref="FrameTree.Turns"/>), which describe the last finished step.</summary>
    public FrameTurns Turns { get; init; }

    /// <summary>The length of the step whose turns the store holds, in s, or 0 before the first step.</summary>
    public double TurnsStep { get; init; }

    /// <summary>The frame's medium.</summary>
    public MediumField Medium { get; init; }

    /// <summary>
    /// The linear gravity that the step applied at <paramref name="place"/>, a place in the frame, in m, in the frame's
    /// axes: the field there less the proper acceleration, in m/s². It equals what physics read during the step, to the bit.
    /// </summary>
    public Double3 LinearGravity(Double3 place) => Field.AtPrecise(place) - ProperAcceleration;

    /// <summary>
    /// The effective gravity at <paramref name="place"/>, in m/s²: <paramref name="linear"/>, the linear gravity there,
    /// plus the centrifugal and the Euler term of the turns. In a frame that does not turn it is <paramref name="linear"/>
    /// itself. It equals the frame tree's effective gravity of a semantic frame, to the bit. It holds no Coriolis term.
    /// </summary>
    public Double3 EffectiveGravity(Double3 place, Double3 linear) =>
        Turns.Turns ? linear + Turns.Centrifugal(place) + Turns.Euler(place, TurnsStep) : linear;

    /// <summary>
    /// The chord of a loose body over the step, in m/s, in the frame's axes, from its <paramref name="place"/> at the start
    /// of the step, in m, and its <paramref name="chord"/> of the step before, in m/s. The push is the linear gravity less
    /// <paramref name="lift"/> times the effective gravity. In a turning frame the two-turn update carries the chord and the
    /// turn of the step carries the push, and in a frame that does not turn the push adds to the chord. The drag then keeps
    /// 1 / (1 + <paramref name="drag"/> · <paramref name="dt"/>) of the result, which is exact for no drag.
    /// </summary>
    /// <param name="place">The body's place at the start of the step, in m.</param>
    /// <param name="chord">The body's chord of the step before, in m/s.</param>
    /// <param name="lift">The lift share, a pure number, 0 in vacuum.</param>
    /// <param name="drag">The body's drag, in 1/s, 0 in vacuum.</param>
    /// <param name="dt">The length of the step, in s.</param>
    public Double3 ChordAhead(Double3 place, Double3 chord, double lift, double drag, double dt)
    {
        Double3 linear = LinearGravity(place);
        // In vacuum the push is the linear gravity itself, so the body takes the expression of physics to the bit.
        Double3 push = lift == 0 ? linear : linear - EffectiveGravity(place, linear) * lift;
        Double3 ahead = Turns.Turns
            ? chord + Turns.FreeChange(place, chord, dt) + Turns.Step.Turn.Rotate(push) * dt
            : chord + push * dt;
        return ahead / (1 + drag * dt);
    }
}
