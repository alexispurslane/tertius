namespace Tertius.Rendering;

/// <summary>
/// The reflection rays' spatiotemporal blue-noise mask, which native/stbn.c writes (ARCHITECTURE.md, "Randomness
/// and determinism"): <see cref="Frames"/> frames of <see cref="Side"/> × <see cref="Side"/> cells, two bytes per
/// texel, a radial number and an angle.
/// </summary>
internal sealed class BlueNoiseMask
{
    /// <summary>The cells across one frame, and down it.</summary>
    public const int Side = 128;

    /// <summary>The frames of one period.</summary>
    public const int Frames = 64;

    /// <summary>The frames across the image, and down it: frame t is tile (t % 8, t / 8).</summary>
    public const int TilesAcross = 8;

    /// <summary>The image's texels across, and down.</summary>
    public const int ImageSide = Side * TilesAcross;

    private const string ResourceName = "Tertius.Rendering.BlueNoiseMask.bin";

    private static readonly Lazy<BlueNoiseMask> shared = new(Load);

    private BlueNoiseMask(byte[] texels)
    {
        Texels = texels;
    }

    public static BlueNoiseMask Shared => shared.Value;

    /// <summary>The image, two bytes per texel, rows top first.</summary>
    public byte[] Texels { get; }

    private static BlueNoiseMask Load()
    {
        using Stream stream =
            typeof(BlueNoiseMask).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The assembly holds no {ResourceName}.");
        byte[] texels = new byte[ImageSide * ImageSide * 2];
        stream.ReadExactly(texels);
        return new(texels);
    }
}
