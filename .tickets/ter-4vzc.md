---
id: ter-4vzc
status: in_progress
deps: []
links: []
created: 2026-10-26T18:16:23Z
type: epic
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, simulation, humans]
---
# Skinning and morph targets

The user added this to the rendering upgrade on 2026-10-26 (see the skin and hair epic for the quote). Meshes with joints and weights deform by a pose of their joints, and meshes with morph targets blend them by weights, in every pass that draws a mesh. The renderer has neither today, and the asset pipeline's importer ignores skins (AS2005) and morph targets (AS2006). Design: docs/plans/skinning-and-morphs.md.


## Notes

**2026-10-27T00:04:42Z**

Part 4 done on ter-4vzc-skinning at de42abc0: posing on the CPU, the one-dispatch deform pass, the review fixes. MeshDeform.MaxVertices went (the 2D grid has no per-mesh limit). The pose buffer and job table stay within maxStorageBufferRange (DeformRegion.Holds).
