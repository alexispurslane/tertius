---
id: ter-7l43
status: open
deps: []
links: []
created: 2026-10-25T15:32:04Z
type: feature
priority: 4
assignee: Alexis Purslane
tags: [rendering, later]
---
# Add the stars' light to the diffuse term through the irradiance buffer

The stars add about 6e-4 of diffuse light in open space, through the w components of the irradiance buffer.

## Design

Left out of the scene environment map (docs/plans/environment-map.md, "Later") because nothing needs it yet. Start it when an image shows the difference. Decision 32 of the design expects that never.

