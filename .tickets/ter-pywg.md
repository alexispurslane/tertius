---
id: ter-pywg
status: open
deps: []
links: [ter-cvyx]
created: 2026-11-02T21:02:10Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, tests]
---
# A slab entry exactly on a pixel's far edge inside a cell skips the pixel that the march meets at its exit

Found on 2026-11-02 in ter-cvyx's hardening, on the branch ter-cvyx-trace-march at 163d4a741, with no change to src.

The trace and the march disagree when a descent solves its slab entry exactly on an edge between two pixels inside a cell, and the ray meets the slab's front at the end of the earlier pixel's stretch. The march tests the earlier pixel over its closed stretch, from its entry edge to its exit, and frontOut = 0 counts as a meeting, so it hits that pixel. PixelAt gives the pixel at s by a half-open rule: the near edge at s or before, the far edge after s. So the walk stands in the later pixel, and never tests the earlier one.

Reproduction on a 16 x 16 sky with one surface at pixel (3, 4) of inverse depth 0.5 (DepthPyramid.FromInverseDepths): new ScreenRay(Start: (-7.5, 4.5), Step: (8, 0), StartInverseDepth: 0.859375, StepInverseDepth: -0.25, End: 2.9, EndsAt: ScreenEdge). Every number is a binary fraction, so the tie is exact. The walk climbs in the empty cells left of the screen, enters the level-4 cell at x = 0 (s = 0.9375), and descends to the slab entry s = 1.4375, where x = 4 exactly. ScreenRayTests.March gives (3, 4, 0). Trace gives RayLeftTheSegment { End = ScreenEdge, Steps = 6 }.

The case needs an exact tie: the entry's parameter equals the edge's parameter, and w at the pixel's exit is at the slab's front or behind it. Random rays did not find it (0 of 30,000,000 in ter-cvyx). On the same tie at a cell's exit, the clamp into the cell keeps the walk in the earlier pixel (ScreenRayTests.ASlabEntryOnItsCellsExitStaysInTheCellAndMeetsThePixelThatTheImageLeavesThere passes).

The mutants C03 (exit <= s to exit < s) and C04 (entry > s to entry >= s) of ScreenRay.PixelShift, and their GLSL twins S03 and S04 of pixelAt, survive ter-cvyx's hardening for this reason. By a reading of the code (not run), C04 puts the walk on the reproduction in the pixel that the image leaves at s, which is the pixel that the march hits. To decide: whether a slab entry on a pixel's edge belongs to the pixel that the image leaves there (then the descent's pixel differs from the crossing's pixel at the same s), or the level-0 test counts a stretch's end point as a meeting only in the next pixel. Either way C# and GLSL change together, and the reproduction becomes a test.

