---
id: ter-b4k3
status: open
deps: []
links: []
created: 2026-10-26T15:36:28Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [assets, blender]
---
# Make the normal bake of large meshes deterministic on the CPU

Found by ter-sdmg on 2026-10-26. Two builds of the astronaut with --device cpu gave model.glb files that differ: Suit_normal.png (2048 x 2048) differs in 2 of 4,194,304 pixels, by one level. The meshes, UV maps and the other five maps are the same bytes. Cause: the bake runs in the first, multithreaded Blender, and Blender computes MikkTSpace tangents on several threads. Ten calls of Mesh.calc_tangents() on the Suit mesh (about 32,000 faces) gave ten different results, and one result under --threads 1. The small Backpack mesh gave one result either way, which may be why the Nostromo's CPU builds matched. The skill and tools/README.md say that two CPU builds give the same .glb bytes; that holds only while no bake object is large enough. Possible fixes: run the bake in a Blender with --threads 1 (measure the cost: Cycles may slow down too), or bake each object in a second single-threaded run like the export.

