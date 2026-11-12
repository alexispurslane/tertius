namespace Tertius.Rendering;

/// <summary>
/// The direct pass shades every pair of a pixel and a light in full: the light's shadow lookup and its response, at the
/// pixel (ARCHITECTURE.md, "The shaders").
/// </summary>
public readonly record struct EveryLightPair;

/// <summary>
/// The direct pass gives each pixel the full response of its key lights only, and the fill response of every other light
/// (docs/plans/light-pairs.md, "Key and fill"). The fill response of a light is its diffuse light as a point light gives
/// it, with no shine. Every light keeps its shadow lookup at the pixel, so every shadow keeps its edge. The loss of a
/// light at a screen corner is what the fill response loses of the full response there, behind the light's shadow. A
/// corner is a point of a grid of <see cref="TileSize"/> render pixels. The key lights of a corner are the at most
/// <paramref name="KeyLights"/> lights of the greatest loss, each with a key share (<see cref="KeyShare"/>), and a pixel
/// takes each light's key share from the four corners around it. A light's response at a pixel is its full response by
/// its key share and its fill response by the rest.
/// </summary>
/// <param name="KeyLights">The most key lights of a corner, from 1 to <see cref="MaxKeyLights"/>.</param>
public readonly record struct KeyAndFill(int KeyLights)
{
    /// <summary>The most key lights of a corner. KeySlots in key_fill.glsl.</summary>
    public const int MaxKeyLights = 4;

    /// <summary>
    /// The copies of the corner grid along each axis of the corner pass's target. Each copy is one group of lights, so a
    /// fragment ranks one light of every <see cref="GroupSide"/>² of the light list. On Apple's GPU the pass's time
    /// follows the length of its fragments: sixteen short fragments per corner took about half the time of one long
    /// fragment (ARCHITECTURE.md, "Key and fill"). GroupSide in key_fill.glsl.
    /// </summary>
    public const int GroupSide = 4;

    /// <summary>The side of a screen tile, in render pixels: the distance between two corners. TileSize in key_fill.glsl.</summary>
    public const int TileSize = 16;

    /// <summary>The most lights of a tile's list: the key lights of its four corners.</summary>
    public const int TileEntries = 4 * MaxKeyLights;

    /// <summary>
    /// The share of a corner's whole direct light below which a light's loss earns no key share: 1/256, under half a step
    /// of an 8-bit image at any level. NegligibleLoss in key_fill.glsl.
    /// </summary>
    public const double NegligibleLoss = 1.0 / 256;

    /// <summary>
    /// The key share of a light whose fill response loses <paramref name="loss"/> at a corner whose threshold is
    /// <paramref name="threshold"/>: 0 at the threshold or below, 1 at twice the threshold or above, and even in between.
    /// The share is continuous in every light's loss, so no light switches between key and fill at once, and it stays the
    /// same when every loss and the corner's light take one factor, such as the exposure's. keyShare in key_fill.glsl is
    /// the copy.
    /// </summary>
    public static double KeyShare(double loss, double threshold)
    {
        if (loss <= 0)
            return 0;
        return threshold <= 0 ? 1 : Math.Clamp(loss / threshold - 1, 0, 1);
    }

    /// <summary>
    /// The threshold of a corner whose lights have <paramref name="losses"/> and whose whole direct light is
    /// <paramref name="whole"/>: the greater of the loss of the light that follows the <see cref="KeyLights"/> lights of
    /// the greatest loss, 0 without one, and <see cref="NegligibleLoss"/> of the whole light.
    /// </summary>
    public double Threshold(ReadOnlySpan<double> losses, double whole)
    {
        double negligible = NegligibleLoss * whole;
        if (losses.Length <= KeyLights)
            return negligible;
        double[] sorted = losses.ToArray();
        Array.Sort(sorted);
        return Math.Max(sorted[^(KeyLights + 1)], negligible);
    }
}

/// <summary>
/// Which pairs of a pixel and a light the direct pass shades in full: <see cref="EveryLightPair"/> or
/// <see cref="KeyAndFill"/>. The default value is EveryLightPair. It is a generated union (see
/// <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(EveryLightPair), typeof(KeyAndFill), ZeroCase = typeof(EveryLightPair))]
public readonly partial struct LightPairSetting
{
    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            EveryLightPair => null,
            KeyAndFill { KeyLights: >= 1 and <= KeyAndFill.MaxKeyLights } => null,
            KeyAndFill split =>
                $"KeyAndFill.KeyLights must be 1 to {KeyAndFill.MaxKeyLights}, not {split.KeyLights}.",
        };
}
