---
id: ter-sglu
status: closed
deps: [ter-ptft, ter-s9cv, ter-zzyr, ter-2t5o, ter-63wr, ter-gcm3, ter-b3h5, ter-a24f, ter-jxv9, ter-yiii, ter-mgze, ter-hro7, ter-6vhj, ter-pq5y, ter-n7lw, ter-ylzk, ter-4xw0, ter-uu2y]
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

**2026-11-03T12:35:13Z**

Closed on 2026-11-03 at the user's request, without its work merged: it belongs to the pre-temporal renderer (spatial reuse, the 12 ms performance push, or the old reflection and bounce pipeline), and epic ter-89xq, the temporal renderer, supersedes it. Its notes and any branch or worktree stay for reference.
