---
id: ter-4vzc
status: open
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

