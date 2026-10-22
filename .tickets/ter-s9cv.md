---
id: ter-s9cv
status: open
deps: [ter-zzyr, ter-2t5o, ter-63wr]
links: []
created: 2026-10-11T13:52:12Z
type: epic
priority: 3
assignee: Alexis Purslane
tags: [rendering, long-term]
---
# Point and spot light shadows, and lights instead of one sun

Design: docs/plans/local-light-shadows.md.

On 2026-10-11 the user folded a second goal into this epic: the sun stops being a special case. Every far light, such as a second star, gets what the sun gets now: far shadows against its own disc, cascaded shadow maps within a budget, and baking into impostor captures. Its brightness comes from its power over its distance squared. The design doc's section "Lights instead of one sun" lists the points that still need two designs.

On performance, the doc's plan holds for local lights: one shared atlas, a tile budget ranked by importance, cached tiles for lights and casters that do not move, culling by range and cone, and GPU timings for 4, 16 and 64 shadowed lights. Far lights add four cascades each, so only the brightest one or two get them.


## Notes

**2026-10-22T00:09:45Z**

2026-10-21: the user chose the design of the part "Lights instead of one sun" from two designs: SI units, one light list for the renderer, a reach derived from the power. It is now ticket ter-bpvl, which waits for automatic exposure (ter-zzyr). The design doc holds the full design, with every reader and writer.
