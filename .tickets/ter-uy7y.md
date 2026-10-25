---
id: ter-uy7y
status: open
deps: [ter-nl5u]
links: []
created: 2026-10-25T15:09:02Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering]
---
# Environment maps per room, so that a lit interior does not light the outdoors seen through its windows

The scene environment map (ter-nl5u) holds the light at the camera's position, and every surface on screen takes its ambient light from it. In the open and in orbit that is correct. Inside the lit cabin of demo-surface, the lamps light the walls to a radiance of about 2, while the sun lights the apron outside to about 0.1 to 0.2, so the apron seen through the canopy gets the cabin's light as its ambient light, and its shadows fill in. The user accepted that for now on 2026-10-25 and chose one map first. This ticket adds maps that belong to places, such as one per room or one per frame of a ship's interior, and chooses for each surface the map of the place that holds it.

Design to decide (two designs, then the user chooses): what a place is (a room volume, an interior frame, a probe the scene places), how a surface finds its map, parallax correction (box projection), when each map updates, and the memory and cost.

## Acceptance Criteria

From inside the lit cabin, the apron seen through the canopy keeps its own ambient light and its shadows. The cost is measured, and captures show it.

