---
id: tc-5br7
status: closed
deps: []
links: []
created: 2026-11-23T18:50:49Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, testing, temporal]
---
# No test pins the light-age record's footprint floor of 1/64: a slow walk across a shadow's edge on one surface while the lamp dims would

**2026-11-25 · Retired.** Preserve the temporal lighting measurements. Choose any change to sampling, weighting or history only after isolating the source of the pattern on the new deck (ter-xzj7).

Found by the review of t0-y88b on 2026-11-23. LeastFootprintWeight (screen_trace_plan.comp, lightAgeOf) keeps a tap of a weight under 1/64 out of the band of last lights that a new light inside the 3 x 3 range must lie near. t0-y88b removed the rounding of a still frame's place, the floor's first reason, and the light-age tests passed with the floor at 0 on main's shaders and on the fix: they cannot see the floor. The review named a second reason, under motion, by static reasoning only. A light edge on one surface, such as a shadow's edge on a floor, has taps across it that pass the surface test. Under a slow motion whose step per draw lies 1/256 to 1/64 texel from a whole texel, the taps across the edge get weights under 1/64. Without the floor, a lit pixel's band reaches the dark side, so a dimming of the lamp inside the range is taken without pending draws, and the next ring inward takes it from that pixel on the next draw: the change spreads one ring per draw and counts no age. t0-y88b kept the floor for that reason. Its value has no measured basis: with the floor, the same spread needs a fraction of 1/64 or more.

The test: light a floor with a lamp so that it casts a straight shadow edge, walk the camera at a constant step of about 1/100 texel per draw across the edge, dim the lamp by 20 %, and count the lit pixels within 4 px of the edge that commit age 0 after LightChangeDraws + 2 draws, at the floor 0 and 1/64. Also compare the false commits that AWalkOverTheSteepLightOfAStillLampCommitsNoLightChange prints at both values. Red on a mutant that sets the floor to 0 first. If the floor does no work there, remove it.

