namespace Tertius.Rendering.Tests;

/// <summary>
/// The reflection rays' blue-noise mask as native/stbn.c writes it: each cell fills the unit square evenly over a
/// period, each frame is blue over space, and nearby frames of a cell spread apart (ARCHITECTURE.md, "Randomness and
/// determinism").
/// </summary>
public sealed class BlueNoiseMaskTests
{
    /// <summary>The radial number and the angle of a cell in a frame, each a byte.</summary>
    private static (int Radial, int Angle) ValueAt(int x, int y, int frame)
    {
        int column = frame % BlueNoiseMask.TilesAcross * BlueNoiseMask.Side + x;
        int row = frame / BlueNoiseMask.TilesAcross * BlueNoiseMask.Side + y;
        int texel = 2 * (row * BlueNoiseMask.ImageSide + column);
        byte[] texels = BlueNoiseMask.Shared.Texels;
        return (texels[texel], texels[texel + 1]);
    }

    /// <summary>The variance of the radial bytes of a frame after a toroidal 5 × 5 box blur, as a share of white noise's.</summary>
    private static double BlurredVarianceShare(int frame)
    {
        const int side = BlueNoiseMask.Side;
        double mean = 0,
            variance = 0;
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
            mean += ValueAt(x, y, frame).Radial;
        mean /= side * side;
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
            variance += Math.Pow(ValueAt(x, y, frame).Radial - mean, 2);
        variance /= side * side;
        double blurred = 0;
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            double sum = 0;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                sum += ValueAt((x + dx + side) % side, (y + dy + side) % side, frame).Radial;
            blurred += Math.Pow(sum / 25 - mean, 2);
        }
        blurred /= side * side;
        // White noise keeps 1/25 of its variance through a box of 25 cells.
        return blurred / (variance / 25);
    }

    [Fact]
    public void EachCellsValuesOverOnePeriodFillEachOfTheSixtyFourStrataOfTheUnitSquareOnce()
    {
        for (int y = 0; y < BlueNoiseMask.Side; y += 7)
        for (int x = 0; x < BlueNoiseMask.Side; x += 5)
        {
            var strata = new HashSet<int>();
            for (int frame = 0; frame < BlueNoiseMask.Frames; frame++)
            {
                (int radial, int angle) = ValueAt(x, y, frame);
                strata.Add((radial >> 5) * 8 + (angle >> 5));
            }
            strata.Should().HaveCount(BlueNoiseMask.Frames, $"cell ({x}, {y}) is a stratified set of 64 points");
        }
    }

    [Fact]
    public void EachFrameKeepsUnderAThirdOfWhiteNoisesVarianceThroughAFiveByFiveBlur()
    {
        for (int frame = 0; frame < BlueNoiseMask.Frames; frame += 9)
            BlurredVarianceShare(frame)
                .Should()
                .BeLessThan(1 / 3.0, "a blue-noise frame has little power at low frequencies, and white noise keeps 1");
    }

    [Fact]
    public void ACellsMeanOverEightNeighboringFramesVariesUnderHalfAsMuchAsWhiteNoisesMean()
    {
        const int side = BlueNoiseMask.Side;
        double sumOfSquares = 0;
        int windows = 0;
        for (int y = 0; y < side; y += 3)
        for (int x = 0; x < side; x += 3)
        for (int start = 0; start < BlueNoiseMask.Frames; start += 8)
        {
            double mean = 0;
            for (int frame = start; frame < start + 8; frame++)
                mean += (ValueAt(x, y, frame).Radial + 0.5) / 256;
            sumOfSquares += Math.Pow(mean / 8 - 0.5, 2);
            windows++;
        }
        // A uniform number's variance is 1/12, and a mean of 8 white ones has 1/96.
        (sumOfSquares / windows)
            .Should()
            .BeLessThan(0.5 / 96, "nearby frames of a cell spread over the square");
    }
}
