---
id: ter-rjts
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
# Depth pyramid and reflections within their share of the 12 ms frame: 4.09 ms to 2.59 ms on demo-exit

Part 3 of docs/plans/rendering-performance.md, split out of ter-3g1g by the orchestrator on 2026-10-31, so that the largest groups of passes are cut in parallel instead of one after another. The measurement (ter-3g1g, note of 2026-10-31): demo-exit at 1920 x 1080, Release, main 87ee9caf, Apple M1 Max, load 2 to 3. Two runs without a trace gave a mean GPU span of 18.04 and 17.94 ms; a trace of every frame without counters gave 17.56 ms. Each pass's share is its own busy time per frame in that trace (the mean over all 3,889 frames), times (12 ms - 1.87 ms of gaps between passes) / 16.03 ms = 0.632.

The four passes of the reflections take, in ms per frame (own time, then share): depth pyramid 0.159 and 0.100, radiance pyramid 0.621 and 0.392, reflection trace 1.390 and 0.879, reflections 1.920 and 1.214. Together 4.090 ms against a share of 2.585 ms, 1.51 ms less. Part 2b (ter-ff6y, 2026-10-26) brought the radiance pyramid, the trace and the reflections to 3.27 ms in two traces; they now take 3.93 ms. ter-r5hb's 256 steps added about 0.14 ms; the rest of the growth is unexplained. The user decided on 2026-10-26 that the half-resolution trace stays the default.

## Acceptance Criteria

The four passes' own busy time on demo-exit (a trace of every frame, two runs or a paired trace) is at most 2.59 ms, or the changes that keep the image are exhausted and a note gives the remainder and each candidate default change with its measured saving and captures. A note explains the growth from 3.27 ms. Every change keeps the image by the pixel comparison of decision 7. ARCHITECTURE.md gives the new cost.

## Notes

**2026-11-01T04:07:35Z**

Profile (builder, 2026-11-01). Base ef3b6ae5 (the code of main 87ee9caf), demo-exit's deck window (120 frames from 399), 1920 x 1080, Release, Apple M1 Max, every trace under the GPU lock. Times are the trace's own busy time per frame in ms, not the timer's.

Limiters, one trace with counters (tools/gpu-trace.sh, load 2.38 at the start), busy mean / top two limiters / read and write GB/s:
- depth pyramid 0.144 / texture cache 29 %, texture sample 28 % / 19.5, 13.8. About 2.8 MB read and 2.0 MB written per frame, against the design's 8.3 MB and 5.5 MB: the caches serve most reads. Over its share by 0.04 to 0.05 ms.
- radiance pyramid 0.565 / last-level cache 38 %, texture cache 36 % / 25.6, 27.0. About 14.5 MB read and 15.3 MB written, against the design's 24.9 MB and 22.1 MB. Bound by the latency of the 13-tap reads, not by DRAM bandwidth. ff6y's two cheaper builds of it changed SMAA edges by 9 levels, so no build of it is tried here.
- reflection trace 1.310 / ALU 85 %, texture sample 10 % / 6.7, 1.2. The loop of up to 256 steps (ter-r5hb).
- reflections (the upsample) 2.196 / ALU 61 %, last-level cache 12 % / 7.7, 1.8.
Four base traces without counters (loads 7.90, 6.52, 4.99 and 6.15 at the start, 5.38 to 7.33 at the end): depth pyramid 0.142 to 0.146, radiance pyramid 0.563 to 0.572, reflection trace 1.292 to 1.318, reflections 2.168 to 2.212; the four passes 4.15 to 4.24.

Why the passes grew from ter-ff6y's 3.27 ms. The brief compares unlike numbers: ff6y's 3.27 ms is the deck window and holds all four passes, the depth pyramid included; ter-3g1g's 3.93 ms is the mean over all 3,889 frames and leaves the depth pyramid out. On the same deck window the four passes went from 3.27 to about 4.19 ms (+0.92): depth pyramid 0.23 to 0.14 (-0.09), radiance pyramid about 0.80 to 0.57 (-0.23), reflection trace 1.69 to 1.30 (-0.38), reflections (the upsample) 0.56 to 2.19 (+1.63). So all the growth is in the upsample. Paired probe traces split it (a temporary build, not committed, pushed variant bits in the top of maxSteps on the odd frames; tools/gpu-trace.sh --no-counters --control reflections gives the mean of the two even neighbors minus the odd frame, the variant's saving; 121 of 121 frames in each trace, parity exact; loads 2.0 to 3.9):
- The own rays at reflected outlines (ter-cuf9, which merged after ff6y's measurement): 1.061 (se 0.029) ms. About 9 % of the pixels of the cabin frame and 1.5 % or more of the deck frame trace their own ray (a magenta probe).
- The footprint f, which reflectingPixel read for every reflecting pixel (12 to 15 G-buffer reads; the cone's f of ter-cuf9 and the map's footprint): 0.234 (0.002) ms in the upsample, 0.017 (0.003) ms in the trace.
- The map's ambient specular term as the delta's fallback (ter-nl5u merged into ff6y after its measurement): 0.081 (0.003) ms.
- The haze of the hit (ter-6vhj): 0.001 (0.002) ms on the deck, which has no air.
- Control, no variant: reflections 0.001 (0.005) ms, trace 0.009 (0.022) ms.
The measured parts give 1.38 of the 1.63 ms. The rest, about 0.25 ms, is not attributed; candidates are the reads that later tickets added to estimatedAt and materialAt (the estimate of ter-a24f and the shading word of ter-k1ne) and the 256 steps of ter-r5hb in the own rays. Why the trace and the pyramids got cheaper is not established either. ter-r5hb's 256 steps add 0.14 ms to the trace and upsample, inside these numbers.

**2026-11-01T04:07:45Z**

Change 2, kept: the reflections read the footprint f only where something reads it (builder, 2026-11-01). reflectingPixel takes the surface with f at its floor p (estimatedAt); the cone reads f at a hit (traceReflection), and the delta's fallback takes it only where the ambient light reads it (withAmbientFootprint, the rule that the ambient pass already used through ambientSurfaceAt). surfaceAt is gone. Each read gives the value that it gave before, so no pixel changes.
Pixels: 26 captures at 1920 x 1080 (one frame from the middle of each of demo-exit's 9 films, /tmp/ter-rjts/compare.json, and every capture of reflections, pbr-spheres, demo-surface, hair-rugs and mirror-balls) are byte-identical to the base: largest difference 0 levels, 0 % of pixels differ.
Cost, interleaved deck traces (120 frames from 399, --no-counters), mean / median of busy time in ms. Base, 4 runs: reflections 2.188 / 2.150, 2.171 / 2.155, 2.212 / 2.154, 2.168 / 2.155; trace 1.305 / 1.275, 1.301 / 1.277, 1.318 / 1.277, 1.292 / 1.277 (loads 7.90, 6.52, 4.99, 6.15 at the start). Change 2, 3 runs: reflections 1.961 / 1.834, 1.928 / 1.834, 1.882 / 1.828; trace 1.348 / 1.250, 1.323 / 1.247, 1.296 / 1.247 (loads 10.67, 4.55, 6.57). So the upsample saves 0.26 ms in the mean (0.32 ms in the median) and the trace 0.03 ms in the median; its means lie within their spread. The paired probe of the same change gave 0.234 (0.002) ms and 0.017 (0.003) ms.
