namespace Tertius.Rendering.Tests;

/// <summary>
/// The near steps of one slice, which block the sectors that their slabs cover and give them their light, and the pick
/// of the far ray's open sector (docs/plans/screen-space-illumination.md, "A near step" and "The far ray of a slice").
/// The slice lies in the plane y = 0, around a center 2 m ahead at the middle of the screen, so the plus side is +x.
/// </summary>
public sealed class SectorSearchTests
{
    private const double Radius = 1;
    private const double ThicknessShare = 0.1;

    // A share of the view depth so large that every slab reaches the edge of the hemisphere: GTAO's model.
    private const double InfiniteThickness = 1e6;

    private static readonly Double3 Center = new(0, 0, 2);
    private static readonly Double3 TowardTheEye = new(0, 0, -1);
    private static readonly Double3 Red = new(3, 0, 0);
    private static readonly Double3 Green = new(0, 2, 0);

    /// <summary>The sectors of the slice at φ = 0 around a normal tilted by <paramref name="normalAngle"/> toward +x.</summary>
    private static SliceSectors Tilted(double normalAngle) =>
        new(
            new HorizonSlice(
                toEye: TowardTheEye,
                normal: new(Math.Sin(normalAngle), 0, -Math.Cos(normalAngle)),
                angle: 0
            )
        );

    /// <summary>The slice of a surface that faces the eye.</summary>
    private static SliceSectors Facing => Tilted(0);

    /// <summary>A step on the plus side at <paramref name="position"/>, of a wall whose normal faces the center.</summary>
    private static NearStep FacingStep(Double3 position, Double3 light) =>
        new(
            Position: position,
            GeometricNormal: (Center - position).Normalized(),
            DirectLight: light,
            Side: SliceSide.Plus
        );

    /// <summary>The near steps of a receiver whose geometric normal is its shading normal, which lies in the slice plane.</summary>
    private static NearSlice Near(SliceSectors sectors, double thicknessShare, params NearStep[] steps) =>
        SectorSearch.Near(
            sectors: sectors,
            center: Center,
            geometricNormal: sectors.DirectionAt(sectors.Slice.NormalAngle),
            radius: Radius,
            thicknessShare: thicknessShare,
            steps: steps
        );

    /// <summary>The sectors that the slab of <paramref name="step"/> covers alone.</summary>
    private static SectorMask Covered(SliceSectors sectors, NearStep step, double thicknessShare)
    {
        (double front, double back) = SectorSearch.StepAngles(
            center: Center,
            toEye: TowardTheEye,
            position: step.Position,
            side: step.Side,
            thicknessShare: thicknessShare
        );
        return sectors.Covering(front, back);
    }

    /// <summary>The signed angle θ of a view-space direction in the slice, from V̂ toward ô.</summary>
    private static double AngleOf(SliceSectors sectors, Double3 direction) =>
        Math.Sign(Double3.Dot(direction, sectors.Slice.TowardPlusSide))
        * Math.Acos(Math.Clamp(Double3.Dot(direction, sectors.Slice.ToEye), -1, 1));

    /// <summary>
    /// The near steps of the slice at φ = 0, which runs along a row, from <paramref name="texel"/> of a 320 × 180 half
    /// image: three per side over 20 texels, on a red wall 5 m ahead that reaches past the image.
    /// </summary>
    private static List<NearStep> StepsOnAWall((int X, int Y) texel) =>
        SectorSearch.SliceSteps(
            texel: texel,
            slice: 0,
            sliceAngle: 0,
            stepsPerSide: 3,
            stepNoise: 0,
            radiusInTexels: 20,
            halfSize: (Width: 320, Height: 180),
            rayThrough: WallRayThrough,
            texelAt: place => new StepTexel(
                Position: WallRayThrough(place) * 5,
                GeometricNormal: TowardTheEye,
                DirectLight: Red
            )
        );

    /// <summary>A view ray, whose z is 1, through a place in the half image of <see cref="StepsOnAWall"/>, 90° wide.</summary>
    private static Double3 WallRayThrough((double X, double Y) place) =>
        new((place.X - 160) / 160, (90 - place.Y) / 160, 1);

    [Fact]
    public void AStepGivesItsLightOnlyToTheSectorsThatNoNearerStepBlocked()
    {
        // The nearer step's slab covers sectors 24 to 31, the farther one's 22 to 25.
        NearStep nearer = FacingStep(position: new(0.2, 0, 1.8), light: Red);
        NearStep farther = FacingStep(position: new(0.3, 0, 1.6), light: Green);
        SectorMask nearerCovers = Covered(Facing, nearer, ThicknessShare);
        SectorMask fartherCovers = Covered(Facing, farther, ThicknessShare);
        SectorMask fresh = fartherCovers.Except(nearerCovers);
        fresh.Count.Should().BeInRange(1, fartherCovers.Count - 1, "the test needs slabs that overlap in part");

        NearSlice near = Near(Facing, ThicknessShare, nearer, farther);

        near.Blocked.Should().Be(nearerCovers.Union(fartherCovers));
        near.Light.Should().Be(Red * nearerCovers.Count + Green * fresh.Count);
    }

    [Theory]
    [InlineData(-0.6)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void AtInfiniteThicknessTheOpenSectorsAreTheSectorsBetweenTheHorizons(double normalAngle)
    {
        SliceSectors sectors = Tilted(normalAngle);
        HorizonSlice slice = sectors.Slice;
        var random = new Random(7);
        for (int trial = 0; trial < 50; trial++)
        {
            // Three steps on each side, in the slice plane within the radius, and GTAO's horizons over them.
            var steps = new List<NearStep>();
            double plusCosine = slice.TangentHorizonCosine(+1);
            double minusCosine = slice.TangentHorizonCosine(-1);
            foreach (SliceSide side in new[] { SliceSide.Plus, SliceSide.Minus })
                for (int i = 0; i < 3; i++)
                {
                    double angle = 0.1 + 1.9 * random.NextDouble();
                    double distance = 0.1 + 0.8 * random.NextDouble();
                    Double3 position =
                        Center
                        + (TowardTheEye * Math.Cos(angle) + slice.TowardPlusSide * ((int)side * Math.Sin(angle)))
                            * distance;
                    // As κ grows, the back of a slab tends to the step's own view ray, at θ = π − α from V̂. It reaches
                    // the hemisphere's edge only when α ≤ π/2 − |γ|, which a center 2 m from the eye keeps.
                    double viewRaysAngle = Math.Acos(Double3.Dot(position.Normalized(), Center.Normalized()));
                    viewRaysAngle
                        .Should()
                        .BeLessThan(Math.PI / 2 - Math.Abs(normalAngle), "the test needs every slab to reach the edge");
                    steps.Add(new(Position: position, GeometricNormal: -TowardTheEye, DirectLight: Red, Side: side));
                    double cosine = Double3.Dot((position - Center).Normalized(), TowardTheEye);
                    if (side == SliceSide.Plus)
                        plusCosine = Math.Max(plusCosine, cosine);
                    else
                        minusCosine = Math.Max(minusCosine, cosine);
                }

            NearSlice near = Near(sectors, InfiniteThickness, [.. steps]);

            (sectors.Weight * near.Blocked.Open.Count / SliceSectors.Count)
                .Should()
                .BeApproximately(
                    slice.Visibility(horizonCosinePlus: plusCosine, horizonCosineMinus: minusCosine),
                    sectors.Weight / SliceSectors.Count + 1e-12,
                    $"trial {trial}: the center rule rounds each horizon to within half a sector"
                );
        }
    }

    [Fact]
    public void AThinSlabLetsTheSkyThroughBehindIt()
    {
        // A pole 0.5 m in front of the center on the plus side: its slab of 0.1 z ends long before the hemisphere's edge.
        NearStep pole = FacingStep(position: new(0.3, 0, 1.5), light: Red);

        NearSlice thin = Near(Facing, ThicknessShare, pole);
        NearSlice infinite = Near(Facing, InfiniteThickness, pole);

        thin.Blocked.Count.Should().BePositive();
        (thin.Blocked.Bits >> 28).Should().Be(0u, "the sectors toward the edge, behind the slab, stay open");
        (infinite.Blocked.Bits >> 28).Should().Be(0b1111u, "GTAO's horizon would hide them");
    }

    [Fact]
    public void AStepAtTheRadiusOrBeyondItBlocksNothing()
    {
        // The step lies exactly 0.625 m from the center: (0.375, 0, −0.5) has an exact length in doubles.
        NearStep step = FacingStep(position: new(0.375, 0, 1.5), light: Red);
        NearSlice Within(double radius) =>
            SectorSearch.Near(
                sectors: Facing,
                center: Center,
                geometricNormal: TowardTheEye,
                radius: radius,
                thicknessShare: ThicknessShare,
                steps: [step]
            );

        Within(0.625)
            .Should()
            .Be(
                new NearSlice(Sectors: Facing, Blocked: SectorMask.None, Light: Double3.Zero),
                "the edge at the radius is hard"
            );
        Within(0.5).Should().Be(new NearSlice(Sectors: Facing, Blocked: SectorMask.None, Light: Double3.Zero));
        Within(0.626).Blocked.Count.Should().BePositive();
    }

    [Fact]
    public void ABlockerThatFacesAwayBlocksWithoutLight()
    {
        // A wall on the plus side whose normal points away from the center: the center sees its back.
        var back = new NearStep(
            Position: new(0.3, 0, 1.6),
            GeometricNormal: Double3.UnitX,
            DirectLight: Red,
            Side: SliceSide.Plus
        );

        NearSlice near = Near(Facing, ThicknessShare, back);

        near.Blocked.Count.Should().BePositive();
        near.Light.Should().Be(Double3.Zero, "no pixel holds the light of a surface's back");
    }

    [Fact]
    public void TheReceiversOwnSurfaceBlocksTheSectorsBelowItBeforeAnyStep()
    {
        // A normal map tilts the shading normal 0.5 rad toward +x from the geometric normal, which faces the eye.
        SliceSectors sectors = Tilted(0.5);

        NearSlice near = SectorSearch.Near(
            sectors: sectors,
            center: Center,
            geometricNormal: TowardTheEye,
            radius: Radius,
            thicknessShare: ThicknessShare,
            steps: []
        );

        near.Blocked.Should().Be(sectors.BelowPlane(TowardTheEye));
        near.Blocked.Count.Should()
            .BePositive("the shading hemisphere reaches 0.5 rad below the surface on the plus side");
        near.Light.Should().Be(Double3.Zero, "a surface's own back holds no light that a pixel shows");
    }

    [Fact]
    public void AStepOnTheReceiversOwnPlaneGivesNoLightToTheSectorsBelowIt()
    {
        // The step lies in the receiver's geometric plane, z = 2, so its slab covers only directions below that plane.
        // Rounding tilts its normal a little toward the center, so it faces the center by a cosine of about 0.
        SliceSectors sectors = Tilted(0.5);
        var ownPlane = new NearStep(
            Position: new(0.3, 0, 2),
            GeometricNormal: new Double3(-1e-5, 0, -1).Normalized(),
            DirectLight: Red,
            Side: SliceSide.Plus
        );
        Covered(sectors, ownPlane, ThicknessShare).Count.Should().BePositive("the test needs a step that blocks");

        NearSlice near = SectorSearch.Near(
            sectors: sectors,
            center: Center,
            geometricNormal: TowardTheEye,
            radius: Radius,
            thicknessShare: ThicknessShare,
            steps: [ownPlane]
        );

        near.Blocked.Should().Be(sectors.BelowPlane(TowardTheEye), "the slab covers only what the surface blocks");
        near.Light.Should().Be(Double3.Zero, "the surface blocks those sectors first, with no light");
    }

    [Fact]
    public void AStepsSurfacePointIsWhereItsExactRayMeetsItsTexelsPlane()
    {
        // The texel's point lies on a floor 1 m below the eye. The exact ray, off the texel's own ray, meets the floor
        // at a view depth of 1 / 0.3.
        Double3 exactRay = new(0.05, -0.3, 1);

        Double3? point = SectorSearch.SurfacePoint(
            exactRay: exactRay,
            texelPosition: new(0.1, -1, 3),
            texelNormal: Double3.UnitY
        );

        point.Should().NotBeNull();
        point!.Value.Y.Should().BeApproximately(-1, 1e-12, "the point lies on the texel's plane");
        (point.Value - exactRay * point.Value.Z)
            .Length.Should()
            .BeLessThan(1e-12, "the point lies on the exact ray, so on the slice");
        point.Value.Z.Should().BeApproximately(1 / 0.3, 1e-12);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0)]
    public void AStepWhosePlaneItsExactRayMeetsOnlyBehindTheEyeOrNeverHasNoSurfacePoint(double rise)
    {
        // The exact ray rises above the floor, or runs along it: the floor does not reach the slice there.
        Double3? point = SectorSearch.SurfacePoint(
            exactRay: new(0, rise, 1),
            texelPosition: new(0, -1, 3),
            texelNormal: Double3.UnitY
        );

        point.Should().BeNull("the step stands for no surface on its slice, as a step on sky");
    }

    [Fact]
    public void TheStepsOfASliceComeNearestFirstAndPlusSideFirst()
    {
        List<NearStep> steps = StepsOnAWall(texel: (X: 160, Y: 90));

        steps
            .Select(step => step.Side)
            .Should()
            .Equal(SliceSide.Plus, SliceSide.Minus, SliceSide.Plus, SliceSide.Minus, SliceSide.Plus, SliceSide.Minus);
        steps
            .Where(step => step.Side == SliceSide.Plus)
            .Select(step => step.Position.X)
            .Should()
            .BeInAscendingOrder("the plus side runs right along the row, and the nearest step comes first");
    }

    [Fact]
    public void AStepWhoseTexelLiesOutsideTheImageIsLeftOut()
    {
        List<NearStep> steps = StepsOnAWall(texel: (X: 0, Y: 90));

        steps
            .Select(step => step.Side)
            .Should()
            .Equal(
                [SliceSide.Plus, SliceSide.Plus, SliceSide.Plus],
                "at the image's left edge every minus step's texel lies left of column 0, though the wall goes on there"
            );
    }

    [Fact]
    public void ABlockersLightDoesNotDependOnTheAngleAtWhichThePointSeesIt()
    {
        // The direction from the step back to the center is (−0.6, 0, 0.8). One wall faces it head on, the other at a
        // grazing angle, with a cosine of about 0.016.
        Double3 position = new(0.375, 0, 1.5);
        var headOn = new NearStep(
            Position: position,
            GeometricNormal: new(-0.6, 0, 0.8),
            DirectLight: Red,
            Side: SliceSide.Plus
        );
        NearStep grazing = headOn with { GeometricNormal = new Double3(0.79, 0, 0.613).Normalized() };

        NearSlice seenHeadOn = Near(Facing, ThicknessShare, headOn);
        NearSlice seenGrazing = Near(Facing, ThicknessShare, grazing);

        seenHeadOn.Light.Should().Be(Red * seenHeadOn.Blocked.Count);
        seenGrazing.Should().Be(seenHeadOn, "a Lambert blocker sends the same radiance in every direction");
    }

    [Fact]
    public void AStepOnTheMinusSideBlocksSectorsOnItsSideOfTheEye()
    {
        var step = new NearStep(
            Position: new(-0.3, 0, 1.6),
            GeometricNormal: Double3.UnitX,
            DirectLight: Red,
            Side: SliceSide.Minus
        );

        NearSlice near = Near(Facing, ThicknessShare, step);

        near.Blocked.Count.Should().BePositive();
        (near.Blocked.Bits >> 16).Should().Be(0u, "facing the eye, the minus side holds sectors 0 to 15");
        near.Light.Should().Be(Red * near.Blocked.Count);
    }

    [Fact]
    public void TheFarRayGoesThroughTheCenterOfAnOpenSector()
    {
        SliceSectors sectors = Tilted(0.5);
        var blocked = new SectorMask(0b1111_0000_1111_0000_1010_1010_0101_0101u);

        foreach (float noise in new[] { 0f, 0.13f, 0.5f, 0.77f, 0.999f })
        {
            int sector = SectorSearch.FarRaySector(blocked, noise)!.Value;
            Double3 direction = sectors.DirectionAt(sectors.CenterAngle(sector));

            ((blocked.Bits >> sector) & 1).Should().Be(0u, $"noise {noise}: the far ray runs only in an open sector");
            (sectors.MeasureFraction(AngleOf(sectors, direction)) * SliceSectors.Count - sector)
                .Should()
                .BeApproximately(0.5, 0.01, $"noise {noise}: the ray goes through the center of sector {sector}");
        }
    }

    [Fact]
    public void TheNoisePicksTheOpenSectorOfItsShareOfTheOpenCount()
    {
        // Sectors 0 to 15 are blocked, so the open sectors 16 to 31 have the ranks 0 to 15.
        var blocked = SectorMask.Run(start: 0, end: 16);

        SectorSearch.FarRaySector(blocked, 0).Should().Be(16);
        SectorSearch.FarRaySector(blocked, 0.5f).Should().Be(24);
        SectorSearch.FarRaySector(blocked, 0.99f).Should().Be(31);
    }

    [Fact]
    public void ANoiseThatRoundsUpToOnePicksTheLastOpenSector()
    {
        // Correctly rounded single floats keep fract and its product with the count below the count, but GLSL lets a
        // driver's fract and product err by an ulp. With 17 open sectors, a noise of 1 is one rank past the last.
        var blocked = SectorMask.Run(start: 0, end: 15);

        SectorSearch.FarRaySector(blocked, 1).Should().Be(31);
    }

    [Fact]
    public void TheFarRayOfASliceWithNoOpenSectorIsNotTraced()
    {
        var blocked = new SectorMask(uint.MaxValue);

        foreach (float noise in new[] { 0f, 0.5f, 1f })
            SectorSearch
                .FarRaySector(blocked, noise)
                .Should()
                .BeNull($"noise {noise}: a slice whose every sector is blocked nearby has no open sector to trace");
    }

    [Theory]
    [InlineData(0f, 0, 3)]
    [InlineData(0.4f, 1, 3)]
    [InlineData(0.9f, 2, 3)]
    [InlineData(0.25f, 5, 6)]
    public void TheFarRayTakesTheNextValueOfItsSlicesStepSequence(float stepNoise, int slice, int stepsPerSide)
    {
        // Step j of slice k takes index k + j n of the R1 sequence, so step n would take the next value past the steps.
        const double radiusInTexels = 1e6;
        double nextStep = HorizonSearch.StepFraction(
            step: stepsPerSide,
            slice: slice,
            stepsPerSide: stepsPerSide,
            stepNoise: stepNoise,
            radiusInTexels: radiusInTexels
        );
        double nextValue = (nextStep - HorizonSearch.NearestStepTexels / radiusInTexels) * stepsPerSide - stepsPerSide;

        SectorSearch
            .FarRayNoise(stepNoise: stepNoise, slice: slice, stepsPerSide: stepsPerSide)
            .Should()
            .BeApproximately((float)nextValue, 1e-5f);
    }
}
