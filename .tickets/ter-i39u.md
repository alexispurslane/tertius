---
id: ter-i39u
status: open
deps: []
links: []
created: 2026-10-18T17:50:48Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [rendering, refactor]
---
# Make RenderLight and LightData custom unions of point and spot lights

Found by the survey of kind enums (2026-10-18). RenderLight (src/Tertius.Core/Seams/RenderSeam.cs:138) and LightData (src/Tertius.Simulation/Entities/RenderData.cs:41) carry LightKind: Point uses color, intensity, range and position; Spot also uses direction and the inner and outer cone angles. One branch (SceneBatcher.cs:254) and one copy (RenderSnapshotBuilder.cs:344). GpuLight keeps its std430 layout; SceneBatcher copies into it. No JSON content names LightData. No change of behavior.

