---
id: ter-3g1g
status: open
deps: [ter-s9cv, ter-a24f, ter-jxv9, ter-nl5u, ter-6vhj, ter-izsu, ter-ff6y, ter-of1p, ter-nydu, ter-p1u7, ter-fhlb, ter-uqmn, ter-ox1a, ter-0y0z, ter-19fm, ter-qsye, ter-4xw0]
links: [ter-r5hb]
created: 2026-10-25T22:29:46Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# The whole frame in 12 ms of GPU time at 1080p

Part 3 of docs/plans/rendering-performance.md, "Order of the work". After every member of the rendering upgrade merges and the three profiles close: measure the whole frame on a quiet machine, rank the passes, optimize the ones over their share with changes that keep the image, then lower defaults where that is not enough, with the full quality kept as a setting.

## Acceptance Criteria

The GPU span of demo-exit at 1920x1080, Release, on the M1 Max, at most 12 ms in two runs on a quiet machine, and the budget table of each pass in ARCHITECTURE.md.


## Notes

**2026-10-25T23:20:19Z**

2026-10-25: the user allows every technique of docs/plans/rendering-performance.md, "The techniques", without asking, half-resolution reflections included, and forbids every temporal technique (decision 8): no accumulation over frames, no TAA, no temporal upscaling. Denoise spatially only.

**2026-10-31T21:27:38Z**

2026-10-31: the orchestrator added ter-0y0z, ter-19fm, ter-qsye and ter-4xw0 as dependencies, because this ticket's own text measures the frame after every member of the rendering upgrade merges, and these four still change the frame's cost.

**2026-11-01T01:22:22Z**

From ter-r5hb (2026-10-31): the reflections' default MaxSteps rose from 128 to 256, and the hair cut of decision 35 (docs/plans/skin-and-hair.md) is gone. On demo-exit's deck window (120 frames from 399, 1920 x 1080, Release), the reflection trace with its upsample takes about 0.14 ms more per frame, inside a 0.2 ms share that ter-r5hb's note justifies. A MaxSteps below 256 brings back a ragged, speckled band on the tilted rug of hair-rugs, because no cut hides those rays now. So a lower default needs one of two things: the hair cut restored, or that band accepted. HairReflectionRenderTests' grazing test fails at 128 steps, so the change cannot happen without notice.
