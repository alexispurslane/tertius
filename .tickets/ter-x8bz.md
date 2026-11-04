---
id: ter-x8bz
status: open
deps: []
links: []
created: 2026-11-04T21:27:50Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# A ray's hit misses the passes after the composite: no haze along the ray, no skin scatter

Found in ter-gayt, part 6 (2026-11-04). A ray's hit takes this frame's direct light and the hit's indirect history (docs/plans/temporal-renderer.md, "Where a hit's light comes from", design B). Two passes run after the composite, so a hit does not see their result:

1. The haze. The haze pass applies the haze over the receiver's view distance only. A reflection of a surface 3 km away shows that surface without the haze along the reflected leg. The old cone-traced reflections read the HDR image after the haze, and AtmosphereRenderTests had a test that the traced reflection matched the map's hazed face within 0.05. That test is now a test of the map alone (AReflectionWithoutTheTracerShowsAFarSurfaceThroughTheHaze).
2. The skin scatter. A mirror shows a skin pixel's direct light before the scatter blurs it. The old test SkinScatterRenderTests.AMirrorShowsTheScatteredSkin is removed.

A fix is a design question: for example, the haze's transmittance and in-scatter along a hit's leg in the resolve, from the same haze tables. Decide whether either loss shows in the demo first.

