namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// Measures the false hits of Jolt's character sweep between a character and a crate that move together (ticket ter-kugq).
/// Jolt moves a character before it steps the rigid bodies, and sweeps the character's whole motion of a tick, V times
/// the step, against the other bodies where they are at the start of the step. So a crate that lies ahead of the character
/// by less than that sweep can stop it, although the crate moves away as fast. The scene is <see cref="MovingPair"/>: the
/// rear body is the character, the front body is the crate. Writes sweep-false-hits.csv.
/// </summary>
public sealed class SweepFalseHitsMeasurement
{
    /// <summary>
    /// A person's character: a capsule along +Y with a radius of 0.3 m and 0.6 m from its center to the center of each
    /// end, 70 kg, as in the demo. In zero gravity the engine keeps a character's up along +Y, so it spawns with no
    /// rotation. Along the unit vector d its surface reaches the radius plus 0.6 m times the part of d along Y.
    /// </summary>
    private static readonly MovingPair.Member Character = new(
        new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70),
        d => 0.3 + 0.6 * Math.Abs(d.Y),
        TurnsOntoD: false
    );

    /// <summary>
    /// A crate of 10 kg that nothing slows down: 1 m deep along d, and 2 m by 2 m across it, so that its back face covers
    /// the character's whole outline along the diagonal too.
    /// </summary>
    private static readonly MovingPair.Member Crate = new(
        new(Shape: new BoxShapeDesc(new(0.5f, 1, 1)), Mass: 10, LinearDamping: 0, AngularDamping: 0),
        _ => 0.5,
        TurnsOntoD: true
    );

    [Fact(Explicit = true)]
    public void SweepFalseHits()
    {
        ContactRuns.Csv csv = MovingPair.Measure(Character, Crate);
        csv.Save("sweep-false-hits");
        csv.Rows.Should().Be(MovingPair.RowCount);
    }
}
