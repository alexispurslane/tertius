namespace Tertius.Rendering.Tests;

/// <summary>
/// The estimate of a texel, the occlusion V, the open share S and the bounce G, from its slices' near sectors
/// (docs/plans/screen-space-illumination.md, "The estimate of a texel"): its shares sum to one, S is the open pieces'
/// share of the map's light, and a furnace gives every direction the same light.
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
            new(
                Sectors: sectors[0],
                Blocked: SectorMask.Run(start: 20, end: 32),
                Light: Red * 12,
                BlockedMapLight: Double3.Zero,
                OpenMapLight: Double3.Zero
            ),
            new(Sectors: sectors[1], Blocked: SectorMask.None, Light: Double3.Zero, BlockedMapLight: Double3.Zero, OpenMapLight: Double3.Zero),
            new(Sectors: sectors[2], Blocked: SectorMask.Run(start: 0, end: 8), Light: Sky * 3, BlockedMapLight: Double3.Zero, OpenMapLight: Double3.Zero),
        ];
        double total = TotalWeight(sectors);

        var estimate = AmbientEstimate.OfSlices(near);

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
    public void TheOpenAndTheBlockedSharesOfATexelSumToOne()
    {
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        var random = new Random(11);
        for (int trial = 0; trial < 100; trial++)
        {
            NearSlice[] near =
            [
                .. sectors.Select(slice => new NearSlice(
                    Sectors: slice,
                    Blocked: new SectorMask((uint)random.NextInt64(1L << 32)),
                    Light: Double3.Zero,
                    BlockedMapLight: Double3.Zero,
                    OpenMapLight: Double3.Zero
                )),
            ];
            double nearShare = Enumerable
                .Range(0, 3)
                .Sum(k => sectors[k].Weight * near[k].Blocked.Count / (SliceSectors.Count * TotalWeight(sectors)));
            var estimate = AmbientEstimate.OfSlices(near);

            (estimate.Occlusion + nearShare).Should().BeApproximately(1, 1e-14, $"trial {trial}");
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
        NearSlice[] near =
        [
            .. sectors.Select(slice => new NearSlice(
                Sectors: slice,
                Blocked: SectorMask.None,
                Light: Double3.Zero,
                BlockedMapLight: Double3.Zero,
                OpenMapLight: Double3.Zero
            )),
        ];

        AmbientEstimate
            .OfSlices(near)
            .Should()
            .Be(AmbientEstimate.Open, "the shares sum to 1 in every texel, not only in the mean over slices");
    }

    [Fact]
    public void ATexelWhoseSlicesHaveNoWeightIsOpen()
    {
        // The normal lies at right angles to the only slice's plane, so the slice has no weight.
        var sectors = new SliceSectors(new HorizonSlice(toEye: TowardTheEye, normal: Double3.UnitY, angle: 0));
        NearSlice[] near =
        [
            new(Sectors: sectors, Blocked: SectorMask.Run(start: 4, end: 20), Light: Red * 16, BlockedMapLight: Double3.Zero, OpenMapLight: Double3.Zero),
        ];
        sectors.Weight.Should().Be(0, "the test needs a slice with no weight");

        AmbientEstimate.OfSlices(near).Should().Be(AmbientEstimate.Open, "a texel with no information is open");
    }

    [Fact]
    public void InAFurnaceTheSkyLightAndTheBounceSumToTheSkyLight()
    {
        // Every blocker emits the sky's radiance and faces the center.
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
                    SliceSide side = i % 2 == 0 ? SliceSide.Plus : SliceSide.Minus;
                    double angle = 0.2 + 1.6 * random.NextDouble();
                    Double3 position =
                        center
                        + (slice.ToEye * Math.Cos(angle) + slice.TowardPlusSide * ((int)side * Math.Sin(angle)))
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
                    // With no normal map, the receiver's own surface blocks no direction of its hemisphere.
                    ownSurface: new OwnPlane(TiltedNormal),
                    radius: 1,
                    thicknessShare: 0.1,
                    steps: steps,
                    mapLight: null
                );
            }
            var estimate = AmbientEstimate.OfSlices(near);

            ShouldBeNear(
                actual: Sky * estimate.Occlusion + estimate.Bounce,
                expected: Sky,
                tolerance: 1e-14,
                because: $"trial {trial}: no direction counts twice and none is lost"
            );
        }
    }

    [Fact]
    public void WithTheMapOnTheBounceIsTheBlockersLightAlone()
    {
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        NearSlice[] near =
        [
            new(
                Sectors: sectors[0],
                Blocked: SectorMask.Run(start: 20, end: 32),
                Light: Red * 12,
                BlockedMapLight: Sky * 12,
                OpenMapLight: Sky * 20
            ),
            new(
                Sectors: sectors[1],
                Blocked: SectorMask.None,
                Light: Double3.Zero,
                BlockedMapLight: Double3.Zero,
                OpenMapLight: Sky * 32
            ),
        ];
        double total = TotalWeight(sectors[..2]);

        var estimate = AmbientEstimate.OfSlices(near);

        ShouldBeNear(
            actual: estimate.Bounce,
            expected: Red * (12 * sectors[0].Weight) / (32 * total),
            tolerance: 1e-15,
            because: "the open share S takes the map's light of the blocked directions away, not the bounce"
        );
    }

    [Fact]
    public void TheOpenShareInEachChannelIsTheOpenPiecesShareOfTheMapsLightOverAllPieces()
    {
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        Double3 blocked0 = new(5, 0.2, 1);
        Double3 open0 = new(1, 2, 0.5);
        Double3 blocked2 = new(0.5, 0.1, 3);
        Double3 open2 = new(2, 1, 0.25);
        NearSlice[] near =
        [
            new(
                Sectors: sectors[0],
                Blocked: SectorMask.Run(start: 20, end: 32),
                Light: Red * 12,
                BlockedMapLight: blocked0,
                OpenMapLight: open0
            ),
            new(
                Sectors: sectors[1],
                Blocked: SectorMask.None,
                Light: Double3.Zero,
                BlockedMapLight: Double3.Zero,
                OpenMapLight: Sky * 32
            ),
            new(
                Sectors: sectors[2],
                Blocked: SectorMask.Run(start: 0, end: 8),
                Light: Double3.Zero,
                BlockedMapLight: blocked2,
                OpenMapLight: open2
            ),
        ];
        Double3 open = open0 * sectors[0].Weight + Sky * (32 * sectors[1].Weight) + open2 * sectors[2].Weight;
        Double3 all = open + blocked0 * sectors[0].Weight + blocked2 * sectors[2].Weight;

        var estimate = AmbientEstimate.OfSlices(near);

        ShouldBeNear(
            actual: estimate.OpenShare,
            expected: open / all,
            tolerance: 1e-15,
            because: "each slice's reads count by its weight, in the open sum as in the sum over all pieces"
        );
    }

    [Fact]
    public void TheOpenShareLiesBetweenZeroAndOneInEachChannel()
    {
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        var random = new Random(13);
        for (int trial = 0; trial < 1000; trial++)
        {
            NearSlice[] near =
            [
                .. sectors.Select(slice =>
                {
                    var blocked = new SectorMask((uint)random.NextInt64(1L << 32));
                    return new NearSlice(
                        Sectors: slice,
                        Blocked: blocked,
                        Light: Double3.Zero,
                        BlockedMapLight: RandomLight(random) * blocked.Count,
                        OpenMapLight: RandomLight(random) * blocked.Open.Count
                    );
                }),
            ];

            Double3 share = AmbientEstimate.OfSlices(near).OpenShare;

            foreach (double channel in (ReadOnlySpan<double>)[share.X, share.Y, share.Z])
                channel.Should().BeInRange(0, 1, "trial {0}: S is a share of the map's light", trial);
        }
    }

    [Fact]
    public void ATexelThatBlocksNothingHasAnOpenShareOfExactlyOne()
    {
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        NearSlice[] near =
        [
            .. sectors.Select(slice => new NearSlice(
                Sectors: slice,
                Blocked: SectorMask.None,
                Light: Double3.Zero,
                BlockedMapLight: Double3.Zero,
                OpenMapLight: new Double3(0.3, 0.7, 1.9) * (32 * slice.Weight)
            )),
        ];

        AmbientEstimate
            .OfSlices(near)
            .OpenShare.Should()
            .Be(new Double3(1, 1, 1), "an open point keeps exactly the map's irradiance at its normal");
    }

    [Fact]
    public void AChannelInWhichTheMapHoldsNoLightAlongAnyPieceTakesTheOpenShareOfTheCosineWeight()
    {
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        NearSlice[] near =
        [
            new(
                Sectors: sectors[0],
                Blocked: SectorMask.Run(start: 20, end: 32),
                Light: Double3.Zero,
                BlockedMapLight: new(1, 0, 2),
                OpenMapLight: new(3, 0, 0)
            ),
            new(
                Sectors: sectors[1],
                Blocked: SectorMask.Run(start: 0, end: 8),
                Light: Double3.Zero,
                BlockedMapLight: new(0, 0, 1),
                OpenMapLight: new(2, 0, 0)
            ),
        ];

        var estimate = AmbientEstimate.OfSlices(near);

        estimate
            .OpenShare.Y.Should()
            .BeApproximately(estimate.Occlusion, 1e-15, "the green channel holds no map light along any piece");
        estimate.OpenShare.Z.Should().Be(0, "the blue light lies along the blocked pieces alone");
    }

    [Fact]
    public void WithTheMapOffTheOpenShareIsTheOpenShareOfTheCosineWeightInEachChannel()
    {
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        NearSlice[] near =
        [
            new(
                Sectors: sectors[0],
                Blocked: SectorMask.Run(start: 20, end: 32),
                Light: Red * 12,
                BlockedMapLight: Double3.Zero,
                OpenMapLight: Double3.Zero
            ),
            new(
                Sectors: sectors[2],
                Blocked: SectorMask.Run(start: 0, end: 8),
                Light: Sky * 3,
                BlockedMapLight: Double3.Zero,
                OpenMapLight: Double3.Zero
            ),
        ];

        var estimate = AmbientEstimate.OfSlices(near);

        ShouldBeNear(
            actual: estimate.OpenShare,
            expected: new Double3(1, 1, 1) * estimate.Occlusion,
            tolerance: 1e-15,
            because: "the hemisphere holds no direction, so an open direction takes its mean light, as V says"
        );
    }

    [Fact]
    public void InAFurnaceUnderAUniformMapTheMapsOpenLightAndTheBounceSumToTheMapsLight()
    {
        // Every blocker emits the map's radiance and faces the center.
        SliceSectors[] sectors = ThreeSlices(TiltedNormal);
        Double3 center = new(0, 0, 2);
        var random = new Random(7);
        for (int trial = 0; trial < 50; trial++)
        {
            NearSlice[] near =
            [
                .. sectors.Select(slice =>
                    SectorSearch.Near(
                        sectors: slice,
                        center: center,
                        ownSurface: new OwnPlane(TiltedNormal),
                        radius: 1,
                        thicknessShare: 0.1,
                        steps:
                        [
                            .. Enumerable
                                .Range(0, 4)
                                .Select(i =>
                                    FacingSkyStep(slice: slice.Slice, center: center, index: i, random: random)
                                ),
                        ],
                        mapLight: (_, _) => Sky
                    )
                ),
            ];
            near.Sum(slice => slice.Blocked.Count).Should().BePositive("the test needs blockers");

            var estimate = AmbientEstimate.OfSlices(near);

            ShouldBeNear(
                actual: Sky * estimate.OpenShare + estimate.Bounce,
                expected: Sky,
                tolerance: 1e-14,
                because: $"trial {trial}: the map's irradiance is the map's light, and each blocker replaces it in kind"
            );
        }
    }

    /// <summary>A light of random channels from 0 to 2, a quarter of them 0.</summary>
    private static Double3 RandomLight(Random random)
    {
        double Channel() => random.Next(4) == 0 ? 0 : 2 * random.NextDouble();
        return new(Channel(), Channel(), Channel());
    }

    /// <summary>A step that emits <see cref="Sky"/> and faces <paramref name="center"/>, on the side that <paramref name="index"/> picks, at a random angle and distance.</summary>
    private static NearStep FacingSkyStep(HorizonSlice slice, Double3 center, int index, Random random)
    {
        SliceSide side = index % 2 == 0 ? SliceSide.Plus : SliceSide.Minus;
        double angle = 0.2 + 1.6 * random.NextDouble();
        Double3 position =
            center
            + (slice.ToEye * Math.Cos(angle) + slice.TowardPlusSide * ((int)side * Math.Sin(angle)))
                * (0.1 + 0.8 * random.NextDouble());
        return new(Position: position, GeometricNormal: (center - position).Normalized(), DirectLight: Sky, Side: side);
    }
}
