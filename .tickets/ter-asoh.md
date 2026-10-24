---
id: ter-asoh
status: in_progress
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


## Notes

**2026-10-24T14:24:43Z**

Converted TransparentRun to [GeneratedUnion(typeof(MeshCommands), typeof(PuffSprites))]. Size 12 -> 20 bytes: MeshCommands holds one CommandRange, which the generator stores whole in its own slot, so it no longer shares the two int slots of PuffSprites. A run is one entry per draw in three small lists per frame, not per instance, so the growth is accepted; flattening MeshCommands to (First, Count) would bring it back to 12. No hand-written member read the fields. No persisted ToString output found. ARCHITECTURE.md now calls it a generated union.
