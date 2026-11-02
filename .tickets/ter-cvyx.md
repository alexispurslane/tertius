---
id: ter-cvyx
status: open
deps: []
links: [ter-eqtb]
created: 2026-11-02T17:32:29Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, tests]
---
# The hierarchical trace and the pixel-by-pixel march disagree on about 1 in 20,000 random rays since bounded planes

Found on 2026-11-02 in ter-eqtb's hardening, on the branch ter-eqtb-smooth-steps at dc0a8f9a4, with no change to src. ScreenRayTests.TheHierarchicalTraceFindsThePixelThatAPixelByPixelMarchFinds says that ScreenRay.Trace finds the pixel and the entry share that the march (the level-0 test at every pixel in order, ScreenRayTests.March) finds. It passes with its 10,000 rays (seed 20261025). With the same scene and the same ray generator but seed 7, 92 of 2,000,000 rays disagree (1 in 21,700; Checked, 24 s). In a sample of 364,778 rays, 25 disagree in two kinds:
- 11: the trace hits a pixel whose surface part stops at a center line (PixelSurface.Low or High not on the pixel's edge), and the march misses or hits a far pixel. This matches the bend that ter-eqtb documents: the level-0 test reads the surface at both ends of a stretch, so its answer depends on where the stretch starts, and the trace starts a stretch at a solved s inside the pixel after a descent, while the march starts it at the pixel's entry edge.
- 14: both pixels' parts reach all four edges. The trace hits a pixel with its hit within 0.005 px of the border to the pixel that the march hits, with the march's entry share from 0.001 to 0.26. Not explained yet: a rounding of the descent's entry across a pixel border inside a cell is one candidate.
Whether the base before ter-eqtb (the flat slab at level 0) also disagreed at this rate was not measured. The search code is in ter-eqtb's hardening note. To decide: whether the march stays the oracle of the trace with bent surfaces (then the trace must give the march's answer, for example by a level-0 stretch from the pixel's entry edge), or the test's claim changes to the kinds of disagreement that the design allows, with the second kind explained or fixed.

