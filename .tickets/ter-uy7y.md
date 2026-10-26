---
id: ter-uy7y
status: closed
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


## Notes

**2026-10-26T12:44:27Z**

2026-10-26, user: one map per semantic frame on the camera's chain (the camera's frame, its parent, up to the root), only those. An object takes the map of the frame that holds it, chosen per draw; an object in a frame off the chain takes the map of its nearest ancestor on the chain. Every map is captured at the eye; the map of a chain frame leaves out the subtree of the chain frame below it. No box projection; updates in turn. The deferred passes read each pixel's map index from the G-buffer.

**2026-10-26T16:49:08Z**

2026-10-26: merged into main (0db4bc32). Coverage of the changed lines, Debug and Release: lines 629/635 (99.1%), branches 147/154. Uncovered: the getter of the test hook DeviceAllocator.AllocationsBeforeFailure (only tests set it, none reads it); GpuImage's guard against a cube array that is not square or not six layers per cube (only EnvironmentMap makes cube arrays, always 6M square layers); the UnreachableException of an unknown light case in RenderSnapshotBuilder. Partial branches: null-conditional disposals of images never made. Lighting costs 0.49 ms more on demo-exit, tracked in ter-51gq. The never-submitted growth edge case is ter-qril.
