namespace Tertius.Rendering.Tests;

/// <summary>
/// One pass of the separable scatter at one skin pixel: the kernel scaled to the pixel's depth, and the rules by which a
/// tap gives way to the center's light (docs/plans/skin-and-hair.md, "The two passes"). Each test builds a small field.
/// </summary>
public sealed class SkinScatterTests
{
    // At a view depth of 1 m, one meter across the view covers 4,000 pixels: a pixel is 0.25 mm, and the reach 32 pixels.
    private const double PixelsPerMeterAtOneMeter = 4000;

    private static readonly Double3 Skin = new(0.6, 0.4, 0.2);

    /// <summary>
    /// A field of <paramref name="width"/> × <paramref name="height"/> pixels whose view covers
    /// <see cref="PixelsPerMeterAtOneMeter"/> pixels per meter at 1 m.
    /// </summary>
    private static ScatterField Field(
        int width,
        int height,
        Func<int, int, Double3> light,
        Func<int, int, bool> skin,
        Func<int, int, double> viewDepth
    )
    {
        var lights = new Double3[width * height];
        bool[] skins = new bool[width * height];
        double[] depths = new double[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            lights[y * width + x] = light(x, y);
            skins[y * width + x] = skin(x, y);
            depths[y * width + x] = viewDepth(x, y);
        }
        return new(
            Width: width,
            Height: height,
            Light: lights,
            Skin: skins,
            ViewDepth: depths,
            TanHalfY: height / (2 * PixelsPerMeterAtOneMeter)
        );
    }

    /// <summary>A row of <paramref name="width"/> pixels, as <see cref="Field"/> builds it.</summary>
    private static ScatterField Row(
        int width,
        Func<int, Double3> light,
        Func<int, bool> skin,
        Func<int, double> viewDepth
    ) =>
        Field(
            width: width,
            height: 1,
            light: (x, _) => light(x),
            skin: (x, _) => skin(x),
            viewDepth: (x, _) => viewDepth(x)
        );

    /// <summary>
    /// A kernel of three taps: half the light at the center and a quarter at <paramref name="offset"/> meters on each side.
    /// </summary>
    private static ScatterKernel ThreeTaps(double offset) =>
        new([
            new ScatterTap(Offset: -offset, Weight: new(0.25, 0.25, 0.25)),
            new ScatterTap(Offset: 0, Weight: new(0.5, 0.5, 0.5)),
            new ScatterTap(Offset: offset, Weight: new(0.25, 0.25, 0.25)),
        ]);

    private static Double3 AlongTheRow(ScatterField field, int x, ScatterKernel kernel) =>
        SkinScatter.Pass(field: field, source: field.Light, x: x, y: 0, axis: ScatterAxis.Rows, kernel: kernel);

    private static void ShouldBeNear(Double3 actual, Double3 expected, double tolerance, string because)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance, because);
        actual.Y.Should().BeApproximately(expected.Y, tolerance, because);
        actual.Z.Should().BeApproximately(expected.Z, tolerance, because);
    }

    [Fact]
    public void AnEvenlyLitSkinRowScattersToItself()
    {
        ScatterField field = Row(width: 101, light: _ => Skin, skin: _ => true, viewDepth: _ => 1);

        Double3 scattered = AlongTheRow(field, x: 50, kernel: SkinProfile.Human.Kernel(8));

        ShouldBeNear(
            actual: scattered,
            expected: Skin,
            tolerance: 1e-14,
            because: "the skin around gives what it takes"
        );
    }

    [Fact]
    public void LightCrossesALitEdgeIntoTheDarkAndRedGoesFarthest()
    {
        // Lit up to pixel 99, dark from pixel 100; a pixel is 0.25 mm.
        ScatterField field = Row(
            width: 201,
            light: x => x < 100 ? new Double3(1, 1, 1) : Double3.Zero,
            skin: _ => true,
            viewDepth: _ => 1
        );
        ScatterKernel kernel = SkinProfile.Human.Kernel(8);

        foreach (int intoTheDark in new[] { 2, 4, 8 })
        {
            Double3 scattered = AlongTheRow(field, x: 99 + intoTheDark, kernel: kernel);
            scattered.Z.Should().BeGreaterThan(0, "even blue light leaves the lit side");
            scattered.X.Should().BeGreaterThan(scattered.Y, $"red light reaches farthest, at {intoTheDark} pixels too");
            scattered
                .Y.Should()
                .BeGreaterThan(scattered.Z, $"green light reaches farther than blue, at {intoTheDark} pixels too");
        }
    }

    [Fact]
    public void ATapOnAPixelThatIsNotSkinGivesWayToTheCenter()
    {
        // Past pixel 50 lies a surface that is not skin, whose scatter image holds 0.
        ScatterField field = Row(
            width: 101,
            light: x => x <= 50 ? Skin : Double3.Zero,
            skin: x => x <= 50,
            viewDepth: _ => 1
        );

        Double3 scattered = AlongTheRow(field, x: 50, kernel: SkinProfile.Human.Kernel(8));

        ShouldBeNear(
            actual: scattered,
            expected: Skin,
            tolerance: 1e-14,
            because: "a tap off the skin takes the center's light, so no light leaves the skin"
        );
    }

    [Fact]
    public void ATapAcrossADepthStepGivesWayToTheCenter()
    {
        // Past pixel 50 lies the skin of another surface, 20 cm behind and in shadow.
        ScatterField field = Row(
            width: 101,
            light: x => x <= 50 ? Skin : Double3.Zero,
            skin: _ => true,
            viewDepth: x => x <= 50 ? 1 : 1.2
        );

        Double3 scattered = AlongTheRow(field, x: 50, kernel: SkinProfile.Human.Kernel(8));

        ShouldBeNear(
            actual: scattered,
            expected: Skin,
            tolerance: 1e-14,
            because: "light does not scatter across a gap between two surfaces"
        );
    }

    [Fact]
    public void ATapWithinTheSlopeLimitKeepsItsOwnLight()
    {
        // Past pixel 50 the skin is in shadow and turns away, its depth rising 0.5 m per meter across: below tan 60°.
        static Double3 light(int x) => x <= 50 ? Skin : Double3.Zero;
        ScatterField sloped = Row(
            width: 101,
            light: light,
            skin: _ => true,
            viewDepth: x => x <= 50 ? 1 : 1 + 0.5 * (x - 50) / PixelsPerMeterAtOneMeter
        );
        ScatterField flat = Row(width: 101, light: light, skin: _ => true, viewDepth: _ => 1);
        ScatterKernel kernel = SkinProfile.Human.Kernel(8);

        Double3 scattered = AlongTheRow(sloped, x: 50, kernel: kernel);

        ShouldBeNear(
            actual: scattered,
            expected: AlongTheRow(flat, x: 50, kernel: kernel),
            tolerance: 1e-14,
            because: "a slope below the limit is the center's own surface"
        );
        scattered.X.Should().BeLessThan(Skin.X, "the lit pixel loses light into the shadow beside it");
    }

    [Fact]
    public void ATapBetweenOneAndTwoSlopeLimitsMixesItsLightWithTheCenters()
    {
        // The tap at +4 mm lands on pixel 66. Its depth rises 1.5 times the slope limit's rise across 4 mm.
        const double offset = 0.004;
        ScatterField field = Row(
            width: 101,
            light: x => x == 66 ? Double3.Zero : new Double3(1, 1, 1),
            skin: _ => true,
            viewDepth: x => x == 66 ? 1 + 1.5 * SkinScatter.SlopeLimit * offset : 1
        );

        Double3 scattered = AlongTheRow(field, x: 50, kernel: ThreeTaps(offset));

        ShouldBeNear(
            actual: scattered,
            expected: new(0.875, 0.875, 0.875),
            tolerance: 1e-12,
            because: "the tap keeps half its own light and takes half the center's: 0.25 + 0.5 + 0.25 · 0.5"
        );
    }

    [Fact]
    public void AKernelBelowHalfAPixelLeavesThePixelAsItWas()
    {
        // At 65 m, the reach of 8 mm covers 0.49 pixels.
        ScatterField field = Row(
            width: 11,
            light: x => x == 5 ? Skin : new Double3(3, 2, 1),
            skin: _ => true,
            viewDepth: _ => 65.3
        );

        Double3 scattered = AlongTheRow(field, x: 5, kernel: SkinProfile.Human.Kernel(8));

        scattered.Should().Be(Skin, "a scatter narrower than a pixel stays inside the pixel");
    }

    [Fact]
    public void TwiceTheDepthHalvesThePixelsOfTheKernel()
    {
        // The tap at +4 mm lands 16 pixels away at 1 m, and 8 pixels away at 2 m.
        static Double3 light(int x) =>
            x switch
            {
                66 => new Double3(1, 0, 0),
                58 => new Double3(0, 1, 0),
                _ => Double3.Zero,
            };
        ScatterField near = Row(width: 101, light: light, skin: _ => true, viewDepth: _ => 1);
        ScatterField far = Row(width: 101, light: light, skin: _ => true, viewDepth: _ => 2);

        Double3 atOneMeter = AlongTheRow(near, x: 50, kernel: ThreeTaps(0.004));
        Double3 atTwoMeters = AlongTheRow(far, x: 50, kernel: ThreeTaps(0.004));

        atOneMeter.Should().Be(new Double3(0.25, 0, 0));
        atTwoMeters.Should().Be(new Double3(0, 0.25, 0));
        SkinScatter
            .PixelsPerMeter(viewDepth: 2, height: 1080, tanHalfY: 0.5)
            .Should()
            .Be(SkinScatter.PixelsPerMeter(viewDepth: 1, height: 1080, tanHalfY: 0.5) / 2);
    }

    [Fact]
    public void ATapReadsThePixelNearestItsOffset()
    {
        // The taps at ±3.9 mm lie 15.6 pixels away: nearest to pixels 34 and 66, not 35 and 65.
        ScatterField field = Row(
            width: 101,
            light: x => x is 34 or 66 ? new Double3(1, 1, 1) : Double3.Zero,
            skin: _ => true,
            viewDepth: _ => 1
        );

        Double3 scattered = AlongTheRow(field, x: 50, kernel: ThreeTaps(0.0039));

        scattered.Should().Be(new Double3(0.5, 0.5, 0.5), "each tap rounds its offset to the nearest pixel");
    }

    [Fact]
    public void TapsPastTheImageReadTheEdgePixel()
    {
        // A column of 5 pixels in the middle of 3 columns; the taps at ±4 mm lie 16 pixels away, past both ends.
        ScatterField field = Field(
            width: 3,
            height: 5,
            light: (x, y) =>
                (x, y) switch
                {
                    (1, 0) => new Double3(0, 0, 1),
                    (1, 4) => new Double3(1, 0, 0),
                    (1, 3) => new Double3(0, 1, 0),
                    (1, _) => Double3.Zero,
                    _ => new Double3(9, 9, 9),
                },
            skin: (_, _) => true,
            viewDepth: (_, _) => 1
        );

        Double3 scattered = SkinScatter.Pass(
            field: field,
            source: field.Light,
            x: 1,
            y: 3,
            axis: ScatterAxis.Columns,
            kernel: ThreeTaps(0.004)
        );

        scattered
            .Should()
            .Be(new Double3(0.25, 0.5, 0.25), "the taps clamp to the first and the last pixel of the column");
    }
}
