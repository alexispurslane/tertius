namespace Tertius.Rendering.Tests;

/// <summary>
/// The 32 sectors of one slice: equal shares of its cosine-weighted arc, their center angles, the sectors that an
/// interval covers and the directions in the slice (docs/plans/screen-space-illumination.md, "The slice and its
/// sectors"). Integrals use an independent midpoint quadrature.
/// </summary>
public sealed class SliceSectorsTests
{
    // The view vector of a pixel at the center of the screen, from the surface toward the eye.
    private static readonly Double3 TowardTheEye = new(0, 0, -1);

    private static readonly double[] NormalAngles = [-1.5, -0.9, -0.3, 0, 0.4, 1.1, 1.5];

    /// <summary>
    /// The sectors of the slice at φ = 0 of a normal tilted from the eye toward +x by <paramref name="normalAngle"/>
    /// radians: in that slice, γ is the tilt.
    /// </summary>
    private static SliceSectors Tilted(double normalAngle) =>
        new(
            new HorizonSlice(
                toEye: TowardTheEye,
                normal: new(Math.Sin(normalAngle), 0, -Math.Cos(normalAngle)),
                angle: 0
            )
        );

    /// <summary>∫ from θ₁ to θ₂ of cos(θ − γ) · |sin θ| dθ, by the midpoint rule.</summary>
    private static double Measure(double from, double to, double normalAngle)
    {
        const int count = 200_000;
        double step = (to - from) / count;
        double integral = 0;
        for (int index = 0; index < count; index++)
        {
            double theta = from + (index + 0.5) * step;
            integral += Math.Cos(theta - normalAngle) * Math.Abs(Math.Sin(theta)) * step;
        }
        return integral;
    }

    /// <summary>The angle where the measure fraction is <paramref name="fraction"/>, by 60 halvings.</summary>
    private static double AngleAt(SliceSectors sectors, double fraction)
    {
        double low = sectors.Slice.NormalAngle - Math.PI / 2;
        double high = sectors.Slice.NormalAngle + Math.PI / 2;
        for (int i = 0; i < 60; i++)
        {
            double middle = (low + high) / 2;
            if (sectors.MeasureFraction(middle) < fraction)
                low = middle;
            else
                high = middle;
        }
        return (low + high) / 2;
    }

    [Fact]
    public void TheSlicesArcIsTheVisibilityOfAnOpenSlice()
    {
        foreach (double normalAngle in NormalAngles)
            Tilted(normalAngle)
                .Arc.Should()
                .BeApproximately(
                    Measure(from: normalAngle - Math.PI / 2, to: normalAngle + Math.PI / 2, normalAngle: normalAngle),
                    1e-9,
                    $"γ = {normalAngle}"
                );
    }

    [Fact]
    public void TheSliceWeightIsGtaosVisibilityOfTheOpenSlice()
    {
        // A normal with a part across the slice plane, so that |n_p| < 1.
        var slice = new HorizonSlice(toEye: TowardTheEye, normal: new Double3(0.5, 0.6, -0.4).Normalized(), angle: 0);

        new SliceSectors(slice)
            .Weight.Should()
            .BeApproximately(
                slice.Visibility(
                    horizonCosinePlus: slice.TangentHorizonCosine(+1),
                    horizonCosineMinus: slice.TangentHorizonCosine(-1)
                ),
                1e-12,
                "W = |n_p| T is GTAO's slice of an open surface"
            );
    }

    [Fact]
    public void EachSectorHoldsAThirtySecondOfTheSlicesArc()
    {
        foreach (double normalAngle in NormalAngles)
        {
            SliceSectors sectors = Tilted(normalAngle);
            for (int sector = 0; sector < SliceSectors.Count; sector += 5)
            {
                double from = AngleAt(sectors, (double)sector / SliceSectors.Count);
                double to = AngleAt(sectors, (sector + 1.0) / SliceSectors.Count);

                Measure(from: from, to: to, normalAngle: normalAngle)
                    .Should()
                    .BeApproximately(sectors.Arc / SliceSectors.Count, 1e-9, $"sector {sector}, γ = {normalAngle}");
            }
        }
    }

    [Fact]
    public void TheMeasureFractionRisesFromZeroAtOneEdgeOfTheHemisphereToOneAtTheOther()
    {
        foreach (double normalAngle in NormalAngles)
        {
            SliceSectors sectors = Tilted(normalAngle);
            double low = normalAngle - Math.PI / 2;
            double high = normalAngle + Math.PI / 2;

            sectors.MeasureFraction(low).Should().BeApproximately(0, 1e-15, $"γ = {normalAngle}");
            sectors.MeasureFraction(high).Should().BeApproximately(1, 1e-14, $"γ = {normalAngle}");
            double previous = 0;
            for (int i = 1; i <= 100; i++)
            {
                double fraction = sectors.MeasureFraction(low + i * Math.PI / 100);
                fraction.Should().BeGreaterThan(previous, $"step {i}, γ = {normalAngle}");
                previous = fraction;
            }
        }
    }

    [Fact]
    public void TheMeasureFractionIsClampedOutsideTheHemisphere()
    {
        foreach (double normalAngle in NormalAngles)
        {
            SliceSectors sectors = Tilted(normalAngle);

            sectors
                .MeasureFraction(normalAngle - Math.PI / 2 - 0.4)
                .Should()
                .Be(
                    sectors.MeasureFraction(normalAngle - Math.PI / 2),
                    "below the surface the integrand turns negative"
                );
            sectors
                .MeasureFraction(normalAngle + Math.PI / 2 + 0.4)
                .Should()
                .Be(sectors.MeasureFraction(normalAngle + Math.PI / 2));
        }
    }

    [Fact]
    public void AnIntervalBlocksTheSectorsWhoseCentersItCovers()
    {
        // The interval's ends lie at 3.4 and 7.6 sectors, so it covers the centers of sectors 3 to 7.
        SliceSectors sectors = Tilted(0.4);

        sectors
            .Covering(AngleAt(sectors, 3.4 / 32), AngleAt(sectors, 7.6 / 32))
            .Should()
            .Be(SectorMask.Run(start: 3, end: 8));
    }

    [Fact]
    public void AnIntervalCoversTheSameSectorsWhicheverEndComesFirst()
    {
        SliceSectors sectors = Tilted(-0.3);
        double a = AngleAt(sectors, 10.2 / 32);
        double b = AngleAt(sectors, 20.9 / 32);

        sectors.Covering(b, a).Should().Be(sectors.Covering(a, b)).And.Be(SectorMask.Run(start: 10, end: 21));
    }

    [Fact]
    public void ACenterOnTheBoundaryGoesToTheLaterSector()
    {
        // Facing the eye, m(θ) = ½ + (1 − cos 2θ) / 4 for θ ≥ 0, so cos 2θ = 0.9375 puts θ exactly on the center of
        // sector 16, at m = 16.5 / 32. The search walks the doubles around it until the code's m is that center bit for bit.
        SliceSectors sectors = Tilted(0);
        double below = Math.Acos(0.9375) / 2;
        double above = below;
        for (int i = 0; i < 1000 && sectors.MeasureFraction(below) * 32 != 16.5; i++)
        {
            below = Math.BitDecrement(below);
            above = Math.BitIncrement(above);
            if (sectors.MeasureFraction(above) * 32 == 16.5)
                below = above;
        }
        double center = below;
        (sectors.MeasureFraction(center) * 32).Should().Be(16.5, "the test needs an angle exactly on the center");

        sectors
            .Covering(0, center)
            .Should()
            .Be(SectorMask.Run(start: 16, end: 17), "floor(32 m + ½) takes the tie to the later boundary, 17");
        sectors.Covering(center, Math.PI / 2).Should().Be(SectorMask.Run(start: 17, end: 32));
    }

    [Fact]
    public void AnIntervalBeyondTheHemisphereBlocksNothingThere()
    {
        SliceSectors sectors = Tilted(0.4);
        double low = 0.4 - Math.PI / 2;
        double high = 0.4 + Math.PI / 2;

        sectors.Covering(high + 0.1, high + 0.5).Should().Be(SectorMask.None);
        sectors.Covering(low - 0.5, low - 0.1).Should().Be(SectorMask.None);
        sectors
            .Covering(AngleAt(sectors, 29.7 / 32), high + 0.5)
            .Should()
            .Be(SectorMask.Run(start: 30, end: 32), "the part beyond the edge adds no sector");
    }

    [Fact]
    public void TheSectorsBelowAGeometricPlaneAreThoseWhoseCentersLieBelowIt()
    {
        // Geometric normals tilted from the shading normal across the slice plane and out of it, as a normal map tilts.
        Double3[] tilts = [new(0.5, 0, 0), new(-0.7, 0, 0), new(0.4, 0.3, 0), new(-0.2, -0.6, 0.1), new(1.2, 0, 0.3)];
        int belowCount = 0;
        foreach (double normalAngle in NormalAngles)
        {
            SliceSectors sectors = Tilted(normalAngle);
            Double3 shading = sectors.DirectionAt(normalAngle);
            foreach (Double3 tilt in tilts)
            {
                Double3 geometric = (shading + tilt).Normalized();

                SectorMask below = sectors.BelowPlane(geometric);

                for (int sector = 0; sector < SliceSectors.Count; sector++)
                {
                    double cosine = Double3.Dot(
                        sectors.DirectionAt(AngleAt(sectors, (sector + 0.5) / SliceSectors.Count)),
                        geometric
                    );
                    if (Math.Abs(cosine) < 1e-3)
                        continue;
                    bool blocked = (below.Bits & (1u << sector)) != 0;
                    blocked.Should().Be(cosine < 0, $"sector {sector}, γ = {normalAngle}, tilt {tilt}");
                    belowCount += cosine < 0 ? 1 : 0;
                }
            }
        }
        belowCount.Should().BePositive("the test needs sectors below the plane");
    }

    [Fact]
    public void AGeometricPlaneAtRightAnglesToTheShadingNormalBlocksNoSector()
    {
        foreach (double normalAngle in NormalAngles)
        {
            SliceSectors sectors = Tilted(normalAngle);

            sectors
                .BelowPlane(sectors.DirectionAt(normalAngle))
                .Should()
                .Be(SectorMask.None, "the shading hemisphere is the geometric one, γ = {0}", normalAngle);
        }
    }

    [Fact]
    public void AGeometricPlaneThatHoldsTheSliceBlocksNoSector()
    {
        // The slice at φ = 0 lies in the plane y = 0. A normal along y with a part of 10⁻⁸ across the shading normal in
        // it: the plane holds the slice to within rounding, so no direction lies below it.
        SliceSectors sectors = Tilted(0.4);
        Double3 normal = (Double3.UnitY + sectors.DirectionAt(0.4 + Math.PI / 2) * 1e-8).Normalized();

        sectors.BelowPlane(normal).Should().Be(SectorMask.None);
    }

    [Fact]
    public void AFullRunBlocksAllThirtyTwoSectors()
    {
        foreach (double normalAngle in NormalAngles)
            Tilted(normalAngle)
                .Covering(normalAngle - Math.PI / 2, normalAngle + Math.PI / 2)
                .Bits.Should()
                .Be(uint.MaxValue, $"γ = {normalAngle}");
    }

    [Fact]
    public void TheDirectionAtAnAngleLiesInTheSlicePlaneAtThatAngleFromTheEye()
    {
        // An eye vector off the center of the screen, a slice at φ = 0.7 and a normal across its plane.
        Double3 toEye = new Double3(-0.3, 0.2, -1).Normalized();
        var slice = new HorizonSlice(toEye: toEye, normal: new Double3(0.2, -0.5, -0.8).Normalized(), angle: 0.7);
        var sectors = new SliceSectors(slice);
        Double3 planeNormal = Double3.Cross(slice.Direction, toEye).Normalized();

        foreach (double angle in new[] { -1.4, -0.5, 0.3, 1.2 })
        {
            Double3 direction = sectors.DirectionAt(angle);

            direction.Length.Should().BeApproximately(1, 1e-12);
            Double3
                .Dot(direction, planeNormal)
                .Should()
                .BeApproximately(0, 1e-12, "the direction lies in the slice plane");
            Math.Acos(Double3.Dot(direction, toEye)).Should().BeApproximately(Math.Abs(angle), 1e-12);
            Math.Sign(Double3.Dot(direction, slice.Direction))
                .Should()
                .Be(Math.Sign(angle), "a positive angle turns toward the slice's plus side on the screen");
        }
    }
}
