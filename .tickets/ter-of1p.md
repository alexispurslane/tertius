---
id: ter-of1p
status: open
deps: [ter-nl5u, ter-nydu]
links: []
created: 2026-10-25T22:22:45Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Profile the environment map's prefilter and bring it to its expected 0.1 to 0.2 ms

The cost runs of ter-nl5u's Part 6 (2026-10-25, demo-exit at 1920x1080, Release, Apple M1 Max) measured the environment prefilter at a mean of 1.27 ms per frame and the faces at 0.41 ms, where docs/plans/environment-map.md ("Cost") expected 0.1 to 0.2 ms for the prefilter. The lighting pass grew from 1.45 to 1.80 ms, where the doc expected a few hundredths. Profile on a quiet machine: the 64 samples per texel, the solid-angle mip downsample, and whether a frame refreshes more than its share of faces.

## Acceptance Criteria

A profile of the passes, the changes that it justifies, and the cost measured again on a quiet machine, with the numbers in ARCHITECTURE.md.


## Notes

**2026-10-25T23:15:06Z**

Part 6 of ter-nl5u (08380881) measured the prefilter at 1.16 to 1.35 ms in every scene (demo-exit, demo-surface, mirror-balls), so the cost is fixed, not scene-dependent. It also measured the CPU side: one face that keeps all 20,000 instances adds 0.79 ms to SceneBatcher.Prepare, as much as the main view, where the design expected a third; a six-face refresh adds 1.75 ms. Profile both. The branch's builder filed ter-jdoo for the prefilter on its branch; it is a duplicate of this ticket and closes when the branch merges.
