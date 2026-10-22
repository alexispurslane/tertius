---
id: ter-m0db
status: open
deps: [ter-qfxn]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, demo]
---
# Convert the unions of Demo to the generator

Convert AscentState, RendezvousState, DockingState, DockingEvent, DisembarkState, DisembarkEvent, ClipState, BoxPlayState, BoxPlayEvent, PassengerState and LoftedHull.PaneCut to [GeneratedUnion] (docs/plans/union-tooling.md, section Conversion). Keep hand-written members in the partial struct. Assert the size of each union whose size a comment names. The state machines use ZeroCase for their first state, and their switches lose the null arms.

## Acceptance Criteria

No hand-written union boilerplate is left in Demo. The union tests pass unchanged, except tests of ToString text. Every suite passes.

