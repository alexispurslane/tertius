---
id: ter-74ng
status: open
deps: []
links: []
created: 2026-10-26T17:23:43Z
type: task
priority: 4
assignee: Alexis Purslane
parent: ter-kge6
---
# Tangents exported for meshes without a normal map

export.write_glb sets export_tangents=True, so every mesh with a UV map carries TANGENT in the .glb, also when its material has no normal map: the MPFB body, eyes, teeth, tongue, hair, eyebrows and eyelashes of the crew member and the astronaut. That is 16 bytes per vertex for nothing, and the importer can compute tangents where it needs them. Decide whether the export or the importer drops them.

