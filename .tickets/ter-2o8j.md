---
id: ter-2o8j
status: closed
deps: []
links: []
created: 2026-10-25T15:32:04Z
type: feature
priority: 4
assignee: Alexis Purslane
tags: [rendering, later]
---
# Cull small objects in the environment map's faces

**2026-11-25 · Retired.** This optimization was scoped to an earlier GPU workload. Reopen against a named film segment if its pass dominates a repeatable GPU timing there.

Less work in the face pass.

## Design

Left out of the scene environment map (docs/plans/environment-map.md, "Later") because nothing needs it yet. Start it when Part 6 of the environment map measures the face pass as too slow.

