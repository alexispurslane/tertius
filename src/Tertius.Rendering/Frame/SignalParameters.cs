namespace Tertius.Rendering;

/// <summary>Which light a signal holds: the diffuse light or the specular light. It chooses the environment's closed form
/// that a pixel without a sample and without a history takes (docs/plans/temporal-renderer.md, section 8, "The model").
/// </summary>
internal enum SignalLight
{
    /// <summary>The cosine-weighted mean of the light from every direction: its closed form is the irradiance term D(n).
    /// </summary>
    Diffuse,

    /// <summary>The light that the specular lobe gathers: its closed form is the map's specular term.</summary>
    Specular,
}

/// <summary>
/// The parameter set of one signal's accumulator (docs/plans/temporal-renderer.md, section 8, "The parameter sets"). The
/// cap is the one store of how long a signal's history can grow (decision 38). It follows a pixel's roughness: from
/// <see cref="SmoothCap"/> up to <see cref="SmoothRoughness"/>, linear to <see cref="Cap"/> at
/// <see cref="RoughRoughness"/>.
/// </summary>
/// <param name="Cap">The largest history length of a rough pixel, in frames: the slow history's weight never falls below
/// 1 / cap.</param>
/// <param name="SmoothCap">The largest history length of a smooth pixel, in frames.</param>
/// <param name="FastCap">The fast history's largest length, in frames, or the cap where the cap is lower.</param>
/// <param name="HistoryFixFrames">Below this length, the fix replaces the slow history with its neighbors' mean.</param>
/// <param name="ClampSigmas">The half width of the clamp's box, in standard deviations of the fast history.</param>
/// <param name="AntiLagShare">The clamp's move, as a share of the mean, at which the length falls to the fast cap.</param>
/// <param name="Light">The signal's light, which chooses its closed form.</param>
/// <param name="ReflectedPlaceBelow">Below this roughness, the reflected place competes and tests hit distances.
/// The surface place never does. 0 disables it.</param>
/// <param name="Confidence">The history's confidence rules: whether a retained deviation widens the clamp's box, and
/// whether its blocks are tested for evidence of a change.</param>
internal readonly record struct SignalParameters(
    int Cap,
    int SmoothCap,
    int FastCap,
    int HistoryFixFrames,
    float ClampSigmas,
    float AntiLagShare,
    SignalLight Light,
    float ReflectedPlaceBelow,
    HistoryConfidence Confidence
)
{
    /// <summary>The roughness up to which a pixel takes <see cref="SmoothCap"/>.</summary>
    public const float SmoothRoughness = 0.1f;

    /// <summary>The roughness from which a pixel takes <see cref="Cap"/>.</summary>
    public const float RoughRoughness = 0.7f;

    /// <summary>
    /// The diffuse signal's set: a cap of 32 frames at every roughness, a box widened by two retained deviations
    /// (docs/plans/bounce-light-confidence.md, section 7), and a block test whose floor is an eighth of the light, above
    /// the stationary difference that the filter's write-back makes, and below which the slow blend settles a change
    /// within 5 % in 60 draws (section 8).
    /// </summary>
    public static SignalParameters Diffuse =>
        new(
            Cap: 32,
            SmoothCap: 32,
            FastCap: 4,
            HistoryFixFrames: 4,
            ClampSigmas: 2,
            AntiLagShare: 0.5f,
            Light: SignalLight.Diffuse,
            ReflectedPlaceBelow: 0,
            Confidence: new WidenedBox(RetainedSigmas: 2, ChangeShare: 1f / 8)
        );

    /// <summary>The specular signal's set: a cap of 4 frames for a mirror and of 32 for a rough lobe, which reuses the
    /// diffuse rays, the reflected point's place below <paramref name="reflectedPlaceBelow"/>, the narrow box, whose rare
    /// bright samples would show as specks without a spatial filter, and a block test of drops whose floor is the settle
    /// tolerance, a twentieth of the light (docs/plans/bounce-light-confidence.md, section 9).</summary>
    /// <param name="reflectedPlaceBelow">The start of the hand-over band, from which a sample's hit distance belongs to a
    /// diffuse ray.</param>
    public static SignalParameters Specular(float reflectedPlaceBelow) =>
        new(
            Cap: 32,
            SmoothCap: 4,
            FastCap: 4,
            HistoryFixFrames: 4,
            ClampSigmas: 1.5f,
            AntiLagShare: 0.25f,
            Light: SignalLight.Specular,
            ReflectedPlaceBelow: reflectedPlaceBelow,
            Confidence: new DropEvidence(ChangeShare: 1f / 20)
        );
}
