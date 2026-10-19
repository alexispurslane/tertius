---
id: ter-r1hi
status: in_progress
deps: [ter-aiuy]
links: []
created: 2026-10-19T01:06:42Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [demo]
---
# Mesh generators for cylinders, rings and tori, with matching collision shapes

Split out of ter-tzf6 (2026-10-18), because the spin gravity station scene ter-n44b and demo step 4 both need it, and it touches no engine code. Demo work in src/Tertius.Demo beside BoxMesh.cs: mesh generators for a cylinder, a ring (an annulus with thickness, at least 256 faces around for a station floor) and a torus, and for each a matching collision shape from the existing shape descriptions (a compound of boxes or convex hulls), so that a walker stands on the inside of a ring as on the mesh. Tests: the mesh's vertex count, normals and winding, and a body that rests on the shape where the mesh is. A headless capture shows each.

