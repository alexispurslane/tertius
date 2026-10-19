---
id: ter-imi5
status: in_progress
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


## Notes

**2026-10-19T13:11:55Z**

To check before the merge (2026-10-19, from ter-fb3w): a station's InteriorShape can be a mesh (the floor people walk on). Under fork F4 the interior turns kinematic and at rest while the hull moves, so a mesh interior must work as a kinematic body too: Jolt allows a kinematic MeshShape only with a mass, as the mesh ghosts already get 1 kg. Test: a spinning kinematic station whose interior is a ring mesh holds a ball at rest on its floor.
