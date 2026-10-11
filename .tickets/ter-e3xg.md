---
id: ter-e3xg
status: open
deps: []
links: []
created: 2026-10-11T18:09:38Z
type: task
priority: 4
assignee: Alexis Purslane
tags: [design, terrain, rendering, physics]
---
# Design explorable planet surfaces with real terrain

Players must be able to land on a planet and explore a detailed surface with real terrain: hills, cliffs, craters, and ground that a character walks on and that bodies rest on. Today a planet is a frame with point gravity and a sphere mesh, so its surface has no shape.

The user's first sketch (2026-10-11): represent the surface as a grid of mesh patches in a curved coordinate system that follows the planet. Far patches draw as impostors. Only the patches closest to the camera become real meshes, first as an impostor and then as geometry.

This ticket is the design question only. When someone picks it up, they design it twice and the user chooses between the two designs, as the global CLAUDE.md says.

## Design

Facts of the engine that the design must fit:
- Frames: a planet is a frame with a point gravity field (ARCHITECTURE.md, Frames). An object feels only the field of the frame that it is in. Positions are frame-local doubles, so a planet frame millions of meters across keeps millimeter precision.
- Physics: each frame has its own Jolt system in double precision. The shapes are box, sphere, capsule, convex hull, mesh and compound (src/Tertius.Core/Seams/PhysicsSeam.cs). Jolt has a height field shape that the engine does not expose yet. A mesh is static only, and a frame's buffers start at 4,096 bodies.
- Rendering: positions are relative to the camera in floats. Three depth ranges cover 5 cm to 2 km, 2 km to 2,000 km, and beyond. Objects below 200 px on screen become impostors that the renderer captures at render time, and they return to geometry above 300 px. A frame with point gravity and a render radius casts planet shadows and eclipses.
- A planet that turns needs a spinning frame for its surface, so that things on the ground stay still in it (Kepler spin, ARCHITECTURE.md).

Common approaches to compare, not chosen:
- A cube sphere: six faces of a cube projected onto the sphere, each face a quadtree of patches. This is one form of the curved grid in the sketch. A patch has its own local origin, which keeps its vertices precise in floats.
- Level of detail by quadtree depth (chunked LOD, CDLOD): far patches are coarse meshes, not impostors. Impostors suit separate objects better than a continuous surface, because a seam between an impostor patch and a mesh patch shows.
- Cracks between patches of different detail: skirts, or stitched edges.
- Height data: procedural noise, authored height maps, or both, made on demand and cached.
- Physics: colliders only for the patches near bodies and characters, as height fields or static meshes in the surface frame. They come and go as bodies move.

Open questions:
- Patches as entities with render rows and bodies, or one terrain system that draws and collides directly, outside the entity tables.
- Impostors for patches, as in the sketch, or coarser meshes, or both.
- Where height data comes from, and how the content formats and the asset pipeline describe a planet's surface.
- How a body crosses from orbit into the surface frame, and how its hull lands on terrain (the frame hand-off rules).
- Streaming and its cost per tick, measured, with a switch for the expensive parts.
- Whether players can change the terrain, and how save and load (ter-72wp) keep the changes.

## Acceptance Criteria

Two designs, written in the chat or a draft doc, and the user's choice on each point where they differ. Then an approved design doc in docs/plans/, and an epic with ordered tickets that names it.

