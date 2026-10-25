namespace Tertius.Rendering.Tests;

/// <summary>
/// The estimate of a texel, the occlusion V and the bounce G, from its slices' near sectors and their far rays
/// (docs/plans/screen-space-illumination.md, "The estimate of a texel"): its shares sum to one, and a furnace gives
/// every direction the same light.
/// </summary>
public sealed class AmbientEstimateTests
{
    private static readonly Double3 TowardTheEye = new(0, 0, -1);
    private static readonly Double3 Red = new(3, 0, 0);
    private static readonly Double3 Sky = new(0.4, 0.7, 1.3);

    // A normal tilted from the eye by 0.5 rad, across every slice's plane.
    private static readonly Double3 TiltedNormal = new(Math.Sin(0.5) * 0.6, Math.Sin(0.5) * 0.8, -Math.Cos(0.5));

    /// <summary>The sectors of three slices around <paramref name="normal"/>, at the screen angles 0.2, 1.2 and 2.2.</summary>
    private static SliceSectors[] ThreeSlices(Double3 normal) =>
        [
            new(new HorizonSlice(toEye: TowardTheEye, normal: normal, angle: 0.2)),
            new(new HorizonSlice(toEye: TowardTheEye, normal: normal, angle: 1.2)),
            new(new HorizonSlice(toEye: TowardTheEye, normal: normal, angle: 2.2)),
        ];

    private static double TotalWeight(SliceSectors[] sectors) => sectors.Sum(slice => slice.Weight);

    /// <summary>U: the share of the texel's estimate that slice <paramref name="index"/>'s far ray decides.</summary>
    private static double OpenShare(SliceSectors[] sectors, NearSlice[] near, int index) =>
        sectors[index].Weight * near[index].Blocked.Open.Count / (SliceSectors.Count * TotalWeight(sectors));

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance, string because)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, because);
        actual.Y.Should().BeApproximately(expected.Y, tolerance, because);
        actual.Z.Should().BeApproximately(expected.Z, tolerance, because);
    }

    [Fact]
    public void TheNearEstimateIsEachSlicesOpenShareAndLightByItsWeightOverTheTotalWeight()
    {
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        NearSlice[] near =
        [
            new(SectorMask.Run(start: 20, end: 32), Red * 12),
            new(SectorMask.None, Double3.Zero),
            new(SectorMask.Run(start: 0, end: 8), Sky * 3),
        ];
        double total = TotalWeight(sectors);

        var estimate = AmbientEstimate.OfSlices(sectors, near);

        estimate
            .Occlusion.Should()
            .BeApproximately(
                (sectors[0].Weight * 20 + sectors[1].Weight * 32 + sectors[2].Weight * 24) / (32 * total),
                1e-15
            );
        ShouldBeNear(
            actual: estimate.Bounce,
            expected: (Red * (12 * sectors[0].Weight) + Sky * (3 * sectors[2].Weight)) / (32 * total),
            tolerance: 1e-15,
            because: "each sector carries W / 32 of its slice, over the total weight"
        );
    }

    [Fact]
    public void TheOpenTheNearAndTheFarSharesOfATexelSumToOne()
    {
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        var random = new Random(11);
        for (int trial = 0; trial < 100; trial++)
        {
            NearSlice[] near =
            [
                .. sectors.Select(_ => new NearSlice(new SectorMask((uint)random.NextInt64(1L << 32)), Double3.Zero)),
            ];
            double nearShare = Enumerable
                .Range(0, 3)
                .Sum(k => sectors[k].Weight * near[k].Blocked.Count / (SliceSectors.Count * TotalWeight(sectors)));
            var estimate = AmbientEstimate.OfSlices(sectors, near);
            double farShare = 0;
            for (int k = 0; k < 3; k++)
            {
                double blocked = random.NextDouble();
                double openShare = OpenShare(sectors, near, k);
                estimate = estimate.WithFarRay(
                    openShare: openShare,
                    outcome: new(Blocked: blocked, Light: Double3.Zero)
                );
                farShare += openShare * blocked;
            }

            (estimate.Occlusion + nearShare + farShare).Should().BeApproximately(1, 1e-14, $"trial {trial}");
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.4)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void AnOpenSurfaceHasFullVisibilityAndNoBounceAtAnyTilt(double tilt)
    {
        SliceSectors[] sectors = ThreeSlices(new Double3(Math.Sin(tilt) * 0.6, Math.Sin(tilt) * 0.8, -Math.Cos(tilt)));
        NearSlice[] near = [.. sectors.Select(_ => new NearSlice(SectorMask.None, Double3.Zero))];

        AmbientEstimate
            .OfSlices(sectors, near)
            .Should()
            .Be(AmbientEstimate.Open, "the shares sum to 1 in every texel, not only in the mean over slices");
    }

    [Fact]
    public void ATexelWhoseSlicesHaveNoWeightIsOpen()
    {
        // The normal lies at right angles to the only slice's plane, so the slice has no weight.
        SliceSectors[] sectors = [new(new HorizonSlice(toEye: TowardTheEye, normal: Double3.UnitY, angle: 0))];
        NearSlice[] near = [new(SectorMask.Run(start: 4, end: 20), Red * 16)];
        sectors[0].Weight.Should().Be(0, "the test needs a slice with no weight");

        AmbientEstimate
            .OfSlices(sectors, near)
            .Should()
            .Be(AmbientEstimate.Open, "a texel with no information is open");
    }

    [Fact]
    public void InAFurnaceTheSkyLightAndTheBounceSumToTheSkyLight()
    {
        // Every blocker, near or far, emits the sky's radiance and faces the center.
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        Double3 center = new(0, 0, 2);
        var random = new Random(5);
        for (int trial = 0; trial < 50; trial++)
        {
            var near = new NearSlice[3];
            for (int k = 0; k < 3; k++)
            {
                HorizonSlice slice = sectors[k].Slice;
                var steps = new NearStep[4];
                for (int i = 0; i < steps.Length; i++)
                {
                    int side = i % 2 == 0 ? +1 : -1;
                    double angle = 0.2 + 1.6 * random.NextDouble();
                    Double3 position =
                        center
                        + (slice.ToEye * Math.Cos(angle) + slice.TowardPlusSide * (side * Math.Sin(angle)))
                            * (0.1 + 0.8 * random.NextDouble());
                    steps[i] = new(
                        Position: position,
                        GeometricNormal: (center - position).Normalized(),
                        DirectLight: Sky,
                        Side: side
                    );
                }
                near[k] = SectorSearch.Near(
                    sectors: sectors[k],
                    center: center,
                    radius: 1,
                    thicknessShare: 0.1,
                    steps: steps
                );
            }
            var estimate = AmbientEstimate.OfSlices(sectors, near);
            for (int k = 0; k < 3; k++)
            {
                double blocked = random.NextDouble();
                estimate = estimate.WithFarRay(
                    openShare: OpenShare(sectors, near, k),
                    outcome: new(Blocked: blocked, Light: Sky * blocked)
                );
            }

            ShouldBeNear(
                actual: Sky * estimate.Occlusion + estimate.Bounce,
                expected: Sky,
                tolerance: 1e-14,
                because: $"trial {trial}: no direction counts twice and none is lost"
            );
        }
    }

    [Fact]
    public void AFarRayThatHitsMovesItsSlicesOpenShareToTheHitsLightByTheEdgeFade()
    {
        var estimate = new AmbientEstimate(Bounce: new(0.5, 0.25, 0.125), Occlusion: 0.75);
        const double edgeFade = 0.375;

        AmbientEstimate after = estimate.WithFarRay(
            openShare: 0.5,
            outcome: new(Blocked: edgeFade, Light: Red * edgeFade)
        );

        after
            .Should()
            .Be(new AmbientEstimate(Bounce: new(0.5 + 0.5 * 0.375 * 3, 0.25, 0.125), Occlusion: 0.75 - 0.5 * 0.375));
    }

    [Fact]
    public void AFarRayThatLeavesTheScreenLeavesItsSlicesOpenShareOpen()
    {
        var estimate = new AmbientEstimate(Bounce: new(0.5, 0.25, 0.125), Occlusion: 0.75);

        estimate.WithFarRay(openShare: 0.5, outcome: FarOutcome.Open).Should().Be(estimate);
    }

    [Fact]
    public void AFarRayIntoItsOwnSurfaceBlocksWithoutLight()
    {
        var estimate = new AmbientEstimate(Bounce: new(0.5, 0.25, 0.125), Occlusion: 0.75);

        estimate
            .WithFarRay(openShare: 0.5, outcome: FarOutcome.IntoItsOwnSurface)
            .Should()
            .Be(new AmbientEstimate(Bounce: new(0.5, 0.25, 0.125), Occlusion: 0.25));
    }
}
