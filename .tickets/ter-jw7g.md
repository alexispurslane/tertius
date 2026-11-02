---
id: ter-jw7g
status: closed
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

**2026-11-02T12:04:28Z**

2026-11-02: the design stage runs as a delegated task (designer, Opus at xhigh, in its own worktree): docs/plans/environment-map-distances.md, its open questions, and the order of the work as tickets.

**2026-11-02T12:38:36Z**

2026-11-02, designer: the design is in docs/plans/environment-map-distances.md. Each map gains a distance cube (R16F, the inverse distance from the eye in km⁻¹, the sky 0, with the radiance map's levels as solid-angle means). The face pass writes it as a second attachment, and the prefilter makes its levels beside the radiance mips. One function, environmentSpecular, reads the specular map where the lobe's central ray first goes behind the stored surfaces: a march of 8 samples in the sweep from the shaded point to infinity, then 3 regula falsi steps, each read at the level of the lobe's cone as the eye sees it, and the read's roughness r times the square root of the hit's reach. A ray that meets no stored surface reads exactly today's texel. The diffuse term, the occlusion sectors and the lit puffs keep today's reads. Open questions for the user: (1) does each face keep the eye that drew it (option 1, the current eye, first and recommended; option 2, a per-face eye through a seam change, ticket 5); (2) does the specular occlusion still darken a found hit (A, yes, recommended; B, only a read with no hit). Proposed tickets: 1 MapSearch in C#, 2 the distance cube, 3 the corrected read (after 1, 2 and ter-gf0t), 4 captures and cost (after 3), 5 per-face eyes (after 3, only under question 1 option 2).

**2026-11-02T12:51:20Z**

2026-11-02, the user's answers to the design's two open questions. Question 1: option 2, each face keeps the eye that drew it, built by ticket 5 after ticket 3 (option 1 ships first, inside ticket 3). Question 2: option A, the specular occlusion multiplies the whole corrected term, also a found hit. The design doc records both. Next: merge the design into main, create tickets 1 to 5 under ter-b2da, and launch tickets 1 and 2 in parallel.

**2026-11-02T12:52:04Z**

2026-11-02: the design merged into main (cc8d58311). Its five implementation tickets, all under ter-b2da: ter-0pj8 (1, MapSearch in C#), ter-55xu (2, the distance cube), ter-s6zr (3, the corrected read, after ter-0pj8 and ter-55xu), ter-g6af (4, captures and cost, after ter-s6zr), ter-pn1w (5, each face keeps its eye, after ter-s6zr). This design ticket closes, and the five carry the work.
