---
id: ter-297a
status: open
deps: [ter-qfxn]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, physics]
---
# Convert the unions of Physics to the generator

Convert CharacterStartVelocity and VelocityShadow to [GeneratedUnion] (docs/plans/union-tooling.md, section Conversion). Keep hand-written members in the partial struct. Assert the size of each union whose size a comment names. 

## Acceptance Criteria

No hand-written union boilerplate is left in Physics. The union tests pass unchanged, except tests of ToString text. Every suite passes.

