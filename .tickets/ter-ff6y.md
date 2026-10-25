---
id: ter-ff6y
status: open
deps: [ter-jxv9, ter-nydu]
links: []
created: 2026-10-25T22:22:45Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Profile the reflection passes and bring screen-space reflections to their expected 1 to 3 ms at 1080p

The cost runs of ter-jxv9's Part 6 (2026-10-25, demo-exit at 1920x1080, Release, Apple M1 Max) measured the reflections pass at a mean of 5.8 ms with MaxRoughness 0.6 and MaxSteps 64, the radiance pyramid at 0.93 ms and the depth pyramid at 0.29 ms: about 7 ms in all, where docs/plans/screen-space-reflections.md expected 1 to 3 ms for the trace, 0.2 for the radiance pyramid and 0.1 for the depth pyramid. With MaxRoughness 0.3 or 0.05 the pass costs about 0.27 ms, because the demo's paint has roughness 0.38 and nearly no pixel traces. So the cost is in the rays: about 5.5 ms when nearly every pixel of the demo traces. Profile on a quiet machine: the steps that a ray takes, the cells it reads at each level, the divergence between neighbors, the cone's reads of the radiance pyramid, and the occupancy. Part 6's check moves the default MaxSteps from 64 to 256, so measure at the default that merged.

## Acceptance Criteria

A profile of each pass, the changes that it justifies, and the cost measured again on a quiet machine, with the numbers in ARCHITECTURE.md.

