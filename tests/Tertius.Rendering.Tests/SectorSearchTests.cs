using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The near steps of one slice, which block the sectors that their slabs cover and give them their light, and the
/// estimate of the sectors that they leave open (docs/plans/screen-space-illumination.md, "A near step").
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
        Near(sectors, thicknessShare, mapLight: null, steps);

    /// <summary>
    /// The near steps of a receiver whose geometric normal is its shading normal, under the map whose light along a
    /// view-space direction, read with a lobe of a width in rad, <paramref name="mapLight"/> gives.
    /// </summary>
    private static NearSlice Near(
        SliceSectors sectors,
        double thicknessShare,
        Func<Double3, double, Double3>? mapLight,
        params NearStep[] steps
    ) =>
        SectorSearch.Near(
            sectors: sectors,
            center: Center,
            geometricNormal: sectors.DirectionAt(sectors.Slice.NormalAngle),
            radius: Radius,
            thicknessShare: thicknessShare,
            steps: steps,
            mapLight: mapLight
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
                steps: [step],
                mapLight: null
            );

        Within(0.625)
            .Should()
            .Be(
                new NearSlice(Sectors: Facing, Blocked: SectorMask.None, Light: Double3.Zero, MapLight: Double3.Zero),
                "the edge at the radius is hard"
            );
        Within(0.5)
            .Should()
            .Be(new NearSlice(Sectors: Facing, Blocked: SectorMask.None, Light: Double3.Zero, MapLight: Double3.Zero));
        Within(0.626).Blocked.Count.Should().BePositive();
    }

    [Fact]
    public void AnOpenSectorBeyondTheRadiusTakesTheAmbientLight()
    {
        // A lit red wall that faces the center, 45° toward the plus side from the eye, 1.5 m to 2.5 m away: every step
        // lies inside the hemisphere and beyond R.
        NearStep[] wall =
        [
            .. new[] { 1.5, 2, 2.5 }.Select(d =>
                FacingStep(position: Center + new Double3(1, 0, -1) * (d / Math.Sqrt(2)), light: Red)
            ),
        ];
        var lobe = new GgxLobe(0.4);
        var source = new AmbientTerms(Diffuse: new(0.31, 0.57, 0.83), Specular: new(0.05, 0.11, 0.23));

        var estimate = AmbientEstimate.OfSlices([Near(Facing, ThicknessShare, wall)]);
        AmbientTerms occluded = source.Occluded(
            occlusion: estimate.Occlusion,
            bounce: estimate.Bounce,
            diffuseColor: new(0.7, 0.4, 0.2),
            nDotV: 0.7,
            lobe: lobe,
            environmentMapOn: false
        );

        estimate
            .Should()
            .Be(
                AmbientEstimate.Open,
                "only a step within the radius blocks, so the wall beyond it leaves every sector open"
            );
        occluded.Diffuse.Should().Be(source.Diffuse, "an open sector takes the ambient source's light, not the wall's");
    }

    [Fact]
    public void ATexelWhoseNearRangeHoldsNoStepTakesTheAmbientLight()
    {
        // The radius spans exactly the nearest step's 1.3 texels: no step of the red wall falls within it.
        List<NearStep> steps = SectorSearch.SliceSteps(
            texel: (160, 90),
            slice: 0,
            sliceAngle: 0,
            stepsPerSide: 3,
            stepNoise: 0,
            radiusInTexels: HorizonSearch.NearestStepTexels,
            halfSize: (Width: 320, Height: 180),
            rayThrough: WallRayThrough,
            texelAt: place => new StepTexel(
                Position: WallRayThrough(place) * 5,
                GeometricNormal: TowardTheEye,
                DirectLight: Red
            )
        );
        var center = new Double3(0, 0, 5);
        NearSlice near = SectorSearch.Near(
            sectors: Facing,
            center: center,
            geometricNormal: TowardTheEye,
            radius: 1,
            thicknessShare: ThicknessShare,
            steps: [.. steps],
            mapLight: null
        );

        steps.Should().BeEmpty("the near range holds no step (decision 21)");
        AmbientEstimate
            .OfSlices([near])
            .Should()
            .Be(AmbientEstimate.Open, "with no step, the whole hemisphere takes the ambient light");
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
            steps: [],
            mapLight: null
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
            steps: [ownPlane],
            mapLight: null
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
            texelNormal: Double3.UnitY,
            depthsAround: (Least: 3, Greatest: 4)
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
            texelNormal: Double3.UnitY,
            depthsAround: (Least: 1, Greatest: HorizonSearch.SkyDepth)
        );

        point.Should().BeNull("the step stands for no surface on its slice, as a step on sky");
    }

    [Theory]
    [InlineData(3.4, 4)]
    [InlineData(3, 3.3)]
    public void AStepWhosePlaneMeetsItsExactRayOutsideTheDepthsAroundItsExactPlaceStandsAtItsTexelsOwnPoint(
        double least,
        double greatest
    )
    {
        // The exact ray meets the texel's floor at a view depth of 1 / 0.3, in front of or beyond every texel around
        // its exact place, as beside a convex edge, where the texel's plane does not reach the place.
        Double3? point = SectorSearch.SurfacePoint(
            exactRay: new(0.05, -0.3, 1),
            texelPosition: new(0.1, -1, 3),
            texelNormal: Double3.UnitY,
            depthsAround: (Least: least, Greatest: greatest)
        );

        point.Should().Be(new Double3(0.1, -1, 3), "the step stands at the real point that it read, P(t)");
    }

    [Theory]
    [InlineData(1e-4)]
    [InlineData(-1e-4)]
    public void AStepOnAWallSquareToTheViewKeepsItsSurfacePointThroughTheRoundingOfItsNormal(double tilt)
    {
        // Every texel around the exact place reads the wall 5 m ahead. The texel's normal, off by a rounding's tilt,
        // puts the meeting point about 4 × 10⁻⁷ of the depth in front of the wall or beyond it.
        Double3 normal = new Double3(tilt, 0, -1).Normalized();

        Double3? point = SectorSearch.SurfacePoint(
            exactRay: new(0.0045, 0, 1),
            texelPosition: new(0.0025, 0, 5),
            texelNormal: normal,
            depthsAround: (Least: 5, Greatest: 5)
        );

        point.Should().NotBeNull();
        point!.Value.Z.Should().NotBe(5, "the test needs a point that the rounding moved off the wall");
        (point.Value - new Double3(0.0045, 0, 1) * point.Value.Z)
            .Length.Should()
            .BeLessThan(
                1e-12,
                "the depths' own precision widens the depths around the exact place, so s_f stays on the slice"
            );
    }

    [Fact]
    public void AStepAtTheImagesCornerReadsNoTexelOutsideTheImage()
    {
        // From the corner texel, the slices put exact places up to half a texel past the image's edges, where a
        // neighbor toward the place lies outside the image.
        var read = new List<(int X, int Y)>();
        StepTexel? WallAt((int X, int Y) texel)
        {
            read.Add(texel);
            return new(
                Position: WallRayThrough((texel.X, texel.Y)) * 5,
                GeometricNormal: TowardTheEye,
                DirectLight: Red
            );
        }

        for (int slice = 0; slice < 16; slice++)
            SectorSearch.SliceSteps(
                texel: (X: 0, Y: 0),
                slice: slice,
                sliceAngle: slice * Math.PI / 8 + 0.1,
                stepsPerSide: 3,
                stepNoise: 0.37f,
                radiusInTexels: 20,
                halfSize: (Width: 320, Height: 180),
                rayThrough: WallRayThrough,
                texelAt: WallAt
            );

        read.Should().NotBeEmpty();
        read.Should()
            .OnlyContain(
                texel => texel.X >= 0 && texel.Y >= 0 && texel.X < 320 && texel.Y < 180,
                "a texel outside the image reads the border texel, as the shader's viewDepthAt does"
            );
    }

    [Fact]
    public void AStepBetweenTheLastRowOfAFloorAndTheSkyKeepsItsSurfacePointBeyondThatRow()
    {
        // A floor 1 m below the eye fills the rows below row 90, whose ray runs level, and the sky the rest. Row 91,
        // the last floor row, lies 160 m ahead. Sky counts as beyond every depth around an exact place.
        static StepTexel? FloorAt((int X, int Y) texel)
        {
            Double3 ray = WallRayThrough((texel.X, texel.Y));
            return ray.Y < 0 ? new(Position: ray / -ray.Y, GeometricNormal: Double3.UnitY, DirectLight: Red) : null;
        }

        var steps = new List<NearStep>();
        for (int row = 95; row < 110; row++)
        for (int slice = 0; slice < 8; slice++)
            steps.AddRange(
                SectorSearch.SliceSteps(
                    texel: (X: 160, Y: row),
                    slice: slice,
                    sliceAngle: Math.PI / 4 + slice * Math.PI / 16,
                    stepsPerSide: 3,
                    stepNoise: 0.37f,
                    radiusInTexels: 20,
                    halfSize: (Width: 320, Height: 180),
                    rayThrough: WallRayThrough,
                    texelAt: FloorAt
                )
            );

        steps.Should().AllSatisfy(step => step.Position.Y.Should().BeApproximately(-1, 1e-9));
        steps
            .Should()
            .Contain(step => step.Position.Z > 160, "the floor's plane reaches past its last row toward the sky");
    }

    [Fact]
    public void OnATiltedPlaneEveryStepOfEverySliceKeepsItsSurfacePointOnThePlane()
    {
        // A plane through (0, 0, 5) that recedes to the right and up, so its depth changes along rows and columns.
        Double3 normal = new Double3(0.4, 0.3, -1).Normalized();
        double planeOffset = Double3.Dot(normal, new(0, 0, 5));
        StepTexel? PlaneAt((int X, int Y) texel)
        {
            Double3 ray = WallRayThrough((texel.X, texel.Y));
            return new StepTexel(
                Position: ray * (planeOffset / Double3.Dot(normal, ray)),
                GeometricNormal: normal,
                DirectLight: Red
            );
        }

        for (int slice = 0; slice < 16; slice++)
        {
            List<NearStep> steps = SectorSearch.SliceSteps(
                texel: (X: 160, Y: 90),
                slice: slice,
                sliceAngle: slice * Math.PI / 8 + 0.1,
                stepsPerSide: 3,
                stepNoise: 0.37f,
                radiusInTexels: 20,
                halfSize: (Width: 320, Height: 180),
                rayThrough: WallRayThrough,
                texelAt: PlaneAt
            );

            steps.Should().HaveCount(6, "the plane reaches every exact place of slice {0}", slice);
            double sliceAngle = slice * Math.PI / 8 + 0.1;
            steps
                .Select(step =>
                    // The screen place of the step's point, against the slice line through the center texel.
                    160 * step.Position.X / step.Position.Z * -Math.Sin(sliceAngle)
                    - (-160 * step.Position.Y / step.Position.Z) * Math.Cos(sliceAngle)
                )
                .Should()
                .AllSatisfy(
                    offLine => offLine.Should().BeApproximately(0, 1e-9),
                    "each step's point lies on the slice, not at P(t)"
                );
            steps
                .Select(step => Double3.Dot(normal, step.Position) - planeOffset)
                .Should()
                .AllSatisfy(offPlane => offPlane.Should().BeApproximately(0, 1e-12));
        }
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
    public void InAUniformMapASlicesMapLightIsItsBlockedCountTimesTheMapsLight()
    {
        // A normal map tilts the shading normal, so the receiver's own surface blocks sectors too.
        Double3 mapRadiance = new(0.4, 0.7, 1.3);

        NearSlice near = SectorSearch.Near(
            sectors: Tilted(0.5),
            center: Center,
            geometricNormal: TowardTheEye,
            radius: Radius,
            thicknessShare: ThicknessShare,
            steps: [FacingStep(position: new(-0.2, 0, 1.8), light: Red) with { Side = SliceSide.Minus }],
            mapLight: (_, _) => mapRadiance
        );

        near.Blocked.Count.Should().BeGreaterThan(near.Sectors.BelowPlane(TowardTheEye).Count, "the test needs a step");
        (near.MapLight - mapRadiance * near.Blocked.Count)
            .Length.Should()
            .BeLessThan(1e-12, "each blocked sector takes away the map's light along it once, the own surface's too");
    }

    [Fact]
    public void ASliceThatBlocksNothingReadsNoMapLight()
    {
        var reads = new List<Double3>();

        NearSlice near = Near(
            Facing,
            ThicknessShare,
            mapLight: (direction, _) =>
            {
                reads.Add(direction);
                return Red;
            }
        );

        reads.Should().BeEmpty("every sector is open, so the map's irradiance stands as it is");
        near.MapLight.Should().Be(Double3.Zero);
    }

    [Fact]
    public void AStepReadsTheMapAlongThePartOfItsRunThatNoNearerStepBlocked()
    {
        // The nearer step's slab covers sectors 24 to 31, the farther one's 22 to 25, so the farther one newly blocks
        // 22 and 23.
        NearStep nearer = FacingStep(position: new(0.2, 0, 1.8), light: Red);
        NearStep farther = FacingStep(position: new(0.3, 0, 1.6), light: Green);
        SectorMask fresh = Covered(Facing, farther, ThicknessShare).Except(Covered(Facing, nearer, ThicknessShare));
        var reads = new List<Double3>();

        Near(
            Facing,
            ThicknessShare,
            mapLight: (direction, _) =>
            {
                reads.Add(direction);
                return Red;
            },
            nearer,
            farther
        );

        reads.Should().HaveCount(2, "each step that blocks a sector reads the map once");
        double measure = Facing.MeasureFraction(AngleInSlice(Facing, reads[1]));
        int first = BitOperations.TrailingZeroCount(fresh.Bits);
        int end = SliceSectors.Count - BitOperations.LeadingZeroCount(fresh.Bits);
        measure
            .Should()
            .BeInRange(
                (double)first / SliceSectors.Count,
                (double)end / SliceSectors.Count,
                "the farther step stands for the sectors that it newly blocks, not for its whole run"
            );
    }

    [Fact]
    public void AStepWhoseNewlyBlockedSectorsLieOnBothSidesOfANearerPoleReadsTheMapOnceForEachUnbrokenPiece()
    {
        // The pole's slab covers the directions from about 52° to 62°, the wall's from 45° to the edge at 90°, so the
        // wall newly blocks two pieces, one on each side of the pole. Steps that stand on their slice's view rays never
        // come in this order, but a step that stands at the point that its texel read, off its ray, can
        // (docs/plans/screen-space-illumination.md, decision 42).
        NearStep pole = FacingStep(position: new(0.7, 0, 1.45), light: Red);
        NearStep wall = FacingStep(position: new(0.15, 0, 1.85), light: Green);
        SectorMask poleSectors = Covered(Facing, pole, ThicknessShare);
        SectorMask fresh = Covered(Facing, wall, ThicknessShare).Except(poleSectors);
        SectorMask below = fresh.Except(
            SectorMask.Run(start: BitOperations.TrailingZeroCount(poleSectors.Bits), end: 32)
        );
        SectorMask above = fresh.Except(below);
        below.Count.Should().BePositive("the test needs fresh sectors below the pole");
        above.Count.Should().BePositive("the test needs fresh sectors above the pole");
        var reads = new List<Double3>();

        Near(
            Facing,
            ThicknessShare,
            mapLight: (direction, _) =>
            {
                reads.Add(direction);
                return Red;
            },
            pole,
            wall
        );

        reads.Should().HaveCount(3, "the pole reads once, and the wall once for each piece that it newly blocks");
        foreach (
            (Double3 read, SectorMask piece) in (ReadOnlySpan<(Double3 Read, SectorMask Piece)>)
                [(reads[1], below), (reads[2], above)]
        )
            Facing
                .MeasureFraction(AngleInSlice(Facing, read))
                .Should()
                .BeInRange(
                    (double)BitOperations.TrailingZeroCount(piece.Bits) / SliceSectors.Count,
                    (double)(SliceSectors.Count - BitOperations.LeadingZeroCount(piece.Bits)) / SliceSectors.Count,
                    "a read stands for its own piece, not for the pole's directions between the pieces"
                );
    }

    [Fact]
    public void TheReceiversOwnSurfaceReadsTheMapAlongTheMiddleOfTheAnglesBelowItWithALobeAsWideAsThem()
    {
        // The shading normal leans 0.5 rad toward +x from the geometric normal, which faces the eye, so the directions
        // from π/2 to π/2 + 0.5 lie below the surface.
        SliceSectors sectors = Tilted(0.5);
        var reads = new List<(Double3 Direction, double Width)>();

        SectorSearch.Near(
            sectors: sectors,
            center: Center,
            geometricNormal: TowardTheEye,
            radius: Radius,
            thicknessShare: ThicknessShare,
            steps: [],
            mapLight: (direction, width) =>
            {
                reads.Add((direction, width));
                return Red;
            }
        );

        reads.Should().ContainSingle();
        AngleInSlice(sectors, reads[0].Direction).Should().BeApproximately(Math.PI / 2 + 0.25, 1e-12);
        reads[0].Width.Should().BeApproximately(0.5, 1e-12);
    }

    [Fact]
    public void APartOfARunStandsAtTheAnglesThatItsPlaceInTheRunTakesInProportion()
    {
        var run = new SectorRun(First: 10, End: 30, Low: 0.2, High: 1.2);

        (double middle, double width) = run.PartAngles(SectorMask.Run(start: 12, end: 16));

        middle
            .Should()
            .BeApproximately(0.4, 1e-15, "sectors 12 to 15 take the angles from 0.3 to 0.5 of the run's 0.2 to 1.2");
        width.Should().BeApproximately(0.2, 1e-15);
    }

    /// <summary>θ of a view-space direction in the slice: its angle from V̂, positive toward ô.</summary>
    private static double AngleInSlice(SliceSectors sectors, Double3 direction) =>
        Math.Atan2(Double3.Dot(direction, sectors.Slice.TowardPlusSide), Double3.Dot(direction, sectors.Slice.ToEye));
}
