---
id: ter-ox1a
status: closed
deps: []
links: []
created: 2026-10-27T11:48:52Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, lights, performance]
---
# A light's reach from the last frame's exposure, so the lights that cannot reach the view drop before the lighting pass

Follows ter-p1u7, which the user dropped on 2026-10-27 (the per-pixel share bound was slower than main: 4.19 -> 4.58 ms in lamp-hall-16, 7.44 -> 8.49 ms in demo-exit). The profile of main found that about 3.6 ms of demo-exit's lighting pass is the loop over every lamp at every pixel: a light's reach is the distance at which its irradiance falls below the cutoff, and the cutoff is set for the highest exposure the camera can reach (+10 stops), so a 10 W lamp reaches 1.46 km and touches every pixel.

The user chose (2026-10-27): set the cutoff from the exposure of the last finished frame. At an exposure of 0 stops a lamp's reach shrinks about 32 times, to about 45 m, and the renderer drops the lamps whose reach misses the view before the lighting pass. Still per pixel, no tile or cluster list, no second pass (the user's decisions on the light list, ter-p1u7). When the view darkens, the exposure rises over about a second and the reach grows with it; a light fades to 0 at the edge of its reach, so it does not pop. In a dark scene such as lamp-hall-16 it saves nothing, because the exposure is already high and every lamp shows there.

Design points for the builder to settle by the rules (stop on a fork that changes what the user sees): where the last frame's exposure comes from on the CPU (the exposure lives on the GPU; stored state describes the last finished step), the margin over that exposure, and the fade band at the edge of the reach. Portable Vulkan only.

## Acceptance Criteria

The lighting pass on demo-exit at 1080p, Release, measured on a quiet machine against main, drops by the share of the lamps whose new reach misses the view; lamp-hall-16 does not get slower. No light pops at the edge of its reach when the exposure changes (a capture series over an exposure change shows it). ARCHITECTURE.md names the rule.


## Notes

**2026-10-31T14:22:50Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 88962f00-4bb7-41be-89dd-bb3951a51fd8), run 8885be90-f0e0-442f-aa07-43c94ccb9dcf, branch ter-ox1a-light-reach-exposure from cd8b233c.

**2026-10-31T15:08:04Z**

2026-10-31, settled implementation choices (builder):
1. Where the exposure comes from: ExposureResources.HighestStops derives S_hi, an upper bound of the exposure that the frame uses or its adapt step writes, from ExposureResources.Latest (the last finished frame's reading; the seed before the first reading) and the adapt steps since, which each slot keeps when it records its frame. ExposureMeter.HighestStops raises the known exposure by each step as the adapt step's approach toward MaxStops with the time constant into the dark; under FixedExposure, S_hi is the fixed stops. The batcher takes S_hi per frame (SceneBatcher.Prepare(highestStops)), and E_cut = pi * 2^-13 / 2^S_hi. The bound holds the frame's own adapt step, so that the meter sees the lights that the next exposure can show: a metering draw after a camera cut has an endless step, so its S_hi is MaxStops and it meters as before. A bound from the reading alone would let a metering draw cut the lamps of a dark room and settle wrongly.
2. The margin over that exposure: none beyond the bound. At 60 Hz with two frames in flight, frame N is prepared after the reading of frame N-2 is collected, so the bound holds three steps of 1/60 s (frames N-2, N-1 and N) and lies 10 * (1 - e^(-0.05/3)) = 0.17 stops above the exposure at 0 stops. A light then reaches about 6 % farther than the reach of the exposure itself. (Corrected in the review fix; the first text said about 0.1 stops.) The window takes at most 1.09 E_cut of a light, about half an 8-bit step on a white matte surface.
3. The fade band: the existing window (1 - d^4/reach^4)^2 of lightIrradiance, unchanged. The reach moves continuously with the exposure, so no light pops. No shader change.

**2026-10-31T15:58:31Z**

2026-10-31, measurements (builder, M1 Max, 1920x1080, Release, base = source export of cd8b233c, builds alternated):
- demo-exit, all 3,888 frames, GPU timer, 2 runs each, load 3 to 15: lighting pass 5.87/5.88 -> 5.66/5.67 ms; frame span 17.06/17.07 -> 16.88/17.01 ms. Lights with InMainView (not the light list; see the review-fix note): 22 -> 19.6 mean; 14.6 in unbelt and 14.8 in deck (exposure near 0 stops); 20 to 22 in the other shots (+2 to +4 stops).
- demo-exit deck, frames 399 to 518, gpu-trace --no-counters, 2 traces each, load 25 to 43 (noisy): lighting pass 7.73/6.10 -> 5.57/5.35 ms; span 22.32/18.58 -> 17.76/17.26 ms. The first base trace was disturbed (p95 10.3 ms).
- lamp-hall-16, 301 frames, GPU timer, 6 runs each, load 6 to 13: lighting pass 2.65 -> 2.61 ms mean; span 10.42 -> 10.21 ms. Exposure +5.05 stops, every lamp kept: no slowdown.
- Images: the tails of demo-exit's nine shots differ from the base by at most one 8-bit level in 99.99 % of pixels. A film over chute-top's exposure rise (-0.34 to +2.45 stops, lamps in view 14 -> 22) differs from the base by more than one level in at most 0.003 % of pixels per frame: no light pops. The orchestrator repeats the timings on a quiet machine.

**2026-10-31T16:12:09Z**

2026-10-31: the first run (8885be90, old script version 88962f00) built the ticket (HEAD 858c84f9, both suites passed) but ended with output_schema. Continuation run 43735db1-af68-4151-a76d-9d28243e9162 (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) verifies the work and runs the review and fix stages on the same worktree and branch.

**2026-10-31T16:26:44Z**

2026-10-31: continuation run 43735db1 verified the work (no change) and its review stopped at fulfillment 'unverified': the demo-exit speed-up is about a third of the share of lamps cut, unexplained, and the lamp counts do not say whether they count map-only lights. Findings: the cut test checks only the exposure (medium), the acceptance gap (medium), the step store keyed by slot (low), 0.1 stops should be about 0.17 (low). Continuation run 95b02844-94b8-44ad-8baa-ed857f22e36d (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) fixes them, measures the pass's fixed cost and the two light counts, adds the invariant check, and checks the shadow tile rank.

**2026-10-31T16:48:06Z**

2026-10-31, review fix (builder, continuation 2):
1. Test gap: AStillAfterACutIntoALampLitRoomSettlesAsAFreshRendererDoes also compares the shown still's luma with a fresh renderer's still (ExposureScenes.SettledStill). Hand mutation: the loop in ExposureResources.HighestStops over the frames since the reading cut to no iterations (earlier < known.Frame). The test failed with luma 0 against 0.502 (the stops check alone passed), and passed again after the restore.
2. Step store: ExposureResources keeps the steps in a ring of FramesInFlight + 1 entries keyed by frame number (steps[frame % Length]) on both write and read. Frame N is prepared after the slot of frame N - FramesInFlight finished, so the ring holds every frame from the reading to frame N, and HighestStops and RecordReadback can run in either order. Only ExposureResources knows how a frame maps to an entry.
3. Contracts: ExposureResources lists three invariants of Latest and the ring in its remarks and checks them in AssertInvariants (the ShadowTiles pattern, Contract.Invariant, on demand). VulkanRenderer.Exposure is a test hook; LightReachRenderTests runs the check through frames in flight, captures and a cut. HighestStops, RecordReadback and Collect carry the preconditions and the postcondition.
4. Measurements, M1 Max, 1920x1080, Release, base = source export of cd8b233c, GPU lock held, uptime 35 days 23:09 to 23:19:
- Light counts per demo-exit shot (one run each, load 2.5 to 5.0). Light list = PreparedScene.LightCount = RenderStats.Lights = the direct pass's loop (frame.counts.x); InMainView = the lights that the main view keeps. Base: 22/22 in every shot. Branch list/InMainView: unbelt 15.50/14.58, deck 15.88/14.82, chute-top 20.86/20.24, chute 22/22, brake-run 22/22, floor 22/22, stack 22/22, spinward 21.86/21.77, antispinward 22/22; all frames 19.99/19.59; deck frames 399 to 518: 15.12/14.25. Map-only lights are 5.7 % of the loop in the deck window and 2.0 % over the run; each costs a pixel a buffer read and the viewKeeps test.
- Deck frames 399 to 518, gpu-trace --no-counters, 2 runs of each of 4 variants alternating base/branch, load 2.3 to 6.0: direct light mean 6.206/6.123 (base) -> 5.413/5.407 (branch) ms; without any local light (a temporary edit of both builds dropped point and spot lights in CollectLights, then reverted) 0.489/0.506 -> 0.487/0.494 ms.
- Result: the lamp-dependent part drops 5.67 -> 4.92 ms (-13 %), while the loop drops 22 -> 15.12 (-31 %). The fixed part (0.49 ms) does not explain the gap. A probe of every prepared light shows why: all 21 deck lamps are point lamps; the 10 nearest hold the 60 atlas tiles on both builds; every dropped lamp is an unshadowed lamp 68 m or more from the eye. Estimate from the sums, not a separate measurement: an unshadowed lamp costs about 0.11 ms, a shadowed one about 0.45 ms. So the pass drops by the dropped lamps' share of the cost, not by their share of the count.
5. Docs: ARCHITECTURE.md, "The reach from the exposure", names both counts, adds the fixed-part row and the split, and corrects the bound to 0.17 stops. docs/plans/local-light-shadows.md (decision 16, the E_cut row) and docs/plans/auto-exposure.md (decision 12) note that ter-ox1a superseded the constant cutoff.
6. Capture series and images (in /tmp, not committed): scripts /tmp/ox1a-scripts/exit-tails.json (the tails of demo-exit's nine shots) and /tmp/ox1a-scripts/exposure-rise.json (chute-top's exposure rise, 90 frames every 4 ticks); captures /tmp/ox1a-cmp/base-exit, base-exit-2, branch-exit, rise-base, rise-branch; heat maps /tmp/ox1a-cmp/heat/{deck-000008,floor-000009,stack-000008,unbelt-000005,unbelt-crop}.png; screenshots /tmp/tertius-ter-ox1a-shots/{unbelt,deck,chute-top-after-rise,exposure-rise-series}.png; counts /tmp/ox1a-cmp2/count-{base,branch}.txt and kinds-{base,branch}.txt; traces /tmp/ox1a-trace2/{base,branch}-{lamps,nolocal}-{1,2}.

**2026-10-31T16:48:13Z**

2026-10-31, open fork for the user (builder, continuation 2): the shadow tiles rank the lights with InMainView first, and InMainView now follows the exposure. So an exposure change can move a tile between two visible lamps while the camera stands still. Scene: an atlas of 1 layer, two equal shadow-casting spot lamps, X 30 m behind the eye aimed away, Z 60 m ahead lighting the floor in view. At an exposure where the reach is 25 m, X misses the view and Z holds the tile. One stop higher (reach 35 m) X's reach holds the eye, X ranks first by eye irradiance (4 times Z's), and Z's shadow goes off while Z's light stays on screen. Before ter-ox1a, X was always in the main view and Z never had the tile. demo-exit does not show it: its 10 shadowed lamps keep their 60 tiles on both builds.

**2026-10-31T17:13:55Z**

2026-10-31: fork settled by the user. The user chose to build the shadow tile hysteresis (ter-4xw0). The orchestrator found that the fork's example comes from the sphere test of a spot lamp's main-view flag and the direction-blind eye irradiance, filed as ter-19fm, and the hysteresis follows it. By the user's rule 'quick fix first', ter-ox1a keeps the pure ranking and merges first; ARCHITECTURE.md names the limit until the two tickets land. Fixer run 95b02844 resolved the four review findings in 22935484 and measured the deck: direct pass base 6.21/6.12 ms, branch 5.41/5.41 ms; without local lights 0.49 ms on both; loop 22 -> 15.12 lights (14.25 in the main view); the cut lamps are all far lamps without shadows, so the lamps' cost drops 13 % for 31 % of the loop (load 2.3 to 6, GPU lock, alternated). The orchestrator accepts these as the quiet repeat.

**2026-10-31T17:14:25Z**

2026-10-31: continuation run 600916c9-75eb-4d98-b153-ba1e7721a9d5 (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) adds the shadow tile limit to ARCHITECTURE.md and runs the review and fix stages on 22935484.

**2026-10-31T17:16:02Z**

2026-10-31, settled fork (user, continuation 3): the shadow tiles keep the pure ranking, as built. The main view's lights rank first, and InMainView follows the exposure, so a change of exposure can move a tile from one visible lamp to another, as a turn of the camera already can. The cause is the sphere test of a spot lamp's main-view flag (SceneBatcher.cs, InMainView = !camera.Culls(relative, reach)) and LightUnits.EyeIrradiance, which ignores a spot lamp's axis. Ticket ter-19fm tests a spot lamp's cone against the view, and ticket ter-4xw0 (after ter-19fm) builds shadow tile hysteresis. By the rule 'build and merge the quick fix first', ter-ox1a merges first and changes neither the ranking, the main-view test nor the eye irradiance. ARCHITECTURE.md, 'The reach from the exposure' and 'Local shadows' (the budget), names the limit. demo-exit shows no such change: the same lamps hold tiles on the base and the branch in all 3,889 frames (the orchestrator's count). Docs and this note only: no code change, so the test records of 22935484 hold for HEAD (code hash de4b09a3, Checked and Release, all 8 test projects).

**2026-10-31T17:44:47Z**

2026-10-31, review of continuation 3 resolved (builder, fix stage). Finding 1 (no test pinned that the bound starts from Latest): SunlitSquareWithABulbBehind now puts the bulb 10 m behind the eye. Its reach is about 23 m at the seed's 0 stops and about 0.6 m at the settled bound of -10.65 stops. Hand mutation: Known => new(Frame: Latest?.Frame ?? 0, Stops: setting.InitialStops) fails UnderTheSunABulb... with 2 lights instead of 1; the restored code passes. Finding 2: ARCHITECTURE.md, 'The reach from the exposure', gives the full example (equal shadow-casting spot lamps of at most 60 degrees, reach 25 m then 35 m one stop higher, X's eye irradiance 4 times Z's, Z holds the tile before) and says that a darker view raises the exposure. The demo-exit measurement is a paragraph with its probe and count: a temporary print of each prepared light's shadow tiles (/tmp/ox1a-cmp2/kinds-{base,branch}.txt) gives the same 10 lamps with the same tiles in all 3,889 prepared frames, numbers 0 to 3,888 (38,890 tile holders on each build, identical). The GPU timer reports 3,888 of these frames, which is the count in the cost table. Finding 3: 'The meter' now says that the clamp lets ExposureMeter.HighestStops take MaxStops as the highest target of every adapt step. Finding 4: ARCHITECTURE.md names the limit of the sum: the cutoff bounds each light alone, so n lamps past their reach can lose about n half-steps at one pixel; no bound on the sum exists, and none is built here. Finding 5: RecordSeed takes no stops and fills the buffer with setting.InitialStops, so ExposureResources alone decides the seed and the bound that starts from it. Finding 6: ExposureResources.Drew(frame, step) keeps a frame's step after SubmitAndPresent, beside EnvironmentMap.Drew and ShadowTiles.Drew; RecordReadback only copies; HighestStops and Drew require frame == NewestDrawn + 1. Gap, impostor signatures over an exposure change: a temporary print of RenderStats' impostor captures per frame finds 0 captures on both builds in demo-exit (3,889 frames), station, deep-yard and spin-station, so no shipped script shows the effect, and it stays unmeasured.

**2026-10-31T18:24:07Z**

2026-10-31, integration: the fix stage of run 600916c9 (commits e7b5f0ef and d7d71349) resolved all six findings of the continuation 3 review, ready for integration. Hand mutations, all killed: the seed written as 0 instead of setting.InitialStops (AutoExposureRenderTests.AFixedExposureNeverMovesAndTimesNoExposurePass), a kept adapt step of 0 (LightReachRenderTests.AStillAfterACutIntoALampLitRoomSettlesAsAFreshRendererDoes), and the reach's bound from the seed instead of the last reading (LightReachRenderTests.UnderTheSunABulbThatCannotLightTheViewLeavesTheLightListOnceTheExposureSettles). Coverage of the changed lines (Checked, against cd8b233c): 71/71 lines and 19/22 branches; the three partial branches are contract conditions of ExposureResources, which fail only on a bug. The merge fixed two things that the branch left: the benchmarks (ImpostorManagerBenchmarks and SceneBatcherBenchmarks) still passed the removed exposure argument and lacked highestStops, so they did not build; they now pass AutoExposure.Default.MaxStops, the reach that every light had before. And ter-6vhj's AtmosphereTests, merged first, now pass Batchers.HighestStops.
