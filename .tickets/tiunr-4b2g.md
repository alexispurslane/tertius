---
id: tiunr-4b2g
status: closed
deps: []
links: [tiunr-wk29, tiunr-zoec]
created: 2026-11-07T21:44:30Z
type: task
priority: 2
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# FSR jitter makes the box clamp and anti-lag cut the uncapped reflection history


## Notes

**2026-11-07T21:44:30Z**

Measured 2026-11-07 (evidence /private/tmp/tertius-uncovered-age-20261107/report.md). With FSR 3.1 at render scale 1 the camera jitters. In a still scene, the accepted slow-history length in the plate's 80x160 reflection crop falls from a median of 90 to 13 frames (rough) and from 1,152 to 239 (smooth). Temporal noise RMS rises from 5.53 to 11.93 (rough) and from 1.00 to 13.81 W/(m² sr) (smooth). Control and light-change age behave the same.

For receivers whose rays hit the emitter, reprojection loses 0.15 to 0.18 frames per frame, and the spatial dispatch (box clamp and anti-lag) loses 0.89 (rough) and 0.82 (smooth), against 0.20 without FSR. Untested guess: jitter moves the 3x3 fast histories at the reflection's edges, so the clamp or the anti-lag fires.

All earlier noise gains of the uncapped history were measured without jitter, so this decides whether they survive in the game's configuration. The uncovered-surface starting age (tiunr-py6q) waits for this fix, because longer histories make more receivers eligible for a cut.

**2026-11-07T21:50:54Z**

2026-11-07: the user chose to diagnose before any fix. A delegated run adds instrumentation only, captures a still camera with no FSR, with FSR jitter, and with jitter or FSR separated if possible, and attributes the length loss to reprojection, clamp and anti-lag, with heat maps of where cuts happen. Hypotheses: jittered edges leave the fast box (H1), bilinear resampling blur (H2), an FSR-path reprojection error (H3), anti-lag sensitivity in dark pixels (H4), and the smooth plate's reflected-place test. No fix until the user picks one of two designs.

**2026-11-07T22:48:01Z**

Diagnosed 2026-11-07 (evidence /private/tmp/tertius-jitter-cuts-20261107/report.md, instrumentation only, uncommitted). The jitter alone cuts the histories, through the per-frame bilinear resample of the slow history (H2). Under jitter, a still pixel reprojects by this draw's jitter minus the last draw's, so every draw blurs the slow history. The blurred history moves light across the reflection's edges: less light than the 3 x 3 fast box just inside an edge, more for about 5 to 8 pixels outside it. The box clamp then moves it, and the anti-lag removes L x lag / 0.25 frames. FSR without jitter (TERTIUS_DEBUG_JITTER=none) equals no FSR, and jitter without FSR (always) equals FSR, in every total, so H3 is ruled out. With TERTIUS_DEBUG_JITTER=held (jitter kept, a still pixel reprojects onto its own texel), the crop's median length returns from 12.3 to 84.3 (rough, 90 without jitter) and from 264 to 1,152 (smooth). Hit-receiver cut share: rough 1.0% held, 5.7% FSR, 1.0% none; smooth 0.3%, 29.5%, 0.3%. So the jittered samples and fast histories (H1) add almost nothing. H4 amplifies: 82% of the smooth loss comes from lags under 2.5% of the mean. The reflected place and its hit-distance test do not drive the smooth cuts. Open: the held history's noise stays above the no-jitter values (rough 9.68 against 5.53, smooth 4.38 against 1.00), mostly at the smooth reflection's edges. Opt-in trace: TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace, byte-identical to off.

**2026-11-07T23:02:42Z**

2026-11-07: the user chose the fix 'jitter is not motion' over a tolerant clamp. A history texel stands for its whole pixel averaged over all jitter offsets, so every history reader takes its place from scene motion alone. The surface test runs on the taps of that place, silhouettes fall back to today's jitter-corrected place, and camera motion keeps the bilinear read. A sharper re-read filter (TAA practice) was rejected: TAA histories fade after about 10 frames, ours keep hundreds, so even a small per-frame blur piles up. The tolerant clamp would stop the cuts but keep the visible softening under FSR. Opt-in build TERTIUS_HISTORY_JITTER=pixel is delegated, with noise also measured on the final FSR output and the smooth reflection's edge width.

**2026-11-08T00:58:09Z**

Opt-in pixel rule built and measured 2026-11-08 (evidence /private/tmp/tertius-jitter-pixel-20261107/report.md, uncommitted). TERTIUS_HISTORY_JITTER=pixel: a history texel stands for its whole pixel, so readers take the pixel center moved by the motion target; the surface test tests those taps against the exact last point and falls back to today's place below LeastHistoryWeight; the reflected place follows; the choice rides in the motion target's alpha, which FSR does not read. The rule lives in #ifdef TERTIUS_PIXEL_HISTORY blocks compiled into 18 _pixel twins, selected only when the renderer's draws jitter. A first runtime-branch build changed the control's output even without FSR (compiler rounding, found by the identity check); the variants keep all 70 control SPIR-V modules byte-identical to the baseline text, and control captures equal the sealed jitter-cuts captures in all files. FSR still, control against pixel: crop median length rough 12.3 -> 88 (90 without jitter), smooth 264 -> 1,152; final FSR output temporal noise (linear HDR, fixed plate mask) rough 2.91 -> 2.48, smooth 1.70 -> 1.11; smooth reflection 10-90% edge width 4.0 -> 1.7 px (1.5 without FSR); plate border keeps its history (held lost it). Dimming with light age under FSR: persistent 90% after 36 instead of 65 updates (plate), 92 instead of 116 (final output). Costs: the rough plate's speckled fringe returns in the final output (spatial grain rough 4.4 -> 8.5); during the 10 deg/s pan the crop plate's cut share is 40% against 22% (cause open). Tests: 19 new; final full Rendering Checked 2,625 run 0 failed 10 skipped, Release 2,624 run 0 failed 10 skipped.

**2026-11-08T02:19:47Z**

2026-11-08: the user accepted the pixel history rule, because the pan diagnosis (tiunr-wk29) cleared it. Next: it becomes the renderer's only rule (the TERTIUS_HISTORY_JITTER switch and the _pixel variants go, the silhouette fallback stays), then full suites, coverage, mutation table, ARCHITECTURE.md and screenshots, all uncommitted. The box fix tiunr-nxtr follows.

**2026-11-08T03:17:03Z**

2026-11-08: the first acceptance run stopped when the disk filled (SQLITE_FULL in the T3 server, then mutate.py could not restore S12 in its repository copy). The worktree source was intact. Done before the stop: the switch removed, full Checked 0 failed, coverage, mutants S01 to S11 (S06 and S10 survived). With the user's permission, old /tmp scratch (14.5 GB) and the raw rasters of the paired-ray and drift runs were deleted; Nix GC freed 245 KB. The dev shell is rooted at ~/.local/state/tertius-devshell/profile. A second run finishes S12 to S22, Release, ARCHITECTURE.md and screenshots.

**2026-11-08T03:54:12Z**

2026-11-08: acceptance hardening of the pixel history rule, second run (the first stopped when the disk filled). Mutants ran through tools/mutate.py in a separate repository copy of the worktree, which was deleted after the runs. Run 1 (S01 to S11) is the first agent's; it died at S12 before it printed its table, so its rows have no seconds. Run 2 ran S06 and S12 to S22, run 3 ran S10 and S16 again after their new tests. Logs: /private/tmp/tertius-pixel-accept-20261108/mutants/run1 and mutants2/run2, run3.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S01 | src/Tertius.Rendering/Shaders/screen.glsl:44 | pixel place drops the motion | killed | PixelHistoryTests.UnderCameraMotionAPixelUnderFsrJitterReadsAtThePlaceThatItsMotionAloneGives | - |
| S02 | src/Tertius.Rendering/Shaders/screen.glsl:51 | readPlace condition inverted | killed | PixelHistoryTests.AStillSlowHistoryUnderFsrJitterGrowsAsLongAsWithoutJitter, PixelHistoryTests.UnderCameraMotion..., PixelHistoryTests.ASilhouettePixel... | - |
| S03 | src/Tertius.Rendering/Shaders/screen.glsl:51 | readPlace always the sample place (old rule) | killed | PixelHistoryTests.AStillSlowHistoryUnderFsrJitterGrowsAsLongAsWithoutJitter, PixelHistoryTests.UnderCameraMotion..., PixelHistoryTests.ASilhouettePixel... | - |
| S04 | src/Tertius.Rendering/Shaders/signals.glsl:111 | surfaceTapsInside skips the inside check | killed | SurfaceTestRenderTests.APixelWhoseLastPlaceLiesJustOutsideTheLastImageFindsNoSurface (2 cases) | - |
| S05 | src/Tertius.Rendering/Shaders/signals.glsl:129 | no silhouette fallback | killed | PixelHistoryTests.ASilhouettePixelWhoseOwnTexelFailsReadsAtTheJitterCorrectedPlace, PixelHistoryTests.EveryPixelWhoseJitterCorrectedPlaceHoldsAHistoryHoldsOne, PlaceTraceTests.APixelFallsBackExactly... | - |
| S06 | src/Tertius.Rendering/Shaders/signals.glsl:129 | fallback decided by the specular bits | killed (run 2; survived run 1) | PixelHistoryTests.AStillPixelAtASeamBetweenTwoRoughnessesOfOnePlaneReadsItsOwnHistoryTexel (new) | 57.5 |
| S07 | src/Tertius.Rendering/Shaders/signals.glsl:132 | fallback marked as the pixel place | killed | PixelHistoryTests.ASilhouettePixel..., PixelHistoryTests.EveryPixelWhoseJitterCorrectedPlace..., PlaceTraceTests.APixelFallsBackExactly... | - |
| S08 | src/Tertius.Rendering/Shaders/surface_test.comp:70 | surface test marks every pixel | killed | PixelHistoryTests.ASilhouettePixel..., PixelHistoryTests.EveryPixelWhoseJitterCorrectedPlace..., PlaceTraceTests.APixelFallsBackExactly... | - |
| S09 | src/Tertius.Rendering/Shaders/surface_test.comp:71 | mark overwrites the depth ratio | killed | UpscalingRenderTests.JitterDoesNotAppearInTheMotionOfAStillBox, PixelHistoryTests.AStillPixelUnderFsrJitterWhoseReflectedPlaceWins..., PixelHistoryTests.EveryPixelWhoseJitterCorrectedPlace... | - |
| S10 | src/Tertius.Rendering/Shaders/surface_test.comp:68 | last point rebuilt at the pixel place | killed (run 3; survived run 1) | PixelHistoryTests.AStillPixelUnderAJitterStepWiderThanThePlaneBoundKeepsItsWholeHistory (new), PixelHistoryTests.UnderAJitterStepWiderThanThePlaneBoundAStillReflectedPlaceHolds... (new) | 28.5 |
| S11 | src/Tertius.Rendering/Shaders/surface_test.comp:62 | surface test runs in a reset frame | killed | SurfaceTestRenderTests.AFrameThatResetsFindsNoSurfaceInTheLastFrame | - |
| S12 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:133 | accumulator reads the sample place | killed | PlaceTraceTests.TheTraceGivesTheSpecularWeightOfThePlaceThatThePixelRead, PixelHistoryTests.ASilhouettePixel..., PixelHistoryTests.AStillPixelAtASeam... | 59.8 |
| S13 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:155 | reflected place ignores the pixel rule | killed | PixelHistoryTests.ASilhouettePixel..., PixelHistoryTests.AStillPixelAtASeam..., PixelHistoryTests.AStillPixelUnderFsrJitterWhoseReflectedPlaceWins... | 31.9 |
| S14 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:121 | pixel reflected place: sign of the last jitter | killed | AccumulationTraceTests.UnderTheHeldJitterNoStillJitteredPixel..., PixelHistoryTests.ASilhouettePixel..., PixelHistoryTests.AStillPixelAtASeam... | 26.9 |
| S15 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:122 | pixel reflected place: sign of this jitter | killed | AccumulationTraceTests.UnderTheHeldJitterNoStillJitteredPixel..., PixelHistoryTests.AStillPixelAtASeam..., PixelHistoryTests.AStillPixelUnderFsrJitterWhoseReflectedPlaceWins... | 27.0 |
| S16 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:151 | reflected test: last point at the read place | killed (run 3; survived run 2) | PixelHistoryTests.UnderAJitterStepWiderThanThePlaneBoundAStillReflectedPlaceHoldsWhereTheSurfacePlaceHolds (new) | 23.4 |
| S17 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:142 | trace marks every pixel place read | killed | PlaceTraceTests.TheTraceGivesTheSpecularWeight..., PlaceTraceTests.TheTraceMarksEveryPixelThatReadsAtItsPixelPlace, PlaceTraceTests.APixelFallsBackExactly... | 22.5 |
| S18 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:159 | trace marks every reflected pixel place | survived | judgment call, below | 22.7 |
| S19 | src/Tertius.Rendering/Shaders/accumulate_temporal.comp:209 | trace weighs the sample place as the pixel place | killed | PlaceTraceTests.TheTraceGivesTheSpecularWeight..., PlaceTraceTests.APixelFallsBackExactly... | 28.5 |
| S20 | src/Tertius.Rendering/Shaders/signals.glsl:130 | pixel place choice returns the sample place | killed | PixelHistoryTests.ASilhouettePixel..., PixelHistoryTests.AStillPixelAtASeam..., PixelHistoryTests.AStillPixelUnderFsrJitterWhoseReflectedPlaceWins... | 39.0 |
| S21 | src/Tertius.Rendering/Shaders/accumulate_diffuse_single_temporal.comp:10 | single diffuse history reads the sample place | survived | judgment call, below | 22.9 |
| S22 | src/Tertius.Rendering/Shaders/screen_trace.glsl:331 | hit light reads the sample place | survived | judgment call, below | 45.2 |
22 rows: killed 19, timeout 0, survived 3, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Answers to the survivors of the runs:
- S06 (killed by a new test): the place choice uses the diffuse signal's surface test, not the specular one with its roughness match, so a pixel at a seam between two roughnesses on one plane keeps its pixel place. The new test puts a 0.2 and a 0.05 plate side by side.
- S10 and S16 (killed by two new tests): the surface test and the reflected test rebuild the last point through the sample place, where the last draw's sample saw it. At the test's 60 degree view a half-row jitter step slid the wrong point by only 0.9 % of the view depth, inside the 1 % plane bound. At 90 degrees it slides 1.4 %, and the tests fail on both mutants. The reflected test asserts a share above 75 % (91.2 % measured), because the hit distance test cuts about 9 % on the original code.
- S18, judgment call: the mutant changes only flag 11 of the opt-in accumulation trace (control-trace), which no image reads; AStillTriedReflectedPlaceIsItsPixelPlace pins its set case, and a test of its clear case would need the reflected pixel place's tap weights, which the trace does not record.
- S21, judgment call: the single-diffuse-history prototype is opt-in research (TERTIUS_INDIRECT_LIGHT_PROTOTYPE=1) that no GPU test runs at all, so no test of this one line exists without a first GPU harness for the prototype; ticket tiunr-zoec records the gap.
- S22, judgment call: the hit light reads a fast history once per frame and never writes back into the history that it reads, so the jittered bilinear read blurs one frame's light by under a pixel without the compounding that the rule exists to stop; a test would have to resolve that sub-pixel blur of one frame's reflected edge.

Suites after the new tests: full Checked 10,312 total, 0 failed, 24 skipped; full Release 10,299 total, 0 failed, 24 skipped (logs checked2.log, release2.log).

**2026-11-08T03:55:43Z**

2026-11-08: verified the acceptance report: checked2.log 10,312 run 0 failed, release2.log 10,299 run 0 failed; no switch left in src or tests (two historical mentions in the capture tool's request.json field and README); no commit outside T3 checkpoints. Mutants 19 killed, 3 judgment calls (S18 trace flag, S21 prototype, ticket tiunr-zoec, S22 one-frame read). Screenshots looked at: the reflections-long floor shows sparse white specks from rare bright hits (ter-12xp).

**2026-11-08T19:13:03Z**

2026-11-08, merge commit da70cd8c: the commit report asked to split TemporalSignal.Accumulate (CC 15, 46 statements), TemporalSignal.cs:390 (64 lines) with a statement wall at 459-477, VulkanRenderer.cs:134 (104 lines) and :2114 (76 lines), and the long methods of the capture tool (Program.History.cs, Program.Uncovered.cs, Program.cs). Not done in this commit. Most of Accumulate's branches and of the long renderer methods select the opt-in research prototypes (single diffuse history, reflection distance guide, instrumentation). Their removal or acceptance is the real simplification, and a split now would only move those branches. The tool methods are research capture scripts. The merge task kept the accepted behavior byte-identical and did not restructure it.
