namespace Tertius.Rendering.Tests;

/// <summary>
/// FLIP, the perceptual difference of two images, on hand-made images whose expected difference follows by hand from
/// the published algorithm (Andersson et al. 2020, LDR-FLIP).
/// <para>
/// The numbers that the derivations use: an sRGB byte v is the linear light ((v/255 + 0.055) / 1.055)^2.4. CIELAB
/// L* = 116 · Y^(1/3) − 16 for linear luminance Y above 0.008856. The largest color error, cmax, is HyAB(green,
/// blue)^0.7 = 203.30^0.7 = 41.276, so pc · cmax = 0.4 · 41.276 = 16.510. A color error e = HyAB^0.7 below 16.510
/// maps to 0.95 · e / 16.510, and one above it to 0.95 + 0.05 · (e − 16.510) / (41.276 − 16.510). The feature
/// difference f of a pixel is (Δ / √2)^0.5, where Δ is the change of its edge or point strength in linear luminance.
/// The FLIP value is (color error)^(1 − f).
/// </para>
/// <para>
/// At 10 pixels per degree the achromatic and red-green filters keep all but 4e-8 of their weight on the pixel itself.
/// The blue-yellow filter does not: its wider term gives each neighbor 0.085 of the center's weight in one dimension,
/// and the pixel keeps only about 0.79 of the weight of the whole filter. That does not change the values below,
/// because every image that these tests compare at 10 pixels per degree is gray, and a gray has 0 in both color
/// channels. The edge and point filters reach one pixel to each side (0.9997 of their weight). So a step or a line
/// there has edge or point strength equal to its contrast in linear luminance.
/// </para>
/// </summary>
public sealed class FlipTests
{
    private const int Size = 32;

    /// <summary>Pixels per degree at which each pixel is large in the view: the filters reach one pixel only.</summary>
    private const double CoarseView = 10;

    [Fact]
    public void IdenticalImagesDifferNowhere()
    {
        Capture image = TestImages.Fill(Size, Size, (x, y) => ((byte)(x * 8), (byte)(y * 8), (byte)((x ^ y) * 8)));

        FlipMap map = Flip.Compare(image, image);

        map.Max.Should().Be(0);
    }

    [Fact]
    public void BlackAgainstWhiteScoresTheErrorOfALightnessStepOf100Everywhere()
    {
        // Uniform images pass the color filters unchanged and have no edges or points, so only the color term counts.
        // Black is L* 0 and white L* 100, both with a* = b* = 0, so HyAB = 100. 100^0.7 = 25.119 is above 16.510, so
        // the error is 0.95 + 0.05 · (25.119 − 16.510) / 24.766 = 0.96738.
        FlipMap map = Flip.Compare(TestImages.Gray(Size, Size, 0), TestImages.Gray(Size, Size, 255));

        map.Values.Should().AllSatisfy(value => value.Should().BeApproximately(0.96738f, 1e-3f));
    }

    [Fact]
    public void GreenAgainstBlueScoresTheLargestError()
    {
        // FLIP scales its color error by the distance of pure green from pure blue, so that pair scores 1.
        FlipMap map = Flip.Compare(TestImages.Uniform(width: Size, height: Size, r: 0, g: 255, b: 0),
            TestImages.Uniform(width: Size, height: Size, r: 0, g: 0, b: 255));

        map.Values.Should().AllSatisfy(value => value.Should().BeApproximately(1f, 1e-4f));
    }

    [Fact]
    public void TwoCloseGraysScoreOnTheLinearPartOfTheScale()
    {
        // Gray 128 is L* 53.585 and gray 118 is L* 49.637, so HyAB = 3.948 and 3.948^0.7 = 2.6150, below 16.510.
        // The error is 0.95 · 2.6150 / 16.510 = 0.15047.
        FlipMap map = Flip.Compare(TestImages.Gray(Size, Size, 128), TestImages.Gray(Size, Size, 118));

        map.Values.Should().AllSatisfy(value => value.Should().BeApproximately(0.15047f, 1e-3f));
    }

    [Fact]
    public void TheDifferenceIsTheSameInBothDirections()
    {
        Capture a = TestImages.Fill(Size, Size,
            (x, y) => ((byte)(x * 7), (byte)(200 - y * 5), (byte)(x > 10 && y > 12 ? 230 : 40)));
        Capture b = TestImages.Fill(Size, Size,
            (x, y) => ((byte)(255 - x * 3), (byte)(y * 6), (byte)(x > 14 && y > 9 ? 20 : 180)));

        FlipMap forward = Flip.Compare(a, b);
        FlipMap backward = Flip.Compare(b, a);

        forward.Max.Should().BeGreaterThan(0.5f, "the two images differ strongly somewhere");
        backward.Values.Should().Equal(forward.Values);
    }

    /// <summary>A checker of one-pixel black and white squares, and gray 188, whose linear light 0.5029 is its mean.</summary>
    private static (Capture Checker, Capture Gray) CheckerAndItsMeanGray(int size) =>
        (TestImages.Fill(size, size, (x, y) => (x + y) % 2 == 0 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255)),
            TestImages.Gray(size, size, 188));

    /// <summary>The values of the pixels at least <paramref name="margin"/> pixels from every border.</summary>
    private static IEnumerable<float> Interior(FlipMap map, int margin)
    {
        for (int y = margin; y < map.Height - margin; y++)
        for (int x = margin; x < map.Width - margin; x++)
            yield return map[x, y];
    }

    [Fact]
    public void AOnePixelCheckerLooksLikeTheGrayOfItsMeanLightAtTheDefaultViewingDistance()
    {
        // At 67 pixels per degree the achromatic filter is a Gaussian of 1.03 px, which passes 1e-4 of a one-pixel
        // checker. So the checker filters to linear light 0.5000, L* 76.069, against 76.247 for the gray: HyAB 0.177,
        // and 0.95 · 0.177^0.7 / 16.510 = 0.017. The edge and point filters (a Gaussian of 2.75 px) see no checker
        // either. The filters reach 10 pixels, so the pixels nearer the border see the border and are left out.
        (Capture checker, Capture gray) = CheckerAndItsMeanGray(64);

        FlipMap map = Flip.Compare(checker, gray);

        Interior(map, margin: 10).Should().AllSatisfy(value => value.Should().BeLessThan(0.03f));
    }

    [Fact]
    public void AOnePixelCheckerDiffersFromGrayWhenEachPixelSpansMoreOfTheView()
    {
        // At 10 pixels per degree the checker stays black and white. Black against gray 188 (L* 76.247) has the color
        // error 0.95861, and white against it 0.52842. The point filter answers the checker with strength 0.8141 in
        // each direction, 1.1514 in all, against 0 for the gray, so f = (1.1514 / √2)^0.5 = 0.90229.
        // So a black square scores 0.95861^0.09771 = 0.99588 and a white square 0.52842^0.09771 = 0.93958.
        (Capture checker, Capture gray) = CheckerAndItsMeanGray(32);

        FlipMap map = Flip.Compare(checker, gray, CoarseView);

        for (int y = 2; y < 30; y++)
        for (int x = 2; x < 30; x++)
            map[x, y].Should()
                .BeApproximately((x + y) % 2 == 0 ? 0.99588f : 0.93958f, 2e-3f, "at pixel ({0}, {1})", x, y);
    }

    [Fact]
    public void AStepEdgeScoresAboveItsColorDifferenceAtTheEdge()
    {
        // The test image turns white from column 16 on; the reference stays gray 128 (linear light 0.21586).
        // White against gray 128 has the color error 0.84455, which is the score where neither image has an edge.
        // At column 16 the step has edge strength 1 − 0.21586 = 0.78414, so f = (0.78414 / √2)^0.5 = 0.74463, and the
        // score is 0.84455^0.25537 = 0.95777. Where the colors agree the score is 0, whatever the edges.
        Capture step = TestImages.GrayWith(width: Size, height: 16, value: 128, changed: (x, _) => x >= 16,
            changedValue: 255);

        FlipMap map = Flip.Compare(TestImages.Gray(Size, 16, 128), step, CoarseView);

        for (int y = 0; y < 16; y++)
        {
            map[15, y].Should().Be(0, "the colors agree left of the step");
            map[16, y].Should().BeApproximately(0.95777f, 1e-3f, "the edge raises the score at the step");
            map[20, y].Should().BeApproximately(0.84455f, 1e-3f, "no edge is near column 20");
        }
    }

    [Fact]
    public void ALineScoresAboveItsColorDifferenceBecauseOfItsPointFeature()
    {
        // A line of gray 128 (linear light 0.21586) in column 16 of a black image. Gray 128 against black has the
        // color error 0.93389. At its center the line has point strength 0.21586 and edge strength 0, so
        // f = (0.21586 / √2)^0.5 = 0.39069, and the score is 0.93389^0.60931 = 0.95918.
        Capture line = TestImages.GrayWith(width: Size, height: 16, value: 0, changed: (x, _) => x == 16,
            changedValue: 128);

        FlipMap map = Flip.Compare(TestImages.Gray(Size, 16, 0), line, CoarseView);

        for (int y = 0; y < 16; y++)
            map[16, y].Should().BeApproximately(0.95918f, 1e-3f);
    }

    [Fact]
    public void ImagesOfDifferentSizesCannotBeCompared()
    {
        Action compare = () => Flip.Compare(TestImages.Gray(8, 8, 0), TestImages.Gray(8, 9, 0));

        compare.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheHeatMapIsBlackWithoutErrorAndBrightensAsTheErrorGrows()
    {
        float[] values = [.. Enumerable.Range(0, 11).Select(i => i / 10f)];

        Capture heatMap = new FlipMap(values.Length, 1, values).HeatMap();

        heatMap.Width.Should().Be(values.Length);
        heatMap.Height.Should().Be(1);
        heatMap.Luma(0, 0).Should().BeLessThan(0.02f, "no error draws black");
        for (int x = 1; x < values.Length; x++)
            heatMap.Luma(x, 0).Should().BeGreaterThan(heatMap.Luma(x - 1, 0),
                "an error of {0} draws brighter than one of {1}", values[x], values[x - 1]);
    }
}
