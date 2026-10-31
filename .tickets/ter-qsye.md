---
id: ter-qsye
status: open
deps: []
links: []
created: 2026-10-31T19:17:55Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Attribute the direct and transparent passes' remaining cost against bf812296

After ter-uqmn, demo-exit's deck window (120 frames from 399, 1920x1080, Release, gpu-trace --no-counters, 2026-10-31) still costs more than at bf812296, the commit before the skin and hair merge. Direct pass: 5.46 to 5.57 ms against 5.25 to 5.45 ms, in both subsurface configurations. Transparent pass: 1.66 to 1.69 ms against 1.56 to 1.60 ms. Measured parts: the shading target's fetch in the direct pass, about 0.1 ms, and the two sums of DirectTerms, about 0.05 ms in each pass (diagnostic builds of ter-uqmn). Not attributed: about 0.05 to 0.2 ms of the direct pass and about 0.05 ms of the transparent pass. No build isolated them. Candidates: the diffuse image, which the default screen-space light makes in both subsurface configurations; forwardShading before the transparent pass's occlusion discard; the runs by sides; and the merges in the base after bf812296: ter-qsfz (89555b40), ter-fs7b (0fe2e820) and ter-vtc7 (cf4a74cc, the uvec3 flat input of mesh.vert and transparent.frag, and environment.glsl). First isolate each part with a diagnostic build from source exports, then remove what can go, or record why it stays. See docs/plans/skin-and-hair.md, "The shading models' cost".

