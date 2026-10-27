---
id: ter-apv5
status: closed
deps: []
links: []
created: 2026-10-26T19:32:52Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, materials]
---
# Skin and hair Part 1: the material API and glass in the material

Part 1 of docs/plans/skin-and-hair.md, 'Order of the work': ShadingModel and AlphaMode unions (write ShadingModel.cs with StrandDirection first, Part 2 builds on it), Metallic into StandardShading, glass into GlassAlpha (fork 2 settled as A), RenderFlags.Transparent goes, GpuMaterial's shading word and cutoff.


## Notes

**2026-10-26T22:20:03Z**

2026-10-26: built, reviewed and fixed on ter-n7lw-skin-hair (acd05cd2, 79c5bbc1, a9eebae6, 93f8a079; review fixes 5235059c).
