---
id: ter-1r63
status: open
deps: [ter-kge6]
links: []
created: 2026-11-02T01:38:36Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-7gtc
tags: [rendering, anti-aliasing]
---
# Specular anti-aliasing for normal-mapped assets, on top of FSR 3.1

The user's request of 2026-11-01, after the asset pipeline (ter-kge6). The engine has SMAA and no TAA (decision 8 of docs/plans/rendering-performance.md), so shiny surfaces with fine normal detail will shimmer: a pixel's normals vary more than one sample shows, and a narrow highlight lands on some frames and misses on others. The usual cure has two parts: in texture space, the mips of a normal map fold their normal variance into the roughness (Toksvig, LEAN and its kin), which belongs in the asset pipeline's import; in screen space, the shading raises the roughness by the screen-space derivatives of the normal (Kaplanyan and Hoffman 2016, Tokuyoshi and Kaplanyan 2019), which also covers curved geometry without normal maps. Design first, two designs for the user. Acceptance: a test scene with a fine bumpy shiny surface and a thin curved one shows less shimmer from frame to frame under a slow camera move, by a measure stated in the design, with captures for the user; portable Vulkan only.


## Notes

**2026-11-03T12:35:27Z**

On 2026-11-03 the user made the renderer temporal (epic ter-89xq), so this ticket's premise of no temporal filtering no longer holds. FSR 3.1 now averages shimmer over frames. This ticket decides whether filtering the normal maps' variance is still needed on top of it, and the title changed to match.
