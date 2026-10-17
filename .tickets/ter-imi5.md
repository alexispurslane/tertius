---
id: ter-imi5
status: open
deps: []
links: [ter-xxmj, ter-3mwi, ter-7ul9]
created: 2026-10-17T13:11:15Z
type: feature
priority: 2
assignee: Alexis Purslane
tags: [physics, frames, contacts]
---
# Complete the ghosts between semantic frames

Decided with the user on 2026-10-17 as one of three general design changes that remove whole families of bugs. This one has nothing to do with numeric frames: it makes a ghost between semantic frames stand in for its real body as fully as Jolt allows.

1. A static body that is not a hull gets a ghost in each child frame whose bubble it touches, as the hull of a frame that physics does not drive already does. Then the bodies of the child meet it (ter-xxmj, and the known limit of queries that start inside a bubble in docs/plans/queries-and-sensors.md).
2. A pair of two ghosts in which one is a sensor is reported, so that a sensor and a body that touch only through two ghosts hear their touch (ter-3mwi, first half).
3. A sensor detects the same things of a frame owner in every frame: its hull's ghost and its interior alike (ter-7ul9), and a pair does not outlive a driver change that makes it impossible.

Decide each case with the contact ledger in src/Tertius.Physics/JoltPhysicsBackend.Contacts.cs, and test each beside the same scene with the bodies in one frame. The child tickets stay open until this one closes them.

