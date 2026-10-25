namespace Tertius.Rendering.Tests;

/// <summary>
/// The DFG and lobe-sky tables: their values against <see cref="HalfVectorQuadrature"/> and closed forms, their shape
/// over the axes, the sampling that the GPU copies, and their determinism (docs/plans/pbr-brdf.md, "The tables").
/// </summary>
public sealed class BrdfTablesTests
{
    private static (double SchlickPart, double LobeAlbedo) DfgTexel(BrdfTables tables, int column, int row)
    {
        int texel = row * BrdfTables.DfgSize + column;
        return ((double)tables.Dfg[2 * texel], (double)tables.Dfg[2 * texel + 1]);
    }

    private static double LobeSkyTexel(BrdfTables tables, int column, int row) =>
        (double)tables.LobeSky[row * BrdfTables.LobeSkyWidth + column];

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 0)]
    [InlineData(31, 0)]
    [InlineData(12, 2)]
    [InlineData(24, 5)]
    [InlineData(0, 8)]
    [InlineData(16, 16)]
    [InlineData(4, 24)]
    [InlineData(0, 31)]
    [InlineData(31, 31)]
    public void EachDfgTexelHoldsTheIntegralsAtItsPointWithinOneThousandthOrOneEightBitStepAtGrazingViews(
        int column,
        int row
    )
    {
        double nDotV = BrdfTables.TexelValue(column, BrdfTables.DfgSize);
        var lobe = new GgxLobe(BrdfTables.TexelValue(row, BrdfTables.DfgSize));
        Double3 toView = HalfVectorQuadrature.View(nDotV);
        Double3 integrals = HalfVectorQuadrature.ColorIntegral(
            toView: toView,
            width: lobe.Alpha,
            integrand: toLight =>
            {
                Double3 h = (toLight + toView).Normalized();
                double vDotH = Double3.Dot(toView, h);
                double lobeAlbedo =
                    lobe.Distribution(h.Z) * lobe.Visibility(nDotL: toLight.Z, nDotV: nDotV) * toLight.Z;
                return new Double3(lobeAlbedo * Math.Pow(1 - vDotH, 5), lobeAlbedo, 0);
            }
        );

        (double schlickPart, double lobeAlbedo) = DfgTexel(BrdfTables.Shared, column, row);

        // The bar is what the 8-bit targets can show, one step of 1/255. The Hammersley set errs by up to 0.003 at
        // grazing views, μ below 0.2, where the integrand changes steeply near the horizon, and by under 0.001 above.
        double tolerance = nDotV < 0.2 ? 1.0 / 255 : 1e-3;
        integrals.Y.Should().BePositive();
        schlickPart.Should().BeApproximately(integrals.X, tolerance, "the Schlick part S");
        lobeAlbedo.Should().BeApproximately(integrals.Y, tolerance, "the lobe albedo E₁");
    }

    [Fact]
    public void AtNormalIncidenceAndRoughnessOneTheLobeAlbedoIsOneMinusLnTwo()
    {
        (double _, double lobeAlbedo) = BrdfTables.IntegrateDfg(
            nDotV: 1,
            roughness: 1,
            samples: BrdfTables.SamplesPerTexel
        );

        lobeAlbedo.Should().BeApproximately(1 - Math.Log(2), 1e-3);
    }

    [Fact]
    public void TheSmoothestRowReflectsEverythingAndItsSchlickPartIsOneMinusMuToTheFifth()
    {
        // Column 0 is μ = 0, where the mirror direction lies on the horizon: only the half vectors tilted toward the
        // eye send light above it, so v·h is the lobe's width there, not μ.
        for (int column = 1; column < BrdfTables.DfgSize; column++)
        {
            double nDotV = BrdfTables.TexelValue(column, BrdfTables.DfgSize);
            (double schlickPart, double lobeAlbedo) = DfgTexel(BrdfTables.Shared, column, row: 0);

            // At the floor the lobe is a near mirror, h ≈ n and v·h ≈ μ. The masking of the floor's microfacets
            // costs about 1 % at the most grazing columns.
            lobeAlbedo.Should().BeApproximately(1, 0.015, $"μ = {nDotV}");
            schlickPart.Should().BeApproximately(Math.Pow(1 - nDotV, 5), 0.015, $"μ = {nDotV}");
        }
    }

    [Fact]
    public void OutsideGrazingViewsARougherLobeNeverReflectsMore()
    {
        // Below μ = 0.1, E₁ dips and rises again with roughness, by up to 0.012 at μ = 1/31. The independent
        // quadrature finds the same rise, so it belongs to the height-correlated GGX lobe, not to the tables.
        for (int column = 0; column < BrdfTables.DfgSize; column++)
        {
            if (BrdfTables.TexelValue(column, BrdfTables.DfgSize) < 0.1)
                continue;
            for (int row = 1; row < BrdfTables.DfgSize; row++)
                DfgTexel(BrdfTables.Shared, column, row)
                    .LobeAlbedo.Should()
                    .BeLessThanOrEqualTo(
                        DfgTexel(BrdfTables.Shared, column, row - 1).LobeAlbedo + 1e-3,
                        $"column {column}, row {row}"
                    );
        }
    }

    [Fact]
    public void ARougherLobeLosesMoreLightToSingleScattering()
    {
        // Head on, E₁ falls from about 1 at the floor to 1 − ln 2 at r = 1 (docs/plans/pbr-brdf.md, "The energy
        // compensation").
        int headOn = BrdfTables.DfgSize - 1;

        DfgTexel(BrdfTables.Shared, headOn, row: 0).LobeAlbedo.Should().BeGreaterThan(0.99);
        DfgTexel(BrdfTables.Shared, headOn, row: BrdfTables.DfgSize - 1).LobeAlbedo.Should().BeLessThan(0.35);
    }

    [Fact]
    public void EveryDfgTexelHoldsAPositiveLobeAlbedoSoTheShadersMayDivideByIt()
    {
        // lobesOf in lighting.glsl divides by E₁ for the energy compensation, with no floor (ARCHITECTURE.md, "Portable
        // Vulkan"). A bilinear read between positive texels is positive too.
        for (int row = 0; row < BrdfTables.DfgSize; row++)
        for (int column = 0; column < BrdfTables.DfgSize; column++)
            DfgTexel(BrdfTables.Shared, column, row)
                .LobeAlbedo.Should()
                .BeGreaterThan(0.1, "the texel at column {0} and row {1} holds a divisor", column, row);
    }

    [Theory]
    [InlineData(20, 31)]
    [InlineData(28, 24)]
    [InlineData(40, 16)]
    [InlineData(36, 8)]
    public void EachLobeSkyTexelHoldsTheLobesSkyShareAtItsPointWithinOneEightBitStep(int column, int row)
    {
        double cosine = 2 * BrdfTables.TexelValue(column, BrdfTables.LobeSkyWidth) - 1;
        var lobe = new GgxLobe(BrdfTables.TexelValue(row, BrdfTables.LobeSkyHeight));
        var up = new Double3(Math.Sqrt(1 - cosine * cosine), 0, cosine);
        // With v = n, the sampling density of l is D / 4, and each sample weighs n·l.
        double Weight(Double3 toLight) => lobe.Distribution((toLight + Double3.UnitZ).Normalized().Z) * toLight.Z;
        double total = HalfVectorQuadrature.Integral(toView: Double3.UnitZ, width: lobe.Alpha, integrand: Weight);
        double above = HalfVectorQuadrature.Integral(
            toView: Double3.UnitZ,
            width: lobe.Alpha,
            integrand: toLight => Double3.Dot(toLight, up) > 0 ? Weight(toLight) : 0
        );

        // One 8-bit step, 1/255, is the bar of what the targets can show.
        LobeSkyTexel(BrdfTables.Shared, column, row).Should().BeApproximately(above / total, 1.0 / 255);
    }

    [Fact]
    public void ASmoothLobeSeesOnlyTheSkyAboveTheHorizonAndOnlyTheGroundBelow()
    {
        for (int column = 0; column < BrdfTables.LobeSkyWidth; column++)
        {
            double cosine = 2 * BrdfTables.TexelValue(column, BrdfTables.LobeSkyWidth) - 1;
            double share = LobeSkyTexel(BrdfTables.Shared, column, row: 0);

            double expected = cosine > 0 ? 1 : 0;
            share.Should().BeApproximately(expected, 0.05, $"d·up = {cosine}");
        }
    }

    [Fact]
    public void TheSkySharesOfATiltAndOfItsMirrorTiltAddUpToOne()
    {
        for (int row = 0; row < BrdfTables.LobeSkyHeight; row++)
        for (int column = 0; column < BrdfTables.LobeSkyWidth / 2; column++)
        {
            int mirror = BrdfTables.LobeSkyWidth - 1 - column;
            double sum = LobeSkyTexel(BrdfTables.Shared, column, row) + LobeSkyTexel(BrdfTables.Shared, mirror, row);

            sum.Should().BeApproximately(1, 2e-3, $"column {column}, row {row}: the error of the sample set");
        }
    }

    [Fact]
    public void TheLobesSkyShareRisesAsTheLobeTiltsUp()
    {
        for (int row = 0; row < BrdfTables.LobeSkyHeight; row++)
        for (int column = 1; column < BrdfTables.LobeSkyWidth; column++)
            LobeSkyTexel(BrdfTables.Shared, column, row)
                .Should()
                .BeGreaterThanOrEqualTo(
                    LobeSkyTexel(BrdfTables.Shared, column - 1, row),
                    $"column {column}, row {row}: each sample's l·up rises with the tilt"
                );
    }

    [Fact]
    public void ARoughLobeLevelWithTheHorizonSeesHalfSkyAndALobeFacingUpSeesMostlySky()
    {
        int roughest = BrdfTables.LobeSkyHeight - 1;
        double nearlyLevel = LobeSkyTexel(BrdfTables.Shared, BrdfTables.LobeSkyWidth / 2, roughest);
        double facingUp = LobeSkyTexel(BrdfTables.Shared, BrdfTables.LobeSkyWidth - 1, roughest);

        nearlyLevel.Should().BeInRange(0.5, 0.6, "the texel's tilt is 0.9° above level");
        facingUp.Should().BeGreaterThan(0.99);
        BrdfTables
            .IntegrateLobeSky(cosine: 0, roughness: 1, samples: BrdfTables.SamplesPerTexel)
            .Should()
            .BeApproximately(0.5, 2e-3);
    }

    [Fact]
    public void TheTexelCoordinateOfATexelsValueIsTheCenterOfThatTexel()
    {
        foreach (int count in new[] { BrdfTables.DfgSize, BrdfTables.LobeSkyWidth })
            for (int index = 0; index < count; index++)
                BrdfTables
                    .TexelCoordinate(BrdfTables.TexelValue(index, count), count)
                    .Should()
                    .BeApproximately(
                        (index + 0.5) / count,
                        1e-15,
                        "the GPU's linear sampler reads a texel alone at its center"
                    );
    }

    [Fact]
    public void TheEndsOfEachAxisReadTheirTexelsExactly()
    {
        BrdfTables tables = BrdfTables.Shared;
        const int last = BrdfTables.DfgSize - 1;

        tables.SampleDfg(nDotV: 1, roughness: 1).Should().Be(DfgTexel(tables, last, last));
        tables.SampleDfg(nDotV: 0, roughness: 0).Should().Be(DfgTexel(tables, 0, 0));
        tables
            .SampleLobeSky(cosine: 1, roughness: 1)
            .Should()
            .Be(LobeSkyTexel(tables, BrdfTables.LobeSkyWidth - 1, BrdfTables.LobeSkyHeight - 1));
        tables.SampleLobeSky(cosine: -1, roughness: 0).Should().Be(LobeSkyTexel(tables, 0, 0));
    }

    [Fact]
    public void SamplingBetweenTexelsIsBilinearAndClampsBeyondTheEnds()
    {
        BrdfTables tables = BrdfTables.Shared;
        const int size = BrdfTables.DfgSize;
        double Value(int index) => BrdfTables.TexelValue(index, size);
        double Cosine(int index) => 2 * BrdfTables.TexelValue(index, BrdfTables.LobeSkyWidth) - 1;
        const double quarterTexel = 0.25 / (size - 1);

        (double schlickAt, double albedoAt) = tables.SampleDfg(nDotV: Value(5), roughness: Value(9));
        schlickAt.Should().BeApproximately(DfgTexel(tables, 5, 9).SchlickPart, 1e-12);
        albedoAt.Should().BeApproximately(DfgTexel(tables, 5, 9).LobeAlbedo, 1e-12);
        (double schlickAcross, double albedoAcross) = tables.SampleDfg(
            nDotV: (Value(5) + Value(6)) / 2,
            roughness: Value(9)
        );
        schlickAcross
            .Should()
            .BeApproximately((DfgTexel(tables, 5, 9).SchlickPart + DfgTexel(tables, 6, 9).SchlickPart) / 2, 1e-12);
        albedoAcross
            .Should()
            .BeApproximately((DfgTexel(tables, 5, 9).LobeAlbedo + DfgTexel(tables, 6, 9).LobeAlbedo) / 2, 1e-12);
        tables
            .SampleDfg(nDotV: Value(5), roughness: Value(9) + quarterTexel)
            .LobeAlbedo.Should()
            .BeApproximately(
                0.75 * DfgTexel(tables, 5, 9).LobeAlbedo + 0.25 * DfgTexel(tables, 5, 10).LobeAlbedo,
                1e-12
            );
        tables.SampleDfg(nDotV: -0.3, roughness: -0.2).Should().Be(DfgTexel(tables, 0, 0));
        tables.SampleDfg(nDotV: 1.4, roughness: 1.2).Should().Be(DfgTexel(tables, size - 1, size - 1));

        tables
            .SampleLobeSky(cosine: (Cosine(40) + Cosine(41)) / 2, roughness: Value(3))
            .Should()
            .BeApproximately((LobeSkyTexel(tables, 40, 3) + LobeSkyTexel(tables, 41, 3)) / 2, 1e-12);
        tables
            .SampleLobeSky(cosine: Cosine(40), roughness: Value(3) + quarterTexel)
            .Should()
            .BeApproximately(0.75 * LobeSkyTexel(tables, 40, 3) + 0.25 * LobeSkyTexel(tables, 40, 4), 1e-12);
        tables.SampleLobeSky(cosine: 1.5, roughness: 1.5).Should().Be(LobeSkyTexel(tables, 63, 31));
    }

    [Fact]
    public void TwoComputationsGiveTheSameBits()
    {
        var first = BrdfTables.Compute();
        var second = BrdfTables.Compute();

        static ushort[] Bits(ReadOnlySpan<Half> halves)
        {
            ushort[] bits = new ushort[halves.Length];
            for (int index = 0; index < halves.Length; index++)
                bits[index] = BitConverter.HalfToUInt16Bits(halves[index]);
            return bits;
        }

        Bits(second.Dfg).Should().Equal(Bits(first.Dfg));
        Bits(second.LobeSky).Should().Equal(Bits(first.LobeSky));
        Bits(first.Dfg).Should().NotBeEquivalentTo(new ushort[first.Dfg.Length], "the table holds values");
    }

    [Fact]
    public void TheSharedTablesAreComputedOnceAndHoldTheComputedValues()
    {
        BrdfTables.Shared.Should().BeSameAs(BrdfTables.Shared);
        BrdfTables.Shared.Dfg.ToArray().Should().Equal(BrdfTables.Compute().Dfg.ToArray());
    }
}
