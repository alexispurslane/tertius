---
id: ter-g3yp
status: open
deps: [ter-ziae, ter-1j1e]
links: []
created: 2026-10-11T23:45:16Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [physics, content]
---
# Let content choose which bodies collide: collision categories and masks

Every body has one of four fixed object layers today: Static, Moving, FrameHull and Ghost (BodyLayer in src/Tertius.Core/Seams/PhysicsSeam.cs). A private pair table in src/Tertius.Physics/Internal/Layers.cs says which layers collide. Content cannot add a category, such as debris that passes through people. No code can ask what a body collides with.

Facts:

- BodyDesc.Layer is checked only with Enum.IsDefined. The backend overrides it for frame hulls, interiors, character inner bodies and ghosts.
- joltc builds with 32 object layer bits. JoltPhysicsSharp 2.23.0 exposes ObjectLayerPairFilterMask, which splits a layer into 16 category bits and 16 mask bits. Tertius uses the table variant today.
- The binding also exposes Jolt's GroupFilterTable. It acts only during simulation, and it filters only inside one group. Queries filter with a BodyFilter instead.
- BodyInterface.SetObjectLayer can change a layer at run time. Nothing in Tertius calls it.
- The content formats design has one collision key in the body section: layer, with the values "static" and "moving".

## Design

Open questions:

- Do masks replace the four layers, or sit beside them? Ghosts must still meet only Moving bodies, and frame hulls must still meet only Static bodies and other hulls.
- Does content name the categories, and where does the table of names live?
- Can a category or a mask change after spawn?
- Does a mask also filter queries and sensors by default?

## Acceptance Criteria

A body's description names its category and its mask. Two bodies collide only when each mask contains the other body's category. Ghosts, frame hulls and interiors keep their current behavior. A test on the World with Jolt shows two categories that pass through each other and a third that hits both.

