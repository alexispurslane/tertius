---
id: ter-fhlb
status: in_progress
deps: [ter-a24f]
links: []
created: 2026-10-26T20:50:21Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Bring the environment map's cost in the lighting pass down

ter-of1p measured the lighting pass on a 120-frame window of demo-exit at 1920x1080, Release, Apple M1 Max (2026-10-26): about 2.15 to 2.29 ms with the map on and 1.39 to 1.77 ms with it off. Two parts of environmentAmbient each cost about 0.2 ms there: reflectionFootprintAt in gbuffer_surface.glsl, which reads the G-buffer at two more pixels to find the footprint f (three dominantDirectionAt calls, each with up to three fetches), and reflectedStars, which hashes the 27 cells around d below the end of the fade. Without both the pass took about 2.0 ms. Candidates that keep the image: skip the footprint where the lobe alone already ends the fade (starWidth(p, alpha) >= 2c, so fade = 1 for any f >= p, since w grows with f), and skip the cells of pixels whose open-sky share is 0. Measure each on demo-exit and mirror-balls, and check the captures pixel by pixel.


## Notes

**2026-10-27T02:34:41Z**

2026-10-26: waits for the bounce light (ter-a24f), whose lighting split moves the environment map's ambient into the ambient pass; this ticket then works on that pass.

**2026-10-27T07:46:09Z**

Built on branch ter-fhlb-map-cost (2026-10-27). The ambient pass reads its surface through ambientSurfaceAt, which leaves the footprint at p where w(p) >= 2c (1 + 2^-10) (lobeEndsStarFade, StarField.LobeEndsFade), and environmentSpecular skips the star cells where the specular map's open-sky share is 0. AmbientSkipsTests compares the HDR image bit for bit with a reference ambient pass built with TERTIUS_EVERY_AMBIENT_TERM. Traces at 1080p: the ambient pass 0.748/0.888 -> 0.438/0.425 ms on the deck window of demo-exit, 0.858/0.854 -> 0.704/0.727 ms on mirror-balls; the footprint skip alone gives 0.404 and 0.681 ms, so the stars skip saves nothing measurable there. Captures byte for byte the base's.
