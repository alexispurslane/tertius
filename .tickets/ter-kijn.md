---
id: ter-kijn
status: open
deps: []
links: []
created: 2026-10-27T12:40:04Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [simulation, precision]
---
# An attached part's velocity read narrows its chain root's velocity to a float

Found in the review of ter-2y6i (2026-10-27). ter-2y6i made a velocity read take the exact double where the engine holds one: a frame owner's from the frame tree, and a pending body's from its creation. An attached part's row velocity is derived in Attachments.Derive from its chain root's row: rootRow.LinearVelocity + (ω × lever), narrowed to a float. So a part at zero lever on a hull that owns a frame, or on a body spawned at 6016.291064 m/s, reads 6016.291015625 while its root reads 6016.291064, and a rigidly attached pair disagrees by up to half a float's step. The state dump prints both, and a jet's puffs are born with the part's velocity.

A fix derives a part's read velocity from its chain root's exact read plus ω × lever, in doubles, in World.VelocityIn and in the puffs' read alike, so that one read path stays.

## Acceptance Criteria

A part at zero lever reads exactly its chain root's velocity, at tick 0 and after, for a root that owns a frame and for a pending spawn; ARCHITECTURE.md, 'Numeric frames', Velocity row, drops its exception for parts.

