---
id: ter-ox1a
status: in_progress
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
