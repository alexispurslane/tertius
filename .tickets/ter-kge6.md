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
