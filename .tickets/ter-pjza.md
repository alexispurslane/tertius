---
id: ter-pjza
status: in_progress
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

**2026-11-01T02:17:05Z**

2026-10-31: workflow run 251261a0-570a-46fb-82f7-d3c4676596e4 (version 869b0b07), branch ter-pjza-screen-space-light-share from ef3b6ae5. Settled by the orchestrator before the launch: a change keeps the image when no pixel of the comparison captures differs by more than 2 of 255 levels in any channel of the final 8-bit image; a larger change is a default change, which ter-3g1g takes to the user with captures.
