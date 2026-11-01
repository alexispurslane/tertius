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
