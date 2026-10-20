---
id: ter-tzf6
status: closed
deps: [ter-s9m0, ter-m13s, ter-r1hi]
links: []
created: 2026-10-18T14:08:42Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo]
---
# Demo station: mesh generators, the wheel, spokes and docking barrel, and the rendezvous autopilot

Step 4 of docs/plans/blue-danube-demo.md. Cylinder, ring and torus mesh generators with matching shapes, the spinning station on its orbit with an axial docking slot, and an autopilot that brings the shuttle to the station.


## Notes

**2026-10-19T13:11:21Z**

From ter-fb3w (2026-10-19): give a station the ring's exact mesh (RevolvedSolid.Mesh as a MeshShapeDesc) as its BodyDesc.InteriorShape, the floor people walk on, and keep the compound of segment hulls as its Shape. On the hull compound a sliding box trips at every joint; on the mesh it does not.

**2026-10-20T20:43:46Z**

Merged 2026-10-20. Coverage of the changed lines in src/, all five modules in Debug and Release: 680 of 696 lines (97.7 %), 221 of 243 branches (90.9 %). KeplerOrbit.cs: 122 of 122 lines. The uncovered lines: the unused HasValue, Equals, GetHashCode, operators and ToString of the custom union RendezvousState; two halts of RendezvousAutopilot that the demo never reaches (a note on ter-yywb, which rewrites them); and Passenger.Return outside the seat's frame (a note on ter-dd9z). Under coverage, the two ContactAllocationTests and one Host timing test failed as tool artifacts (ter-odo6). Settled demo forks: BrakeEndSpeed 0.1 m/s; the trim's push at most half of FrameTree.ZeroGravityThreshold; the nose turns at RendezvousPlan.WeightlessTurnRate, 0.002 rad/s, while the cabin is weightless, because a fast turn gives the strapped person a felt gravity above the threshold.
