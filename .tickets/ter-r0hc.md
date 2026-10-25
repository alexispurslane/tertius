---
id: ter-r0hc
status: closed
deps: []
links: []
created: 2026-10-25T20:07:17Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-a24f
tags: [rendering]
---
# Bounce light: the C# copy of the sector math

Part 1 of docs/plans/screen-space-illumination.md on branch ter-a24f-ssgi, "Order of the work".


## Notes

**2026-10-25T21:29:41Z**

Built, reviewed and fixed on ter-a24f-ssgi on 2026-10-25: b24615f8 (part 1), with the review's ten fixes in 33b194ca (FarRaySector gives null for a full mask, ShaderInput.SurfaceLight as the one copy of the caps, SliceSide as an enum, NearSlice carries its SliceSectors, the doc's domain of the infinite-thickness reduction). Part 1's Gives in the doc match. Coverage runs after Part 6.
