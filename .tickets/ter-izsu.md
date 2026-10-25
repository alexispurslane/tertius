---
id: ter-izsu
status: open
deps: [ter-sdks]
links: []
created: 2026-10-25T18:34:36Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, performance]
---
# Profile the four occlusion passes and bring them under 1 ms at 1080p

ter-b3h5 measured the four occlusion passes at about 1.7 ms together at 1920x1080 (depth 0.13, horizons 0.70, denoise 0.16, upsample 0.78 ms), over the design's limit of 1 ms. The user chose on 2026-10-25 to merge and then profile. Profile on a quiet machine, the upsample first, because it costs the most and the depth chain (open fork 2 B of docs/plans/ambient-occlusion.md) does not touch it. Then measure the depth chain if the horizon search stays the largest pass.

## Acceptance Criteria

A profile of each pass, the changes that it justifies, and the cost measured again on a quiet machine, with the numbers in ARCHITECTURE.md.

