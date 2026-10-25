---
id: ter-of1p
status: open
deps: [ter-nl5u]
links: []
created: 2026-10-25T22:22:45Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, performance]
---
# Profile the environment map's prefilter and bring it to its expected 0.1 to 0.2 ms

The cost runs of ter-nl5u's Part 6 (2026-10-25, demo-exit at 1920x1080, Release, Apple M1 Max) measured the environment prefilter at a mean of 1.27 ms per frame and the faces at 0.41 ms, where docs/plans/environment-map.md ("Cost") expected 0.1 to 0.2 ms for the prefilter. The lighting pass grew from 1.45 to 1.80 ms, where the doc expected a few hundredths. Profile on a quiet machine: the 64 samples per texel, the solid-angle mip downsample, and whether a frame refreshes more than its share of faces.

## Acceptance Criteria

A profile of the passes, the changes that it justifies, and the cost measured again on a quiet machine, with the numbers in ARCHITECTURE.md.

