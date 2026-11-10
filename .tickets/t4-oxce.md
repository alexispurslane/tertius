---
id: t4-oxce
status: open
deps: []
links: []
created: 2026-11-10T01:57:56Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-b4ux
---
# No puff test lights a lit puff with a tube or a rectangle light

Found in the mutation run of t4-6p04 (2026-11-09). scatteredLight in lighting.glsl reads a tube's and a rectangle's scalar irradiance for the lit puffs. Two mutants survived PuffRenderTests: shapeOf(light) replaced by PointShape (every light shades a puff as a point), and the rectangle's solid angle turned off. No puff test has a tube or a rectangle light. Add a GPU test per shape that compares a puff's light with the C# scalar irradiance (face radiance times solid angle, times albedo / 4π).

