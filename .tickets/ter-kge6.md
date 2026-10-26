---
id: ter-kge6
status: open
deps: [ter-ynqi, ter-sglu]
links: []
created: 2026-10-11T13:52:13Z
type: epic
priority: 3
assignee: Alexis Purslane
tags: [content, assets]
---
# Asset pipeline

Design: docs/plans/asset-pipeline.md.


## Notes

**2026-10-22T00:25:39Z**

2026-10-21: this epic now waits for the rendering upgrade epic (ter-sglu), which moves the renderer to a PBR BRDF with metallic-roughness materials (ter-ptft). Decisions 12 and 13 of docs/plans/asset-pipeline.md (the conversion of glTF materials to Blinn-Phong, and metals that cannot look right) then change: the importer takes glTF metallic-roughness materials as they are. Revise those decisions with the user when this epic starts.

**2026-10-25T21:16:27Z**

2026-10-25, from ter-v0yz: a material without a normal map now has no map (GpuMaterial.NoMap) and shades with its exact vertex normal. Authored normal maps keep glTF's decode, xyz * 2 - 1, so an authored 'flat' 8-bit texel (128, 128, 255) still tilts by about 0.32 degrees. The pipeline's plan of BC5 UNORM for normal maps keeps that half step. If the pipeline wants flat texels exact, it chooses an SNORM format (BC5_SNORM or R8G8_SNORM, where 0 is exact) or a decode of (255t - 128)/127, both departures from glTF that the importer then converts to.
