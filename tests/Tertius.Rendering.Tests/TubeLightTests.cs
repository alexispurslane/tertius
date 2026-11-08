using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# tube light (ARCHITECTURE.md, "Tube lights"): its diffuse light and its solid angle against numerical integrals
/// over the capsule's surface, its limit at length 0, and its highlight in a mirror, which shows the capsule's face
/// radiance.
/// </summary>
public sealed class TubeLightTests
{
    /// <summary>The capsule of the demo's glowing bar: 2 m tall and 0.2 m square, of the same surface area.</summary>
    private const double BarRadius = 0.1337,
        BarHalfLength = 1 - BarRadius;

    /// <summary>
    /// A tube above or beside a floor point at the origin with the normal +Y, by name: its middle, from the middle to an end,
    /// and its radius, in m. Each stands at least five radii from the point.
    /// </summary>
    public static TheoryData<string> Tubes =>
        ["below-middle", "below-end", "beside", "far-beside", "slanted", "bar-near", "bar-far", "short", "past-end"];

    /// <summary>The capsule of <see cref="Tubes"/> by its name.</summary>
    private static (Double3 Center, Double3 HalfSegment, double Radius) TubeNamed(string name) =>
        name switch
        {
            "below-middle" => (new(0, 1, 0), new(1, 0, 0), 0.1),
            "below-end" => (new(1, 1, 0), new(1, 0, 0), 0.1),
            "beside" => (new(0, 1, 1.5), new(1, 0, 0), 0.1),
            "far-beside" => (new(0, 2, 6), new(1, 0, 0), 0.1),
            "slanted" => (new(0.5, 1.2, -0.3), new Double3(0.6, 0.5, 0.4), 0.08),
            "bar-near" => (new(0.7, 1, 0), new(0, BarHalfLength, 0), BarRadius),
            "bar-far" => (new(3, 1, 0), new(0, BarHalfLength, 0), BarRadius),
            "short" => (new(0, 1, 0), new(0.05, 0, 0), 0.1),
            "past-end" => (new(1.5, 1.2, 0), new(0, 0.5, 0), 0.1),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such tube."),
        };

    /// <summary>
    /// The integral over the capsule's surface seen from the origin, per unit face radiance, of
    /// cos θ_s / r² (its solid angle, in sr) and of max(n·s, 0) cos θ_s / r² (its irradiance on the normal
    /// <paramref name="normal"/>, in sr), with θ_s the angle at the surface. A convex body hides no part of its facing side.
    /// The side takes a grid of 4000 × 720 cells, each end a grid of 400 × 720.
    /// </summary>
    private static (double SolidAngle, double Irradiance) Integral(
        Double3 center,
        Double3 halfSegment,
        double radius,
        Double3 normal
    )
    {
        Double3 axis = halfSegment.NormalizedOr(Double3.UnitY);
        Double3 first = Double3.Cross(axis, Math.Abs(axis.X) < 0.9 ? Double3.UnitX : Double3.UnitY).Normalized();
        var second = Double3.Cross(axis, first);
        double halfLength = halfSegment.Length;
        double solidAngle = 0,
            irradiance = 0;
        void Add(Double3 place, Double3 outward, double area)
        {
            double distanceSquared = place.LengthSquared;
            Double3 toPlace = place / Math.Sqrt(distanceSquared);
            double facing = -Double3.Dot(outward, toPlace);
            if (facing <= 0)
                return;
            solidAngle += facing * area / distanceSquared;
            irradiance += Math.Max(Double3.Dot(normal, toPlace), 0) * facing * area / distanceSquared;
        }

        const int lengthSteps = 4000,
            turnSteps = 720,
            latitudeSteps = 400;
        for (int i = 0; i < lengthSteps; i++)
        {
            double along = -halfLength + (i + 0.5) / lengthSteps * 2 * halfLength;
            for (int j = 0; j < turnSteps; j++)
            {
                double turn = (j + 0.5) / turnSteps * 2 * Math.PI;
                Double3 outward = Math.Cos(turn) * first + Math.Sin(turn) * second;
                Add(
                    center + along * axis + radius * outward,
                    outward,
                    radius * (2 * Math.PI / turnSteps) * (2 * halfLength / lengthSteps)
                );
            }
        }

        foreach (double end in new[] { -1.0, 1.0 })
            for (int i = 0; i < latitudeSteps; i++)
            {
                double polar = (i + 0.5) / latitudeSteps * Math.PI / 2;
                for (int j = 0; j < turnSteps; j++)
                {
                    double turn = (j + 0.5) / turnSteps * 2 * Math.PI;
                    Double3 outward =
                        Math.Cos(polar) * end * axis
                        + Math.Sin(polar) * (Math.Cos(turn) * first + Math.Sin(turn) * second);
                    Add(
                        center + end * halfSegment + radius * outward,
                        outward,
                        radius * radius * Math.Sin(polar) * (Math.PI / 2 / latitudeSteps) * (2 * Math.PI / turnSteps)
                    );
                }
            }

        return (solidAngle, irradiance);
    }

    [Theory]
    [MemberData(nameof(Tubes))]
    public void ATubeLightLightsAFloorPointAsTheIntegralOverItsCapsuleDoesWithinTwoAndAHalfPercent(string name)
    {
        (Double3 center, Double3 halfSegment, double radius) = TubeNamed(name);
        var tube = new TubeView(toCenter: center, halfSegment: halfSegment, radius: radius);

        double irradiance = Double3.Dot(Double3.UnitY, tube.CosineVector);

        double reference = Integral(
            center: center,
            halfSegment: halfSegment,
            radius: radius,
            normal: Double3.UnitY
        ).Irradiance;
        (irradiance / reference)
            .Should()
            .BeApproximately(
                1,
                // The line model misses the cylinder's near-field shape, about (R / D)²: 2.0 % at five radii.
                0.025,
                "the side as a line of elements of width 2R sin ψ and the ends as half spheres give the capsule's "
                    + "irradiance {0} sr on the floor of the tube {1}",
                reference,
                name
            );
    }

    [Theory]
    [MemberData(nameof(Tubes))]
    public void ATubeLightsSolidAngleMatchesTheIntegralOverItsCapsuleWithinTwoPercent(string name)
    {
        (Double3 center, Double3 halfSegment, double radius) = TubeNamed(name);
        var tube = new TubeView(toCenter: center, halfSegment: halfSegment, radius: radius);

        double reference = Integral(
            center: center,
            halfSegment: halfSegment,
            radius: radius,
            normal: Double3.UnitY
        ).SolidAngle;

        (tube.SolidAngle / reference)
            .Should()
            .BeApproximately(
                1,
                0.02,
                "the specular part takes the light of the capsule's whole solid angle {0} sr",
                reference
            );
    }

    [Fact]
    public void OnItsAxisATubeSendsOnlyTheLightOfItsEnds()
    {
        // A tube 1 m long stands 1 m above the point, which looks up its axis and sees no part of the side.
        var tube = new TubeView(toCenter: new(0, 1.5, 0), halfSegment: new(0, 0.5, 0), radius: 0.1);

        double ends = Math.PI * 0.1 * 0.1 / 2 * (1 / 1.0 + 1 / 4.0);

        tube.SolidAngle.Should().BeApproximately(ends, 1e-12, "the side shows no width to a point on its axis");
    }

    [Fact]
    public void APointInsideATubesCapsuleBesideItsSegmentTakesTheLightOfItsSurface()
    {
        // A tube 1 m long and 0.1 m thick, seen from half its radius off its axis and from its surface, both beside its
        // middle. Only the two ends, each 0.5 m away along the axis, lie at a slightly different distance.
        var inside = new TubeView(toCenter: new(0.05, 0, 0), halfSegment: new(0, 0.5, 0), radius: 0.1);
        var surface = new TubeView(toCenter: new(0.1, 0, 0), halfSegment: new(0, 0.5, 0), radius: 0.1);

        inside
            .SolidAngle.Should()
            .BeApproximately(surface.SolidAngle, surface.SolidAngle * 0.01, "a point inside counts as on the surface");
    }

    [Fact]
    public void ATubeThinnerThanOneCentimeterCountsAsOneCentimeterThick()
    {
        var hairline = new TubeView(toCenter: new(0, 1, 0), halfSegment: new(0.5, 0, 0), radius: 0);
        var thin = new TubeView(
            toCenter: new(0, 1, 0),
            halfSegment: new(0.5, 0, 0),
            radius: LightUnits.NearestDistance
        );

        hairline.Radius.Should().Be(LightUnits.NearestDistance);
        hairline.SolidAngle.Should().Be(thin.SolidAngle, "the shader floors the radius at 1 cm");
    }

    [Fact]
    public void AMirrorRayAlmostAlongATubesSegmentTakesItsMiddleAsTheRepresentativePoint()
    {
        // The ray runs 10⁻⁴ rad off the segment's direction. Every point of the segment then lies about as near to it,
        // and the nearest point's formula divides by the square of that angle.
        var tube = new TubeView(toCenter: new(0.3, 1, 0), halfSegment: new(0.5, 0, 0), radius: 0.05);
        var mirror = new Double3(1, 1e-4, 0).Normalized();

        (Double3 direction, double crossSine, _) = tube.Representative(mirror);

        Double3 middle = GgxLobe.SphereLightDirection(tube.ToCenter.Normalized(), mirror, crossSine);
        (direction - middle).Length.Should().BeLessThan(1e-12, "the middle stands for every point of the segment");
        crossSine.Should().BeApproximately(0.05 / tube.ToCenter.Length, 1e-12, "the radius seen from the middle");
    }

    [Fact]
    public void ASurfaceThatFacesAwayFromATubeTakesNoneOfItsLight()
    {
        var matte = new MaterialSample(BaseColor: new(0.8, 0.8, 0.8), Metallic: 0, Roughness: 1, Reflectance: 0.5);
        var tube = new TubeView(toCenter: new(0, -1, 0), halfSegment: new(1, 0, 0), radius: 0.1);

        DirectTerms terms = matte.TubeTerms(
            normal: Double3.UnitY,
            toView: Double3.UnitY,
            tube: tube,
            tables: BrdfTables.Shared
        );

        terms.Should().Be(default(DirectTerms), "the whole tube lies below the surface");
    }

    [Fact]
    public void ACapsuleOfNoSizeHasThePeakIntensityOfAPointLightOfItsPower()
    {
        double intensity = LightUnits.TubeIntensity(power: 12, radius: 0, length: 0);

        intensity.Should().Be(LightUnits.PointIntensity(12), "a capsule of no size is a point");
    }

    [Fact]
    public void ATubeOfLengthZeroLightsAsASphereLightOfTheSamePowerDoes()
    {
        const double radius = 0.1,
            power = 50;
        var center = new Double3(0.3, 2, -0.4);
        var tube = new TubeView(toCenter: center, halfSegment: Double3.Zero, radius: radius);
        double faceRadiance = power / (Math.PI * 4 * Math.PI * radius * radius);

        double irradiance = faceRadiance * Double3.Dot(Double3.UnitY, tube.CosineVector);

        double sphere = LightUnits.PointIntensity(power) / center.LengthSquared * center.Normalized().Y;
        irradiance
            .Should()
            .BeApproximately(sphere, 1e-9 * sphere, "a capsule of length 0 is the sphere of the point light");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void AMirrorShowsTheInsideOfAThickTubesImageWithinThreePercentBelowTheTubesFaceRadiance(double viewTilt)
    {
        // A black glossy mirror of roughness 0 and F0 = 0.04 at the origin, normal +Z, seen from viewTilt rad off its
        // normal, and a tube 1 m above it across the plane of view, whose middle the mirror ray meets. The floored α adds
        // to the half-widths of the widened lobe, 0.1 across this tube and 0.36 along it, which takes 2.5 %.
        var mirror = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: 0, Reflectance: 0.5);
        var toView = new Double3(0, Math.Sin(viewTilt), Math.Cos(viewTilt));
        Double3 mirrorRay = Mirror(Double3.UnitZ, toView);
        var tube = new TubeView(toCenter: mirrorRay, halfSegment: new(0.5, 0, 0), radius: 0.2);

        DirectTerms terms = mirror.TubeTerms(
            normal: Double3.UnitZ,
            toView: toView,
            tube: tube,
            tables: BrdfTables.Shared
        );

        // Schlick's F at the mirror, where h = n, times the energy compensation, which the black glossy material's lobe
        // albedo near 1 leaves within a fraction of a percent.
        double fresnel = 0.04 + 0.96 * Math.Pow(1 - toView.Z, 5);
        terms
            .Specular.X.Should()
            .BeInRange(
                0.97 * fresnel,
                1.001 * fresnel,
                "a mirror shows the tube's own face radiance times F where the mirror ray meets the capsule, less the "
                    + "floored lobe's share"
            );
    }

    [Fact]
    public void AMirrorShowsATubeWiderAlongItsSegmentThanAcrossIt()
    {
        // The mirror of the test above, seen head-on, with the tube's segment along X: a ray 0.4 m off the middle along the
        // segment still meets the capsule, and one 0.1 m off across it, twice the radius, does not.
        var mirror = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: 0, Reflectance: 0.5);
        var tube = new TubeView(toCenter: new(0, 0, 2), halfSegment: new(0.5, 0, 0), radius: 0.05);
        Double3 ViewAt(double x, double y) => new Double3(-x, -y, 2).Normalized();
        double Shown(double x, double y) =>
            mirror
                .TubeTerms(normal: Double3.UnitZ, toView: ViewAt(x, y), tube: tube, tables: BrdfTables.Shared)
                .Specular.X;

        double along = Shown(0.4, 0);
        double across = Shown(0, 0.1);

        along.Should().BeGreaterThan(0.9 * Shown(0, 0), "the image of the capsule runs along its segment");
        across.Should().BeLessThan(0.01 * Shown(0, 0), "the image of the capsule is twice its radius wide");
    }
}
