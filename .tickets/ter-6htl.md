---
id: ter-6htl
status: closed
deps: [ter-m13s]
links: [ter-ybti]
created: 2026-10-19T21:14:46Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [demo]
---
# Demo launch: a ramp that curves up like a rocket sled

The user wants the launch ramp to curve up like a rocket sled (2026-10-19). ter-m13s built a straight 30-degree ramp, because a curved ramp made of a static compound of 1-degree planks threw the shuttle at a joint above about 90 m/s, where Jolt sweeps the hull (ticket ter-ybti). Try first, demo only: build the curved ramp as one MeshShapeDesc on its static body, with the triangles of neighboring segments sharing their vertices. Jolt marks a concave edge between two triangles as inactive, and its sweep (the CCD linear cast) uses the same active edge rule as contacts, so the cast should take the next triangle's normal instead of the edge's. Test on the real World and Jolt: the shuttle slides up the whole curve at its launch speed and leaves the top within 1 degree of the curve's end direction, with no pitch rate above what the curve itself gives. If the mesh does not stop the throw, stop and report: the engine fix in ter-ybti goes to the user first. Update scripts/demo-launch.json and its captures, and send screenshots.


## Notes

**2026-10-19T23:13:24Z**

2026-10-19: the user decided to keep the straight ramp and drop the curve. The curve built as one MeshShapeDesc (500 m radius, 30-degree end, 121 segments with shared vertices, 4 m wide, frictionless) still threw the shuttle at 89.9 m/s; see the note on ter-ybti. Branch ter-6htl-curved-launch-ramp at afddb0e keeps the curved mesh and its four curve tests, which fail on the current engine, for whoever fixes ter-ybti. Closed without a merge.
