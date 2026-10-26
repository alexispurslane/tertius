---
id: ter-cuf9
status: in_progress
deps: []
links: []
created: 2026-10-26T16:46:24Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, reflections]
---
# Reflections: the cone covers the gap between neighboring rays, so a half-resolution mirror is soft instead of stepped

The user chose on 2026-10-26: soft mirrors at no cost (design A), not full-resolution rays for sharp pixels (design B). Cause of the 2-pixel steps at half resolution: coneLevel (reflection.glsl) sizes the cone only by the roughness, so a mirror's ray reads one sharp point of the radiance pyramid, while the hits of neighboring rays lie 2 or more pixels apart. Point reads at that spacing alias, and the upsample's blend cannot restore the edge. Fix: the cone's radius at the hit is the larger of the roughness spread (today's 1.7602 alpha) and the spread between neighboring rays, from the pixel's reflection footprint f (reflectionFootprintAt in gbuffer_surface.glsl, the change of the reflection direction to the next pixel) times the trace's texel side (2 at TraceResolution.Half, 1 at Full). The environment map's fallback already reads at that footprint, so the cone becomes its analogue. Change the GLSL and the C# copy (ReflectionCone) together. Expected: no stair steps at reflected edges in pbr-spheres and demo-surface at half resolution, a ramp about 2 pixels wide, less aliasing in curved mirrors at full resolution too, and a cost of one comparison per ray.

