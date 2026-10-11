---
id: ter-3euw
status: open
deps: [ter-juuk]
links: []
created: 2026-10-11T23:45:16Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [physics, simulation, gameplay]
---
# Shape casts and overlap queries from components

After ray casts, a component needs two more questions. A shape cast asks which bodies a moving shape would hit, for example along a thrown crate's path. An overlap query asks which bodies overlap a shape now, for example an explosion's sphere or a grab volume. Snapshot.QueryRadius answers only which entity origins lie within a radius, with no shapes and no filter.

Facts:

- JoltPhysicsSharp exposes NarrowPhaseQuery.CastShape, CollideShape and CollidePoint in double precision. Their contact points are floats relative to a base offset that the caller passes.
- BroadPhaseQuery takes callbacks only, with float coordinates.
- With CollectFacesMode.CollectFaces, joltc allocates face arrays that the binding never frees. Do not collect faces.
- The facts of the ray cast ticket apply too: the query path, the filters, the frames and determinism.

## Acceptance Criteria

A component casts a sphere, a box or a capsule along a direction and gets the first hit. It also asks which bodies overlap a sphere, a box or a capsule. Both filter by mask, work in the component's own frame and across bubbles, and give the same results in every run. Tests on the World with Jolt cover each query.

