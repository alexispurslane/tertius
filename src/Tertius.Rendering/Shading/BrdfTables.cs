namespace Tertius.Rendering;

/// <summary>The DFG and lobe-sky tables, computed once per process (docs/plans/pbr-brdf.md, "The tables").</summary>
/// <remarks>The texels of each axis span its range from end to end (<see cref="TexelValue"/>).</remarks>
internal sealed class BrdfTables
{
    public const int DfgSize = 32;
    public const int LobeSkyWidth = 64;
    public const int LobeSkyHeight = 32;
    public const int SamplesPerTexel = 16384;

    private static readonly Lazy<BrdfTables> shared = new(Compute);

    private readonly Half[] dfg;
    private readonly Half[] lobeSky;

    private BrdfTables(Half[] dfg, Half[] lobeSky)
    {
        this.dfg = dfg;
        this.lobeSky = lobeSky;
    }

    /// <summary>The tables of this process, computed on first use, thread-safe.</summary>
    public static BrdfTables Shared => shared.Value;

    /// <summary><see cref="DfgSize"/>² texels of two halves, (S, E₁), row by row from r = 0.</summary>
    public ReadOnlySpan<Half> Dfg => dfg;

    /// <summary><see cref="LobeSkyWidth"/> × <see cref="LobeSkyHeight"/> halves, row by row from r = 0.</summary>
    public ReadOnlySpan<Half> LobeSky => lobeSky;

    /// <summary>
    /// Integrates every texel of both tables with <see cref="SamplesPerTexel"/> samples, then rounds to halves. The
    /// rows run in parallel: each texel sums its own samples into its own slot, so the bits do not depend on the
    /// threads.
    /// </summary>
    public static BrdfTables Compute()
    {
        var dfg = new Half[2 * DfgSize * DfgSize];
        var lobeSky = new Half[LobeSkyWidth * LobeSkyHeight];
        Parallel.For(fromInclusive: 0, toExclusive: DfgSize, body: row => ComputeDfgRow(dfg, row));
        Parallel.For(fromInclusive: 0, toExclusive: LobeSkyHeight, body: row => ComputeLobeSkyRow(lobeSky, row));
        return new(dfg, lobeSky);
    }

    /// <summary>
    /// The value that texel <paramref name="index"/> of an axis of <paramref name="texelCount"/> texels holds: the
    /// first texel holds 0 and the last 1, so both ends read exact values.
    /// </summary>
    public static double TexelValue(int index, int texelCount) => (double)index / (texelCount - 1);

    /// <summary>
    /// The texture coordinate of <paramref name="value"/> from 0 to 1 on an axis of <paramref name="texelCount"/>
    /// texels, ((n − 1) x + 0.5) / n, the inverse of <see cref="TexelValue"/>. The GLSL copy uses the same formula.
    /// </summary>
    public static double TexelCoordinate(double value, int texelCount) => ((texelCount - 1) * value + 0.5) / texelCount;

    /// <summary>
    /// (S, E₁) at (<paramref name="nDotV"/>, <paramref name="roughness"/>), bilinear between texels and clamped at the
    /// ends, as the GPU samples.
    /// </summary>
    public (double SchlickPart, double LobeAlbedo) SampleDfg(double nDotV, double roughness)
    {
        double u = TexelCoordinate(nDotV, DfgSize);
        double v = TexelCoordinate(roughness, DfgSize);
        return (
            Bilinear(table: dfg, width: DfgSize, height: DfgSize, channels: 2, channel: 0, u: u, v: v),
            Bilinear(table: dfg, width: DfgSize, height: DfgSize, channels: 2, channel: 1, u: u, v: v)
        );
    }

    /// <summary>
    /// The lobe's sky share at d·up = <paramref name="cosine"/>, sampled as <see cref="SampleDfg"/> samples.
    /// </summary>
    public double SampleLobeSky(double cosine, double roughness) =>
        Bilinear(
            table: lobeSky,
            width: LobeSkyWidth,
            height: LobeSkyHeight,
            channels: 1,
            channel: 0,
            u: TexelCoordinate((cosine + 1) / 2, LobeSkyWidth),
            v: TexelCoordinate(roughness, LobeSkyHeight)
        );

    /// <summary>
    /// S and E₁ of the lobe of <paramref name="roughness"/> for the view at the cosine <paramref name="nDotV"/>.
    /// </summary>
    internal static (double SchlickPart, double LobeAlbedo) IntegrateDfg(double nDotV, double roughness, int samples)
    {
        var lobe = new GgxLobe(roughness);
        var points = new Hammersley(samples);
        var toView = new Double3(Math.Sqrt(1 - nDotV * nDotV), 0, nDotV);
        double schlickPart = 0;
        double lobeAlbedo = 0;
        // The sum runs in the order of k, so each texel is the same bits on every run.
        for (int index = 0; index < samples; index++)
        {
            (double u1, double u2) = points[index];
            Double3 halfVector = lobe.SampleHalfVector(u1: u1, u2: u2);
            double vDotH = Double3.Dot(toView, halfVector);
            double nDotL = (2 * vDotH * halfVector - toView).Z;
            if (nDotL <= 0)
                continue;
            // The integrand over the sampling density D (n·h) / (4 v·h).
            double weight = 4 * lobe.Visibility(nDotL: nDotL, nDotV: nDotV) * nDotL * vDotH / halfVector.Z;
            schlickPart += weight * Math.Pow(1 - vDotH, 5);
            lobeAlbedo += weight;
        }
        return (schlickPart / samples, lobeAlbedo / samples);
    }

    /// <summary>
    /// The share of the light that the lobe of <paramref name="roughness"/> gathers from above the horizon of an up at
    /// the cosine <paramref name="cosine"/> to the lobe's center, with v = n.
    /// </summary>
    internal static double IntegrateLobeSky(double cosine, double roughness, int samples)
    {
        var lobe = new GgxLobe(roughness);
        var points = new Hammersley(samples);
        var up = new Double3(Math.Sqrt(1 - cosine * cosine), 0, cosine);
        double total = 0;
        double above = 0;
        for (int index = 0; index < samples; index++)
        {
            (double u1, double u2) = points[index];
            Double3 halfVector = lobe.SampleHalfVector(u1: u1, u2: u2);
            Double3 toLight = 2 * halfVector.Z * halfVector - Double3.UnitZ;
            if (toLight.Z <= 0)
                continue;
            // Weighted by n·l, as Karis's prefiltering weights.
            total += toLight.Z;
            if (Double3.Dot(toLight, up) > 0)
                above += toLight.Z;
        }
        return above / total;
    }

    private static void ComputeDfgRow(Half[] dfg, int row)
    {
        for (int column = 0; column < DfgSize; column++)
        {
            (double schlickPart, double lobeAlbedo) = IntegrateDfg(
                nDotV: TexelValue(column, DfgSize),
                roughness: TexelValue(row, DfgSize),
                samples: SamplesPerTexel
            );
            int texel = row * DfgSize + column;
            dfg[2 * texel] = (Half)schlickPart;
            dfg[2 * texel + 1] = (Half)lobeAlbedo;
        }
    }

    private static void ComputeLobeSkyRow(Half[] lobeSky, int row)
    {
        for (int column = 0; column < LobeSkyWidth; column++)
        {
            double share = IntegrateLobeSky(
                cosine: 2 * TexelValue(column, LobeSkyWidth) - 1,
                roughness: TexelValue(row, LobeSkyHeight),
                samples: SamplesPerTexel
            );
            lobeSky[row * LobeSkyWidth + column] = (Half)share;
        }
    }

    /// <summary>
    /// A channel of a row-major table at the texture coordinate (u, v), filtered as a Vulkan linear sampler with clamp
    /// to edge.
    /// </summary>
    internal static double Bilinear(Half[] table, int width, int height, int channels, int channel, double u, double v)
    {
        (int left, int right, double across) = Neighbors(u, width);
        (int top, int bottom, double down) = Neighbors(v, height);
        double Texel(int column, int row) => (double)table[(row * width + column) * channels + channel];
        double upper = double.Lerp(Texel(left, top), Texel(right, top), across);
        double lower = double.Lerp(Texel(left, bottom), Texel(right, bottom), across);
        return double.Lerp(upper, lower, down);
    }

    /// <summary>
    /// The two texels around <paramref name="coordinate"/> on an axis of <paramref name="count"/> texels, and the
    /// weight of the second. Clamping the texel coordinate to the outer centers is the GPU's clamp to edge.
    /// </summary>
    private static (int First, int Second, double Weight) Neighbors(double coordinate, int count)
    {
        double texel = Math.Clamp(coordinate * count - 0.5, 0, count - 1);
        int first = (int)Math.Floor(texel);
        return (first, Math.Min(first + 1, count - 1), texel - first);
    }
}
