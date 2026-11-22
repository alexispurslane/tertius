---
id: t0-hfo4
status: in_progress
deps: []
links: []
created: 2026-11-12T13:56:23Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Keep the bounce light's retained deviation and widen its clamp by it, without the gate

Design: docs/plans/bounce-light-confidence.md, section 22. Branch t7-3e22-retained-deviation. Step 2a.


## Notes

**2026-11-12T14:43:37Z**

2026-11-12, step 2a built. The bounce light keeps its retained deviation σr in the alpha of the diffuse fast history: weight 1 / min(L, 32), rounding rule BlendRounding = 1/768 (accumulate_temporal.comp). The clamp's box takes max(ClampSigmas σs, RetainedSigmas σr · S / lum(S)) (accumulate_spatial.comp). HistoryConfidence is a built-in union of WidenedBox(RetainedSigmas) and NarrowBox (Frame/HistoryConfidence.cs); SignalParameters.Diffuse holds WidenedBox(2), Specular NarrowBox, and the single-history prototype takes NarrowBox. AccumulatePushConstants gains retainedSigmas (40 bytes). The gate is gone: showsChange, LeastMotion, the spatial dispatch's binding 11 and its light-age variant (ShaderVariant.LightAge lists only accumulate_temporal.comp). The light-age cut stays.
Change against the design, settled by its section 3 definition and recorded in section 6: the step of the σr update goes from the fast history F, not from S. The filter's feedback writes moved light into S, so a step from S counted it as spread; at a stable edge that held the luminance stop open (AnEdgeOfLightKeepsItsShapeBesideNoise: the dark pixel held 1.5 after 200 draws), and AYoungHistoryKeepsItsOwnRetainedDeviation read 0.82 instead of 0.943. Both green with the step from F.
Tests (AccumulatorRenderTests): ASingleBrightRayBlockSampleFadesFromTheSlowHistoryAtTheSlowBlendsPace, TheRetainedDeviationFadesWithTheSlowBlendNotTheFastOne (red: 0.099 against 0.69), TheRetainedDeviationTakesTheExposureRatio, ARejectedHistoryStartsItsRetainedDeviationAtZero, AConstantSampleKeepsARetainedDeviationOfExactlyZero. The two dimming tests now run under NarrowBox (UnderTheNarrowBox...), because a pixel's own step never leaves its widened box; step 4's block test follows such steps for bounce light. Removed: AnImageThatMovesOnScreenIsEvidenceOfAChangeForTheClamp, AChangeThatANeighborsRaysSawOnTheSameSurfaceOpensTheClamp. HistoryShaderTests: the spatial dispatch takes no light-age variant (red first). Hand mutant RetainedSigmas 0 (NarrowBox for Diffuse) killed InAStillView..., both impulse-fade tests, the quiet-pixel test and the panel at 4.95 m (0.0388 against 0.0433 ± 10 %).
