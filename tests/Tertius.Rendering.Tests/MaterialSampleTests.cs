namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# BRDF of one material: its F0, its Fresnel term, its reciprocity, its energy and the ambient light's formula
/// (docs/plans/pbr-brdf.md, "The BRDF"). Integrals use <see cref="HalfVectorQuadrature"/>.
/// </summary>
public sealed class MaterialSampleTests
{
    private static readonly Double3 White = new(1, 1, 1);
    private static readonly double[] ViewCosines = [0.1, 0.3, 0.5, 0.8, 1];
    private static readonly double[] Roughnesses = [0, 0.2, 0.4, 0.6, 0.8, 1];

    /// <summary>The canopy glass of the demo shuttle, without its opacity: a reflectance of 1 gives an F0 of 0.16.</summary>
    private static readonly MaterialSample Glass = new(
        BaseColor: new(0.45, 0.65, 1),
        Metallic: 0,
        Roughness: 0.18,
        Reflectance: 1
    );

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance, string because)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, because);
        actual.Y.Should().BeApproximately(expected.Y, tolerance, because);
        actual.Z.Should().BeApproximately(expected.Z, tolerance, because);
    }

    [Fact]
    public void AReflectanceOfOneHalfGivesANonMetalAnF0OfFourPercent()
    {
        var plastic = new MaterialSample(BaseColor: new(0.8, 0.1, 0.1), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);

        ShouldBeNear(actual: plastic.F0, expected: new(0.04, 0.04, 0.04), tolerance: 1e-15, because: "F0 = 0.16 ρ²");
    }

    [Fact]
    public void ANonMetalsF0IsTheSameInEveryChannelWhateverItsBaseColor()
    {
        var red = new MaterialSample(BaseColor: new(0.9, 0.05, 0), Metallic: 0, Roughness: 0.5, Reflectance: 0.8);
        var gray = new MaterialSample(BaseColor: new(0.3, 0.3, 0.3), Metallic: 0, Roughness: 0.5, Reflectance: 0.8);

        red.F0.Should().Be(gray.F0);
        red.F0.X.Should().Be(red.F0.Y);
        red.F0.Y.Should().Be(red.F0.Z);
        red.F0.X.Should().BeApproximately(0.16 * 0.8 * 0.8, 1e-15);
    }

    [Fact]
    public void ANonMetalsDiffuseColorIsItsBaseColor()
    {
        var plastic = new MaterialSample(BaseColor: new(0.8, 0.4, 0.1), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);

        plastic.DiffuseColor.Should().Be(new Double3(0.8, 0.4, 0.1));
    }

    [Fact]
    public void AMetalTakesItsF0FromItsBaseColorAndHasNoDiffuseColor()
    {
        var gold = new MaterialSample(BaseColor: new(1, 0.78, 0.34), Metallic: 1, Roughness: 0.3, Reflectance: 0.5);

        gold.F0.Should().Be(new Double3(1, 0.78, 0.34));
        gold.DiffuseColor.Should().Be(Double3.Zero);
    }

    [Fact]
    public void AHalfMetalMixesBothF0sAndHalvesItsDiffuseColor()
    {
        var half = new MaterialSample(BaseColor: new(0.6, 0.4, 0.2), Metallic: 0.5, Roughness: 0.3, Reflectance: 0.5);

        ShouldBeNear(
            actual: half.F0,
            expected: new(0.32, 0.22, 0.12),
            tolerance: 1e-15,
            because: "F0 = (1 − m) 0.04 + m b"
        );
        ShouldBeNear(
            actual: half.DiffuseColor,
            expected: new(0.3, 0.2, 0.1),
            tolerance: 1e-15,
            because: "the diffuse color is b (1 − m)"
        );
    }

    [Fact]
    public void TheMaterialsLobeHasTheMaterialsRoughnessAfterTheFloor()
    {
        new MaterialSample(BaseColor: White, Metallic: 0, Roughness: 0.01, Reflectance: 0.5)
            .Lobe.Should()
            .Be(new GgxLobe(0.01));
        new MaterialSample(BaseColor: White, Metallic: 0, Roughness: 0.7, Reflectance: 0.5)
            .Lobe.Roughness.Should()
            .Be(0.7);
    }

    [Fact]
    public void FresnelHeadOnIsF0()
    {
        var gold = new MaterialSample(BaseColor: new(1, 0.78, 0.34), Metallic: 1, Roughness: 0.3, Reflectance: 0.5);

        gold.Fresnel(1).Should().Be(gold.F0);
    }

    [Fact]
    public void FresnelAtAGrazingAngleIsOne()
    {
        var plastic = new MaterialSample(BaseColor: new(0.8, 0.1, 0.1), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);

        ShouldBeNear(
            actual: plastic.Fresnel(0),
            expected: White,
            tolerance: 1e-15,
            because: "plain Schlick reaches 1 at every F0"
        );
    }

    [Fact]
    public void FresnelAtSixtyDegreesFollowsSchlicksFifthPower()
    {
        var plastic = new MaterialSample(BaseColor: new(0.8, 0.1, 0.1), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);

        ShouldBeNear(
            actual: plastic.Fresnel(0.5),
            expected: White * (0.04 + 0.96 / 32),
            tolerance: 1e-15,
            because: "F0 + (1 − F0) (1/2)⁵"
        );
    }

    [Fact]
    public void TheSingleScatteringBrdfIsReciprocal()
    {
        MaterialSample[] materials =
        [
            new(BaseColor: new(0.8, 0.4, 0.1), Metallic: 0, Roughness: 0.045, Reflectance: 0.5),
            new(BaseColor: new(0.2, 0.6, 0.9), Metallic: 0, Roughness: 0.5, Reflectance: 0.9),
            new(BaseColor: new(1, 0.78, 0.34), Metallic: 1, Roughness: 0.3, Reflectance: 0.5),
            new(BaseColor: new(0.9, 0.9, 0.9), Metallic: 1, Roughness: 1, Reflectance: 0.5),
        ];
        double[] polarAngles = [0.1, 0.7, 1.2, 1.5];
        double[] azimuths = [0, 1.6, 3.5];
        Double3 normal = new Double3(0.2, -0.1, 1).Normalized();
        var directions = new List<Double3>();
        foreach (double polar in polarAngles)
        foreach (double azimuth in azimuths)
        {
            var tangentSpace = new Double3(
                Math.Sin(polar) * Math.Cos(azimuth),
                Math.Sin(polar) * Math.Sin(azimuth),
                Math.Cos(polar)
            );
            // Tilts the direction with the normal, so that both stay above its horizon.
            directions.Add((tangentSpace + (normal - Double3.UnitZ)).Normalized());
        }

        foreach (MaterialSample material in materials)
        foreach (Double3 toView in directions)
        foreach (Double3 toLight in directions)
        {
            if (Double3.Dot(toView, normal) <= 0.01 || Double3.Dot(toLight, normal) <= 0.01)
                continue;
            Double3 forward = material.SingleScatteringBrdf(normal: normal, toView: toView, toLight: toLight);
            Double3 backward = material.SingleScatteringBrdf(normal: normal, toView: toLight, toLight: toView);
            forward.X.Should().BeGreaterThan(0);
            ShouldBeNear(
                actual: backward,
                expected: forward,
                tolerance: 1e-9 * Math.Max(1, forward.X),
                because: $"{material}, v = {toView}, l = {toLight}"
            );
        }
    }

    [Fact]
    public void TheLambertPartUnderAUniformSkyReflectsExactlyTheDiffuseColor()
    {
        // A non-metal's F0 does not depend on its base color, so the two materials share their specular part and
        // differ by the Lambert part alone.
        var colored = new MaterialSample(BaseColor: new(0.8, 0.5, 0.2), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);
        MaterialSample black = colored with { BaseColor = Double3.Zero };

        foreach (double nDotV in ViewCosines)
        {
            Double3 toView = HalfVectorQuadrature.View(nDotV);
            Double3 reflected = HalfVectorQuadrature.ColorIntegral(
                toView: toView,
                width: 1,
                integrand: toLight =>
                    (
                        colored.SingleScatteringBrdf(normal: Double3.UnitZ, toView: toView, toLight: toLight)
                        - black.SingleScatteringBrdf(normal: Double3.UnitZ, toView: toView, toLight: toLight)
                    ) * toLight.Z
            );

            ShouldBeNear(actual: reflected, expected: new(0.8, 0.5, 0.2), tolerance: 1e-3, because: $"n·v = {nDotV}");
        }
    }

    [Fact]
    public void TheSingleScatteringLobeNeverReflectsMoreThanItReceives()
    {
        foreach (double roughness in Roughnesses)
        foreach (double nDotV in ViewCosines)
        {
            // A white metal has no diffuse part, and its Fresnel term is 1 at every angle.
            var mirror = new MaterialSample(BaseColor: White, Metallic: 1, Roughness: roughness, Reflectance: 0.5);
            Double3 toView = HalfVectorQuadrature.View(nDotV);
            double lobeAlbedo = HalfVectorQuadrature.Integral(
                toView: toView,
                width: mirror.Lobe.Alpha,
                integrand: toLight =>
                    mirror.SingleScatteringBrdf(normal: Double3.UnitZ, toView: toView, toLight: toLight).X * toLight.Z
            );

            lobeAlbedo.Should().BeLessThanOrEqualTo(1 + 1e-4, $"r = {roughness}, n·v = {nDotV}");
            lobeAlbedo.Should().BeGreaterThan(0.3, "even the roughest lobe keeps 1 − ln 2 head on");
        }
    }

    [Fact]
    public void WithTheCompensationAWhiteMetalReflectsAllTheLightAtEveryAngleAndRoughness()
    {
        foreach (double roughness in Roughnesses)
        foreach (double nDotV in ViewCosines)
        {
            var mirror = new MaterialSample(BaseColor: White, Metallic: 1, Roughness: roughness, Reflectance: 0.5);
            Double3 toView = HalfVectorQuadrature.View(nDotV);
            double reflected = HalfVectorQuadrature.Integral(
                toView: toView,
                width: mirror.Lobe.Alpha,
                integrand: toLight =>
                    mirror
                        .Reflected(normal: Double3.UnitZ, toView: toView, toLight: toLight, tables: BrdfTables.Shared)
                        .X
            );

            reflected.Should().BeApproximately(1, 0.01, $"the white furnace, r = {roughness}, n·v = {nDotV}");
        }
    }

    [Fact]
    public void WithTheCompensationNoMetalReflectsMoreThanItReceives()
    {
        double[] f0s = [0, 0.25, 0.5, 0.75, 1];
        foreach (double roughness in Roughnesses)
        foreach (double nDotV in ViewCosines)
        {
            Double3 toView = HalfVectorQuadrature.View(nDotV);
            foreach (double f0 in f0s)
            {
                var metal = new MaterialSample(
                    BaseColor: White * f0,
                    Metallic: 1,
                    Roughness: roughness,
                    Reflectance: 0.5
                );
                double reflected = HalfVectorQuadrature.Integral(
                    toView: toView,
                    width: metal.Lobe.Alpha,
                    integrand: toLight =>
                        metal
                            .Reflected(
                                normal: Double3.UnitZ,
                                toView: toView,
                                toLight: toLight,
                                tables: BrdfTables.Shared
                            )
                            .X
                );

                reflected
                    .Should()
                    .BeLessThanOrEqualTo(1.01, $"the furnace's tolerance, F0 = {f0}, r = {roughness}, n·v = {nDotV}");
            }
        }
    }

    [Fact]
    public void WithAnF0OfZeroTheCompensationChangesNothing()
    {
        var black = new MaterialSample(BaseColor: Double3.Zero, Metallic: 1, Roughness: 0.8, Reflectance: 0.5);
        Double3 toView = HalfVectorQuadrature.View(0.6);
        Double3 toLight = new Double3(-0.5, 0.2, 0.7).Normalized();

        Double3 compensated = black.Reflected(
            normal: Double3.UnitZ,
            toView: toView,
            toLight: toLight,
            tables: BrdfTables.Shared
        );
        Double3 single =
            black.SingleScatteringBrdf(normal: Double3.UnitZ, toView: toView, toLight: toLight) * toLight.Z;

        ShouldBeNear(
            actual: compensated,
            expected: single,
            tolerance: 1e-15,
            because: "c = 1 + F0 (1 / E₁ − 1) is 1 at F0 = 0"
        );
        compensated.X.Should().BeGreaterThan(0, "the Schlick part still reflects");
    }

    [Fact]
    public void AViewBelowTheShadingNormalsHorizonStillGivesAFinitePositiveLobe()
    {
        // A normal map can turn the shading normal away from the eye, and the visibility term divides by μ. A black
        // non-metal shows the lobe alone.
        var black = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: 0.3, Reflectance: 0.5);
        Double3 toView = new Double3(0.866, 0, -0.5).Normalized();
        Double3 toLight = new Double3(-0.995, 0, 0.1).Normalized();

        Double3 lobe = black.SingleScatteringBrdf(normal: Double3.UnitZ, toView: toView, toLight: toLight);

        double.IsFinite(lobe.X).Should().BeTrue();
        lobe.X.Should().BeGreaterThan(0, "μ keeps its floor of 1e-4");
    }

    [Fact]
    public void ALightBelowTheHorizonReflectsNothing()
    {
        var plastic = new MaterialSample(BaseColor: new(0.8, 0.4, 0.1), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);

        Double3 reflected = plastic.Reflected(
            normal: Double3.UnitZ,
            toView: HalfVectorQuadrature.View(0.6),
            toLight: new Double3(0.3, 0, -0.2).Normalized(),
            tables: BrdfTables.Shared
        );

        reflected.Should().Be(Double3.Zero);
    }

    [Fact]
    public void UnderAUniformSkyTheAmbientPartIsTheDiffuseColorPlusTheCompensatedSpecularAlbedo()
    {
        MaterialSample[] materials =
        [
            new(BaseColor: new(0.7, 0.4, 0.2), Metallic: 0, Roughness: 0.6, Reflectance: 0.5),
            new(BaseColor: new(0.3, 0.6, 0.8), Metallic: 0, Roughness: 1, Reflectance: 1),
            new(BaseColor: new(0.9, 0.6, 0.3), Metallic: 1, Roughness: 0.3, Reflectance: 0.5),
            new(BaseColor: new(0.5, 0.5, 0.5), Metallic: 1, Roughness: 0, Reflectance: 0.5),
        ];
        Double3 sky = new(0.6, 0.8, 1.2);

        foreach (MaterialSample material in materials)
        foreach (double nDotV in ViewCosines)
        {
            Double3 toView = HalfVectorQuadrature.View(nDotV);
            Double3 ambient = material.ReflectedHemisphere(
                normal: Double3.UnitZ,
                toView: toView,
                sky: sky,
                ground: sky,
                normalSkyShare: 0.3,
                lobeSkyShare: 0.8,
                tables: BrdfTables.Shared
            );
            Double3 integrated = HalfVectorQuadrature.ColorIntegral(
                toView: toView,
                width: material.Lobe.Alpha,
                integrand: toLight =>
                    material.Reflected(
                        normal: Double3.UnitZ,
                        toView: toView,
                        toLight: toLight,
                        tables: BrdfTables.Shared
                    ) * sky
            );

            ambient.X.Should().BeGreaterThan(0);
            ShouldBeNear(actual: ambient, expected: integrated, tolerance: 0.01, because: $"{material}, n·v = {nDotV}");
        }
    }

    [Fact]
    public void TheDiffusePartSeesTheSkyByTheNormalsShareAndTheSpecularPartByTheLobesShare()
    {
        var plastic = new MaterialSample(BaseColor: new(0.7, 0.4, 0.2), Metallic: 0, Roughness: 0.5, Reflectance: 0.5);
        Double3 toView = HalfVectorQuadrature.View(0.7);
        Double3 Ambient(double normalSkyShare, double lobeSkyShare) =>
            plastic.ReflectedHemisphere(
                normal: Double3.UnitZ,
                toView: toView,
                sky: White,
                ground: Double3.Zero,
                normalSkyShare: normalSkyShare,
                lobeSkyShare: lobeSkyShare,
                tables: BrdfTables.Shared
            );

        Double3 diffuseOnly = Ambient(normalSkyShare: 1, lobeSkyShare: 0);
        Double3 specularOnly = Ambient(normalSkyShare: 0, lobeSkyShare: 1);
        Double3 both = Ambient(normalSkyShare: 1, lobeSkyShare: 1);

        ShouldBeNear(
            actual: diffuseOnly,
            expected: plastic.DiffuseColor,
            tolerance: 1e-15,
            because: "the ground is black, and only the normal sees the sky"
        );
        ShouldBeNear(
            actual: diffuseOnly + specularOnly,
            expected: both,
            tolerance: 1e-15,
            because: "the two parts add"
        );
        specularOnly.X.Should().BeInRange(0.03, 0.2, "a non-metal's specular albedo is a few percent");
    }

    private static Double3 GlassAlbedo(double nDotV) =>
        Glass.CompensatedSpecularAlbedo(
            normal: Double3.UnitZ,
            toView: HalfVectorQuadrature.View(nDotV),
            tables: BrdfTables.Shared
        );

    [Fact]
    public void HeadOnGlassReflectsAboutItsF0()
    {
        Double3 albedo = GlassAlbedo(1);

        ShouldBeNear(
            actual: albedo,
            expected: Glass.F0,
            tolerance: 0.01,
            because: "a smooth surface seen head on reflects F0 of a uniform light"
        );
    }

    [Fact]
    public void GlassReflectsSeveralTimesAsMuchAtAGrazingAngleAsHeadOn()
    {
        Double3 headOn = GlassAlbedo(1);
        Double3 grazing = GlassAlbedo(0.1);

        grazing.X.Should().BeGreaterThan(3 * headOn.X, "Fresnel's term rises toward one at grazing angles");
    }

    [Fact]
    public void TheSpecularAlbedoIsTheShareOfAUniformWhiteLightThatTheSpecularPartReflects()
    {
        // No diffuse color, so the whole BRDF is its specular part.
        MaterialSample blackGlass = Glass with
        {
            BaseColor = Double3.Zero,
        };
        var gold = new MaterialSample(BaseColor: new(1, 0.78, 0.34), Metallic: 1, Roughness: 0.3, Reflectance: 0.5);

        foreach (MaterialSample material in new[] { blackGlass, gold })
        foreach (double nDotV in ViewCosines)
        {
            Double3 toView = HalfVectorQuadrature.View(nDotV);
            Double3 albedo = material.CompensatedSpecularAlbedo(
                normal: Double3.UnitZ,
                toView: toView,
                tables: BrdfTables.Shared
            );
            Double3 integrated = HalfVectorQuadrature.ColorIntegral(
                toView: toView,
                width: material.Lobe.Alpha,
                integrand: toLight =>
                    material.Reflected(
                        normal: Double3.UnitZ,
                        toView: toView,
                        toLight: toLight,
                        tables: BrdfTables.Shared
                    )
            );

            ShouldBeNear(actual: albedo, expected: integrated, tolerance: 0.01, because: $"{material}, n·v = {nDotV}");
        }
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(0.2, 0.1)]
    [InlineData(0, 0.5)]
    [InlineData(0.7, 0.5)]
    public void APaneLetsThroughOneMinusItsOpacityOfTheLightThatItsSurfaceDoesNotReflect(double opacity, double nDotV)
    {
        Double3 toView = HalfVectorQuadrature.View(nDotV);

        Double3 transmittance = Glass.Transmittance(
            opacity: opacity,
            normal: Double3.UnitZ,
            toView: toView,
            tables: BrdfTables.Shared
        );

        ShouldBeNear(
            actual: transmittance,
            expected: (1 - opacity) * (White - GlassAlbedo(nDotV)),
            tolerance: 1e-15,
            because: "T = (1 − opacity)(1 − E · c) per channel"
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void AnOpaquePaneLetsNothingThrough(double opacity)
    {
        Double3 transmittance = Glass.Transmittance(
            opacity: opacity,
            normal: Double3.UnitZ,
            toView: HalfVectorQuadrature.View(0.5),
            tables: BrdfTables.Shared
        );

        transmittance.Should().Be(Double3.Zero, "an opacity of one or more hides what lies behind");
    }

    [Fact]
    public void AClearPaneOfANegativeOpacityLetsThroughWhatAClearPaneOfOpacityZeroDoes()
    {
        Double3 toView = HalfVectorQuadrature.View(0.5);

        Double3 negative = Glass.Transmittance(
            opacity: -0.5,
            normal: Double3.UnitZ,
            toView: toView,
            tables: BrdfTables.Shared
        );
        Double3 clear = Glass.Transmittance(
            opacity: 0,
            normal: Double3.UnitZ,
            toView: toView,
            tables: BrdfTables.Shared
        );

        negative.Should().Be(clear, "the shader clamps the opacity to [0, 1]");
    }

    [Fact]
    public void AClearWhiteMetalPaneLetsNothingThroughAtAnyAngle()
    {
        var mirror = new MaterialSample(BaseColor: White, Metallic: 1, Roughness: 0.4, Reflectance: 0.5);

        foreach (double nDotV in ViewCosines)
        {
            Double3 transmittance = mirror.Transmittance(
                opacity: 0,
                normal: Double3.UnitZ,
                toView: HalfVectorQuadrature.View(nDotV),
                tables: BrdfTables.Shared
            );

            transmittance.X.Should().BeInRange(0, 1e-12, "a white metal reflects all the light, n·v = {0}", nDotV);
            transmittance.Y.Should().BeInRange(0, 1e-12);
            transmittance.Z.Should().BeInRange(0, 1e-12);
        }
    }
}
