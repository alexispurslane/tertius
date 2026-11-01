---
id: ter-mfyv
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
# Direct light within its share of the 12 ms frame: 6.01 ms to 3.80 ms on demo-exit

Part 3 of docs/plans/rendering-performance.md, split out of ter-3g1g by the orchestrator on 2026-10-31, so that the largest groups of passes are cut in parallel instead of one after another. The measurement (ter-3g1g, note of 2026-10-31): demo-exit at 1920 x 1080, Release, main 87ee9caf, Apple M1 Max, load 2 to 3. Two runs without a trace gave a mean GPU span of 18.04 and 17.94 ms; a trace of every frame without counters gave 17.56 ms. Each pass's share is its own busy time per frame in that trace (the mean over all 3,889 frames), times (12 ms - 1.87 ms of gaps between passes) / 16.03 ms = 0.632.

The direct light pass takes 6.009 ms of the frame's 17.56 ms in the trace, 34 % of the frame, and its share is 3.798 ms, 2.21 ms less. It is the largest pass by far. History: ter-p1u7 (a per-pixel bound on the lights, dropped by the user because it was slower), ter-ox1a (a light's reach from the last frame's exposure), ter-uqmn (the shading models' cost); the profile of main in ter-p1u7 found about 3.6 ms of demo-exit's lighting in the lamps' reach (every lamp at every pixel) and about 1.3 ms in the lamps' shadows, limited by PCF sample rate. ARCHITECTURE.md, 'The scenes and the cost of the shadows', has the halls that isolate the light loop.

## Acceptance Criteria

The direct light pass's own busy time on demo-exit (a trace of every frame, two runs or a paired trace) is at most 3.80 ms, or the changes that keep the image are exhausted and a note gives the remainder and each candidate default change with its measured saving and captures. Every change keeps the image by the pixel comparison of decision 7. ARCHITECTURE.md gives the new cost.
