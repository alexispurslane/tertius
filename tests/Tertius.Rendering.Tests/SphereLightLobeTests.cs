using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The specular lobe of a sphere light in the C# BRDF (docs/plans/pbr-brdf.md, "Sized lights"): Karis's representative
/// point with the widened lobe's normalization. The light keeps about the energy of a point light of the same
/// irradiance, and a mirror never shows it brighter than the light's own surface. A white metal has F = 1, so the
/// specular part is the compensated lobe alone.
/// </summary>
public sealed class SphereLightLobeTests
{
    /// <summary>The sine of the Sun's angular radius from Earth: 6.957 × 10⁸ m over 1 AU.</summary>
    private const double SunSine = 6.957e8 / 1.495978707e11;

    /// <summary>
    /// Roughness from the floor to rough, by the sine of the light's angular radius from the Sun to a lamp 5.7° wide,
    /// by the angle of the light's center from the normal, in rad.
    /// </summary>
    public static TheoryData<double, double, double> Lights
    {
        get
        {
            var lights = new TheoryData<double, double, double>();
            foreach (double roughness in new[] { 0, 0.18, 0.38, 0.8 })
            foreach (double lightSine in new[] { SunSine, 0.03, 0.1 })
            foreach (double lightAngle in new[] { 0, 0.6, 1.2 })
                lights.Add(roughness, lightSine, lightAngle);
            return lights;
        }
    }

    /// <summary>The sizes and angles of <see cref="Lights"/> on a mirror of the roughness floor.</summary>
    public static TheoryData<double, double> LightsOnAMirror
    {
        get
        {
            var lights = new TheoryData<double, double>();
            foreach (double lightSine in new[] { SunSine, 0.03, 0.1 })
            foreach (double lightAngle in new[] { 0, 0.6, 1.2 })
                lights.Add(lightSine, lightAngle);
            return lights;
        }
    }

    private static MaterialSample WhiteMetal(double roughness) =>
        new(BaseColor: new(1, 1, 1), Metallic: 1, Roughness: roughness, Reflectance: 0.5);

    /// <summary>The unit direction toward a light whose center stands <paramref name="lightAngle"/> rad from the normal +Z.</summary>
    private static Double3 TowardLight(double lightAngle) => new(Math.Sin(lightAngle), 0, Math.Cos(lightAngle));

    /// <summary>
    /// The specular part over every view direction from a light of unit irradiance: its radiance times n·v, summed over
    /// the hemisphere. The grid is polar about the mirror direction of the light's center, and its rings crowd toward
    /// it, θ = π u⁴, so the narrowest lobe spans over a hundred of them.
    /// </summary>
    private static double SpecularExitance(double roughness, double lightAngle, double lightSine)
    {
        const int rings = 800,
            spokes = 180;
        MaterialSample metal = WhiteMetal(roughness);
        Double3 toLight = TowardLight(lightAngle);
        Double3 mirror = Mirror(Double3.UnitZ, toLight);
        Double3 across = Double3.Cross(mirror, Double3.UnitY).Normalized();
        var up = Double3.Cross(mirror, across);
        double sum = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            double u = (ring + 0.5) / rings;
            double theta = Math.PI * u * u * u * u;
            double ringWeight = Math.Sin(theta) * (4 * Math.PI * u * u * u / rings) * (2 * Math.PI / spokes);
            for (int spoke = 0; spoke < spokes; spoke++)
            {
                double phi = (spoke + 0.5) / spokes * 2 * Math.PI;
                Double3 toView =
                    Math.Cos(theta) * mirror + Math.Sin(theta) * (Math.Cos(phi) * across + Math.Sin(phi) * up);
                if (toView.Z <= 0)
                    continue;
                DirectTerms terms = metal.ReflectedTerms(
                    normal: Double3.UnitZ,
                    toView: toView,
                    toLight: toLight,
                    tables: BrdfTables.Shared,
                    lightSine: lightSine
                );
                sum += terms.Specular.X * toView.Z * ringWeight;
            }
        }

        return sum;
    }

    [Theory]
    [MemberData(nameof(Lights))]
    public void ASizedLightReflectsBetween85And100PercentOfTheLightOfAPointLight(
        double roughness,
        double lightSine,
        double lightAngle
    )
    {
        double point = SpecularExitance(roughness: roughness, lightAngle: lightAngle, lightSine: 0);
        double sized = SpecularExitance(roughness: roughness, lightAngle: lightAngle, lightSine: lightSine);

        (sized / point)
            .Should()
            .BeInRange(
                0.85,
                1.005,
                "a sphere light of the same irradiance reflects about the point light's energy, and the widened lobe "
                    + "never adds light (roughness {0}, sine {1}, {2} rad from the normal)",
                roughness,
                lightSine,
                lightAngle
            );
    }

    [Theory]
    [MemberData(nameof(LightsOnAMirror))]
    public void AMirrorShowsASizedLightNoBrighterThanTheLightsOwnSurface(double lightSine, double lightAngle)
    {
        // A sphere of radiance L_e and angular radius ϕ gives the irradiance E = π sin²ϕ L_e across its axis, so its
        // surface shows E / (π sin²ϕ) per unit irradiance. A mirror shows it times F = 1 and the lobe's compensation.
        MaterialSample mirror = WhiteMetal(0);
        Double3 toLight = TowardLight(lightAngle);
        double surface = 1 / (Math.PI * lightSine * lightSine);
        double brightest = 0;
        for (int ring = 0; ring < 400; ring++)
        for (int spoke = 0; spoke < 36; spoke++)
        {
            // Out from the mirror direction to 3 times the light's angular radius, past the edge of its disc.
            double theta = 3 * Math.Asin(lightSine) * ring / 399.0;
            double phi = spoke / 36.0 * 2 * Math.PI;
            Double3 center = Mirror(Double3.UnitZ, toLight);
            Double3 across = Double3.Cross(center, Double3.UnitY).Normalized();
            var up = Double3.Cross(center, across);
            Double3 toView = Math.Cos(theta) * center + Math.Sin(theta) * (Math.Cos(phi) * across + Math.Sin(phi) * up);
            double nDotV = Math.Max(toView.Z, 1e-4);
            double compensation =
                1 / BrdfTables.Shared.SampleDfg(nDotV: nDotV, roughness: mirror.Lobe.Roughness).LobeAlbedo;
            DirectTerms terms = mirror.ReflectedTerms(
                normal: Double3.UnitZ,
                toView: toView,
                toLight: toLight,
                tables: BrdfTables.Shared,
                lightSine: lightSine
            );
            brightest = Math.Max(brightest, terms.Specular.X / compensation / surface);
        }

        brightest
            .Should()
            .BeLessThanOrEqualTo(
                1.01,
                "a mirror shows a sized light at most at the light's own surface radiance, with no clamp (sine {0}, "
                    + "{1} rad from the normal)",
                lightSine,
                lightAngle
            );
    }

    [Theory]
    [InlineData(0.1, 0.0)]
    [InlineData(0.1, 1.2)]
    [InlineData(0.3, 0.6)]
    public void AMirrorShowsALargeLightAtNearlyItsOwnSurfaceRadianceInsideItsDisc(double lightSine, double lightAngle)
    {
        // The roughness floor spreads a little of the disc's light past its edge, (1 + 2 α √(v·h) / sin ϕ)⁻² of it
        // stays: 92 % for a light of sine 0.1 head-on.
        MaterialSample mirror = WhiteMetal(0);
        Double3 toLight = TowardLight(lightAngle);
        Double3 toView = Mirror(Double3.UnitZ, toLight);
        double compensation = 1 / BrdfTables.Shared.SampleDfg(nDotV: toView.Z, roughness: 0).LobeAlbedo;
        DirectTerms terms = mirror.ReflectedTerms(
            normal: Double3.UnitZ,
            toView: toView,
            toLight: toLight,
            tables: BrdfTables.Shared,
            lightSine: lightSine
        );

        (terms.Specular.X / compensation * Math.PI * lightSine * lightSine)
            .Should()
            .BeInRange(0.9, 1.0, "the disc shows the light's surface, less the floored lobe's spread");
    }

    [Theory]
    [InlineData(0.3, 0.0)]
    [InlineData(0.3, 0.6)]
    [InlineData(0.5, 0.0)]
    [InlineData(0.5, 0.6)]
    public void AFullyRoughLobeWidensNoFurtherSoALargeLightReflectsNearlyThePointLightsEnergy(
        double lightSine,
        double lightAngle
    )
    {
        // At roughness 1, α′ stops at α = 1: GGX has no wider lobe, so the normalization (α / α′)² stays 1 and only the
        // representative point moves. Its move gives 0.961 to 1.013 of the point light's energy over these cases. Without
        // the stop, (α / α′)² would take about 24 % of it at sine 0.3 and 36 % at sine 0.5.
        double point = SpecularExitance(roughness: 1, lightAngle: lightAngle, lightSine: 0);
        double sized = SpecularExitance(roughness: 1, lightAngle: lightAngle, lightSine: lightSine);

        (sized / point)
            .Should()
            .BeInRange(
                0.95,
                1.02,
                "a fully rough surface reflects a large light with nearly the point light's energy (sine {0}, {1} rad from the "
                    + "normal)",
                lightSine,
                lightAngle
            );
    }

    [Fact]
    public void WhereASizedLightsRepresentativePointSinksBelowTheHorizonItAddsNoSpecularLight()
    {
        // A light of sine 0.3 stands 1.4 rad from the normal, and the eye looks from its side at a grazing angle. The
        // mirror ray points away from the light, so the point of the sphere nearest the ray's line lies 0.12 below the
        // horizon, while the light's center stands 0.17 above it.
        var gray = new MaterialSample(BaseColor: new(0.5, 0.5, 0.5), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);
        DirectTerms terms = gray.ReflectedTerms(
            normal: Double3.UnitZ,
            toView: new Double3(0.98, 0, 0.2).Normalized(),
            toLight: TowardLight(1.4),
            tables: BrdfTables.Shared,
            lightSine: 0.3
        );

        terms
            .Specular.Should()
            .Be(Double3.Zero, "a representative point below the horizon gives no specular light, never a negative one");
    }
}
