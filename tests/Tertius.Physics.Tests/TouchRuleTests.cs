namespace Tertius.Physics.Tests;

/// <summary>
/// The rule by which a contact that Jolt found is a real touch in a step: the bodies are no more than 1 mm apart, or they
/// approach fast enough to close the gap within the step, so that the solver pushes them apart. A penetration depth below
/// zero is a gap, and a normal velocity below zero is an approach.
/// </summary>
public class TouchRuleTests
{
    private const float Step = 1f / 60;

    [Fact]
    public void BodiesThatOverlapTouch() => FrameWorld.Touches(0.002f, 0, Step).Should().BeTrue();

    [Fact]
    public void BodiesAtZeroSeparationTouch() => FrameWorld.Touches(0, 0, Step).Should().BeTrue();

    [Fact]
    public void BodiesLessThanAMillimeterApartTouch() => FrameWorld.Touches(-0.0009f, 0, Step).Should().BeTrue();

    [Fact]
    public void BodiesOneCentimeterApartAtRestDoNotTouch() => FrameWorld.Touches(-0.01f, 0, Step).Should().BeFalse();

    [Fact]
    public void BodiesThatCloseTheirGapWithinTheStepTouch() => FrameWorld.Touches(-0.01f, -1f, Step).Should().BeTrue("1 m/s closes 1.7 cm in a step");

    [Fact]
    public void BodiesThatApproachTooSlowlyToCloseTheirGapInTheStepDoNotTouch() =>
        FrameWorld.Touches(-0.01f, -0.1f, Step).Should().BeFalse("0.1 m/s closes 1.7 mm in a step");

    [Fact]
    public void BodiesThatMoveApartDoNotTouch() => FrameWorld.Touches(-0.01f, 5f, Step).Should().BeFalse();
}
