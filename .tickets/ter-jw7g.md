---
id: ter-jw7g
status: open
deps: []
links: []
created: 2026-11-02T11:59:25Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, environment-map]
---
# Rough surfaces miss the reflection of bright things near them, because the eye's map is read by direction alone (the terminal floor's doorway glow)

The user's decision of 2026-11-02, after ter-gf0t: open a design ticket for the reflected brightness that the SSR limit of 0.37 removed. Evidence (captures/f3g2 and captures/gf0t/analysis/glow.txt in the main checkout): against ter-f3g2's 64-ray reference (g/lobenorm), the image at 0.37 loses, on the mean of each box in levels of 255, terminal door 42.9 (24.6 against 65.5: the soft glow of the sunlit doorway on the hall floor), brake-run teeth 17.9, floor film lamp cut 15.6, chute-top slide 14.4, chute ceiling 8.4, terminal floor 7.1, antispinward edge 6.5, floor steps 3.4, unbelt floor 3.2, deck 0.2. Cause: every map is captured at the eye (ARCHITECTURE.md, 'Every map is captured at the eye') and a surface reads it by its reflected direction alone, as if everything were infinitely far. A floor spot below the doorway sees the doorway in a direction that, from the eye, points at the dark hall. SSR used to supply the doorway (too strongly, 80.0, and with hard edges). ter-gf0t's widened lobe gives none of it back, because the doorway is not a light. Design: two designs in chat and the user's choice; then a designer records the settled design in docs/plans/. Constraints: no probes placed by the scene and no box projection (the user's decision 4 of environment-maps-per-frame.md and decision 12 of environment-map.md), nothing accumulates over frames (decision 8 of rendering-performance.md), portable Vulkan.


## Notes

**2026-11-02T12:02:24Z**

2026-11-02, the user's choice between two designs: design A, the map knows its distances. Each map stores each texel's distance from the eye (a distance cube per map), and a surface reads the map at the place where its reflected ray ends (a few secant steps against the distances, after Szirmay-Kalos and others, 'Approximate Ray-Tracing on the GPU with Distance Impostors', Eurographics 2005), not by its reflected direction alone. It covers on screen and off screen, every roughness, and SSR's fallback where a ray leaves the screen. Known miss: things that the eye cannot see. Rejected: design B, a coarse screen-space ray for roughness 0.37 to 0.6 (only on screen, a cut at the screen's edge, the risk of the old hard edges). Next: one designer records the design in docs/plans/, then the implementation tickets.
