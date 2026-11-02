---
id: ter-0pj8
status: open
deps: []
links: []
created: 2026-11-02T12:51:56Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, environment-map]
---
# The map's ray search in C#: MapSearch.Hit against an inverse distance field

Ticket 1 of ter-jw7g's design (docs/plans/environment-map-distances.md, 'Ticket 1' and 'The search'). MapSearch.Hit and IInverseDistanceField: the march of 8 samples, the 3 regula falsi steps, the cone's level and the reach, against analytic test fields (the sky, a plane, a box before a wall, a pole before a wall, a far planet, a sphere room). Owns src/Tertius.Rendering/Shading/MapSearch.cs and tests/Tertius.Rendering.Tests/MapSearchTests.cs. Acceptance: tests 1 to 12 of the design pass in Checked and Release, each red first; Samples, SecantSteps and ConeWidthPerAlpha are named once.

