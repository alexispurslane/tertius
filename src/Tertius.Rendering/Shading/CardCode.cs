namespace Tertius.Rendering;

/// <summary>
/// A hair pixel's card code in the shading target, and the layout of the target's B and A that holds it: the C# twin of
/// cardCode and of the word helpers of Shaders/frame.glsl (docs/plans/skin-and-hair.md, "The shading target"). The code
/// is the card number mixed with the instance into the 13 spare bits, 7 above B's light mark and 6 above A's model code.
/// A pixel of another model writes 0 there and has no card. A C# reader of B or A decodes it only here.
/// </summary>
internal static class CardCode
{
    /// <summary>The bits of a code.</summary>
    public const int Bits = 13;

    private const uint Mask = (1u << Bits) - 1;

    /// <summary>
    /// The step between the codes of one card on two neighbor instances: 2^13 / φ, rounded to an odd number, so that
    /// neighbor instances lie far apart in the codes and the instances of one card take every code before one repeats.
    /// </summary>
    private const uint InstanceStep = 5063;

    /// <summary>
    /// The code of <paramref name="card"/>, a number of <see cref="MeshCards.Number"/>, on <paramref name="instance"/>,
    /// the instance's place among the frame's placed instances. The cards of one instance take distinct codes up to
    /// 2^13 cards.
    /// </summary>
    public static uint Of(uint card, uint instance) => (card + instance * InstanceStep) & Mask;

    /// <summary>
    /// The shading target's B and A of a pixel of <paramref name="model"/>, a <see cref="ShadingCode"/>: the code of a
    /// hair pixel, or 0 for another model, above the light mark and the model's code.
    /// </summary>
    public static (byte B, byte A) Channels(uint code, bool holdsLight, uint model)
    {
        uint written = model == ShadingCode.Hair ? code & Mask : 0;
        uint lightMark = holdsLight ? 1u : 0u;
        return (B: (byte)(((written & 0x7F) << 1) | lightMark), A: (byte)(((written >> 7) << 2) | model));
    }

    /// <summary>The card code that B and A hold, or null where the pixel is not hair. The twin of cardOfWords.</summary>
    public static uint? Read(byte b, byte a) =>
        ModelOf(a) == ShadingCode.Hair ? (uint)(b >> 1) | ((uint)(a >> 2) << 7) : null;

    /// <summary>The <see cref="ShadingCode"/> of the pixel's model that A holds. The twin of modelOfWord.</summary>
    public static uint ModelOf(byte a) => a & 0b11u;

    /// <summary>B's light mark: 1 where the pixel's entity holds a light, 0 otherwise. The twin of lightMarkOfWord.</summary>
    public static uint LightMarkOf(byte b) => b & 1u;
}
