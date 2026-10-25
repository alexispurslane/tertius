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

The cost runs of ter-jxv9's Part 6 (2026-10-25, demo-exit at 1920x1080, Release, Apple M1 Max) measured the reflections pass at a mean of 5.76 ms with MaxRoughness 0.6 and 5.85 ms with 0.3, the radiance pyramid at 0.92 ms and the depth pyramid at 0.29 ms: about 6.1 ms more than the base, where docs/plans/screen-space-reflections.md expected 1 to 3 ms. A lower MaxRoughness does not lower the cost, so the cost is not in the rays of rough pixels. Profile on a quiet machine first: which pixels trace, how many steps a ray takes, the fallback reads, and the occupancy.

## Acceptance Criteria

A profile of each pass, the changes that it justifies, and the cost measured again on a quiet machine, with the numbers in ARCHITECTURE.md.

