---
id: ter-izsu
status: open
deps: [ter-y1s7]
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


## Notes

**2026-10-25T20:07:18Z**

2026-10-25: the user chose 'build, then cut' for the bounce light, whose passes replace and extend these four (4.0 to 8.4 ms by arithmetic at 1080p). So this ticket now profiles the screen-space light passes after the bounce light's Part 6, and tries the three levers of docs/plans/screen-space-illumination.md, 'Cost' (fewer far rays, a half-resolution copy of the direct light, and what the profile finds, the upsample first), each with captures, before Part 7 picks the defaults.
