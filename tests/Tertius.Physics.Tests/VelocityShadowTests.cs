namespace Tertius.Physics.Tests;

/// <summary>
/// A velocity shadow carried into another home (docs/plans/spin-gravity.md, "V6, the velocity shadow"): a body with no
/// shadow keeps none, and a shadow moves its tracking as <see cref="ShadowTracking.Moved"/> says.
/// </summary>
public class VelocityShadowTests
{
    private static readonly Double3 Offset = new(12.5, -3.25, 7);
    private static readonly Double3 HomeVelocity = new(0.5, 1.75, -2);

    [Fact]
    public void ABodyWithNoShadowKeepsNoShadowInTheNewHome()
    {
        VelocityShadow moved = default(VelocityShadow).Moved(Offset, HomeVelocity);

        moved.HasValue.Should().BeFalse();
    }

    [Fact]
    public void AShadowMovesItsTrackingIntoTheNewHome()
    {
        var tracking = new ShadowTracking(Chord: new(1, 2, 3), Place: new(4, 5, 6), JoltTarget: new(7, 8, 9), Step: 41);
        VelocityShadow shadow = tracking;

        VelocityShadow moved = shadow.Moved(Offset, HomeVelocity);

        moved.TryGetValue(out ShadowTracking movedTracking).Should().BeTrue();
        movedTracking.Should().Be(tracking.Moved(Offset, HomeVelocity));
    }
}
