---
id: t0-0fhn
status: in_progress
deps: [t0-jdaq]
links: []
created: 2026-11-13T01:01:07Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Act on the rays' evidence in the accumulation, and remove the block test, the own-sample mean and the reflections' deviation images

Design: docs/plans/bounce-light-confidence.md (Design A with Design X's evidence), section 23. Branch t7-3e22-retained-deviation. Step 12, sections 8.2 to 8.6, 13, 18 and 20 (accumulator). The spatial dispatch reads its pixel's sample age (signal binding 11, new light-age variant of accumulate_spatial.comp), forms the block's evidence level by a shared atomicMax between two barriers in uniform control flow, and acts: light evidence narrows the box and cuts L to FastCap, motion evidence narrows the box only. The temporal dispatch masks the motion bit out of the light code. HistoryConfidence keeps WidenedBox(RetainedSigmas) and NarrowBox; WidenedBox needs a light age. Removes accumulate_blocks.glsl, the block sums (13), the reflections' sigma_r pair (14, 15), the own-sample mean (16, 17), DropEvidence, ChangeShare, KeepsRetainedDeviation, the push constants changeShare and blockTest, and the block test's tests. Files in section 23.


## Notes

**2026-11-13T02:22:30Z**

Built: the spatial dispatch forms each 8x8 block's evidence level (shared uint, atomicMax between two barriers in uniform control flow, evidenceOfSample the one switch), light evidence narrows the box and cuts L to FastCap, motion evidence narrows the box only; the temporal dispatch masks the motion bit; the diffuse light-age variant now covers both dispatches; WidenedBox needs a light age (ConfidenceOf). Removed accumulate_blocks.glsl, block sums (13), reflections' sigma_r pair (14, 15), own-sample mean (16, 17), DropEvidence, ChangeShare, KeepsRetainedDeviation, push constants changeShare/blockTest (40 bytes). probe.patch: block hunks and ProbeBlockSums removed, TERTIUS_A_MOTION_CUTS added; applies and builds as the survey builds it. New tests (red first): ARecentLightCodeInABlock..., ALightAgeOfSevenOrOlder..., ASampleThatSawMotion..., EvidenceStaysInsideItsBlock, BlocksWithSkyOrOffImagePixelsStillActOnTheirEvidence, AHalfDimmingWhoseRaysSeeItsLightAge..., TheMotionBitLeavesThePixelsOwnLightAgeCut, AMovingWallNarrowsTheBoxOfItsBlocksWhileItMoves, AfterTheMotionStops..., AReflectionWithARecentLightCodeIsCutToTheFastCap (2 modes), WithoutALightAgeTheBounceLightKeepsTheNarrowBox, ALampThatGoesOutCuts... and AfterALampGoesOutTheBounceLightSettles... (real rays). Full Checked green: 10,860 passed.

**2026-11-13T04:15:07Z**

Design change from t0-3kyb (coordinator, 2026-11-13): the per-pixel light-age cut before the blend in accumulate_temporal.comp went for both signals; the block's light evidence acts after the blend instead. Reason measured by t0-3kyb: young sun-strip records (0.13 % of pixels) gave their ~25x bright samples the weight 1/(age+1), biasing the bounce history up 6-11 %. The light-age variant now covers the spatial dispatch alone. New lock-in: ABrightSampleWithAYoungLightCodeTakesTheNormalWeightInItsDraw (red first: 1.19 instead of 2.5). Rewritten to the block cut: ALightChangeThatTheRaysSawMovesTheHistoryToTheFastWindow, ALightCodeWithTheMotionBitStillGivesItsBlockLightEvidence, and eight LightAgeHistoryTests (lengths now fall to FastCap 4 after the blend instead of to the age).

**2026-11-14T14:23:09Z**

Committed the removal of the cut before the blend: the light-age variant covers the spatial dispatch alone (HistoryShaderTests theory renamed ...OfTheSpatialDispatchAlone, red first with the temporal dispatch still listed); stale doc comments in TemporalSignal, DescriptorLayouts and section 8.4 now say only the spatial dispatch reads the sample age. Full Checked green: 10,897 passed (Rendering 3,186).
