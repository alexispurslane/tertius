namespace Tertius.Rendering;

/// <summary>
/// The confidence rules of a signal's history (docs/plans/bounce-light-confidence.md, section 12): whether it keeps a
/// retained deviation, how the clamp's box takes it, and whether its blocks are tested for evidence of a change. A built-in
/// C# 15 union, so that no signal widens a box or tests blocks without a deviation to widen it by or to pool. The default
/// value holds no case.
/// </summary>
internal union HistoryConfidence(WidenedBox, DropEvidence, NarrowBox);

/// <summary>
/// The bounce light's rules: the retained deviation lives in the alpha of the fast history, and
/// <see cref="RetainedSigmas"/> of it widen the clamp's box (docs/plans/bounce-light-confidence.md, section 7). The block
/// test counts drops and rises of the light past <see cref="ChangeShare"/> as evidence of a change (section 8).
/// </summary>
/// <param name="RetainedSigmas">The box's least half width, in retained deviations of the pixel's slow history.</param>
/// <param name="ChangeShare">The block test's floor: the least difference of a window's fast and slow light that is
/// evidence, as a share of the larger of the two.</param>
internal sealed record WidenedBox(float RetainedSigmas, float ChangeShare);

/// <summary>
/// The rules of reflections with a precise history: the retained deviation lives in an image pair of its own, beside the
/// hit distance in the fast history's alpha, and the box stays narrow. So the slow history lies low by the rare samples
/// that the box cuts, and the block test counts only drops (docs/plans/bounce-light-confidence.md, section 9).
/// </summary>
/// <param name="ChangeShare">The block test's floor: the least drop of a window's slow light below its fast light that is
/// evidence, as a share of the slow light.</param>
internal sealed record DropEvidence(float ChangeShare);

/// <summary>
/// The rules without a retained deviation: the clamp's box holds only the spread of the fast histories, and no block test
/// runs.
/// </summary>
internal sealed record NarrowBox;
