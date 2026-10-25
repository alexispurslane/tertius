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

**2026-10-25T13:56:59Z**

Built on branch ter-mgze-glass: dual-source glass blend (BlendMode.Transmittance), dualSrcBlend required, lobesRadiance and compensatedSpecularAlbedo in lighting.glsl, MaterialSample.Transmittance, GlassRenderTests. Transparent pass on demo-exit at 1080p: mean 0.335/0.334 ms before, 0.334/0.336 ms after; no visible cost. Demo glass unchanged; captures await review.

**2026-10-25T14:12:49Z**

Decision (2026-10-25): with the highlight at full strength, each cabin lamp shows in the nose pane as a soft bright spot in the window films, as in a train window at night. The user saw that view and kept it: the shuttle's glass keeps its coating (AntiReflective 0.18) and the cabin lamps keep their strength. ShuttleGlareTests now guards only what the user does not want, a lamp's reflection that hides the view: the brightest channel at the lamp's mirror image stays below 224 of 255. The coated glass gives 186 (nose pane) and 200 (port pane); glass at Reflectance 1 (16 % head-on, as the orbit scene's glass) gives 254 in all three cases, a white blob.
