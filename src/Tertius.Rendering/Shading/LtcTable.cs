namespace Tertius.Rendering;

/// <summary>
/// The inverse matrices of the linearly transformed cosines that stand for our GGX lobe, fitted by tools/Tertius.LtcFit:
/// column i at √(1 − μ) = i / (n − 1), row j at roughness j / (n − 1) (ARCHITECTURE.md, "Rectangle lights").
/// </summary>
internal sealed class LtcTable
{
    public const int Size = 64;

    /// <summary>The halves of one texel: the inverse matrix's m00, m02, m20 and m22, with m11 = 1.</summary>
    public const int Channels = 4;

    /// <summary>The name of the embedded table in this assembly.</summary>
    public const string ResourceName = "Tertius.Rendering.LtcTable.bin";

    private static readonly Lazy<LtcTable> shared = new(Load);

    private readonly Half[] texels;

    /// <summary>A table of <see cref="Size"/>² × <see cref="Channels"/> halves, laid out as the remarks say.</summary>
    /// <exception cref="ArgumentException">The table has another length.</exception>
    public LtcTable(Half[] texels)
    {
        if (texels.Length != Size * Size * Channels)
            throw new ArgumentException($"An LTC table holds {Size * Size * Channels} halves, not {texels.Length}.", nameof(texels));
        this.texels = texels;
    }

    /// <summary>The embedded table, read on first use, thread-safe.</summary>
    public static LtcTable Shared => shared.Value;

    /// <summary>The texels, as the GPU image takes them.</summary>
    public ReadOnlySpan<Half> Texels => texels;

    /// <summary>
    /// The inverse matrix at the floored <paramref name="roughness"/> and the view cosine <paramref name="nDotV"/>,
    /// bilinear between texels and clamped at the ends, as the GPU samples it.
    /// </summary>
    public LtcMatrix Sample(double roughness, double nDotV)
    {
        double u = BrdfTables.TexelCoordinate(Math.Sqrt(1 - Math.Clamp(nDotV, 0, 1)), Size);
        double row = BrdfTables.TexelCoordinate(roughness, Size);
        double Channel(int channel) =>
            BrdfTables.Bilinear(table: texels, width: Size, height: Size, channels: Channels, channel: channel, u: u, v: row);
        return new(M00: Channel(0), M02: Channel(1), M20: Channel(2), M22: Channel(3));
    }

    private static LtcTable Load()
    {
        using Stream stream =
            typeof(LtcTable).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The assembly holds no {ResourceName}.");
        var texels = new Half[Size * Size * Channels];
        stream.ReadExactly(System.Runtime.InteropServices.MemoryMarshal.AsBytes(texels.AsSpan()));
        return new(texels);
    }
}

/// <summary>
/// The inverse of a linearly transformed cosine's matrix in the frame of the view: x toward the view, z the normal. It
/// keeps y and mixes x and z. Scaled so that m11 = 1, which leaves the directions that it gives unchanged.
/// </summary>
internal readonly record struct LtcMatrix(double M00, double M02, double M20, double M22)
{
    /// <summary><paramref name="v"/> mapped into the space of the clamped cosine.</summary>
    public Double3 Transform(Double3 v) => new(M00 * v.X + M02 * v.Z, v.Y, M20 * v.X + M22 * v.Z);
}
