using Tertius.ReflectionReference;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The capture tool's per-pixel byte of the accumulation trace (ticket tiunr-wk29, <see cref="ScreenFlag"/>): which of one
/// draw's events at a pixel the whole-screen raster measured-trace-flags.u8.gz records, read from the trace's layers.
/// </summary>
public sealed class TraceScreenFlagsTests
{
    // The flags of layer 0's alpha (TemporalSignal.AccumulationTrace).
    private const int ReflectedWon = 1 << 4;
    private const int HasSample = 1 << 8;
    private const int SurfacePixel = 1 << 9;
    private const int PixelPlaceRead = 1 << 10;

    [Fact]
    public void APixelThatShowsNoSurfaceHasNoFlags()
    {
        float[][] layers = Trace(flags: 0);

        TraceWriters.ScreenFlags(layers, History(), 0).Should().Be((ScreenFlag)0);
    }

    [Fact]
    public void ASurfacePixelIsMarkedAsASurface()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample);

        TraceWriters.ScreenFlags(layers, History(), 0).Should().HaveFlag(ScreenFlag.Surface);
    }

    [Fact]
    public void APixelWhoseAntiLagShortenedItsLengthIsMarkedCut()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample);
        layers[4][0] = 100;
        layers[4][1] = 60;

        TraceWriters.ScreenFlags(layers, History(), 0).Should().HaveFlag(ScreenFlag.Cut);
    }

    [Fact]
    public void APixelWhoseAntiLagKeptItsLengthIsNotMarkedCut()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample);

        TraceWriters.ScreenFlags(layers, History(), 0).Should().NotHaveFlag(ScreenFlag.Cut);
    }

    [Fact]
    public void APixelWhoseClampMovedTheSlowHistoryIsMarked()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample);
        layers[5][0] = 2;
        layers[5][3] = 1;

        TraceWriters.ScreenFlags(layers, History(), 0).Should().HaveFlag(ScreenFlag.ClampMoved);
    }

    [Fact]
    public void APixelThatReadItsPixelPlaceIsMarked()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample | PixelPlaceRead);

        TraceWriters.ScreenFlags(layers, History(), 0).Should().HaveFlag(ScreenFlag.PixelPlaceRead);
    }

    [Fact]
    public void APixelWhosePixelPlaceHoldsLessThanTheLeastWeightIsMarked()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample);
        layers[8][0] = 0.04f;

        TraceWriters.ScreenFlags(layers, History(), 0).Should().HaveFlag(ScreenFlag.PixelPlaceFails);
    }

    [Fact]
    public void APixelWhosePixelPlaceHoldsTheLeastWeightIsNotMarkedAsFailing()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample);
        layers[8][0] = 0.05f;

        TraceWriters.ScreenFlags(layers, History(), 0).Should().NotHaveFlag(ScreenFlag.PixelPlaceFails);
    }

    [Fact]
    public void APixelWhoseSampleHitASurfaceIsMarked()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample);
        layers[2][2] = 3.5f;

        TraceWriters.ScreenFlags(layers, History(), 0).Should().HaveFlag(ScreenFlag.Hit);
    }

    [Fact]
    public void APixelWhoseReprojectionLostMoreThanHalfAFrameIsMarked()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample);
        layers[0][0] = 100;
        layers[0][1] = 99.4f;

        TraceWriters.ScreenFlags(layers, History(), 0).Should().HaveFlag(ScreenFlag.ReprojectionLost);
    }

    [Fact]
    public void APixelWhoseReflectedPlaceWonIsMarked()
    {
        float[][] layers = Trace(flags: SurfacePixel | HasSample | ReflectedWon);

        TraceWriters.ScreenFlags(layers, History(), 0).Should().HaveFlag(ScreenFlag.ReflectedWon);
    }

    /// <summary>
    /// The nine layers of a one-pixel trace with layer 0's <paramref name="flags"/>: a length of 100 kept by the
    /// reprojection and the anti-lag, no clamp, a miss, and both places' whole weight.
    /// </summary>
    private static float[][] Trace(int flags)
    {
        float[][] layers = [.. Enumerable.Range(0, 9).Select(_ => new float[4])];
        layers[0] = [100, 100, 1, flags];
        layers[4] = [101, 101, 0, 0];
        layers[8] = [1, 1, 1, 1];
        return layers;
    }

    /// <summary>A one-pixel slow history of light 1 and length 101.</summary>
    private static float[] History() => [1, 1, 1, 101];
}
