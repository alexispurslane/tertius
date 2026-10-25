namespace Tertius.Rendering.Tests;

/// <summary>
/// The search of GTAO around one half-resolution texel: the view depth of a distance, the texel width, the fade below
/// one texel and the spacing of the steps (docs/plans/ambient-occlusion.md, "The method").
/// </summary>
public sealed class HorizonSearchTests
{
    private const double TanHalfX = 0.75;
    private const int FullWidth = 1920;

    /// <summary>The view ray of the full-resolution pixel column <paramref name="column"/> on the middle row.</summary>
    private static Double3 RayOfColumn(int column) => new((2 * (column + 0.5) / FullWidth - 1) * TanHalfX, 0, 1);

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(0.01)]
    public void ARadiusOfOneTexelOrLessGivesFullVisibility(double radiusInTexels) =>
        HorizonSearch.Faded(estimate: 0.3, fade: HorizonSearch.Fade(radiusInTexels)).Should().Be(1);

    [Theory]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(40.0)]
    public void ARadiusOfTwoTexelsOrMoreKeepsTheWholeEstimate(double radiusInTexels) =>
        HorizonSearch
            .Faded(estimate: 0.3, fade: HorizonSearch.Fade(radiusInTexels))
            .Should()
            .BeApproximately(0.3, 1e-15);

    [Fact]
    public void BetweenOneAndTwoTexelsTheEstimateFadesLinearlyToFullVisibility()
    {
        HorizonSearch
            .Faded(estimate: 0.3, fade: HorizonSearch.Fade(1.25))
            .Should()
            .BeApproximately(0.825, 1e-15, "a quarter of the way from 1 to 2 texels keeps a quarter of the darkening");
        HorizonSearch.Faded(estimate: 0.3, fade: HorizonSearch.Fade(1.5)).Should().BeApproximately(0.65, 1e-15);
    }

    [Fact]
    public void ATexelIsTwoPixelsWideAtItsViewDepth()
    {
        const double viewDepth = 7;
        double twoPixels = (RayOfColumn(1001).X - RayOfColumn(999).X) * viewDepth;

        HorizonSearch
            .TexelWidth(viewDepth: viewDepth, tanHalfX: TanHalfX, fullWidth: FullWidth)
            .Should()
            .BeApproximately(twoPixels, 1e-12);
    }

    [Fact]
    public void TheRadiusInTexelsHalvesWhenTheViewDepthDoubles()
    {
        const double radius = 1;
        double near = radius / HorizonSearch.TexelWidth(viewDepth: 50, tanHalfX: TanHalfX, fullWidth: FullWidth);
        double far = radius / HorizonSearch.TexelWidth(viewDepth: 100, tanHalfX: TanHalfX, fullWidth: FullWidth);

        far.Should().BeApproximately(near / 2, 1e-12);
    }

    [Fact]
    public void TheStepsOfASliceFillTheRadiusEvenlyBeyondTheNearestStep()
    {
        const int stepsPerSide = 4;
        const double radiusInTexels = 9;
        double nearest = HorizonSearch.NearestStepTexels / radiusInTexels;
        foreach (float stepNoise in new[] { 0f, 0.3f, 0.77f, 0.999f })
        foreach (int slice in new[] { 0, 1, 2 })
            for (int step = 0; step < stepsPerSide; step++)
            {
                double fraction = HorizonSearch.StepFraction(
                    step: step,
                    slice: slice,
                    stepsPerSide: stepsPerSide,
                    stepNoise: stepNoise,
                    radiusInTexels: radiusInTexels
                );

                (fraction - nearest)
                    .Should()
                    .BeGreaterThanOrEqualTo((double)step / stepsPerSide)
                    .And.BeLessThan((step + 1.0) / stepsPerSide, $"step {step} of slice {slice}, noise {stepNoise}");
            }
    }

    [Fact]
    public void TheSlicesTurnEvenlyThroughHalfATurn()
    {
        const int slices = 3;
        const float sliceNoise = 0.25f;

        HorizonSearch
            .SliceAngle(slice: 0, slices: slices, sliceNoise: sliceNoise)
            .Should()
            .BeApproximately(Math.PI / 12, 1e-7);
        HorizonSearch
            .SliceAngle(slice: 2, slices: slices, sliceNoise: sliceNoise)
            .Should()
            .BeApproximately(Math.PI * 0.75, 1e-7);
    }

    [Fact]
    public void ADistanceBecomesTheViewDepthAlongItsRay()
    {
        // At the center of the screen the ray is the forward axis. Off the center, the point (3, 4, 12) lies 13 m from
        // the eye on the ray (1/4, 1/3, 1).
        HorizonSearch.ViewDepth(distance: 5, ray: new(0, 0, 1)).Should().BeApproximately(5, 1e-15);
        HorizonSearch.ViewDepth(distance: 13, ray: new(0.25, 1.0 / 3, 1)).Should().BeApproximately(12, 1e-12);
    }

    [Fact]
    public void ASkyPixelTakesTheSkyDepth() =>
        HorizonSearch.ViewDepth(distance: 0, ray: new(0.25, 1.0 / 3, 1)).Should().Be(HorizonSearch.SkyDepth);
}
