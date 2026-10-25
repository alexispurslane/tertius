---
id: ter-mgze
status: open
deps: [ter-ptft]
links: []
created: 2026-10-25T02:19:43Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering]
---
# Decide whether glass keeps its specular highlight at full strength, not faded by its opacity

The transparent pass blends the whole shaded color, specular highlight included, by the material's opacity (BaseColor.W). So the canopy glass at opacity 0.2 shows a fifth of its highlight. A PBR renderer built from scratch blends with premultiplied alpha: the diffuse part is scaled by the opacity, and the specular reflection off the surface adds at full strength, because the reflection happens at the surface whatever the glass lets through. The design agent of ter-ptft found this on 2026-10-24 and left it out as a separate decision: it would make the highlights of both demo glasses about five times brighter. Waits for the user's choice: keep the faded highlight, or blend with premultiplied alpha.

## Acceptance Criteria

The user chose. If premultiplied: the transparent pass's blend state and transparent.frag change, a GPU test shows a clear pane's highlight at the strength of an opaque pane's, the demo glass is reviewed in screenshots, and ARCHITECTURE.md says the rule.

