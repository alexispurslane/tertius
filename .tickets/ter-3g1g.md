---
id: ter-3g1g
status: open
deps: [ter-s9cv, ter-a24f, ter-jxv9, ter-nl5u, ter-6vhj, ter-izsu, ter-ff6y, ter-of1p, ter-nydu, ter-fhlb]
links: []
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
