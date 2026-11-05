---
id: ter-186q
status: open
deps: []
links: []
created: 2026-11-04T21:53:20Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
---
# The walk misjudges a far slab near a vanishing point: a planet's reflection misses or fades at a fifth of its pixels

Found in ter-gayt, part 6 (2026-11-04), when ScreenRayTests.EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanet moved from the old trace to ScreenRay.Walk.

Near a vanishing point, the rounding of the ray's inverse depth, StartInverseDepth + StepInverseDepth · s (about 3e-8), is larger than the slab of the planet (a rectangle 10,000 km away, κ · 1e-7). Of the test's 3,613 rays:

1. 209 rays leave the segment at the vanishing point without meeting the slab. They take the miss's closed form: the map's planet while the map is on, the hemisphere while it is off.
2. 531 rays hit the planet at the vanishing point's pixel, but with an entry share q from 0.05 to 0.59 (median 0.08). The walk takes q from the inverse depth at s, where the ray enters the cell, so q is noise, and the hit's fade 1 − q dims those pixels.

The old cone trace judged no slab by the inverse depth at a solved entry, and its test asserted a hit with q = 0 on every ray. The skipped test ScreenRayTests.EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanetThroughTheFrontOfItsSlab holds that behavior. ARayFromANearFloorHitsAPlanetAtItsVanishingPoint, one ray, passes. A fix changes walkScreen in screen_ray.glsl and ScreenRay.Walk together, and the GPU pin tests of ScreenTraceRenderTests must stay green. One idea: at a hit whose s a descent solved, take q from the side that the ray came from when the coarse level solved the entry (0 from in front, 1 from behind), where the inverse depth is far from the slab.

