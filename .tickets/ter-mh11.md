---
id: ter-mh11
status: open
deps: [ter-qfxn]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, simulation]
---
# Convert the unions of Simulation to the generator

Convert QueryFilter, PhysicsQuestion, LightData and SpatialIndex.Placement to [GeneratedUnion] (docs/plans/union-tooling.md, section Conversion). Keep hand-written members in the partial struct. Assert the size of each union whose size a comment names. QueryFilter's constructors check their arguments: keep the checks in its factories.

## Acceptance Criteria

No hand-written union boilerplate is left in Simulation. The union tests pass unchanged, except tests of ToString text. Every suite passes.

