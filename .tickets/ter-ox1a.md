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
2. The margin over that exposure: none beyond the bound. At 60 Hz with two frames in flight the bound lies about 0.1 stops above the exposure. The window takes at most 1.09 E_cut of a light, about half an 8-bit step on a white matte surface.
3. The fade band: the existing window (1 - d^4/reach^4)^2 of lightIrradiance, unchanged. The reach moves continuously with the exposure, so no light pops. No shader change.

**2026-10-31T15:58:31Z**

2026-10-31, measurements (builder, M1 Max, 1920x1080, Release, base = source export of cd8b233c, builds alternated):
- demo-exit, all 3,888 frames, GPU timer, 2 runs each, load 3 to 15: lighting pass 5.87/5.88 -> 5.66/5.67 ms; frame span 17.06/17.07 -> 16.88/17.01 ms. Lights that the main view keeps: 22 -> 19.6 mean; 14.6 in unbelt and 14.8 in deck (exposure near 0 stops); 20 to 22 in the other shots (+2 to +4 stops).
- demo-exit deck, frames 399 to 518, gpu-trace --no-counters, 2 traces each, load 25 to 43 (noisy): lighting pass 7.73/6.10 -> 5.57/5.35 ms; span 22.32/18.58 -> 17.76/17.26 ms. The first base trace was disturbed (p95 10.3 ms).
- lamp-hall-16, 301 frames, GPU timer, 6 runs each, load 6 to 13: lighting pass 2.65 -> 2.61 ms mean; span 10.42 -> 10.21 ms. Exposure +5.05 stops, every lamp kept: no slowdown.
- Images: the tails of demo-exit's nine shots differ from the base by at most one 8-bit level in 99.99 % of pixels. A film over chute-top's exposure rise (-0.34 to +2.45 stops, lamps in view 14 -> 22) differs from the base by more than one level in at most 0.003 % of pixels per frame: no light pops. The orchestrator repeats the timings on a quiet machine.
