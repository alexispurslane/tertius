---
id: ter-mgze
status: in_progress
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


## Notes

**2026-10-25T13:41:10Z**

Decision (2026-10-25): the user has no strong opinion and wants glass, steel and chrome to look glossy and beautiful on spaceships, and asked for the orchestrator's recommendation. Recommended and taken: the reflection happens at the surface whatever the glass lets through, so the specular part and the emission add at full strength, and only the diffuse part scales by the opacity (premultiplied alpha, Filament's transparent mode). Also the glass lets through less of what lies behind it where it reflects more: the background is multiplied by (1 - opacity) * (1 - E), where E is the directional specular albedo that already weights the ambient specular term (the BRDF tables and the energy compensation). This takes dual-source blending (the Vulkan feature dualSrcBlend, which Metal provides). No RendererOptions switch: this corrects the transparent blend and adds no effect with a cost of its own, and a switch would keep the faded highlight alive as a second path. The transparent pass's GPU time is measured before and after. The demo glass is not retuned in this ticket: the user sees captures first.
