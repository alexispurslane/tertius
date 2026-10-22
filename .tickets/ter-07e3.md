---
id: ter-07e3
status: open
deps: [ter-qfxn]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, core]
---
# Convert the unions of Core to the generator

Convert Target, GravityField, MediumField, HemisphereUp, RenderLight, QueryShape, MotionIntent, PuffShading, InputEvent and FrameOutline to [GeneratedUnion] (docs/plans/union-tooling.md, section Conversion). Keep hand-written members in the partial struct. Assert the size of each union whose size a comment names. Measure the generated size of Target. If it is more than 8 bytes, keep Target hand-written and suppress TU0001 there with the reason.

## Acceptance Criteria

No hand-written union boilerplate is left in Core. The union tests pass unchanged, except tests of ToString text. Every suite passes.

