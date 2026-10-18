---
id: ter-935n
status: open
deps: []
links: []
created: 2026-10-18T17:50:48Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [physics, refactor]
---
# Make QueryShape a custom union of sphere, box and capsule

Found by the survey of kind enums (2026-10-18). QueryShape (src/Tertius.Core/Seams/PhysicsQueries.cs:62) carries QueryShapeKind: Sphere uses Radius, Box uses HalfExtents, Capsule uses Radius and HalfHeight. 3 switch sites: BoundingRadius, Problem, QueryShapeCache.Create (Jolt shape creation only; no JSON, payload or dump). The documented default (a sphere of radius 0 that every query refuses) becomes HasValue == false. No change of behavior.

