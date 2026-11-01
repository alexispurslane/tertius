---
id: ter-prio
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
# The other passes within their share of the 12 ms frame: 3.17 ms to 2.00 ms on demo-exit

Part 3 of docs/plans/rendering-performance.md, split out of ter-3g1g by the orchestrator on 2026-10-31, so that the largest groups of passes are cut in parallel instead of one after another. The measurement (ter-3g1g, note of 2026-10-31): demo-exit at 1920 x 1080, Release, main 87ee9caf, Apple M1 Max, load 2 to 3. Two runs without a trace gave a mean GPU span of 18.04 and 17.94 ms; a trace of every frame without counters gave 17.56 ms. Each pass's share is its own busy time per frame in that trace (the mean over all 3,889 frames), times (12 ms - 1.87 ms of gaps between passes) / 16.03 ms = 0.632.

The 17 passes outside the direct light, the reflections and the screen-space light take 3.171 ms against a share of 2.004 ms, 1.17 ms less. The largest, in ms per frame (own time, then share): transparent 0.601 and 0.380 (1.374 ms in the 1,701 frames that draw it), bloom 0.411 and 0.260, environment faces 0.407 and 0.257, G-buffer 0.372 and 0.235, environment prefilter 0.251 and 0.158, local shadows 0.239 and 0.151, shadow maps 0.189 and 0.120, tone map 0.150 and 0.095, exposure 0.145 and 0.092, the three SMAA passes 0.241 and 0.152, scatter rows and columns 0.113 and 0.072, capture 0.035 and 0.022. Also find what the 1.87 ms of gaps between passes holds (the timer's stamps, barriers, drains); the shares treat the gaps as fixed, but a cut there shortens the span too.

## Acceptance Criteria

These passes' own busy time on demo-exit (a trace of every frame, two runs or a paired trace) is at most 2.00 ms, or the changes that keep the image are exhausted and a note gives the remainder, what the gaps hold, and each candidate default change with its measured saving and captures. Every change keeps the image by the pixel comparison of decision 7. ARCHITECTURE.md gives the new cost.
