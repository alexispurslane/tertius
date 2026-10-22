namespace Tertius.Physics.Tests.Precision;

/// <summary>
/// Measures the false hits of Jolt's linear cast between two dynamic boxes that move together (ticket ter-kugq). The
/// engine gives every dynamic body Jolt's motion quality LinearCast: when a body moves more than a share of its inner
/// radius in a step, Jolt casts its shape along that motion against the other bodies instead of moving it at once. The
/// scene is <see cref="MovingPair"/>: the rear body and the front body are both the box. Writes cast-false-hits.csv.
/// </summary>
public sealed class CastFalseHitsMeasurement
{
    /// <summary>A cube with sides of 1 m and 10 kg that nothing slows down, turned +X onto d, so that its faces are square to d.</summary>
    private static readonly MovingPair.Member Box = new(
        new(new BoxShapeDesc(new(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0),
        _ => 0.5,
        TurnsOntoD: true);

    [Fact(Explicit = true)]
    public void CastFalseHits()
    {
        ContactRuns.Csv csv = MovingPair.Measure(Box, Box);
        csv.Save("cast-false-hits");
        csv.Rows.Should().Be(MovingPair.RowCount);
    }
}
