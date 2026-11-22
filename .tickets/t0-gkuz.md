---
id: t0-gkuz
status: in_progress
deps: []
links: []
created: 2026-11-15T08:36:14Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t0-iido
---
# Two test gaps that the frame time cuts' mutants showed: a mature pixel beside young taps in the filter's feedback step, and a history that a fourth tap carries

Found by the mutation run of the frame time cuts (t0-89zi). Three mutants survive on lines that the ticket only moved, so the gaps are older than that ticket. They belong to the hardening of Design X.


## Design (line orchestrator, 2026-11-15)

Tests only. No change under `src`.

### Gap 1: a mature pixel beside young taps in the filter's feedback step

The feedback step of the diffuse filter (`diffuse_filter_wavelet.glsl` under `TERTIUS_FILTER_FEEDBACK`) keeps two sums over a pixel's taps:

```glsl
deviationSum += share * abs(light.a);
carriedSum += share * max(light.a, 0.0);
```

where

- `light.a` = the tap's deviation from the filter's input. A young tap (a history shorter than `YoungFrames`) carries its deviation with a negative sign.
- `share` = the part of the difference between the tap and the pixel that the step moves.
- `deviationSum` = the sum that reaches the next filter step: every tap counts, by its magnitude.
- `carriedSum` = the sum that reaches the store, the fast history's retained deviation: only mature taps count.

Two mutants survive: `abs(light.a)` replaced by `light.a` (S01), and `max(light.a, 0.0)` replaced by `abs(light.a)` (S02). No test has a mature pixel with young neighbors.

The test, in `DiffuseFilterRenderTests`: one flat surface. A center region holds a history of at least `YoungFrames` draws, and the pixels around it hold a young history with a known nonzero noise. After one draw, read the filter's output deviation and the fast history's retained deviation at a mature pixel on the border. Two statements, one test each:

- The retained deviation of a mature pixel takes nothing from its young neighbors.
- The deviation that a mature pixel hands to the next filter step counts its young neighbors' noise.

### Gap 2: a history that only the fourth tap carries

`reprojectionOf(place, bits)` (`signals.glsl`) weights the four texels around a last place by the bilinear weights, each times its pass bit. The mutant "`surfacePassBits` drops tap 3" (S18) survives: no test has a history where tap 3, the texel at (+1, +1), holds weight and its value differs from the other taps.

The test, in `AccumulatorRenderTests` or beside the surface test's tests, whichever holds the fitting scene builder: a last place whose fraction is near (1, 1), so that tap 3 holds nearly all the weight, over a slow history whose texels differ, with taps 0 to 2 failing the surface test (another surface) and tap 3 passing. Statement: a pixel whose surface lies only under the fourth texel of its last place keeps that texel's history.

## Acceptance

- Each new test is red on its mutant first (`tools/mutate.py` with a list of S01, S02 and S18 by their new line numbers), and the note holds the table.
- The Checked and Release suites of the Rendering tests pass.

## Notes

**2026-11-22T10:12:14Z**

Launch (line orchestrator, 2026-11-22, about 05:45). Ticket lead: Claude Opus 5.5 at high effort, clientRequestId design-x-line-t0-gkuz-lead-1, task node:delegated-task:command%3Amcp%3A16c59c64-d6e8-44ed-9353-8ca02be2fd77%3Adelegate-task%3Adesign-x-line-t0-gkuz-lead-1, child thread thread:delegated-task:command%3Amcp%3A16c59c64-d6e8-44ed-9353-8ca02be2fd77%3Adelegate-task%3Adesign-x-line-t0-gkuz-lead-1, review base 6d8a68332, its own worktree, no cloud money. The lead finds the three mutants' lines again by their code, because the line changed since the design.

**2026-11-22T11:19:55Z**

Build (test gaps lead). Tests only, no change under src. Review base cb6dc2a93: the line's head after the orchestrator merged main into it. The lead's branch t3/diffuse-feedback-history-tests began at main's c4c1f1f2f and went forward to cb6dc2a93 by a fast-forward merge.

The mutant lines today: S01 and S02 at diffuse_filter_wavelet.glsl:72 and :73, and S18 at signals.glsl:28 (`return word & 0xFu;`). Before the new tests, all three survived (list t0-gkuz-mutants.json, 354 s): S01 and S02 against DiffuseFilterRenderTests, S18 against AccumulatorRenderTests and AccumulationTraceTests.

The tests:

- DiffuseFilterRenderTests.TheRetainedDeviationOfAMaturePixelTakesNothingFromItsYoungNeighbors and DiffuseFilterRenderTests.TheDeviationThatAMaturePixelHandsToTheNextFilterStepCountsItsYoungNeighborsNoise. For 8 draws, a box hides the right half of the wall. Then the box is gone: the left half is mature with light 1 and no noise, and the right half is young with one sample of the checker of 0 and 2. The pixel left of the center has three young taps.
- AccumulatorRenderTests.APixelWhoseSurfaceLiesOnlyUnderTheFourthTexelOfItsLastPlaceKeepsThatTexelsHistory. A box 10 m ahead has its top left corner at the center of the view. It stands 0.9 pixels right of and below that for 8 draws, then moves there. The pixel at the corner reads its last place at the fraction (0.9, 0.9). Taps 0 to 2 showed the wall 10 m behind, and tap 3 showed the box with a history of 8 draws. The pixel's length after the draw is 9.

Changes to the design, with their reasons:

1. The deviation test reads the feedback step alone. A kernel of one dispatch, made by an UnsafeAccessor to DiffuseFilterKernel's private constructor (as WorldBugOnlyTests does in the Physics tests), writes the feedback step's output into DiffuseFilter.Filtered. The full kernel hides S01: the later steps square each deviation, and with a mature deviation of 0 the mutant only flips the sign. Behind five more steps, the young pixels' own deviations also drown the one border pixel. Both tests use the one-dispatch kernel. So the first test can also make sure that the young noise reached the pixel's sums, by magnitude, which neither mutant changes.
2. The deviation test asserts a lower bound (more than 0.19), not a value. The young estimate rests on the spread over the 7 × 7 window and on the luminance stop. Only their lower bounds (more than 1, more than exp(−1/4)) are in the test. The value on this Mac is 0.367.
3. The fourth-tap test has no history whose texels differ. Taps 0 to 2 fail the surface test, so only tap 3 can carry a history, and the length (9, against 1 with no history) tells whether it did.

Mutation table (tools/mutate.py, list t0-gkuz-mutants.json, on the final test code):

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S01 | src/Tertius.Rendering/Shaders/diffuse_filter_wavelet.glsl:72 | feedback deviation sum loses the young sign's magnitude | killed | DiffuseFilterRenderTests.TheDeviationThatAMaturePixelHandsToTheNextFilterStepCountsItsYoungNeighborsNoise | 68.9 |
| S02 | src/Tertius.Rendering/Shaders/diffuse_filter_wavelet.glsl:73 | young taps carry into the store | killed | DiffuseFilterRenderTests.TheRetainedDeviationOfAMaturePixelTakesNothingFromItsYoungNeighbors | 64.3 |
| S18 | src/Tertius.Rendering/Shaders/signals.glsl:28 | surfacePassBits drops tap 3 | killed | AccumulatorRenderTests.APixelWhoseSurfaceLiesOnlyUnderTheFourthTexelOfItsLastPlaceKeepsThatTexelsHistory | 96.3 |
3 rows: killed 3, timeout 0, survived 0, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Each failure says the reason in the test name. S01: the step handed on −0.367, below the bound 0.19. S02: the retained deviation was 0.367, not 0. S18: the length was 1, not 9. Each test passes on the code as it is.

Suites: the full Checked suite passed on the final code (11,069 tests: 11,042 passed, 27 skipped, 0 failed; Rendering 3,332 passed in 17 m 39 s), then the full Release suite (11,056 tests: 11,029 passed, 27 skipped, 0 failed; Rendering 3,331 passed in 17 m 29 s).

**2026-11-22T11:48:06Z**

Fix (test gaps lead), after the one review (verdict pass on correctness, fulfillment and design, with three low findings and one gap):

- F1 (the fourth-tap test isolates tap 1 by 0.011 px under the draw-8 jitter): rejected with evidence. The draws of these tests do not jitter. VulkanRenderer.DrawsJitter jitters only under FSR, or when TERTIUS_DEBUG_JITTER asks for it (DebugJitter.Selected: the default is "upscaler"), and AccumulatorRenderTests makes no FSR renderer. So each texel shows what lies at its center, and the margin to the box's last edge is 0.4 px, not 0.011 px. The test's comment now says so.
- F2 (the deviation test's comment skips steps): fixed. The comment now gives the window (21 fast histories of 1 and 28 of the checker), the estimate 4 / √7 ≈ 1.51, the plane and normal stops of 1 on the flat wall, and the bound 0.25 · exp(−1 / 4) ≈ 0.19 for any estimate above 1.
- F3 (the static field runs the UnsafeAccessor in the type initializer): fixed. FeedbackStepAlone is a property, so a change of the constructor fails only the two tests that use it.
- The gap (measured 0.367, traced 0.32): settled with a diagnostic run that was not kept. The young taps' fast histories hold 0 and 2 and their lengths are 1, as traced. But their slow light, which the luminance stop compares, is near 1, because the accumulation's fix of a short history takes its neighbors' mean weighted by their lengths (9 on the mature side). So the stop is near 1, not 0.85, and 0.25 · 1.51 · ~0.97 ≈ 0.367. The comment now says this. The asserted bound 0.19 rests only on the light lying between 0 and 2, so it holds either way.

Mutation table on the code after the fix (tools/mutate.py, list t0-gkuz-mutants.json, 370 s):

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S01 | src/Tertius.Rendering/Shaders/diffuse_filter_wavelet.glsl:72 | feedback deviation sum loses the young sign's magnitude | killed | DiffuseFilterRenderTests.TheDeviationThatAMaturePixelHandsToTheNextFilterStepCountsItsYoungNeighborsNoise | 68.3 |
| S02 | src/Tertius.Rendering/Shaders/diffuse_filter_wavelet.glsl:73 | young taps carry into the store | killed | DiffuseFilterRenderTests.TheRetainedDeviationOfAMaturePixelTakesNothingFromItsYoungNeighbors | 61.2 |
| S18 | src/Tertius.Rendering/Shaders/signals.glsl:28 | surfacePassBits drops tap 3 | killed | AccumulatorRenderTests.APixelWhoseSurfaceLiesOnlyUnderTheFourthTexelOfItsLastPlaceKeepsThatTexelsHistory | 103.6 |
3 rows: killed 3, timeout 0, survived 0, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

The fix changes only test comments and the field into a property, so the affected checks are the Rendering tests: the full project passed in Checked (3,343 tests: 3,332 passed, 11 skipped, 0 failed, 17 m 52 s), then in Release (3,342 tests: 3,331 passed, 11 skipped, 0 failed, 18 m 22 s).
