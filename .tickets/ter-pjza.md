---
id: ter-pjza
status: open
deps: []
links: []
created: 2026-11-01T02:14:19Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Occlusion passes and ambient light within their share of the 12 ms frame: 2.76 ms to 1.75 ms on demo-exit

Part 3 of docs/plans/rendering-performance.md, split out of ter-3g1g by the orchestrator on 2026-10-31, so that the largest groups of passes are cut in parallel instead of one after another. The measurement (ter-3g1g, note of 2026-10-31): demo-exit at 1920 x 1080, Release, main 87ee9caf, Apple M1 Max, load 2 to 3. Two runs without a trace gave a mean GPU span of 18.04 and 17.94 ms; a trace of every frame without counters gave 17.56 ms. Each pass's share is its own busy time per frame in that trace (the mean over all 3,889 frames), times (12 ms - 1.87 ms of gaps between passes) / 16.03 ms = 0.632.

The screen-space light's passes and the ambient pass take, in ms per frame (own time, then share): occlusion depth 0.122 and 0.077, occlusion sectors 1.578 and 0.997, occlusion denoise 0.107 and 0.067, occlusion upsample 0.310 and 0.196, ambient light 0.647 and 0.409. Together 2.763 ms against a share of 1.746 ms, 1.02 ms less. Part 2a (ter-izsu) profiled the occlusion passes, and ter-fhlb cut the ambient pass's footprint.

## Acceptance Criteria

The five passes' own busy time on demo-exit (a trace of every frame, two runs or a paired trace) is at most 1.75 ms, or the changes that keep the image are exhausted and a note gives the remainder and each candidate default change with its measured saving and captures. Every change keeps the image by the pixel comparison of decision 7. ARCHITECTURE.md gives the new cost.

## Notes

**2026-11-01T03:19:41Z**

Profile before any change (builder, 2026-10-31), step 1. Base ef3b6ae5 (main 87ee9caf plus tickets only), Release, 1920 x 1080, Apple M1 Max, every GPU run under the GPU lock.

Counter traces (tools/gpu-trace.sh with counters, 120 frames; own busy time of each pass in ms, then the top limiters and the read bandwidth):
- deck, --start 399 (load 3.94 at the start): occlusion depth 0.115 (texture cache 77 %, texture sample 77 %, 59 GB/s, compute occupancy 65 %); occlusion sectors 1.365 (ALU 72 %, last-level cache 53 %, 54 GB/s, compute occupancy 18 %); occlusion denoise 0.096 (ALU 40 %, texture filtering 39 %); occlusion upsample 0.267 (ALU 73 %, texture sample 23 %, occupancy 49 %); ambient light 0.573 (ALU 32 %, texture filtering 16 %, fragment occupancy 22 %, 22 GB/s read).
- floor, --start 2412 (load 5.79): depth 0.126, sectors 1.712 (last-level cache 66 %, ALU 64 %, 92 GB/s), denoise 0.110, upsample 0.319, ambient 0.641 (ALU 30 %).
- Whole run, ter-3g1g's trace of every frame (same code): depth 0.122, sectors 1.578, denoise 0.107, upsample 0.310, ambient 0.647.

Limiters. The sector pass is bound by its arithmetic and its last-level cache together, at a low occupancy (18 %): the steps read the depth, the geometric normal, the two depths around and the bounce source at scattered places, and their misses show where the radius spans many texels (floor). The upsample is bound by its arithmetic. The ambient pass is bound by no unit (ALU 30 to 32 %, occupancy 22 %): latency. The depth pass is bound by its strided reads of the full-resolution images (one pixel of each 2 x 2 block), and is small.

Against the design (docs/plans/screen-space-illumination.md, "Cost"): the sectors pass takes 1.37 to 1.71 ms against 0.9 to 2.8 ms expected, the upsample 0.27 to 0.32 against 1.4 to 1.5, the denoise 0.10 to 0.11 against 0.5 to 0.6, the depth pass 0.12 against 0.13, the ambient pass 0.57 to 0.65 against 0.3 to 0.7. Each is within or below its estimate; the group is over its share of the 12 ms frame (2.763 against 1.746 ms), not over its design.

Where the time goes, by probe builds (temporary shader edits, not committed): plain headless runs of the deck window (the trimmed script of the trace, --video none, about 6 s each), the probe build against a saved copy of the base build in 6 rounds of alternating order (A B, B A, ...), the sum of the five passes from the renderer's timer (a sum of neighbouring passes, which holds on MoltenVK), median of the per-round differences. A/A control (two copies of the base): +0.033 ms (range +0.008 to +0.394; the large one a slow run). Loads 1.3 to 8 during the probes.
- the whole step loop of the sector pass: -1.03 ms (no steps). So the setup of each texel and its slices, with its own surface's run, costs about 0.38 ms, and the steps about 1.04 ms.
- inside the steps: the depth read and the loop alone about 0.28 ms; the read and decode of each step's geometric normal 0.21 ms; the two depths around each step's place 0.11 ms; the rest up to the radius test about 0.13 ms; the sector runs and the bounce 0.12 ms; the map's light along the blocked directions 0.10 ms.
- the own surface's run (atan2 and its map light): 0.12 ms.
- the ambient pass's additive write to the diffuse image under the skin scatter (one more attachment loaded and stored over the whole screen, to add zeros everywhere but at skin): 0.076 ms.

Plan, largest first, each measured on its own: (1) the sector steps read one image for the depth and the geometric normal of a texel; (2) the upsample works per 2 x 2 block of pixels, which shares the texels' reads and positions; (3) the ambient pass adds the skin's ambient part only at skin pixels; then the setup of the sector pass.
