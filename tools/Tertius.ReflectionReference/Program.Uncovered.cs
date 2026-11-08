using System.IO.Compression;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

internal static partial class Program
{
    /// <summary>
    /// The plate's pixels on the whole screen: the probe's material field says not an emitter, and its distance field holds
    /// a surface.
    /// </summary>
    private static bool[] PlateMask(float[][] fields)
    {
        bool[] plate = new bool[fields[1].Length / 2];
        for (int pixel = 0; pixel < plate.Length; pixel++)
            plate[pixel] = fields[1][2 * pixel] < 0.5f && fields[4][2 * pixel + 1] > 0;
        return plate;
    }
}

/// <summary>
/// The per-frame measurements of uncovered surfaces (ticket tiunr-py6q) on the whole 1280 × 720 screen: the grain of the
/// plate's output in the reflection region by accepted history length, for every capture, and under
/// <see cref="HistoryInstrumentation.LightAgeDiagnostic"/> the diagnostic of every plate receiver. It writes the raw diagnostic
/// rasters and one record per plate receiver whose rays hit a never-changed surface.
/// </summary>
internal sealed class UncoveredWriters(string output) : IDisposable
{
    private const int Width = 1280;
    private const int Height = 720;

    /// <summary>The grain bins: bin b holds accepted lengths M with floor(log2 M) = b, the last bin every longer one.</summary>
    private const int GrainBins = 13;

    private static readonly ReferenceCrop Region = new(Left: 320, Top: 180, Width: 640, Height: 480);

    private readonly BinaryWriter seen = Compressed(Path.Combine(output, "measured-seen.u16.gz"));
    private readonly BinaryWriter lengths = Compressed(Path.Combine(output, "measured-length-before-blend.f32.gz"));
    private readonly BinaryWriter receivers = new(File.Create(Path.Combine(output, "uncovered-receivers.bin")));

    private static BinaryWriter Compressed(string path) =>
        new(new GZipStream(File.Create(path), CompressionLevel.Fastest));

    /// <summary>
    /// Measures one drawn frame. The grain of a pixel is its output minus the mean of its eight neighbors, for plate pixels
    /// of the reflection region whose eight neighbors are plate too, and its length is the accepted length after the draw.
    /// </summary>
    /// <param name="plate">The plate mask of <see cref="Program"/>.PlateMask.</param>
    /// <param name="output">The H13 output, two floats per pixel, pre-exposed.</param>
    /// <param name="scale">The pre-exposure, which the output divides by.</param>
    /// <param name="history">The specular slow history after the draw, four floats per pixel.</param>
    public object Measure(
        VulkanRenderer renderer,
        int frame,
        bool[] plate,
        float[] output,
        double scale,
        float[] history
    )
    {
        long[] grainCount = new long[GrainBins];
        double[] grainSquares = new double[GrainBins];
        double[] grainInverseLengths = new double[GrainBins];
        bool[] grainPopulation = new bool[Width * Height];
        for (int row = Region.Top; row < Region.Top + Region.Height; row++)
        for (int column = Region.Left; column < Region.Left + Region.Width; column++)
        {
            int pixel = row * Width + column;
            if (!plate[pixel])
                continue;
            double neighbors = 0;
            bool inside = true;
            for (int y = -1; y <= 1 && inside; y++)
            for (int x = -1; x <= 1; x++)
            {
                int neighbor = pixel + y * Width + x;
                if (neighbor == pixel)
                    continue;
                if (!plate[neighbor])
                {
                    inside = false;
                    break;
                }
                neighbors += output[2 * neighbor];
            }
            float length = history[4 * pixel + 3];
            if (!inside || !(length >= 1))
                continue;
            grainPopulation[pixel] = true;
            double grain = (output[2 * pixel] - neighbors / 8) / scale;
            int bin = Math.Min((int)Math.Floor(Math.Log2(length)), GrainBins - 1);
            grainCount[bin]++;
            grainSquares[bin] += grain * grain;
            grainInverseLengths[bin] += 1 / length;
        }
        object grains = new
        {
            Bins = "floor(log2 accepted length), last bin open",
            Pixels = grainCount,
            SquareSums = grainSquares,
            InverseLengthSums = grainInverseLengths,
        };
        if (renderer.ScreenTraceSignals.Specular.UncoveredDiagnostic(0) is not { } image)
            return new { Grain = grains };

        float[] diagnostic = renderer.ReadImage(image);
        int count = 0,
            uncovered = 0,
            firstShown = 0,
            uncoveredHoldingShare = 0;
        double lengthSum = 0;
        var lengthHistogram = new SortedDictionary<int, int>();
        int[] seenHistogram = new int[65];
        for (int pixel = 0; pixel < Width * Height; pixel++)
        {
            float frames = diagnostic[4 * pixel];
            float before = diagnostic[4 * pixel + 1];
            seen.Write((ushort)(frames < 0 ? 0 : Math.Min(frames + 1, ushort.MaxValue)));
            lengths.Write(before);
            if (!plate[pixel] || before < 0)
                continue;
            count++;
            lengthSum += before;
            int floor = (int)Math.Floor(before);
            lengthHistogram[floor] = lengthHistogram.GetValueOrDefault(floor) + 1;
            if (frames < 0)
                continue;
            bool holdsShare = diagnostic[4 * pixel + 2] > 0.5f;
            uncovered++;
            firstShown += frames == 0 ? 1 : 0;
            uncoveredHoldingShare += holdsShare ? 1 : 0;
            seenHistogram[(int)Math.Min(frames, 64)]++;
            receivers.Write((ushort)frame);
            receivers.Write(pixel);
            receivers.Write((ushort)Math.Min(frames, ushort.MaxValue));
            receivers.Write(before);
            receivers.Write(history[4 * pixel + 3]);
            receivers.Write((byte)((holdsShare ? 1 : 0) | (grainPopulation[pixel] ? 2 : 0)));
        }
        return new
        {
            Grain = grains,
            Receivers = new
            {
                PlateReceivers = count,
                LengthBeforeBlendSum = lengthSum,
                LengthBeforeBlendHistogram = lengthHistogram,
                WithSeenHit = uncovered,
                WithSeenHitHoldingCutShare = uncoveredHoldingShare,
                SeenZero = firstShown,
                SeenHistogram = seenHistogram,
            },
        };
    }

    public void Dispose()
    {
        seen.Dispose();
        lengths.Dispose();
        receivers.Dispose();
    }
}
