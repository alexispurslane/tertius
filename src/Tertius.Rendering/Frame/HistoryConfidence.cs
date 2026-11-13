namespace Tertius.Rendering;

/// <summary>
/// The confidence rules of a signal's history (docs/plans/bounce-light-confidence.md, section 13): whether a retained
/// deviation widens the clamp's box. A built-in C# 15 union, so that no box widens without a deviation to widen it by. The
/// evidence that narrows a widened box again is no case: it acts on every signal whose samples have a light age, because
/// the sample age carries it (section 8). The default value holds no case.
/// </summary>
internal union HistoryConfidence(WidenedBox, NarrowBox);

/// <summary>
/// The bounce light's rules beside a light age: the retained deviation lives in the alpha of the fast history, and
/// <see cref="RetainedSigmas"/> of it widen the clamp's box where the pixel's block has no evidence of a change in this
/// draw (docs/plans/bounce-light-confidence.md, section 7).
/// </summary>
/// <param name="RetainedSigmas">The box's least half width, in retained deviations of the pixel's slow history.</param>
internal sealed record WidenedBox(float RetainedSigmas);

/// <summary>
/// The rules without a widened box: the clamp's box holds only the spread of the fast histories. The reflections keep it,
/// and so does every signal without a light age, whose box no evidence could narrow again.
/// </summary>
internal sealed record NarrowBox;
