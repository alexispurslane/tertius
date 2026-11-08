using System.IO.Compression;
using Tertius.Rendering;

namespace Tertius.ReflectionReference;

/// <summary>
/// The per-frame measurements of the accumulation trace (ticket tiunr-4b2g, <see cref="HistoryInstrumentation.ControlTrace"/>)
/// on the whole 1280 × 720 screen. It writes every trace layer of the comparison crop in every measured frame, a byte of
/// <see cref="ScreenFlag"/> per pixel of the whole screen in every measured frame (ticket tiunr-wk29), the whole trace in
/// three frames, per-pixel sums over the measured frames, and per-frame sums of each length change for the plate
/// receivers, for those whose sample hit a surface, and for the crop.
/// </summary>
internal sealed class TraceWriters(string output, ReferenceCrop crop) : IDisposable
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int Pixels = Width * Height;

    /// <summary>The fast cap and the anti-lag share of the specular signal, and the clamp's half width in deviations.</summary>
    private static readonly SignalParameters Specular = SignalParameters.Specular(0.4f);

    /// <summary>signals.glsl's LeastHistoryWeight: the least weight of passing taps that holds a history.</summary>
    private const float LeastHistoryWeight = 0.05f;

    /// <summary>The per-pixel sums over the measured frames, in the order of trace-sums.f32.</summary>
    public static readonly string[] SumNames =
    [
        "SampleFrames",
        "HitFrames",
        "ReprojectionChange",
        "ReprojectionLossFrames",
        "ReprojectionGainFrames",
        "AntiLagChange",
        "AntiLagCutFrames",
        "AntiLagFloorFrames",
        "ClampRanFrames",
        "ClampMovedFrames",
        "LagSum",
        "ReflectedWonFrames",
        "AcceptedLengthSum",
        "LastAcceptedLengthSum",
        "BoxMeanLuminanceSum",
        "RelativeHalfWidthSum",
        "WeightLostFrames",
        "TapSpreadSum",
        "SlowLuminanceSum",
        "FastLuminanceSum",
        "SampleLuminanceSum",
        "MeasuredFrames",
    ];

    private readonly string output = output;
    private readonly ReferenceCrop crop = crop;
    private readonly BinaryWriter cropWriter = new(
        new GZipStream(File.Create(Path.Combine(output, "measured-trace-crop.f32.gz")), CompressionLevel.Fastest)
    );
    private readonly Stream flagWriter = new GZipStream(
        File.Create(Path.Combine(output, "measured-trace-flags.u8.gz")),
        CompressionLevel.Fastest
    );
    private readonly byte[] flagFrame = new byte[Pixels];
    private readonly double[][] sums = [.. SumNames.Select(_ => new double[Pixels])];

    /// <summary>Measures one drawn frame, or returns null without the trace.</summary>
    /// <param name="plate">The plate mask of <see cref="Program"/>.PlateMask.</param>
    /// <param name="history">The specular slow history after the draw, four floats per pixel.</param>
    /// <param name="dumpWhole">Whether to write the whole trace of this frame.</param>
    public object? Measure(VulkanRenderer renderer, int frame, bool[] plate, float[] history, bool dumpWhole)
    {
        if (renderer.ScreenTraceSignals.Specular.AccumulationTrace(0) is not { } image)
            return null;
        float[][] layers =
        [
            .. Enumerable.Range(0, TemporalSignal.TraceLayers).Select(layer => renderer.ReadImageLayer(image, layer)),
        ];
        foreach (float[] layer in layers)
            for (int row = crop.Top; row < crop.Top + crop.Height; row++)
            for (int index = 4 * (row * Width + crop.Left); index < 4 * (row * Width + crop.Left + crop.Width); index++)
                cropWriter.Write(layer[index]);
        if (dumpWhole)
            using (
                var whole = new BinaryWriter(
                    new GZipStream(
                        File.Create(Path.Combine(output, $"trace-{frame:D4}.f32.gz")),
                        CompressionLevel.Fastest
                    )
                )
            )
                foreach (float[] layer in layers)
                foreach (float value in layer)
                    whole.Write(value);

        var populations = new Dictionary<string, Totals>
        {
            ["Plate"] = new(),
            ["PlateHit"] = new(),
            ["CropPlate"] = new(),
            ["CropPlateHit"] = new(),
        };
        for (int pixel = 0; pixel < Pixels; pixel++)
            flagFrame[pixel] = (byte)ScreenFlags(layers, history, pixel);
        flagWriter.Write(flagFrame);
        for (int pixel = 0; pixel < Pixels; pixel++)
        {
            if (((int)layers[0][4 * pixel + 3] & (1 << 9)) == 0)
                continue;
            PixelChange change = ChangeAt(layers, history, pixel);
            for (int sum = 0; sum < change.Sums.Length; sum++)
                sums[sum][pixel] += change.Sums[sum];
            if (!plate[pixel])
                continue;
            bool inCrop =
                pixel % Width >= crop.Left
                && pixel % Width < crop.Left + crop.Width
                && pixel / Width >= crop.Top
                && pixel / Width < crop.Top + crop.Height;
            populations["Plate"].Add(change);
            if (change.Hit)
                populations["PlateHit"].Add(change);
            if (inCrop)
                populations["CropPlate"].Add(change);
            if (inCrop && change.Hit)
                populations["CropPlateHit"].Add(change);
        }
        return populations;
    }

    /// <summary>What the draw did to the length at a surface pixel, and its values of the sums of <see cref="SumNames"/>.</summary>
    private static PixelChange ChangeAt(float[][] layers, float[] history, int pixel)
    {
        int flags = (int)layers[0][4 * pixel + 3];
        bool sampled = (flags & (1 << 8)) != 0;
        float lastAccepted = layers[0][4 * pixel];
        float beforeBlend = layers[0][4 * pixel + 1];
        float hit = layers[2][4 * pixel + 2];
        float before = layers[4][4 * pixel];
        float after = layers[4][4 * pixel + 1];
        bool clamped = layers[5][4 * pixel + 3] == 1;
        float lag = layers[6][4 * pixel + 3];
        float meanLuminance = Luminance(layers[6], pixel);
        float halfWidth = Specular.ClampSigmas * Luminance(layers[7], pixel);
        bool moved =
            clamped
            && Enumerable.Range(0, 3).Any(channel => layers[5][4 * pixel + channel] != history[4 * pixel + channel]);
        bool cut = after < before * (1 - 1e-6f);
        double[] values =
        [
            sampled ? 1 : 0,
            hit > 0 ? 1 : 0,
            beforeBlend - lastAccepted,
            beforeBlend - lastAccepted < -0.5f ? 1 : 0,
            beforeBlend - lastAccepted > 0.5f ? 1 : 0,
            after - before,
            cut ? 1 : 0,
            clamped && lag >= Specular.AntiLagShare * (1 - 1e-6f) ? 1 : 0,
            clamped ? 1 : 0,
            moved ? 1 : 0,
            lag,
            (flags & (1 << 4)) != 0 ? 1 : 0,
            history[4 * pixel + 3],
            lastAccepted,
            meanLuminance,
            clamped ? halfWidth / MathF.Max(meanLuminance, 1e-4f) : 0,
            layers[0][4 * pixel + 2] < 0.999f ? 1 : 0,
            layers[3][4 * pixel + 1] - layers[3][4 * pixel],
            Luminance(history, pixel),
            layers[3][4 * pixel + 2],
            layers[2][4 * pixel + 3],
            1,
        ];
        return new PixelChange(
            Sums: values,
            Hit: hit > 0,
            Reprojection: beforeBlend - lastAccepted,
            Blend: before - beforeBlend,
            AntiLag: after - before,
            Flags: (sampled ? 1 : 0) | (clamped ? 2 : 0) | (moved ? 4 : 0) | (cut ? 8 : 0),
            LastAccepted: lastAccepted,
            Accepted: history[4 * pixel + 3]
        );
    }

    /// <summary>
    /// The flags of one pixel in measured-trace-flags.u8.gz (<see cref="ScreenFlag"/>), 0 for a pixel that shows no
    /// surface.
    /// </summary>
    /// <param name="layers">The trace's layers, four floats per pixel each.</param>
    /// <param name="history">The specular slow history after the draw, four floats per pixel.</param>
    public static ScreenFlag ScreenFlags(float[][] layers, float[] history, int pixel)
    {
        int traced = (int)layers[0][4 * pixel + 3];
        if ((traced & (1 << 9)) == 0)
            return 0;
        int flags = ChangeAt(layers, history, pixel).Flags;
        return ScreenFlag.Surface
            | ((flags & 8) != 0 ? ScreenFlag.Cut : 0)
            | ((flags & 4) != 0 ? ScreenFlag.ClampMoved : 0)
            | ((traced & (1 << 10)) != 0 ? ScreenFlag.PixelPlaceRead : 0)
            | (layers[8][4 * pixel] < LeastHistoryWeight ? ScreenFlag.PixelPlaceFails : 0)
            | (layers[2][4 * pixel + 2] > 0 ? ScreenFlag.Hit : 0)
            | (layers[0][4 * pixel + 1] - layers[0][4 * pixel] < -0.5f ? ScreenFlag.ReprojectionLost : 0)
            | ((traced & (1 << 4)) != 0 ? ScreenFlag.ReflectedWon : 0);
    }

    private static float Luminance(float[] rgba, int pixel) =>
        0.2126f * rgba[4 * pixel] + 0.7152f * rgba[4 * pixel + 1] + 0.0722f * rgba[4 * pixel + 2];

    /// <summary>Writes the per-pixel sums, one float32 raster of 1280 × 720 per sum in the order of <see cref="SumNames"/>.</summary>
    public void Finish()
    {
        using var writer = new BinaryWriter(File.Create(Path.Combine(output, "trace-sums.f32")));
        foreach (double[] sum in sums)
        foreach (double value in sum)
            writer.Write((float)value);
    }

    public void Dispose()
    {
        cropWriter.Dispose();
        flagWriter.Dispose();
    }

    /// <summary>
    /// A surface pixel's length changes in one draw: by the reprojection, by the blend, which adds a frame, and by the
    /// anti-lag, whose sum is the change from the last accepted length to this one. Flags: bit 0 sampled, 1 clamped, 2 the
    /// clamp moved a channel, 3 the anti-lag cut the length.
    /// </summary>
    private readonly record struct PixelChange(
        double[] Sums,
        bool Hit,
        double Reprojection,
        double Blend,
        double AntiLag,
        int Flags,
        double LastAccepted,
        double Accepted
    );

    /// <summary>The sums of the length changes of one population in one frame (<see cref="PixelChange"/>).</summary>
    private sealed class Totals
    {
        public long Pixels { get; private set; }
        public double Reprojection { get; private set; }
        public double Blend { get; private set; }
        public double AntiLag { get; private set; }
        public long Sampled { get; private set; }
        public long Clamped { get; private set; }
        public long Moved { get; private set; }
        public long Cut { get; private set; }
        public double LastAccepted { get; private set; }
        public double Accepted { get; private set; }

        public void Add(PixelChange change)
        {
            Pixels++;
            Reprojection += change.Reprojection;
            Blend += change.Blend;
            AntiLag += change.AntiLag;
            Sampled += change.Flags & 1;
            Clamped += (change.Flags >> 1) & 1;
            Moved += (change.Flags >> 2) & 1;
            Cut += (change.Flags >> 3) & 1;
            LastAccepted += change.LastAccepted;
            Accepted += change.Accepted;
        }
    }
}

/// <summary>
/// What the trace saw at one pixel in one draw, one byte per pixel of the whole screen in measured-trace-flags.u8.gz
/// (ticket tiunr-wk29).
/// </summary>
[Flags]
internal enum ScreenFlag : byte
{
    /// <summary>The anti-lag shortened the length.</summary>
    Cut = 1 << 0,

    /// <summary>The box clamp moved a channel of the slow history.</summary>
    ClampMoved = 1 << 1,

    /// <summary>The pixel read its history at its pixel place, not at its sample place (the pixel history rule).</summary>
    PixelPlaceRead = 1 << 2,

    /// <summary>The taps of the pixel place that pass the surface test hold less than LeastHistoryWeight.</summary>
    PixelPlaceFails = 1 << 3,

    /// <summary>The sample's rays hit a surface.</summary>
    Hit = 1 << 4,

    /// <summary>The reprojection lost more than half a frame of length.</summary>
    ReprojectionLost = 1 << 5,

    /// <summary>The reflected place won.</summary>
    ReflectedWon = 1 << 6,

    /// <summary>The pixel shows a surface.</summary>
    Surface = 1 << 7,
}
