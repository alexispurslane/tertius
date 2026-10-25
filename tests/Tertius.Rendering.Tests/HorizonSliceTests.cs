namespace Tertius.Rendering.Tests;

/// <summary>
/// One slice of GTAO: its inner integral between the horizons, its tangent-plane horizons, the hard edge at the radius
/// and the guard against a normal with no part in the slice (docs/plans/ambient-occlusion.md, "The visibility of a
/// slice"). Integrals use an independent midpoint quadrature.
/// </summary>
public sealed class HorizonSliceTests
{
    // The view vector of a pixel at the center of the screen, from the surface toward the eye.
    private static readonly Double3 TowardTheEye = new(0, 0, -1);

    /// <summary>A unit normal tilted from the eye toward +x by <paramref name="tilt"/> radians.</summary>
    private static Double3 Tilted(double tilt) => new(Math.Sin(tilt), 0, -Math.Cos(tilt));

    /// <summary>∫ from h0 to h1 of cos(θ − γ) · |sin θ| dθ, by the midpoint rule.</summary>
    private static double CosineWeightedArc(double h0, double h1, double normalAngle)
    {
        const int count = 200_000;
        double step = (h1 - h0) / count;
        double integral = 0;
        for (int index = 0; index < count; index++)
        {
            double theta = h0 + (index + 0.5) * step;
            integral += Math.Cos(theta - normalAngle) * Math.Abs(Math.Sin(theta)) * step;
        }
        return integral;
    }

    [Fact]
    public void AnOpenSliceFacingTheEyeHasAnInnerIntegralOfOne()
    {
        double arc = HorizonSlice.SliceArc(-Math.PI / 2, 0) + HorizonSlice.SliceArc(Math.PI / 2, 0);

        arc.Should().BeApproximately(1, 1e-15, "the whole cosine-weighted half disc of the slice is open");
    }

    [Fact]
    public void TheInnerIntegralIsTheCosineWeightedArcBetweenTheTwoHorizons()
    {
        double[] lowerHorizons = [-Math.PI / 2, -1.2, -0.6, -0.1, 0];
        double[] upperHorizons = [0, 0.2, 0.7, 1.3, Math.PI / 2];
        double[] normalAngles = [-1.4, -0.7, -0.2, 0, 0.3, 0.9, 1.5];

        foreach (double h0 in lowerHorizons)
        foreach (double h1 in upperHorizons)
        foreach (double normalAngle in normalAngles)
        {
            double arc = HorizonSlice.SliceArc(h0, normalAngle) + HorizonSlice.SliceArc(h1, normalAngle);

            arc.Should()
                .BeApproximately(
                    CosineWeightedArc(h0: h0, h1: h1, normalAngle: normalAngle),
                    1e-9,
                    $"h0 = {h0}, h1 = {h1}, γ = {normalAngle}"
                );
        }
    }

    [Fact]
    public void AveragedOverEverySliceAnOpenSurfaceIsFullyVisibleAtAnyTilt()
    {
        foreach (double degrees in new[] { 0.0, 10, 30, 45, 60, 75, 89 })
        {
            Double3 normal = Tilted(degrees * Math.PI / 180);

            // (1/π) ∫ from 0 to π of the visibility of the open slice at φ, by the midpoint rule over φ.
            const int count = 20_000;
            double step = Math.PI / count;
            double integral = 0;
            for (int index = 0; index < count; index++)
            {
                var slice = new HorizonSlice(toEye: TowardTheEye, normal: normal, angle: (index + 0.5) * step);
                double open = slice.Visibility(
                    horizonCosinePlus: slice.TangentHorizonCosine(+1),
                    horizonCosineMinus: slice.TangentHorizonCosine(-1)
                );
                integral += open * step;
            }

            (integral / Math.PI).Should().BeApproximately(1, 1e-4, $"a tilt of {degrees}°");
        }
    }

    [Theory]
    [InlineData(0.6)]
    [InlineData(-0.6)]
    public void EachSidesLowestHorizonIsTheSurfacesTangentPlane(double tilt)
    {
        // In the slice of φ = 0, the plus side runs toward +x. The tangent plane of the tilted normal meets the slice
        // in the direction (cos t, 0, sin t) on the plus side and its opposite on the minus side.
        var slice = new HorizonSlice(toEye: TowardTheEye, normal: Tilted(tilt), angle: 0);
        Double3 tangentPlus = new(Math.Cos(tilt), 0, Math.Sin(tilt));

        slice.TangentHorizonCosine(+1).Should().BeApproximately(Double3.Dot(tangentPlus, TowardTheEye), 1e-12);
        slice.TangentHorizonCosine(-1).Should().BeApproximately(Double3.Dot(-tangentPlus, TowardTheEye), 1e-12);
    }

    [Fact]
    public void ASampleAtTheRadiusOrBeyondItHidesNothing()
    {
        const double radius = 1.5;

        HorizonSlice.Hides(sampleDistance: 1.4999, radius: radius).Should().BeTrue();
        HorizonSlice.Hides(sampleDistance: radius, radius: radius).Should().BeFalse("the edge at the radius is hard");
        HorizonSlice.Hides(sampleDistance: 1.5001, radius: radius).Should().BeFalse();
    }

    [Fact]
    public void HorizonsAtTheNormalHideTheWholeSlice()
    {
        // The normal faces the eye, γ = 0, so horizon cosines of 1 put both horizons at the normal.
        var slice = new HorizonSlice(toEye: TowardTheEye, normal: TowardTheEye, angle: 0.4);

        slice.Visibility(horizonCosinePlus: 1, horizonCosineMinus: 1).Should().BeApproximately(0, 1e-15);
    }

    [Fact]
    public void ANormalPerpendicularToTheSlicePlaneAddsNothing()
    {
        // The slice of φ = 0 holds the x and z axes, so a normal along y has no part in it.
        var slice = new HorizonSlice(toEye: TowardTheEye, normal: Double3.UnitY, angle: 0);

        double visibility = slice.Visibility(
            horizonCosinePlus: slice.TangentHorizonCosine(+1),
            horizonCosineMinus: slice.TangentHorizonCosine(-1)
        );

        visibility.Should().Be(0, "the guard turns the 0 / 0 of the normal's angle into 0, not NaN");
    }
}
