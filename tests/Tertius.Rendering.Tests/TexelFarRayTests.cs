namespace Tertius.Rendering.Tests;

/// <summary>
/// The pick of a texel's one far ray under <see cref="FarRays.OnePerTexel"/> (docs/plans/screen-space-illumination.md,
/// fork 2's option B): one ray through an open sector of any slice, picked by its slice's weight, so that each open
/// sector of the texel is as likely as its share of the estimate. Its outcome decides the texel's whole open share.
/// </summary>
public sealed class TexelFarRayTests
{
    private static readonly Double3 TowardTheEye = new(0, 0, -1);

    /// <summary>The sectors of the slice at φ = 0 around a normal tilted by <paramref name="normalAngle"/> toward +x.</summary>
    private static SliceSectors Tilted(double normalAngle) =>
        new(
            new HorizonSlice(
                toEye: TowardTheEye,
                normal: new(Math.Sin(normalAngle), 0, -Math.Cos(normalAngle)),
                angle: 0
            )
        );

    private static NearSlice Slice(SliceSectors sectors, SectorMask blocked) =>
        new(Sectors: sectors, Blocked: blocked, Light: Double3.Zero);

    [Theory]
    [InlineData(0f, 0, 16)]
    [InlineData(0.5f, 0, 28)]
    [InlineData(0.75f, 1, 2)]
    [InlineData(0.99f, 1, 7)]
    public void TheNoisePicksTheOpenSectorOfItsShareOfTheTexelsOpenSectors(float noise, int slice, int sector)
    {
        // Two slices of one weight: slice 0 has the 16 open sectors 16 to 31, slice 1 the 8 open sectors 0 to 7. In
        // order they hold the ranks 0 to 23, and the noise picks rank ⌊24 η⌋.
        NearSlice[] slices =
        [
            Slice(Tilted(0), SectorMask.Run(start: 0, end: 16)),
            Slice(Tilted(0), SectorMask.Run(start: 8, end: 32)),
        ];

        SectorSearch.TexelFarRay(slices, noise).Should().Be((Slice: slice, Sector: sector));
    }

    [Fact]
    public void ASliceOfMoreWeightTakesALargerShareOfTheNoise()
    {
        // Each slice has 16 open sectors, so slice 0 takes the noise below w₀ / (w₀ + w₁) and slice 1 the rest. A pick
        // by slice alone would split the noise at one half.
        SliceSectors light = Tilted(0);
        SliceSectors heavy = Tilted(1.2);
        heavy.Weight.Should().BeGreaterThan(1.3 * light.Weight, "the test needs weights far apart");
        NearSlice[] slices = [Slice(light, SectorMask.Run(start: 0, end: 16)), Slice(heavy, SectorMask.Run(0, 16))];
        float boundary = (float)(light.Weight / (light.Weight + heavy.Weight));

        SectorSearch.TexelFarRay(slices, boundary - 0.01f)!.Value.Slice.Should().Be(0);
        SectorSearch.TexelFarRay(slices, boundary + 0.01f)!.Value.Slice.Should().Be(1);
        SectorSearch.TexelFarRay(slices, 0.45f)!.Value.Slice.Should().Be(1, "the heavy slice owns more than half");
        // W₁ = cos 1.2 + 1.2 sin 1.2 = 1.4808: noise 0.9 is 35.72 of 39.69, 19.72 into slice 1, rank ⌊19.72 / 1.4808⌋.
        SectorSearch
            .TexelFarRay(slices, 0.9f)
            .Should()
            .Be((Slice: 1, Sector: 16 + 13), "within a slice, each open sector counts the slice's weight");
    }

    [Fact]
    public void ANoiseOnTheBoundaryOfTwoSlicesPicksTheFirstOpenSectorOfTheLaterSlice()
    {
        // Rank ⌊η Σ W c⌋: each slice owns the half-open run of ranks [start, end), as each sector does. Two slices of one
        // weight with 12 open sectors each, so η = 1/2 is rank 12 exactly, the first of slice 1.
        NearSlice[] slices =
        [
            Slice(Tilted(0), SectorMask.Run(start: 0, end: 20)),
            Slice(Tilted(0), SectorMask.Run(start: 12, end: 32)),
        ];

        SectorSearch.TexelFarRay(slices, 0.5f).Should().Be((Slice: 1, Sector: 0));
    }

    [Fact]
    public void ANoiseThatRoundsUpToOnePicksTheLastOpenSectorOfTheLastOpenSlice()
    {
        // GLSL lets a driver's fract and product err by an ulp, so the noise can reach 1.
        NearSlice[] slices =
        [
            Slice(Tilted(0), SectorMask.Run(start: 0, end: 15)),
            Slice(Tilted(0), new SectorMask(uint.MaxValue)),
        ];

        SectorSearch.TexelFarRay(slices, 1).Should().Be((Slice: 0, Sector: 31));
    }

    [Fact]
    public void ATexelWithNoOpenSectorTracesNoFarRay()
    {
        NearSlice[] slices =
        [
            Slice(Tilted(0), new SectorMask(uint.MaxValue)),
            Slice(Tilted(0.5), new SectorMask(uint.MaxValue)),
        ];

        foreach (float noise in new[] { 0f, 0.5f, 1f })
            SectorSearch.TexelFarRay(slices, noise).Should().BeNull($"noise {noise}: every sector is blocked nearby");
    }

    [Fact]
    public void TheTexelsOneFarRayDecidesItsWholeOpenShare()
    {
        // The near estimate's V is the sum of the slices' open shares, so a blocked ray leaves no open share.
        NearSlice[] slices =
        [
            Slice(Tilted(0), SectorMask.Run(start: 0, end: 16)),
            Slice(Tilted(0.5), SectorMask.Run(start: 8, end: 32)),
        ];
        AmbientEstimate near = AmbientEstimate.OfSlices(slices);

        AmbientEstimate far = near.WithTexelFarRay(new FarOutcome(Blocked: 1, Light: new(2, 0, 0)));

        far.Occlusion.Should().BeApproximately(0, 1e-12);
        far.Bounce.Should().Be(new Double3(2 * near.Occlusion, 0, 0));
    }
}
