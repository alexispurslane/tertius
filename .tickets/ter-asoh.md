---
id: ter-asoh
status: open
deps: [ter-qfxn]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, rendering]
---
# Convert the unions of Rendering to the generator

Convert TransparentRun to [GeneratedUnion] (docs/plans/union-tooling.md, section Conversion). Keep hand-written members in the partial struct. Assert the size of each union whose size a comment names. 

## Acceptance Criteria

No hand-written union boilerplate is left in Rendering. The union tests pass unchanged, except tests of ToString text. Every suite passes.

