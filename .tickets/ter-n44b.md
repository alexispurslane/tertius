---
id: ter-n44b
status: open
deps: [ter-1b21, ter-vja8, ter-r1hi]
links: []
created: 2026-10-16T23:00:45Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-s9m0
tags: [demo, docs]
---
# A station scene with spin gravity, its checked-in test, screenshots, and the decisions in ARCHITECTURE.md and CLAUDE.md

Design: docs/plans/spin-gravity.md. Sections 'Content' and 'Tests'. A station with a floor of at least 256 faces per ring, a person who stands, walks both ways and jumps, and crates that drop and are thrown; a checked-in Host test against inertial lines; Opus builds and runs, Sonnet reads the outputs; screenshots of a person on the inside of the ring. Change the scope rule in CLAUDE.md.


## Notes

**2026-10-19T13:11:21Z**

From ter-fb3w (2026-10-19): give a station the ring's exact mesh (RevolvedSolid.Mesh as a MeshShapeDesc) as its BodyDesc.InteriorShape, the floor people walk on, and keep the compound of segment hulls as its Shape. On the hull compound a sliding box trips at every joint; on the mesh it does not.
