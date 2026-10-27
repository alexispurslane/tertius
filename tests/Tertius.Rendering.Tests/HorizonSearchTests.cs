using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The search of GTAO around one half-resolution texel: the view depth of a distance, the texel width, the empty near
/// range below the nearest step and the spacing of the steps (docs/plans/ambient-occlusion.md, "The method", and
/// docs/plans/screen-space-illumination.md, "Where the near range is empty").
/// </summary>
public sealed class HorizonSearchTests
{
    private const double TanHalfX = 0.75;
    private const int FullWidth = 1920;

    /// <summary>The view ray of the full-resolution pixel column <paramref name="column"/> on the middle row.</summary>
    private static Double3 RayOfColumn(int column) => new((2 * (column + 0.5) / FullWidth - 1) * TanHalfX, 0, 1);

    [Theory]
    [InlineData(1.3)]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void WhereTheRadiusSpansAtMostTheNearestStepTheNearRangeHoldsNoStep(double radiusInTexels) =>
        HorizonSearch
            .NearRangeHoldsSteps(radiusInTexels)
            .Should()
            .BeFalse(
                "the texel takes the ambient light in full there (docs/plans/screen-space-illumination.md, decision 21)"
            );

    [Theory]
    [InlineData(1.31)]
    [InlineData(2.0)]
    [InlineData(40.0)]
    public void WhereTheRadiusSpansMoreThanTheNearestStepTheNearRangeHoldsSteps(double radiusInTexels) =>
        HorizonSearch.NearRangeHoldsSteps(radiusInTexels).Should().BeTrue();

    [Fact]
    public void WhereTheNearRangeHoldsNoStepEveryStepWouldLieAtTheRadiusOrBeyond()
    {
        const int stepsPerSide = 4;
        foreach (double radiusInTexels in new[] { 0.01, 1.0, 1.3 })
        foreach (float stepNoise in new[] { 0f, 0.3f, 0.999f })
        foreach (int slice in new[] { 0, 1, 2 })
            for (int step = 0; step < stepsPerSide; step++)
                HorizonSearch
                    .StepFraction(
                        step: step,
                        slice: slice,
                        stepsPerSide: stepsPerSide,
                        stepNoise: stepNoise,
                        radiusInTexels: radiusInTexels
                    )
                    .Should()
                    .BeGreaterThanOrEqualTo(1, "the skipped steps are the ones that the radius never reaches");
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
    public void AStepSnapsToTheNearestTexelAlongItsSlice() =>
        HorizonSearch
            .StepOffset(
                HorizonSearch.ExactStepOffset(
                    stepFraction: 1,
                    sliceAngle: Math.PI / 4,
                    radiusInTexels: 10,
                    halfSize: (Width: 960, Height: 540)
                )
            )
            .Should()
            .Be((7, -7), "the slice at 45° runs right and up the screen, whose rows run down");

    [Fact]
    public void AStepsExactPlaceLiesOnItsSliceLineBeforeTheSnap()
    {
        (double x, double y) = HorizonSearch.ExactStepOffset(
            stepFraction: 1,
            sliceAngle: Math.PI / 4,
            radiusInTexels: 10,
            halfSize: (Width: 960, Height: 540)
        );

        x.Should().BeApproximately(10 * Math.Sqrt(0.5), 1e-12, "the slice at 45° runs right");
        y.Should().BeApproximately(-10 * Math.Sqrt(0.5), 1e-12, "and up the screen, whose rows run down");
    }

    [Fact]
    public void AStepFarBeyondTheImageLandsJustPastItsFarthestEdge()
    {
        // A radius of 10^6 m on a surface at the 0.05 m near plane is about 10^10 texels at 1080p, past the range of an
        // int, whose conversion from a float out of range Vulkan leaves undefined.
        (int Width, int Height) halfSize = (Width: 960, Height: 540);

        HorizonSearch
            .StepOffset(
                HorizonSearch.ExactStepOffset(stepFraction: 1, sliceAngle: 0, radiusInTexels: 1e10, halfSize: halfSize)
            )
            .Should()
            .Be((961, 0), "one texel past the longer side lies outside the image from every texel in it");
        HorizonSearch
            .StepOffset(
                HorizonSearch.ExactStepOffset(
                    stepFraction: 1,
                    sliceAngle: Math.PI / 2,
                    radiusInTexels: 1e10,
                    halfSize: halfSize
                )
            )
            .Should()
            .Be((0, -961));
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
    public void ASurfaceSampleLiesAtItsViewDepthAlongItsRay() =>
        HorizonSearch.SamplePosition(ray: new(0.25, 1.0 / 3, 1), viewDepth: 12).Should().Be(new Double3(3, 4, 12));

    [Theory]
    [InlineData(170.0, 16.0 / 9)]
    [InlineData(60.0, 40.0)]
    public void ASkySampleHasNoPositionWhereItsPositionWouldSquarePastTheSingleFloatRange(
        double verticalFovDegrees,
        double aspect
    )
    {
        // The ray through the image's corner, at a very wide field of view, and at the default one in a very wide window.
        double tanHalfY = Math.Tan(verticalFovDegrees * Math.PI / 360);
        var corner = new Double3(tanHalfY * aspect, tanHalfY, 1);
        var skyInSingleFloats = new Vector3(
            (float)(corner.X * HorizonSearch.SkyDepth),
            (float)(corner.Y * HorizonSearch.SkyDepth),
            (float)HorizonSearch.SkyDepth
        );
        skyInSingleFloats
            .LengthSquared()
            .Should()
            .Be(float.PositiveInfinity, "the shader's length of this sky position would see an infinity");

        HorizonSearch
            .SamplePosition(ray: corner, viewDepth: HorizonSearch.SkyDepth)
            .Should()
            .BeNull("sky hides nothing, so the shader measures no sky sample");
    }

    [Fact]
    public void ASkyPixelTakesTheSkyDepth() =>
        HorizonSearch.ViewDepth(distance: 0, ray: new(0.25, 1.0 / 3, 1)).Should().Be(HorizonSearch.SkyDepth);
}
