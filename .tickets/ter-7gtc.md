---
id: ter-7gtc
status: open
deps: [ter-kge6, ter-sglu]
links: []
created: 2026-11-02T01:38:36Z
type: epic
priority: 3
assignee: Alexis Purslane
tags: [rendering]
---
# Rendering after the asset pipeline: specular anti-aliasing and volumetric light

The user's plan of 2026-11-01: a continuation of the rendering upgrade (ter-sglu) that starts after the full asset pipeline (ter-kge6), because both of its features matter only with real assets. Its tickets: specular anti-aliasing first, volumetric light in the far future. Each keeps decision 8 of docs/plans/rendering-performance.md: nothing accumulates over frames.


## Notes

**2026-11-03T14:54:04Z**

2026-11-03: decision 8 of docs/plans/rendering-performance.md, which this ticket cites, is superseded by docs/plans/temporal-renderer.md (epic ter-89xq). The renderer now reuses light over time.
