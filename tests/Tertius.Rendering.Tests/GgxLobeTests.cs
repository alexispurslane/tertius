namespace Tertius.Rendering.Tests;

/// <summary>
/// The GGX lobe of the C# BRDF: its distribution, its visibility term, its roughness floor, its dominant direction
/// (docs/plans/pbr-brdf.md, "The terms") and its specular occlusion (docs/plans/ambient-occlusion.md, "The lighting
/// pass").
/// </summary>
public sealed class GgxLobeTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.045)]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(1.0)]
    public void TheDistributionIntegratesToOneOverTheProjectedHalfVectors(double roughness)
    {
        var lobe = new GgxLobe(roughness);

        // D depends only on the polar angle of h, so the integral over the hemisphere is 2π times one over θ. The
        // step of 2.5e-6 rad puts about 800 points across the width of the floor's lobe, α ≈ 0.002 rad.
        const int count = 640_000;
        double step = Math.PI / 2 / count;
        double integral = 0;
        for (int index = 0; index < count; index++)
        {
            double theta = (index + 0.5) * step;
            integral += lobe.Distribution(Math.Cos(theta)) * Math.Cos(theta) * Math.Sin(theta) * step;
        }

        (2 * Math.PI * integral).Should().BeApproximately(1, 1e-6);
    }

    [Fact]
    public void TheVisibilityTermIsSymmetricInTheLightAndTheView()
    {
        double[] cosines = [0.01, 0.2, 0.5, 0.9, 1];
        foreach (double roughness in new[] { 0.045, 0.3, 0.7, 1 })
        {
            var lobe = new GgxLobe(roughness);
            foreach (double nDotL in cosines)
            foreach (double nDotV in cosines)
                lobe.Visibility(nDotL: nDotL, nDotV: nDotV).Should().BePositive();
            foreach (double nDotL in cosines)
            foreach (double nDotV in cosines)
                lobe.Visibility(nDotL: nDotL, nDotV: nDotV)
                    .Should()
                    .Be(lobe.Visibility(nDotL: nDotV, nDotV: nDotL), $"r = {roughness}, n·l = {nDotL}, n·v = {nDotV}");
        }
    }

    [Fact]
    public void TheVisibilityTermAtNormalIncidenceIsAQuarter() =>
        // Head on, G₂ = 1, so Vis = G₂ / (4 (n·l) (n·v)) = 1/4 at every roughness.
        new GgxLobe(0.5)
            .Visibility(nDotL: 1, nDotV: 1)
            .Should()
            .BeApproximately(0.25, 1e-15);

    [Fact]
    public void TheRoughnessFloorKeepsAlphaAtTheFloorSquared()
    {
        var smoothest = new GgxLobe(0);
        var roughest = new GgxLobe(1);

        smoothest.Roughness.Should().Be(GgxLobe.MinRoughness);
        smoothest.Alpha.Should().BeApproximately(0.045 * 0.045, 1e-18);
        roughest.Roughness.Should().Be(1);
        roughest.Alpha.Should().Be(1);
    }

    [Fact]
    public void AStoredRoughnessAboveOneClampsToOne() =>
        new GgxLobe(1.5).Roughness.Should().Be(1, "the floor's clamp also has a ceiling");

    [Fact]
    public void ASmoothLobesDominantDirectionIsTheMirrorDirectionAndARoughOneLeansToTheNormal()
    {
        Double3 normal = Double3.UnitZ;
        Double3 mirror = new Double3(1, 0, 1).Normalized();

        Double3 smooth = new GgxLobe(0).DominantDirection(normal: normal, mirror: mirror);
        Double3 middle = new GgxLobe(0.5).DominantDirection(normal: normal, mirror: mirror);
        Double3 rough = new GgxLobe(1).DominantDirection(normal: normal, mirror: mirror);

        Double3.Dot(smooth, mirror).Should().BeGreaterThan(Math.Cos(0.002), "at the floor the lobe is a near mirror");
        smooth.Length.Should().BeApproximately(1, 1e-12);
        rough.X.Should().BeApproximately(0, 1e-12, "at α = 1 the dominant direction is the normal");
        rough.Z.Should().BeApproximately(1, 1e-12);
        middle.Length.Should().BeApproximately(1, 1e-12);
        double middleTilt = Math.Atan2(middle.X, middle.Z);
        middleTilt.Should().BeInRange(0.1, Math.PI / 4 - 0.05, "a lobe of middle roughness leans part of the way");
    }

    [Fact]
    public void ADominantDirectionHalfwayFromTheNormalToItsOppositeIsTheNormal()
    {
        // A surface seen straight from behind its normal mirrors the view to −n, and the mix of the two at one half is
        // the zero vector, which has no direction. brdf.glsl falls back to the normal there.
        Double3 normal = Double3.UnitZ;

        GgxLobe
            .DominantDirection(normal: normal, mirror: -normal, towardMirror: 0.5)
            .Should()
            .Be(normal, "the C# copy falls back to the normal as the shader does");
    }

    [Fact]
    public void AtSixtyDegreesTheGgxTailIsOverAHundredTimesTheBlinnPhongTailOfTheSameHighlight()
    {
        var lobe = new GgxLobe(0.6);
        double exponent = 2 / (lobe.Alpha * lobe.Alpha) - 2;
        double nDotH = Math.Cos(Math.PI / 3);

        double ggx = lobe.Distribution(nDotH);
        double blinnPhong = (exponent + 2) / (2 * Math.PI) * Math.Pow(nDotH, exponent);

        (ggx / blinnPhong).Should().BeInRange(250, 350, "the doc computes about 300 times");
    }

    [Fact]
    public void TheDistributionPeaksAtOneOverPiAlphaSquared()
    {
        var lobe = new GgxLobe(0);

        lobe.Distribution(1).Should().BeApproximately(1 / (Math.PI * lobe.Alpha * lobe.Alpha), 1e-6);
    }

    [Fact]
    public void SampledHalfVectorsAreUnitAndSpreadByTheGgxDistribution()
    {
        var lobe = new GgxLobe(0.6);

        // The cumulative share of half vectors within θ of the normal is u₂ under GGX: tan²θ = α² u₂ / (1 − u₂).
        Double3 h = lobe.SampleHalfVector(u1: 0.25, u2: 0.5);

        h.Length.Should().BeApproximately(1, 1e-12);
        h.X.Should().BeApproximately(0, 1e-12, "u₁ = 1/4 turns the azimuth to 90°");
        h.Y.Should().BeGreaterThan(0);
        double tanSquared = (h.X * h.X + h.Y * h.Y) / (h.Z * h.Z);
        tanSquared.Should().BeApproximately(lobe.Alpha * lobe.Alpha, 1e-12);
    }

    [Fact]
    public void AnUnoccludedPointHasNoSpecularOcclusion()
    {
        foreach (double roughness in new[] { 0, 0.2, 0.5, 1 })
        foreach (double nDotV in new[] { 1e-4, 0.1, 0.5, 1 })
            new GgxLobe(roughness)
                .SpecularOcclusion(nDotV: nDotV, occlusion: 1)
                .Should()
                .Be(1, $"r = {roughness}, μ = {nDotV}");
    }

    [Fact]
    public void AFullyOccludedPointHasFullSpecularOcclusion()
    {
        foreach (double roughness in new[] { 0, 0.2, 0.5, 1 })
        foreach (double nDotV in new[] { 1e-4, 0.1, 0.5, 1 })
            new GgxLobe(roughness)
                .SpecularOcclusion(nDotV: nDotV, occlusion: 0)
                .Should()
                .Be(0, $"r = {roughness}, μ = {nDotV}");
    }

    [Fact]
    public void ARoughLobesSpecularOcclusionIsItsOcclusion()
    {
        var rough = new GgxLobe(1);
        foreach (double occlusion in new[] { 0.1, 0.4, 0.7, 0.95 })
        foreach (double nDotV in new[] { 0.05, 0.5, 1 })
            rough
                .SpecularOcclusion(nDotV: nDotV, occlusion: occlusion)
                .Should()
                .BeApproximately(occlusion, 1e-4, $"V = {occlusion}, μ = {nDotV}: α = 1 makes the exponent 2⁻¹⁷");
    }

    [Fact]
    public void ASmoothLobeSeenHeadOnLosesLessThanItsOcclusionAndAtAGrazingAngleMore()
    {
        var smooth = new GgxLobe(0.1);
        const double occlusion = 0.5;

        smooth.SpecularOcclusion(nDotV: 1, occlusion: occlusion).Should().BeGreaterThan(occlusion);
        smooth.SpecularOcclusion(nDotV: 0.05, occlusion: occlusion).Should().BeLessThan(occlusion);
    }
}

/// <summary>
/// The independent check of the BRDF's integrals over the light directions: a midpoint rule over the half vectors h
/// around the normal +Z, not the Hammersley sampler. The polar angle is θ = atan(w tan(π s / 2)) over a uniform s, so
/// that a lobe of width w gets most points; w = 1 spreads θ evenly. Each h maps to the light direction that mirrors
/// the view direction about it, with dω_l = 4 (v·h) dω_h.
/// </summary>
internal static class HalfVectorQuadrature
{
    private const int PolarCount = 768;
    private const int AzimuthCount = 384;

    /// <summary>The integral of <paramref name="integrand"/> over the light directions above the horizon.</summary>
    public static Double3 ColorIntegral(Double3 toView, double width, Func<Double3, Double3> integrand)
    {
        Double3 sum = Double3.Zero;
        double polarStep = 1.0 / PolarCount;
        double azimuthStep = 2 * Math.PI / AzimuthCount;
        for (int polar = 0; polar < PolarCount; polar++)
        {
            double s = (polar + 0.5) * polarStep;
            double x = Math.Tan(Math.PI * s / 2);
            double theta = Math.Atan(width * x);
            double thetaPerS = width * (Math.PI / 2) * (1 + x * x) / (1 + width * width * x * x);
            for (int azimuth = 0; azimuth < AzimuthCount; azimuth++)
            {
                double phi = (azimuth + 0.5) * azimuthStep;
                var h = new Double3(Math.Sin(theta) * Math.Cos(phi), Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta));
                double vDotH = Double3.Dot(toView, h);
                Double3 toLight = 2 * vDotH * h - toView;
                if (vDotH <= 0 || toLight.Z <= 0)
                    continue;
                double solidAngle = 4 * vDotH * Math.Sin(theta) * thetaPerS * polarStep * azimuthStep;
                sum += integrand(toLight) * solidAngle;
            }
        }
        return sum;
    }

    /// <summary>The integral of a scalar <paramref name="integrand"/> over the light directions above the horizon.
    /// </summary>
    public static double Integral(Double3 toView, double width, Func<Double3, double> integrand) =>
        ColorIntegral(toView: toView, width: width, integrand: toLight => new Double3(integrand(toLight), 0, 0)).X;

    /// <summary>The unit view direction at the cosine <paramref name="nDotV"/> to the normal +Z, in the XZ plane.
    /// </summary>
    public static Double3 View(double nDotV) => new(Math.Sqrt(1 - nDotV * nDotV), 0, nDotV);
}
