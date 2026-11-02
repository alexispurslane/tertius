---
id: ter-f3g2
status: in_progress
deps: [ter-y8z0]
links: [ter-eqtb]
created: 2026-11-02T00:33:34Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [rendering, reflections]
---
# Soft rough reflections: probe stochastic rays with a fixed noise and ray reuse against the reference, before the design choice

The user's main concern after ter-y8z0 (2026-11-01): the blockiness of the screen-space reflections on rough surfaces (ter-y8z0's artifact 3). Cause, from ter-y8z0: one ray along the dominant direction decides which surface a pixel reflects, and the cone only blurs that surface's color, so a reflected outline stays hard and the reflection breaks into blocks. The converged lobe (64 GGX rays per pixel) shows a soft glow instead. The user asked whether temporal SSR, which breaks decision 8 of docs/plans/rendering-performance.md, is worth it. Two designs go to the user with images: (1) temporal SSR: one stochastic ray per pixel per frame, ray reuse, and accumulation over frames with reprojection of the reflected point (needs motion vectors, which the engine does not have); (2) frame-pure stochastic SSR: rays jittered by a fixed noise per pixel, as the screen-space light does (decisions 39 to 43 of docs/plans/screen-space-illumination.md), and a resolve that reuses the hits of neighbors weighted by BRDF over pdf (Stachowiak 2015), then a blur. This ticket probes design 2 on the views of artifact 3 against ter-y8z0's corrected (normalized) lobe reference, measures its cost, and shows the converged lobe as the best case of design 1 for a still camera. It changes no default. Its place in or out of epic ter-b2da waits for the user's answer on the FSR order (ter-htg7).

