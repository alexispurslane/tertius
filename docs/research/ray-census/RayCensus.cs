using System.Globalization;

namespace Tertius.Rendering;

/// <summary>
/// PROBE (ter-8pco, the ray census, never commit to src). With TERTIUS_PROBE_CENSUS=&lt;prefix&gt;, the renderer reads
/// the diffuse sample image back after each draw whose index, counted from 0 over every draw of the run, lies in
/// TERTIUS_PROBE_CENSUS_DRAWS="a-b" (a &lt;= draw &lt; b), and keeps running sums per render pixel in double. The resolve
/// of the probe (screen_trace_resolve.comp) writes the sample's class into its alpha, which no pass reads for the
/// diffuse signal: -1 for a pixel without a traced surface, else bin + log2(r) / 16. Bin 0 is a miss (the record's hit
/// distance is 0); a hit at distance d takes bin clamp(floor(2 log2 d) + 21, 1, 63), so bins 1 to 22 lie below 2 m
/// (class 1, a near hit) and bins 23 to 63 at or beyond it (class 2, a far hit); class 0 is a miss. r &gt;= 1 is the
/// pre-cap sample's luminance over the sample's (log2 r capped at 15). X is the sample's luminance (Rec. 709 weights),
/// as the accumulator receives it; Xpre = X * r is the sample before the caps of MostRayWeight and MaxRadiance.
/// At draw b - 1 it writes &lt;prefix&gt;.sums (float64 arrays, each width * height, row by row from the top: n,
/// sum X, sum X^2, then per class sum X_c, sum X_c * X, count; then the same for Xpre), &lt;prefix&gt;.json (sizes, the
/// draws, the exposure stops per counted draw, and the luminance of the slow diffuse history at the last draw, a
/// separate float32 file &lt;prefix&gt;.slow), and for each rectangle of TERTIUS_PROBE_CENSUS_KEEP
/// ("x0,y0,x1,y1;..." in render pixels, x1 and y1 exclusive) every sample as float32 X, float32 Xpre, byte bin, then
/// the pixel's reflection sample as float32 luminance and float32 hit distance (0 for a miss), in
/// &lt;prefix&gt;.keep&lt;k&gt;, row by row and draw by draw; the json gives the samples per draw of each rectangle.
/// </summary>
internal static class RayCensus
{
    private static readonly string? Prefix =
        Environment.GetEnvironmentVariable("TERTIUS_PROBE_CENSUS") is { Length: > 0 } prefix ? prefix : null;

    private static readonly (int First, int End) Draws = ParseDraws(
        Environment.GetEnvironmentVariable("TERTIUS_PROBE_CENSUS_DRAWS") ?? "240-640"
    );

    private static readonly (int X0, int Y0, int X1, int Y1)[] Keeps = ParseKeeps(
        Environment.GetEnvironmentVariable("TERTIUS_PROBE_CENSUS_KEEP") ?? ""
    );

    private static readonly HashSet<int> DumpAt =
    [
        .. (Environment.GetEnvironmentVariable("TERTIUS_PROBE_CENSUS_DUMP_AT") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => int.Parse(v, CultureInfo.InvariantCulture)),
    ];

    private static int draw;
    private static int width;
    private static int height;
    private static double[][]? sums; // 12 arrays for X, then 12 for Xpre
    private static readonly List<float> Stops = [];
    private static BinaryWriter[]? keepWriters;
    private static uint[] viewport = [];
    private static readonly List<int> KeepCounts = [];

    public static bool On => Prefix is not null;

    private static (int, int) ParseDraws(string text)
    {
        string[] parts = text.Split('-');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static (int, int, int, int)[] ParseKeeps(string text) =>
        [
            .. text.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(rect => rect.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray())
                .Select(v => (v[0], v[1], v[2], v[3])),
        ];

    /// <summary>Called after each submitted draw.</summary>
    public static void Observe(VulkanRenderer renderer, float? stops, uint renderWidth, uint renderHeight)
    {
        if (Prefix is null)
            return;
        int index = draw++;
        if (index < Draws.First || index >= Draws.End)
            return;
        GpuImage image = renderer.ScreenTraceSignals.Diffuse.Sample(0);
        float[] values = renderer.ReadImage(image);
        if (sums is null)
        {
            width = (int)image.Extent.Width;
            height = (int)image.Extent.Height;
            sums = [.. Enumerable.Range(0, 24).Select(_ => new double[width * height])];
            keepWriters = [.. Keeps.Select((_, k) => new BinaryWriter(File.Create($"{Prefix}.keep{k}")))];
        }
        Stops.Add(stops ?? float.NaN);
        viewport = [renderWidth, renderHeight];
        double[][] s = sums;
        Parallel.For(0, height, row =>
        {
            for (int column = 0; column < width; column++)
            {
                int pixel = row * width + column;
                float a = values[4 * pixel + 3];
                if (!(a >= 0f))
                    continue;
                int bin = (int)a;
                int kind = bin == 0 ? 0 : bin <= 22 ? 1 : 2;
                double ratio = double.Exp2((a - bin) * 16f);
                double x =
                    0.2126 * values[4 * pixel] + 0.7152 * values[4 * pixel + 1] + 0.0722 * values[4 * pixel + 2];
                Add(s, 0, pixel, kind, x);
                Add(s, 12, pixel, kind, x * ratio);
            }
        });
        float[] specular = Keeps.Length > 0 ? renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0)) : [];
        for (int k = 0; k < Keeps.Length; k++)
        {
            (int x0, int y0, int x1, int y1) = Keeps[k];
            BinaryWriter writer = keepWriters![k];
            int count = 0;
            for (int row = y0; row < y1; row++)
            for (int column = x0; column < x1; column++)
            {
                int pixel = row * width + column;
                float a = values[4 * pixel + 3];
                if (!(a >= 0f))
                    continue;
                int bin = (int)a;
                float x = 0.2126f * values[4 * pixel] + 0.7152f * values[4 * pixel + 1] + 0.0722f * values[4 * pixel + 2];
                writer.Write(x);
                writer.Write((float)(x * double.Exp2((a - bin) * 16f)));
                writer.Write((byte)bin);
                writer.Write(0.2126f * specular[4 * pixel] + 0.7152f * specular[4 * pixel + 1] + 0.0722f * specular[4 * pixel + 2]);
                writer.Write(specular[4 * pixel + 3]);
                count++;
            }
            KeepCounts.Add(count);
        }
        if (DumpAt.Contains(index))
            DumpImages(renderer, $".d{index}");
        if (index == Draws.End - 1)
            Write(renderer);
    }

    private static void Add(double[][] s, int at, int pixel, int kind, double x)
    {
        s[at][pixel] += 1;
        s[at + 1][pixel] += x;
        s[at + 2][pixel] += x * x;
        s[at + 3 + 3 * kind][pixel] += x;
        s[at + 4 + 3 * kind][pixel] += x * x;
        s[at + 5 + 3 * kind][pixel] += 1;
    }

    // The images that this draw's rays read: the hit light's two layers (the light that leaves each pixel toward a ray of
    // the kind, which the plan writes from this draw's direct light and the last draw's histories), levels 0 to 2 of the
    // light pyramid that the guided diffuse rays aim by, and this draw's diffuse and reflection samples (rgba: light and,
    // in alpha, the census code or the hit distance), as float32 files <prefix><suffix>.<name>.f32 with their sizes in
    // <prefix><suffix>.dump.json. RayCensus writes them at the last counted draw with TERTIUS_PROBE_CENSUS_DUMP=1, and at
    // each draw listed in TERTIUS_PROBE_CENSUS_DUMP_AT with the suffix ".d<draw>".
    private static void DumpImages(VulkanRenderer renderer, string suffix)
    {
        ScreenTraceImages images = renderer.ScreenTraceImages;
        var sizes = new Dictionary<string, int[]>();
        void Dump(string name, (float[] Values, int Width, int Height) level)
        {
            using var writer = new BinaryWriter(File.Create($"{Prefix}{suffix}.{name}.f32"));
            foreach (float value in level.Values)
                writer.Write(value);
            sizes[name] = [level.Width, level.Height, level.Values.Length / (level.Width * level.Height)];
        }
        Dump("hit-reflection", renderer.CensusReadLevel(images.HitLight, 0, 0));
        Dump("hit-diffuse", renderer.CensusReadLevel(images.HitLight, 1, 0));
        Dump("pyramid-0", renderer.CensusReadLevel(images.LightPyramidImages.Finest, 0, 0));
        Dump("pyramid-1", renderer.CensusReadLevel(images.LightPyramidImages.Image, 0, 0));
        Dump("pyramid-2", renderer.CensusReadLevel(images.LightPyramidImages.Image, 0, 1));
        Dump("sample-diffuse", renderer.CensusReadLevel(renderer.ScreenTraceSignals.Diffuse.Sample(0), 0, 0));
        Dump("sample-reflection", renderer.CensusReadLevel(renderer.ScreenTraceSignals.Specular.Sample(0), 0, 0));
        File.WriteAllText($"{Prefix}{suffix}.dump.json", System.Text.Json.JsonSerializer.Serialize(sizes));
    }

    private static void Write(VulkanRenderer renderer)
    {
        using (var writer = new BinaryWriter(File.Create($"{Prefix}.sums")))
            foreach (double[] array in sums!)
                foreach (double value in array)
                    writer.Write(value);
        foreach (BinaryWriter keep in keepWriters!)
            keep.Dispose();
        if (Environment.GetEnvironmentVariable("TERTIUS_PROBE_CENSUS_DUMP") == "1")
            DumpImages(renderer, "");
        float[] slow = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Slow(0));
        using (var writer = new BinaryWriter(File.Create($"{Prefix}.slow")))
            for (int pixel = 0; pixel < width * height; pixel++)
                writer.Write(0.2126f * slow[4 * pixel] + 0.7152f * slow[4 * pixel + 1] + 0.0722f * slow[4 * pixel + 2]);
        File.WriteAllText(
            $"{Prefix}.json",
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    Width = width,
                    Height = height,
                    Viewport = viewport,
                    DrawFirst = Draws.First,
                    DrawEnd = Draws.End,
                    Keeps = Keeps.Select(k => new[] { k.X0, k.Y0, k.X1, k.Y1 }),
                    Stops = Stops.Select(v => float.IsNaN(v) ? (double?)null : v),
                    Encoding = "bins",
                    KeepCounts,
                }
            )
        );
    }
}
