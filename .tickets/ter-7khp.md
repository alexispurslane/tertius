---
id: ter-7khp
status: open
deps: [ter-vges, ter-odli]
links: []
created: 2026-11-03T12:30:05Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, performance]
---
# Time-sliced environment maps and far shadow cascades

The user's choice of 2026-11-03: a later step of the temporal renderer, not in its first design. The environment cube renders 1 or 2 of its 6 faces per frame, and the far shadow cascades update every 2nd or 4th frame. About 1.25 ms of fixed-size work today (environment faces 0.35 ms, prefilter 0.62 ms, shadow maps 0.27 ms on demo-exit at 1080p, docs/research/temporal-renderer/pass-map.md) does not shrink with the render size. Cost: off-screen reflections and far shadows of moving objects lag by a few frames.

