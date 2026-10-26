---
id: ter-m0ux
status: open
deps: []
links: []
created: 2026-10-26T17:23:43Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-kge6
---
# Exported materials are all double-sided

Every material that the build tool exports has doubleSided true, because Blender writes doubleSided for every material without backface culling, and no model script turns culling on. Closed meshes (skin, suits, hulls) then draw their back faces too. Found on the crew member and the astronaut (ter-1qce). Decide the rule: culling on by default in export.write_glb or in the model library, and double-sided only where a material asks for it (hair cards, eyebrows, eyelashes, thin sheets).

