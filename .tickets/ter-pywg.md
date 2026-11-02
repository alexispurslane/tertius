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


## Notes

**2026-11-02T22:10:35Z**

2026-11-02, the fix (branch ter-pywg-edge-tie from ea24deae6). The orchestrator's rule holds for every tie: a slab entry on a pixel's edge belongs to the pixel that the image leaves there, the earlier one in the march's order. The level-0 test and the march (the oracle) do not change.

The change, in C# and GLSL alike (ScreenRay.Trace and traceRay). After a descent to an entry past s, the walk takes PixelAt / pixelAt at the largest float below the entry (MathF.BitDecrement in C#, the new floatBelow in reflection.glsl), not at the entry. No float lies between the two, so an edge parameter e satisfies e <= below(entry) exactly when e < entry: the unchanged half-open rule of PixelAt then gives the pixel that the image leaves at the entry on each axis, also when floor(S(s)) rounds to either side. PixelAt, PixelShift and the crossing after a miss do not change. The entry is past s >= 0, so below(entry) is a number. In GLSL the select also evaluates the case entry = s, where floatBelow(0) gives NaN bits, and drops that pixel, as before. Every texelFetch keeps its beyondTheScreen guard: the pixel stays clamped into the cell.

A first form gave PixelAt and PixelShift a bool `leaving` that flips both comparisons at a descent (patch kept at /private/tmp/pywg/bool-variant.patch). It passes the same tests. I kept the float-below form because it is the smaller change and adds one integer subtraction and one select to a step of the walk, not four comparisons and two selects.

Tests (ScreenRayTests), each run on the base first:
- ASlabEntryOnAPixelsEdgeInsideACellMeetsThePixelThatTheImageLeavesThere, 4 rows: the ticket's reproduction (16 x 16 sky, surface (3, 4) at 0.5, Start (-7.5, 4.5), Step (8, 0), w 0.859375 - 0.25 s, End 2.9), the same to the left (Start (23.5, 4.5), Step (-8, 0), surface (12, 4)), down ((4.5, -7.5), (0, 8), (4, 3)) and up ((4.5, 23.5), (0, -8), (4, 12)). Red on the base in all 4 for the ticket's reason: the march gives the surface pixel with q = 0, the trace gives RayLeftTheSegment { End = ScreenEdge, Steps = 6 }. Green after the change.
- ACoarseCrossingThroughAPixelCornerOnTheCellsEdgeEntersThePixelThatTheMarchEnters, 4 rows: constant w 0.5, the walk leaves the level-3 cell of pixels 0 to 7 across the line 8 at s = 0.9375, exactly through a pixel corner inside that edge ((0.5, 5.9375) step (8, -1); (0.5, 4.0625) step (8, 1); and the two transposed). The entered pixel and the pixel that the image touches only at the corner both see a surface. Green on the base and after: it locks in that the crossing keeps the pixel that the image enters, on both signs of travel on the uncrossed axis.

Mutants of ter-cvyx's table, by a reading of the code (no mutant run; the hardening pass runs them):
- C03 (PixelShift: exit <= s -> exit < s): now killed, expected by the crossing rows "across and up" and "down and to the left". With travel -1 on the uncrossed axis, floor gives the pixel that the image leaves at the corner, whose far edge gives s, and the mutant keeps the walk there: a hit in the touched pixel.
- C04 (PixelShift: entry > s -> entry >= s): now killed, expected by the crossing rows "across and down" and "down and to the right", the same with travel +1. So C03 and C04 never needed ter-pywg's tie: an exact corner on a coarse cell's uncrossed axis tells them apart.
- S03, S04 (the twins in pixelAt): a judgment call, as S01, S02 and S05 in ter-cvyx. Each needs a ray exactly through a pixel corner, and no GPU harness runs a chosen ray through traceRay or compares hit pixels ray by ray.
- New rows for the hardening: C (Trace: the descent's pixel at the entry, not MathF.BitDecrement(entry)) is the base code, red on the 4 descent rows. S (traceRay: pixelAt at entry, not floatBelow(entry)) is its twin, a judgment call for the same reason as S03.

Images (/private/tmp/pywg/captures, Release, headless, 1920 x 1080; base = ea24deae6, whose code equals main 93a427773): pbr-spheres front and low (the step view), mirror-balls balls, plate and belly, reflections front, low and edge, and its 200 timing frames are byte-identical to the base. 0 pixels change. The branch's Tertius.Rendering.dll embeds the new reflection_half_trace SPIR-V, which differs from the base's.

Cost (/private/tmp/pywg/timing4/runs.txt): 14 interleaved rounds in a rotated order of base, control (a byte copy of the base build), after and the bool form, each under the GPU lock, the four reflection passes' summed mean over 120 frames of the step view (pbr-spheres low), 1-minute load 7.4 to 37.5. Base 2.625 ms. Control - base +0.024 (bootstrap 95 % -0.019 to +0.050). After - mean(base, control) +0.004 (-0.078 to +0.077). Bool form -0.010 (-0.062 to +0.038). So the cost lies within the control's spread. An earlier 24 rounds of the bool form alone gave +0.046 (-0.002 to +0.085), 18 of 24 positive, at loads up to 22, which the 14-round run did not repeat.

Docs: docs/plans/screen-space-reflections.md, "4. The trace through the pyramid" (which pixel holds a point on an edge after a crossing and after a descent, below(s_entry), the rule before this ticket), its test list and its hand mutations. ARCHITECTURE.md's SSR trace paragraph.

Checks: dotnet build Tertius.slnx -c Checked, 0 warnings. dotnet test --solution Tertius.slnx -c Checked: 9,989 passed, 0 failed, 14 skipped (Rendering 2,401). The same with -c Release: 9,976 passed, 0 failed, 14 skipped.
