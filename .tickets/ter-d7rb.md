---
id: ter-d7rb
status: open
deps: []
links: []
created: 2026-10-25T15:32:04Z
type: feature
priority: 4
assignee: Alexis Purslane
tags: [rendering, later]
---
# Read a 5 x 5 x 5 block of star cells for the reflected stars

Single stars in a flat mirror up to about roughness 0.12 instead of 0.058 to 0.082, at 4.6 times the cost per smooth pixel.

## Design

Left out of the scene environment map (docs/plans/environment-map.md, "Later") because nothing needs it yet. Start it when a capture shows chrome between roughness 0.06 and 0.12 that misses its stars.

