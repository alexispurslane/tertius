---
id: ter-nmbm
status: in_progress
deps: [ter-ohqr]
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# The direct signal: the accumulator, the composite, ReSTIR DI and the still rule

Build ticket 3 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, sections 7, 8, 9 ("The composite") and 10 ("The still rule"), and decisions 7, 10, 18, 28, 29 and 32. Acceptance: section 14, item 3. The user sees direct light of many lamps at a lower cost, with noise that settles over a few frames.


## Notes

**2026-11-03T15:24:48Z**

2026-11-03: the user chose the count of exact lights: the lights whose shadow slot is a cascade set (RendererOptions.CascadedLights, 1 by default and 4 at most) take the exact loop. Every other light of the main view, far lights without a set included, is a sampled light in ReSTIR DI. Decision 28 and section 7 of docs/plans/temporal-renderer.md hold the rule, and known limit 19 holds its cost.

**2026-11-03T17:53:57Z**

2026-11-03: launched as workflow run 46d28d1d-5d6f-4186-b518-019afd9643f0, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-nmbm-direct-light from 2d0ea65e8 (ter-ohqr's branch merged with main, before ter-ohqr's hardening). ter-ohqr's hardening runs at the same time as delegated task ter-ohqr-hardening-1, with one fix inside the surface test: the last view depth moves from the half-float surface history to a kept R32F device depth, so surfaces beyond 65,504 m pass.

**2026-11-03T19:37:12Z**

2026-11-03: the build of ticket 3 (branch ter-nmbm-direct-light).

Built:
- TemporalSignal, the accumulator (Frame/TemporalSignal.cs, accumulate_temporal.comp, accumulate_spatial.comp), with the direct signal's two parts, diffuse and specular, demodulated and pre-exposed. The temporal dispatch caps the reprojected histories at MaxRadiance after the exposure ratio, whose exponent it clamps to ±64.
- CompositePass in place of AmbientLightPass (composite.frag, composite_scatter.frag): the histories times their material factors, today's ambient terms with the estimate, the emission and the sky. The diffuse image now exists only under the skin scatter, and the occlusion depth pass reads the direct signal's diffuse history as the bounce source.
- ReSTIR DI: LightTable (alias table, exact lights, remap), LightReservoirs (R0, R1), the light candidates pass and the direct pass's two variants (direct_every.frag, direct_resampled.frag). The exact lights are the lights with a cascade set.
- The still rule (StillRule, FrameHistory.DrawsOf and Begin) with RendererOptions.WarmUpFrames, 32 by default; the host's --direct-light resampled|every.

Bias and noise against EveryLight, Release, 1280 x 720, after a still's 32 warm-up draws (DirectLightBiasMeasurement): the hall of lamp-hall-64 bias -0.022, history error 0.124, sample error 0.419; demo-exit's deck at frame 120 -0.107, 0.104, 0.264; the cabin of cabin-light -0.004, 0.079, 0.074. The deck's still with SpatialNeighbors = 0 measured -0.006, so the spatial reuse's biased combine causes the deck's bias. Ticket ter-t8z7 holds the design of a visibility-aware combine.

GPU time, Release, M1 Max, Metal traces without counters, 120 frames, interleaved with the base 2d0ea65e8, loads 6 to 29 (ARCHITECTURE.md, "ReSTIR DI", has the table). At 1280 x 720: hall, light candidates 0.83 to 0.85 ms + direct light 0.82 to 0.84 ms, accumulate direct 0.67 to 0.69 ms, frame span 11.7 to 13.2 ms on the base and 7.9 to 8.1 ms on the branch; deck, 1.35 + 1.79 to 1.83 ms, accumulate 1.15 ms, span 12.2 to 13.0 against 12.2 ms. At 1920 x 1080: hall 1.47 to 1.53 + 1.51 to 1.55 ms, accumulate 1.46 to 1.48 ms; deck 2.22 to 2.24 + 3.06 ms, accumulate 2.39 to 2.40 ms. Both are over the budget of 0.6 to 1.3 ms and 0.15 to 0.30 ms. Ticket ter-9nrc holds the cost.

Screenshots (1280 x 720, headless, Release):
- /private/tmp/nmbm-shots/base-lamp-hall-64-still/hall.png and after-lamp-hall-64-still/hall.png: the hall of 64 lamps. After, the floor shows grain and the pillars' shadows lose most of their contrast.
- /private/tmp/nmbm-shots/base-demo-exit-deck/deck.png and after-demo-exit-deck/deck.png: demo-exit's deck. After, the thin shadows of the glasshouse's frame on the floor nearly vanish. branch-every-deck/deck.png, the branch under --direct-light every, matches the base.
- /private/tmp/nmbm-shots/base-cabin/cabin.png and after-cabin/cabin.png: the cabin of cabin-light under the star, an exact light. Before and after look the same.

**2026-11-03T21:28:46Z**

2026-11-03: the user chose the full fix of the review's shadow fork in this ticket. The default ResampledLights(4, 2) washes out thin lamp shadows (the deck's glasshouse frame, the hall's pillars), because the spatial combine takes a neighbor's light with no visibility test at this pixel. Before the merge, ticket 3 builds the visibility-aware combine that ticket ter-t8z7 describes: Bitterli's unbiased combine, each neighbor's light weighted with its visibility at this pixel, and 1 / Z over the reservoirs whose surface sees the chosen light. The visibility tests go through lightVisibility, the one rule of every pass, so an atlas light's test ends at the shadow atlas's bounds (ShadowAtlas.Bounds, tileShadow) wherever its 5 x 5 texels agree, and only pixels near a shadow edge take the gathers. The user asked about screen tiles of 16 x 16 pixels for this; the atlas bounds already decide each pixel at a finer grain, so no screen-tile pass. The default keeps 2 neighbors. The merge waits for the combine's GPU time, its bias on the three views, new screenshots and its review. A GPU test of a scene with thin shadows under the default fails if they wash out.

**2026-11-03T22:13:22Z**

2026-11-03: the fix stage after the review.

Fixed:
- One test of a reservoir's choice, W > 0 (holdsChoice in restir.glsl), and one combine of a reused reservoir (addReservoir) at the temporal reuse, the pixel's own and the neighbors'. The temporal reuse now keeps the M of a reservoir without a choice, as the spatial reuse did (test AReservoirWhoseCandidatesFoundNoLightStillStandsForThemInTheNextFrame, red at M = 4 before the fix). Only a light gone from the list empties a reservoir. A W that is not finite makes no choice.
- LightTable samples only a light whose list index lies below NoLight (test ALightWhoseIndexInTheListDoesNotFitAReservoirIsNotSampled, red before the fix). Fill ensures that the shares add to 1 and that every exact light holds a cascade set.
- Contracts: TemporalSignal and LightReservoirs keep the targets and the surface history that they bind. Their Resize takes no argument, requires the resized surface history and ensures the targets' size. Accumulate requires its images at the targets' size. A test calls the three AssertInvariants methods after frames and after a resize.
- Coupled arguments: LightReservoirs holds its ResampledLights, so DirectLightPass, LightCandidatesPass and AddLightingPasses take the reservoirs alone. AccumulatePass reads the signal's name. FrameContext.ExposureStep is gone: ExposurePass reads the pending frame's step.
- lighting.glsl: lightPath, pathVisibility and pathLight replace unshadowedLight. The exact loop and ReSTIR's shading evaluate a light's response only where its visibility is above 0, as the base did, and ReSTIR's visibility test evaluates none.
- New tests, each seen to fail on its mutant with tools/mutate.py: ALightWithACascadeSetStaysExactBesideASampledLamp (a sampled star, no exact loop), InABoxUnderAStarWithACascadeSetTheSamplesHoldOnlyTheNoiseOfTheLamps (0.308 measured, 0.797 with the star sampled, bound 0.40), WhenTheExposureMovesBetweenTwoFramesTheHistoryTakesTheNewExposureInThatFrame (ratio 1, no previousStops write).
- Three stale comments (lighting.glsl, GpuTimingTests.cs, ExposureResources.cs).

Measured at HEAD, 1280 x 720, Release (DirectLightBiasMeasurement): with 2 neighbors the same as before to the third decimal (hall -0.022 / 0.123 / 0.419, deck -0.107 / 0.104 / 0.265, cabin -0.004 / 0.079 / 0.074). With SpatialNeighbors = 0: hall -0.031 / 0.379 / 0.471, deck -0.015 / 0.214 / 0.290, cabin -0.004 / 0.038 / 0.030 (bias / history error / sample error).

Timing by the recipe of section 11, 7 interleaved rounds of base, control and branch on time-deck and time-floor, loads 2.4 to 10.0 (runs and loads per run: /private/tmp/nmbm-timing2/runs.txt, summary.md). Span medians, base / control / branch: deck 720p 12.60 / 12.86 / 11.40, deck 1080p 15.64 / 15.61 / 17.67, floor 720p 12.07 / 12.14 / 11.05, floor 1080p 15.57 / 15.56 / 17.33. Traces of the deck with the warm-up draws taken out (/private/tmp/nmbm-timing2/traces.txt, traces.md): at 720p light candidates 1.63, direct light 1.90, accumulate direct 1.18, composite 0.29 ms against the base's direct light 2.95 to 3.32 and ambient 0.26 ms. Every unchanged pass ran 10 to 25 % faster in the branch's trace, which explains the equal 720p spans (likely the GPU's clock). ARCHITECTURE.md, "ReSTIR DI", has the tables.

Screenshots (1280 x 720, headless Release; base = 2d0ea65e8, head = this branch, sn0 = this branch with SpatialNeighbors = 0):
- /private/tmp/nmbm-shots/base-demo-exit-deck/deck.png: the deck with the glasshouse frame's lamp shadows on the floor and the left wall.
- /private/tmp/nmbm-shots2/head-demo-exit-deck/deck.png: the same view, smooth, with those shadows nearly gone.
- /private/tmp/nmbm-shots2/sn0-demo-exit-deck/deck.png: the shadows are back, under a coarse grain on every lamp-lit surface.
- /private/tmp/nmbm-shots/base-lamp-hall-64-still/hall.png: the hall with clear pillar shadows.
- /private/tmp/nmbm-shots2/head-lamp-hall-64-still/hall.png: faint pillar shadows, a little grain on the floor.
- /private/tmp/nmbm-shots2/sn0-lamp-hall-64-still/hall.png: a heavy grain over the whole floor and the pillars.
- /private/tmp/nmbm-shots/base-cabin/cabin.png and /private/tmp/nmbm-shots2/head-cabin/cabin.png: the sunlit cabin, the same in both.

Open fork for the user: the default setting (merge as is with ter-t8z7 next, SpatialNeighbors = 0, the visibility-aware combine now, or EveryLight until ter-t8z7 and ter-9nrc).

**2026-11-03T22:47:20Z**

Checks of the fix stage: the full Checked suite passed (10,124 of 10,139, 15 skipped). The full Release suite failed once on GpuTimingTests.APassThatRecordsNothingAddsNearlyNoTime... (0.060 ms against 0.05 ms), then passed (10,111 of 10,126). That test fails about one run in ten under load also on 16f48fb82, before the fix stage: ticket ter-35w5 holds the measures.

**2026-11-03T22:50:15Z**

2026-11-03: the first run (46d28d1d-5d6f-4186-b518-019afd9643f0) ended at the fork after its fix stage, head 253e5f149. The continuation that builds the visibility-aware combine is workflow run 5af508ba-4e08-4585-9c48-f5b9a8ae3478 (version 869b0b07-3413-4b06-8707-a88a91bf6e91), on the same worktree. It first merges main for the motion vectors' far-depth fix.

**2026-11-04T02:13:37Z**

2026-11-03: the continuation (workflow run 5af508ba-4e08-4585-9c48-f5b9a8ae3478) built the visibility-aware combine of ter-t8z7.

Done:
- Merge of main c6572ca3d (c11b3cbe8). The branch reads only the surface test image from the surface history, so no shader changed. Main's 11 new surface tests and one frame history test now step one tick between their frames, because a second draw of the same time is a still under this branch's still rule.
- The red test UnderTheSpatialReuseTheStripesThatOneOfTwoLampsShadowsKeepTheOthersLight (slats, ResampledLights(4, 2), bound 0.05): -0.102 on the biased combine, -0.003 on the final build.
- Bitterli's unbiased spatial combine (af4cef60d): p-hat' = the luminance of the light's shaded light at the pixel, W = sum w / (Z p-hat'(y)), Z = the M of the reservoirs whose surface can choose y. Every visibility test goes through pathVisibility. The temporal reuse keeps 1 / M, which equals 1 / Z in a still. ARCHITECTURE.md, 'ReSTIR DI', has the proof.
- The combine reuses each shaded light for the shade (1f14fadb9), walks its neighbors again for Z instead of arrays indexed at run time (1021dbe1b, half the added cost), and the random streams hash their seed (b20714c0c), because streams 0x2 and 0x3 in the low bits of one seed word gave correlated numbers: +0.46 on the deck's samples.

Bias, history error and sample error, 1280 x 720, Release (DirectLightBiasMeasurement), unbiased b20714c0c / biased c11b3cbe8 / no neighbors b20714c0c: hall -0.014 / -0.022 / -0.030, 0.123 / 0.123 / 0.379, 0.419 / 0.419 / 0.471; deck -0.047 / -0.107 / -0.014, 0.080 / 0.104 / 0.214, 0.270 / 0.265 / 0.290; cabin -0.004 / -0.004 / -0.004, 0.025 / 0.079 / 0.037, 0.030 / 0.074 / 0.030. The deck's samples outside one hot spot next to a lamp measure +0.000 (biased -0.079); the hot spot, 17 % of the deck's light, -0.18 under every spatial combine: ticket ter-x31x.

GPU time (section 11's recipe, 7 interleaved rounds, base c6572ca3d, a byte-copy control, biased c11b3cbe8, unbiased b20714c0c, loads 3.7 to 11.8): the deck's direct light 1.39 -> 2.42 ms at 1280 x 720 and 2.77 -> 4.73 ms at 1920 x 1080; its mean span 11.42 -> 12.84 and 17.85 -> 20.04 ms (base 11.67 and 15.53). Traces with the warm-up correction: direct light 1.85 to 1.87 -> 3.23 to 3.30 ms and 3.03 -> 5.15 to 5.16 ms. ter-9nrc holds the cost.

Screenshots, 1280 x 720, headless Release (/private/tmp/nmbm3/shots, base = c6572ca3d, before = c11b3cbe8, branch = b20714c0c):
- base-demo-exit-deck/deck.png and branch-demo-exit-deck/deck.png: the glasshouse frame's lamp shadows on the deck's floor are back in the branch, but softer and lower in contrast than the base's, with a sparkle of single bright pixels on the floor, the walls and the glass that neither the base nor the biased build has. before-demo-exit-deck/deck.png has the shadows washed out. (Caption corrected by the fix stage.)
- base-lamp-hall-64-still/hall.png and branch-lamp-hall-64-still/hall.png: the pillars' shadows on the floor come back only faintly, much weaker than the base's, under a bright grain. before-... shows almost none. (Caption corrected by the fix stage.)
- base-cabin/cabin.png and branch-cabin/cabin.png: the sunlit cabin, the same in both.

Measurement files: /private/tmp/nmbm3 (builds.txt names every export; runs.txt, summary.md, traces.txt, traces.md, bias/, analysis/).

**2026-11-04T02:58:29Z**

2026-11-03, the fix stage of the continuation (review: changes required, 5 low findings).

Done:
- One surface per pixel and one full-shadow test (25f1f9485): both passes build a pixel's surface with resampledSurfaceAt(pixel), from the ray through the pixel's center, and tileFullyShadowed and tileShadow's early 0 call one predicate, fullyShadowed. So the domain equality that the unbiased combine needs no longer rests on four separate pieces of code. The comment of Z and ARCHITECTURE.md now name the neighbors without a choice.
- The red run, reproduced and kept: c11b3cbe8 (the biased combine) with the slats test fails at -0.1021 against the bound 0.05 (/private/tmp/nmbm4/red.log). The final build passes it.
- DirectLightBiasMeasurement reports the sample bias. On 25f1f9485 it repeats the unbiased rows within 0.002: hall -0.0136, 0.1225, 0.4185, +0.0039, deck -0.0469, 0.0805, 0.2703, -0.0295, cabin -0.0042, 0.0252, 0.0302, +0.0000.
- The hall re-measured on the final build (ter-9nrc's note): it saves about 1.5 to 2.2 ms of the frame at 1280 x 720, not the first build's 4 ms.
- The deck's sparkle counted: 0.21 % of the pixels above 4 times their exact light (1.85 % of the light, brightest 437 times), against 0.07 % biased and 0.13 % without neighbors.
- Docs: ARCHITECTURE.md 'ReSTIR DI' (Z, the one surface rule, the sparkle, the timed-out trace at 1920 x 1080, the hall's table), section 7 of the design doc (a dated note, item 3 of 'The bias', the cost model's Z sentence), ter-abun's acceptance restored.

Screenshots of 25f1f9485 beside the base, 1280 x 720, headless Release (/private/tmp/nmbm4/shots): the deck's frame shadows are back but softer than the base's, with a sparkle of bright pixels. The hall's pillar shadows come back only faintly, under a bright grain. The cabin is the same as the base.

For the user before the merge: the unbiased combine brings the thin shadows back at a cost of about 1.0 to 2.1 ms more direct light on the deck (4.9 ms for light candidates and direct light at 1280 x 720, against a budget of 0.6 to 1.3 ms) and a new sparkle of bright single pixels.

For the hardening stage: lock in the hashed seed (for example on the deck in DirectLightBiasTests) and the domain equality (a light's p-hat' is above 0 exactly where the candidates pass can keep it, for example on the slats scene).
