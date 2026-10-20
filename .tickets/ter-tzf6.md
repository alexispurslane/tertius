---
id: ter-tzf6
status: in_progress
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
