namespace Tertius.Rendering.Tests;

/// <summary>
/// The light of a hair fiber, a diffuse part and two highlights shifted along the strand, and the strand and the fiber
/// normal of a card's point (docs/plans/skin-and-hair.md, "The hair model", "The strand and the fiber normal"). Directions
/// are given by their longitudinal angle θ from the plane across the strand and their azimuth φ around it.
/// </summary>
public sealed class HairFiberTests
{
    private static readonly Double3 Strand = Double3.UnitY;

    // The card's normal, across the strand: the azimuth 0 of every direction below.
    private static readonly Double3 CardNormal = Double3.UnitZ;

    private static readonly Double3 Brown = new(0.5, 0.3, 0.15);

    private static readonly HairFiber BrownHair = new(BaseColor: Brown, Roughness: 0.2, Reflectance: 0.5);
    private static readonly HairFiber BlackHair = BrownHair with { BaseColor = Double3.Zero };

    public static TheoryData<StrandDirection, double, Double3> TextureAxes =>
        new()
        {
            { StrandDirection.PlusU, 1, Double3.UnitX },
            { StrandDirection.MinusU, 1, -Double3.UnitX },
            { StrandDirection.PlusV, 1, Double3.UnitY },
            { StrandDirection.MinusV, 1, -Double3.UnitY },
            { StrandDirection.PlusV, -1, -Double3.UnitY },
            { StrandDirection.MinusV, -1, Double3.UnitY },
        };

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    /// <summary>The unit direction at the longitudinal angle <paramref name="theta"/> and the azimuth <paramref name="phi"/>, in radians.</summary>
    private static Double3 Direction(double theta, double phi) =>
        Strand * Math.Sin(theta) + (CardNormal * Math.Cos(phi) + Double3.UnitX * Math.Sin(phi)) * Math.Cos(theta);

    /// <summary>cos θ of <paramref name="direction"/>: the width of a fiber of unit width seen from it.</summary>
    private static double WidthAcross(Double3 direction) => Math.Sqrt(1 - Math.Pow(Double3.Dot(Strand, direction), 2));

    private static DirectTerms Reflected(HairFiber fiber, Double3 toView, Double3 toLight) =>
        fiber.Reflected(
            strand: Strand,
            fiberNormal: HairFiber.FiberNormal(strand: Strand, toView: toView, cardNormal: CardNormal),
            toView: toView,
            toLight: toLight
        );

    /// <summary>
    /// The midpoint rule of <paramref name="f"/> over the sphere of directions, with dω = cos θ dθ dφ.
    /// </summary>
    private static Double3 OverTheSphere(Func<Double3, Double3> f)
    {
        const int thetaSteps = 1800;
        const int phiSteps = 720;
        double thetaStep = Math.PI / thetaSteps;
        double phiStep = 2 * Math.PI / phiSteps;
        Double3 sum = Double3.Zero;
        for (int i = 0; i < thetaSteps; i++)
        {
            double theta = -Math.PI / 2 + (i + 0.5) * thetaStep;
            for (int j = 0; j < phiSteps; j++)
            {
                double phi = -Math.PI + (j + 0.5) * phiStep;
                sum += f(Direction(theta, phi)) * Math.Cos(theta);
            }
        }
        return sum * (thetaStep * phiStep);
    }

    /// <summary>θ_h in degrees, (θ_i + θ_r) / 2, where <paramref name="highlight"/> peaks over θ_r at φ = 0.</summary>
    private static double PeakHalfAngleDegrees(double lightThetaDegrees, Func<Double3, Double3, double> highlight)
    {
        Double3 toLight = Direction(Radians(lightThetaDegrees), 0);
        double bestTheta = double.NaN;
        double best = double.NegativeInfinity;
        for (double thetaR = -89; thetaR <= 89; thetaR += 0.01)
        {
            double value = highlight(Direction(Radians(thetaR), 0), toLight);
            if (value > best)
                (best, bestTheta) = (value, thetaR);
        }
        return (lightThetaDegrees + bestTheta) / 2;
    }

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance, string because)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, because);
        actual.Y.Should().BeApproximately(expected.Y, tolerance, because);
        actual.Z.Should().BeApproximately(expected.Z, tolerance, because);
    }

    [Fact]
    public void UnderEvenLightTheDiffusePartSendsTheBaseColorTimesTheLight()
    {
        Double3 toView = Direction(Radians(10), Radians(30));

        Double3 sent = OverTheSphere(toLight => Reflected(BrownHair, toView, toLight).Diffuse);

        ShouldBeNear(
            actual: sent,
            expected: Brown,
            tolerance: 1e-5,
            because: "a white fiber in an even light shows that light, as a white Lambert surface does"
        );
    }

    [Fact]
    public void EachHighlightSendsAtMostItsWeightOfTheInterceptedLight()
    {
        // Each light puts its highlight's cone across the strand, θ_r = 2α − θ_i = 0, so a fiber's width cos θ_r loses
        // little of it.
        Double3 primaryLight = Direction(Radians(-10), 0);
        Double3 secondaryLight = Direction(Radians(15), 0);
        var material = new MaterialSample(BaseColor: Brown, Metallic: 0, Roughness: 0.2, Reflectance: 0.5);

        // Black hair has no secondary highlight, and the primary's weight is the Fresnel term at the half vector.
        Double3 primary = OverTheSphere(toView =>
            Reflected(BlackHair, toView, primaryLight).Specular
            / material.Fresnel(Double3.Dot(toView, (toView + primaryLight).Normalized()))
            * WidthAcross(toView)
        );
        Double3 secondary = OverTheSphere(toView =>
            (
                Reflected(BrownHair, toView, secondaryLight).Specular
                - Reflected(BlackHair, toView, secondaryLight).Specular
            )
            / (material.F0 * Brown)
            * WidthAcross(toView)
        );

        double primaryShare = primary.X / Math.Cos(Radians(-10));
        double secondaryShare = secondary.X / Math.Cos(Radians(15));
        primaryShare
            .Should()
            .BeInRange(0.95, 1, "the primary highlight sends its Fresnel share of the light it intercepts");
        secondaryShare.Should().BeInRange(0.95, 1, "the secondary highlight sends F0 · b of the light it intercepts");
    }

    [Fact]
    public void ThePrimaryHighlightPeaksFiveDegreesTowardTheRootOfTheMirrorAngle()
    {
        double peak = PeakHalfAngleDegrees(
            lightThetaDegrees: 20,
            highlight: (toView, toLight) => Reflected(BlackHair, toView, toLight).Specular.X
        );

        peak.Should().BeApproximately(-5, 0.25, "the cuticle's scales tilt the reflection toward the root");
    }

    [Fact]
    public void TheSecondaryHighlightPeaksTowardTheTipAndTakesTheBaseColor()
    {
        static Double3 secondary(Double3 toView, Double3 toLight) =>
            Reflected(BrownHair, toView, toLight).Specular - Reflected(BlackHair, toView, toLight).Specular;

        double peak = PeakHalfAngleDegrees(lightThetaDegrees: 20, highlight: (v, l) => secondary(v, l).X);

        peak.Should().BeApproximately(7.5, 0.25, "light that crossed the fiber leaves 7.5° toward the tip");
        Double3 atPeak = secondary(Direction(Radians(2 * peak - 20), 0), Direction(Radians(20), 0));
        (atPeak.Y / atPeak.X).Should().BeApproximately(Brown.Y / Brown.X, 1e-12, "the fiber tints what crossed it");
        (atPeak.Z / atPeak.X).Should().BeApproximately(Brown.Z / Brown.X, 1e-12, "the fiber tints what crossed it");
    }

    [Fact]
    public void TheSecondaryHighlightIsTwiceAsWideAsThePrimary()
    {
        Double3 toLight = Direction(Radians(20), 0);
        var material = new MaterialSample(BaseColor: Brown, Metallic: 0, Roughness: 0.2, Reflectance: 0.5);
        // Each highlight over its weight, at φ = 0 and over θ_r in steps of 0.01°.
        double[] thetas = [.. Enumerable.Range(-8900, 17801).Select(step => step / 100.0)];
        double[] primary =
        [
            .. thetas.Select(theta =>
            {
                Double3 toView = Direction(Radians(theta), 0);
                Double3 fresnel = material.Fresnel(Double3.Dot(toView, (toView + toLight).Normalized()));
                return Reflected(BlackHair, toView, toLight).Specular.X / fresnel.X;
            }),
        ];
        double[] secondary =
        [
            .. thetas.Select(theta =>
            {
                Double3 toView = Direction(Radians(theta), 0);
                return Reflected(BrownHair, toView, toLight).Specular.X
                    - Reflected(BlackHair, toView, toLight).Specular.X;
            }),
        ];
        double widthAtHalfTheMaximum(double[] values)
        {
            double half = values.Max() / 2;
            return thetas.Where((_, i) => values[i] >= half).Max() - thetas.Where((_, i) => values[i] >= half).Min();
        }

        (widthAtHalfTheMaximum(secondary) / widthAtHalfTheMaximum(primary))
            .Should()
            .BeApproximately(2, 0.02, "the secondary highlight is spread by its path through the fiber");
    }

    [Fact]
    public void AnEyeAlongTheStrandTakesTheAzimuthOfTheCardsNormal()
    {
        // The light at θ_i = −80° puts θ_h at 5°, between the two highlights' cones.
        static double highlightAt(double lightPhiDegrees) =>
            Reflected(BrownHair, Strand, Direction(Radians(-80), Radians(lightPhiDegrees))).Specular.X;

        highlightAt(0).Should().BeGreaterThan(0);
        highlightAt(180)
            .Should()
            .BeLessThan(1e-6 * highlightAt(0), "the light lies on the far side of the card's normal");
    }

    [Fact]
    public void ALightAlongTheStrandAddsNothing()
    {
        Double3 toView = Direction(Radians(10), Radians(30));

        Reflected(BrownHair, toView, Strand).Should().Be(default(DirectTerms), "a fiber has no width along itself");
        Reflected(BrownHair, toView, -Strand).Should().Be(default(DirectTerms), "a fiber has no width along itself");
    }

    [Fact]
    public void TheHighlightsPeakWhereTheEyeIsOnTheLightsSideOfTheFiber()
    {
        // The primary highlight of black hair over its Fresnel term, which grows as the eye turns to the far side, and the
        // secondary highlight, whose weight F0 · b is the same at every azimuth.
        Double3 toLight = Direction(Radians(20), 0);
        var material = new MaterialSample(BaseColor: Brown, Metallic: 0, Roughness: 0.2, Reflectance: 0.5);
        double primaryAt(double phiDegrees)
        {
            Double3 toView = Direction(Radians(-30), Radians(phiDegrees));
            Double3 fresnel = material.Fresnel(Double3.Dot(toView, (toView + toLight).Normalized()));
            return Reflected(BlackHair, toView, toLight).Specular.X / fresnel.X;
        }
        double secondaryAt(double phiDegrees)
        {
            Double3 toView = Direction(Radians(10), Radians(phiDegrees));
            return Reflected(BrownHair, toView, toLight).Specular.X - Reflected(BlackHair, toView, toLight).Specular.X;
        }

        foreach (Func<double, double> highlightAt in new[] { primaryAt, secondaryAt })
        {
            highlightAt(0).Should().BeGreaterThan(highlightAt(45));
            highlightAt(45).Should().BeGreaterThan(highlightAt(90));
            highlightAt(90).Should().BeGreaterThan(highlightAt(135));
            highlightAt(180)
                .Should()
                .BeLessThan(1e-6 * highlightAt(0), "no highlight leaves the far side of the fiber");
        }
    }

    [Theory]
    [MemberData(nameof(TextureAxes))]
    public void TheStrandFollowsTheTextureAxisThatTheMaterialNames(
        StrandDirection direction,
        double bitangentSign,
        Double3 expected
    )
    {
        Double3 strand = HairFiber.Strand(
            normal: Double3.UnitZ,
            tangent: Double3.UnitX,
            bitangentSign: bitangentSign,
            direction: direction
        );

        ShouldBeNear(
            actual: strand,
            expected: expected,
            tolerance: 1e-15,
            because: "+v is the bitangent n × t times its sign"
        );
    }

    [Fact]
    public void ANormalMapTiltsTheStrandWithItsFrame()
    {
        Double3 mappedNormal = new Double3(1, 0, 1).Normalized();

        Double3 strand = HairFiber.Strand(
            normal: mappedNormal,
            tangent: Double3.UnitX,
            bitangentSign: 1,
            direction: StrandDirection.PlusU
        );

        ShouldBeNear(
            actual: strand,
            expected: new Double3(1, 0, -1).Normalized(),
            tolerance: 1e-15,
            because: "the tangent is made perpendicular to the mapped normal"
        );
    }

    [Fact]
    public void AZeroTangentGivesAStrandAcrossTheNormal()
    {
        foreach (Double3 normal in new[] { Double3.UnitZ, Double3.UnitX, new Double3(0.6, 0.8, 0) })
        {
            Double3 strand = HairFiber.Strand(
                normal: normal,
                tangent: Double3.Zero,
                bitangentSign: 1,
                direction: StrandDirection.PlusU
            );

            strand.Length.Should().BeApproximately(1, 1e-15, $"the strand at the normal {normal} is a direction");
            Double3
                .Dot(strand, normal)
                .Should()
                .BeApproximately(0, 1e-15, $"the strand lies in the card at the normal {normal}");
        }
    }

    [Fact]
    public void TheFiberNormalFacesTheEyeAcrossTheStrand()
    {
        Double3 toView = new Double3(0.3, 0.6, 0.74).Normalized();

        Double3 fiberNormal = HairFiber.FiberNormal(strand: Strand, toView: toView, cardNormal: CardNormal);

        ShouldBeNear(
            actual: fiberNormal,
            expected: new Double3(0.3, 0, 0.74).Normalized(),
            tolerance: 1e-15,
            because: "the fiber normal is the eye's direction with its part along the strand removed"
        );
    }

    [Fact]
    public void AStrandAlongTheLineOfSightTakesTheCardsNormal() =>
        HairFiber.FiberNormal(strand: Strand, toView: Strand, cardNormal: CardNormal).Should().Be(CardNormal);
}
