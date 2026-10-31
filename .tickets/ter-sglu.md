---
id: ter-sglu
status: open
deps: [ter-ptft, ter-s9cv, ter-zzyr, ter-2t5o, ter-63wr, ter-gcm3, ter-b3h5, ter-a24f, ter-jxv9, ter-yiii, ter-mgze, ter-hro7, ter-6vhj, ter-pq5y, ter-n7lw, ter-ylzk]
links: []
created: 2026-10-22T00:25:05Z
type: epic
priority: 2
assignee: Alexis Purslane
tags: [rendering]
---
# Rendering upgrade: PBR shading, the HDR pipeline, lights and shadows, and screen-space effects

Design: docs/plans/rendering-upgrade.md.

One epic for every rendering feature that changes the look of a frame: a PBR BRDF first, then automatic exposure, the tone curve, bloom, SMAA, SSAO, SSGI, SSR, and the light shadows with lights instead of one sun. The user grouped them on 2026-10-21. The snapshot tests (ter-rtxu) and the asset pipeline (ter-kge6) come after this epic, so that reference images and imported materials meet the final look.


## Notes

**2026-10-26T18:16:23Z**

2026-10-26: the user closed the scope of the rendering upgrade: it adds skinning and morph targets (ter-4vzc) and skin and hair shading (ter-n7lw), 'and then that'll be it'.
