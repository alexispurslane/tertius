namespace Tertius.Rendering;

/// <summary>
/// The confidence rules of a signal's history (docs/plans/bounce-light-confidence.md, section 12): whether it keeps a
/// retained deviation, and how the clamp's box takes it. A built-in C# 15 union, so that no signal widens a box without a
/// deviation to widen it by. The default value holds no case.
/// </summary>
internal union HistoryConfidence(WidenedBox, NarrowBox);

/// <summary>
/// The bounce light's rules: the retained deviation lives in the alpha of the fast history, and
/// <see cref="RetainedSigmas"/> of it widen the clamp's box (docs/plans/bounce-light-confidence.md, section 7).
/// </summary>
/// <param name="RetainedSigmas">The box's least half width, in retained deviations of the pixel's slow history.</param>
internal sealed record WidenedBox(float RetainedSigmas);

/// <summary>The rules without a retained deviation: the clamp's box holds only the spread of the fast histories.</summary>
internal sealed record NarrowBox;
