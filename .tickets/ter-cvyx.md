---
id: ter-cvyx
status: closed
deps: []
links: [ter-eqtb, ter-pywg]
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


## Notes

**2026-11-02T18:02:21Z**

2026-11-02: launched as workflow run ceff2951-d5f6-4f5c-a308-470c6d8857dd, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (build, review, fix), branch ter-cvyx-trace-march, base 6c71a2943 (brief /tmp/briefs/cvyx.txt). The orchestrator's decision: the march stays the oracle of the trace, because the pyramid is an acceleration structure whose answer must not depend on the walk's path; a fork only if that cannot hold or costs more than about 0.05 ms on the step view.

**2026-11-02T19:06:05Z**

2026-11-02, the fix (branch ter-cvyx-trace-march from 6c71a2943). The march stays the oracle (the orchestrator's decision), and the trace now gives its answer.

Rates. The search: the scene and ray generator of the property test, seed 7, 2,000,000 rays, Checked. This branch before the fix: 92 disagree (1 in 21,700). The code before bounded planes (b2a9f7120, exported with git archive into /private/tmp): 55 disagree (1 in 36,400), all of kind 2. After the fix: 0 of 30,000,000 rays (seeds 7, 1, 2, 3, 4 and 5, 5,000,000 each).

Kind 2, the cause (50 of the 92, and all 55 of the base). The crossing nudge moved a position on a cell's edge 0.01 px ahead on both axes. Near a pixel's corner it skipped a pixel that the image crosses for less than 0.01 px. Traced ray 22280 (seed 7): the image leaves pixel (23, 41) across x = 23 at s = 1.327571, where y = 41.9955. The nudge gives (22.99, 42.0055), so the march goes to (22, 42) and never tests (22, 41), which the image crosses for 0.047 px across and 0.0045 px down. The trace reached (22, 41) by a descent at level 1, with its solved entry at (22.979, 41.9975) inside that pixel and no nudge, and hit it with q = 0. The march hit (22, 42) with q = 0.0013. Ray 72816 is the same at (92, 74.0084): the nudge skips (91, 74). So the march, not only the trace, broke the claim "every pixel in order", and the trace's own crossings skipped such pixels too: a march that steps across the edges that the image leaves through disagreed with the old trace on 701 of the 2,000,000 rays.

Kind 1, the cause (42 of the 92). Traced ray 62162: pixel (68, 34) has a surface part from y 34 to 34.5 (it stops at the center line toward its lower neighbor), with w_c 0.211514 and a gradient of 0.012554 per pixel down. The march's stretch runs from the pixel's entry edge (y 35, ray 0.214615, surface 0.211514 past the bend) to its exit (y 34, ray 0.207321, surface 0.205237): in front of the front at both ends, a miss. It hits (70, 10) later. The trace descended at level 3 to a solved entry at (68.255, 34.575) with the carried inW 0.211514, where the surface past the bend is also 0.211514: front_in = 0, a hit. The ray really passes inside the slab at the bend (at y 34.5 it stands at 0.21097 against 0.21151), which is the documented miss of the bend: the march makes it, the trace did not.

The change, in C# and in GLSL alike (ScreenRay.Trace and traceRay, ScreenRay.MeetPixel and meetsPixelSurface):
- The walk stands in a pixel. The cell of each level is the one that holds it. A miss moves it into the cell across the edges that the image leaves through (cellExit / CellExit now also give those edges), a descent to an entry past s moves it to the cell's pixel that holds the entry, and an entry at s keeps it. pixelIn / PixelIn clamp floor(position) into the cell. The crossing nudge, its constant and the on-edge flag are gone.
- The level-0 test reads the stretch from the pixel's entry edge to its exit, with w at the entry edge, also after a descent. The same edge gives q, so one surface read fewer per level-0 test.
- Every texelFetch stays inside its image: tracedCellBounds and meetsPixelSurface keep their beyondTheScreen guards, and a cell is floor(pixel / 2^k) of a pixel that the guard tests.
- The test's march now steps across the edges that the image leaves through, so it visits every pixel that the image crosses.

Tests, each red first for the reason named:
- ARayThatADescentBringsInsideAPixelWhoseSurfaceBendsFindsThePixelThatTheMarchFinds, three rays (62162, 32413, 109079): red, the trace hit (68, 34), (68, 34) and (108, 25) where the march missed or hit (70, 10).
- ARayThatCrossesAPixelOnlyNearItsCornerFindsThePixelThatTheMarchFinds, three rays (100887, 504906, 1892628, found with the new march and the old trace): red, the trace left the segment and the march hit (0, 5), (127, 78) and (34, 26).
- ATraceMeetsAPixelThatTheImageCrossesOnlyNearItsCorner (16 × 16 sky, one surface pixel crossed for 0.04 × 0.004 px): red, the trace left the segment after 6 steps.
- RandomRays.TheHierarchicalTraceFindsThePixelThatAPixelByPixelMarchFinds, now 1,000,000 rays (was 10,000) in a nested class: red, ray 7453 hit (77, 6) against the march's (76, 6). It runs in about 3 s after the per-pixel assertion left the march.

Images (captures/cvyx in this worktree, Release, headless, 1920 x 1080; main is 6c71a2943). A control build (a byte copy of main) gives byte-identical images. No change is visible. Pixels more than 4 levels off and the largest change: pbr-spheres low (the step view) 5 px, 10; pbr-spheres front 0, 3; mirror-balls balls 1 px, 45; plate and belly identical; reflections front 50 px, 44; low 0, 4; edge 0, 1. At the old limit 0.6 (ter-eqtb's p-maxr06.patch) every film view changes at single scattered pixels: the floor film 9 px, 10; unbelt 154 px, 47; spinward 82; chute-top 74; brake-run and antispinward about 45 to 55; deck 16; stack 25; chute 22. They are pixels where a corner sliver or a bend pixel now gets the march's answer. Isolated dots appear and disappear in about equal numbers (for example reflections front 11 dots in main, 15 after; chute-top 14 and 2). Sheets: sheets/shot-step-view.png, shot-reflections-front.png, shot-unbelt06.png, shot-chute-top06.png (main | ter-cvyx | difference x8).

Cost (timing/runs.txt, summary.txt): interleaved rounds in a rotated order of main, main2 (the byte-copy control) and after, each under the GPU lock, the four reflection passes' summed mean by the renderer's timer, the median of the per-round differences from main. No xctrace run was made.

| Window | Rounds | main | Control | ter-cvyx | 1-minute load |
|---|---|---|---|---|---|
| step view, pbr-spheres low | 20 | 2.59 ms | +0.014 (-0.203 to +0.064) | +0.017 (-0.149 to +0.098) | 4.3 to 10.5 |
| deck window | 8 | 2.10 ms | +0.022 (-0.014 to +0.058) | -0.010 (-0.068 to +0.069) | 4.3 to 7.9 |
| floor window | 8 | 2.36 ms | -0.003 (-0.026 to +0.031) | +0.005 (-0.043 to +0.056) | 4.2 to 8.1 |

So the cost lies within the control's spread on every window, below the bound of 0.05 ms.

Docs: docs/plans/screen-space-reflections.md, "4. The trace through the pyramid" (the pixel walk, the stretch from the entry edge, the bend's paragraph, the precision note), its constants table (no crossing nudge), its test list, and a line that marks "The shaders" and "The C# copy" as the design's first draft. ARCHITECTURE.md's SSR paragraph on the trace.

**2026-11-02T19:56:24Z**

2026-11-02, the review's fix stage. The review passed all three parts and raised four low findings. All four are fixed.

1. The walk's pixel at a coarse crossing or a descent (correctness). The first fix took P = floor(S(s)) clamped into the cell. On the axis that a coarse crossing does not cross, and on both axes after a descent, that floor can pass a pixel's corner by a rounding while the march's comparison of edge parameters enters the sliver pixel. Reproduced by hand on a 16 × 16 sky: Start (5.656115, 8.483798), Step (10, 2.2023344), constant w 0.5, one surface at (8, 8). The image leaves the level-1 cell (3, 4) across x = 8 at s = 0.23438850 and crosses y = 9 at s = 0.23438855, so the march enters (8, 8). S(s).y rounds to 9.0, so the trace stood in (8, 9), descended there, and left the segment after 8 steps. The test ACoarseCrossingWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters was red for that reason. The fix: PixelAt and pixelAt start from floor(S(s)) and shift each axis by one pixel where the pixel's far edge gives s or less, or its near edge more than s, by the quotients that CellExit and the march compare (EdgeParameters, edgeParameters). Then they clamp into the cell. Random rays do not reach this case: the code under review and the fix both give 0 disagreements of 2,000,000 rays at 128 × 96 and 2,000,000 at 1920 × 1440 (seed 7). After the fix, 2,000,000 more at 1920 × 1440 with seed 1 also gave 0.
2. The level-0 test's exit (design). MeetPixel(pyramid, pixel) and meetsPixelSurface(ray, inverse, pixel, travel, ...) now derive the exit from the pixel with CellExit at level 0, so no caller passes a value that depends on the path. In GLSL, pixelEntry now uses the ray's reciprocals as cellExit does, so a pixel's entry parameter is the same number as its neighbor's exit. Before, it divided.
3. The kind-1 regression's premise (fulfillment). Each case of ARayThatADescentBringsInsideAPixelWhoseSurfaceBendsFindsThePixelThatTheMarchFinds names its pixel ((68, 34), (68, 34), (108, 25)) and first asserts that the pixel's surface stops at a center line and that the ray misses the pixel from its entry edge. FromInverseDepths makes only flat pixels that face the eye, so a synthetic bent pixel needs a new pyramid constructor. The premise check costs none.
4. The docs (fulfillment). The plan's hand mutations no longer name the removed nudge mutants and list this ticket's mutants. crossed reads "the earlier of the two parameters". The walk's paragraph states what holds in floats and what holds only in exact arithmetic. "Every pixel that the image crosses lies on the walk, however near its corner" is gone from both docs and both code comments.

Images (captures/cvyx/g/after2 and after2-06, Release, headless, 1920 × 1080): every view of pbr-spheres, mirror-balls, reflections and the films at the old limit 0.6 is byte-identical to the reviewed build's images. The build embeds the new trace shader: its SPIR-V module lies in after2's Tertius.Rendering.dll and not in after's. So the sheets of the first fix stay valid.

Cost (captures/cvyx/timing2/runs.txt, summary.txt): interleaved rounds in a rotated order of main, main2 (the byte-copy control) and after2, each under the GPU lock, the four reflection passes' summed mean. No xctrace run.

| Window | Rounds | main | Control | ter-cvyx after the fix | Against the mean of main and main2, 95 % bootstrap | 1-minute load |
|---|---|---|---|---|---|---|
| step view, pbr-spheres low | 20 | 2.58 ms | -0.045 | +0.003 | -0.009 (-0.034 to +0.034) | 5.2 to 11.0 |
| deck window | 8 | 2.08 ms | +0.000 | +0.015 | +0.010 (-0.028 to +0.054) | 7.5 to 11.5 |
| floor window | 8 | 2.35 ms | +0.046 | +0.028 | -0.007 (-0.029 to +0.064) | 5.2 to 9.9 |

The cost lies within the control's spread on every window, and on the step view its interval lies inside the bound of 0.05 ms.

**2026-11-02T21:07:01Z**

2026-11-02, the hardening (on 163d4a741, no change to src).

Coverage of the changed lines in src (tools/coverage/run.sh's command for Tertius.Rendering.Tests only, Debug and Release, then changed-lines.py --base 6c71a2943): 34/34 lines and 20/20 branches of ScreenRay.cs. Nothing uncovered. GLSL has no line coverage: the mutants below test it.

Mutants (tools/mutate.py, /tmp/ter-cvyx-mutants.json, 31 rows). C# rows run *.ScreenRayTests* (with the nested RandomRays), GLSL rows run ReflectionShaderTests, ReflectionRenderTests and GrazingReflectionRenderTests. Six C# rows first failed to build (the analyzers reject a value that the mutant no longer reads, and unreachable code), so they were rewritten to compile with the same behavior (for example "* 0") and run again. C02, C08 and C14 were run again with --only after the new tests and gave killed. The table merges the three runs, the last result of each row:

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| C01 | src/Tertius.Rendering/Shading/ScreenRay.cs:319 | PixelAt: no shift, floor(S(s)) alone | killed | ScreenRayTests.ACoarseCrossingWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters, ScreenRayTests.ASlabEntryWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters | 8.0 |
| C02 | src/Tertius.Rendering/Shading/ScreenRay.cs:321 | PixelAt: no clamp into the cell | killed | ScreenRayTests.ASlabEntryOnItsCellsExitStaysInTheCellAndMeetsThePixelThatTheImageLeavesThere | 7.1 |
| C03 | src/Tertius.Rendering/Shading/ScreenRay.cs:329 | PixelShift: exit <= s -> exit < s | survived |  | 7.3 |
| C04 | src/Tertius.Rendering/Shading/ScreenRay.cs:331 | PixelShift: entry > s -> entry >= s | survived |  | 7.5 |
| C07 | src/Tertius.Rendering/Shading/ScreenRay.cs:317 | PixelAt: near edge on the far side (travel's sign) | timeout |  | 122.4 |
| C08 | src/Tertius.Rendering/Shading/ScreenRay.cs:354 | CellExit: a corner crosses only x, never diagonally | killed | ScreenRayTests.ARayThroughPixelCornersStepsDiagonallyAndNeverStandsInAPixelThatItTouchesOnlyAtACorner | 6.8 |
| C10 | src/Tertius.Rendering/Shading/ScreenRay.cs:196 | Trace: a descent to an entry at s also moves the pixel | survived |  | 7.3 |
| C11 | src/Tertius.Rendering/Shading/ScreenRay.cs:196 | Trace: a descent never moves the pixel | killed | ScreenRayTests.ARayThatADescentBringsInsideAPixelWhoseSurfaceBendsFindsThePixelThatTheMarchFinds, ScreenRayTests.EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanet | 6.6 |
| C12 | src/Tertius.Rendering/Shading/ScreenRay.cs:188 | Trace: a miss clamps into the old cell | timeout |  | 122.7 |
| C13 | src/Tertius.Rendering/Shading/ScreenRay.cs:188 | Trace: a crossing takes floor(S(s)) on the uncrossed axis, no shift | killed | ScreenRayTests.ACoarseCrossingWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters | 6.6 |
| C14 | src/Tertius.Rendering/Shading/ScreenRay.cs:197 | Trace: a descent takes floor(S(entry)), no shift | killed | ScreenRayTests.ASlabEntryWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters | 7.7 |
| C15 | src/Tertius.Rendering/Shading/ScreenRay.cs:160 | Trace: the walk starts in the origin's pixel | killed | ScreenRayTests.ARayThatADescentBringsInsideAPixelWhoseSurfaceBendsFindsThePixelThatTheMarchFinds, ScreenRayTests.ARayRisingFromAFlatFloorDoesNotHitTheFloor, ScreenRayTests.ARayThatCrossesAPixelOnlyNearItsCornerFindsThePixelThatTheMarchFinds | 22.3 |
| C16 | src/Tertius.Rendering/Shading/ScreenRay.cs:169 | Trace: the cell from the position at s, not the walk pixel | timeout |  | 123.8 |
| C17 | src/Tertius.Rendering/Shading/ScreenRay.cs:240 | MeetPixel: the exit of the level-1 cell, not the pixel's | killed | ScreenRayTests.ARayThatComesDownOntoAFloorSeenAtAGrazingAngleHitsItsFrontWhereItCrossesIt, ScreenRayTests.ARayThatADescentBringsInsideAPixelWhoseSurfaceBendsFindsThePixelThatTheMarchFinds | 6.5 |
| C19 | src/Tertius.Rendering/Shading/ScreenRay.cs:171 | Trace: the level-0 stretch from the walk's s and inW, not the pixel's entry edge (the old rule) | killed | ScreenRayTests.ARayThatADescentBringsInsideAPixelWhoseSurfaceBendsFindsThePixelThatTheMarchFinds, ScreenRayTests+RandomRays.TheTracesHitIsWhatItsPixelAloneGivesHoweverTheWalkReachedThePixel | 9.3 |
| S01 | src/Tertius.Rendering/Shaders/reflection.glsl:144 | pixelAt: no shift, floor(S(s)) alone | survived |  | 54.9 |
| S02 | src/Tertius.Rendering/Shaders/reflection.glsl:146 | pixelAt: no clamp into the cell | survived |  | 47.7 |
| S03 | src/Tertius.Rendering/Shaders/reflection.glsl:144 | pixelAt: exit <= s -> < | survived |  | 48.7 |
| S04 | src/Tertius.Rendering/Shaders/reflection.glsl:144 | pixelAt: entry > s -> >= | survived |  | 50.3 |
| S05 | src/Tertius.Rendering/Shaders/reflection.glsl:129 | cellExit: a corner crosses only x, never diagonally | survived |  | 72.0 |
| S06 | src/Tertius.Rendering/Shaders/reflection.glsl:280 | traceRay: a descent to an entry at s also moves the pixel | survived |  | 55.0 |
| S07 | src/Tertius.Rendering/Shaders/reflection.glsl:280 | traceRay: a descent never moves the pixel | killed | ReflectionRenderTests.AFarPlanetShowsInANearMirror | 48.7 |
| S08 | src/Tertius.Rendering/Shaders/reflection.glsl:279 | traceRay: a miss clamps into the old cell | killed | GrazingReflectionRenderTests.AMirrorBallReflectsAGlowingFloorThatTheEyeSeesAtAGrazingAngleWithNoBands, ReflectionShaderTests.EachTexelOfTheHalfResolutionTraceHoldsTheRayOfItsRepresentativePixel, ReflectionShaderTests.EachTexelOfTheHalfTraceHoldsNoRayOrAMissOfZerosOrAHitAtItsDepthWhoseCoverageAHalfFloatHoldsAsANormal | 45.7 |
| S09 | src/Tertius.Rendering/Shaders/reflection.glsl:256 | traceRay: the cell from the position at s, not the walk pixel | killed | GrazingReflectionRenderTests.AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines, GrazingReflectionRenderTests.AMirrorBallReflectsAGlowingFloorThatTheEyeSeesAtAGrazingAngleWithNoBands, ReflectionShaderTests.EachTexelOfTheHalfResolutionTraceHoldsTheRayOfItsRepresentativePixel | 48.4 |
| S10 | src/Tertius.Rendering/Shaders/reflection.glsl:247 | traceRay: the walk starts in the origin's pixel | killed | GrazingReflectionRenderTests.AMirrorBallReflectsAGlowingFloorThatTheEyeSeesAtAGrazingAngleWithNoBands | 48.7 |
| S12 | src/Tertius.Rendering/Shaders/reflection.glsl:165 | pixelEntry: a division, not the reciprocals | survived |  | 49.4 |
| S13 | src/Tertius.Rendering/Shaders/reflection.glsl:211 | meetsPixelSurface: the exit of the level-1 cell, not the pixel's | survived |  | 48.1 |
| S14 | src/Tertius.Rendering/Shaders/reflection.glsl:141 | pixelAt: near edge on the far side (travel's sign) | killed | GrazingReflectionRenderTests.AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines, GrazingReflectionRenderTests.AMirrorBallReflectsAGlowingFloorThatTheEyeSeesAtAGrazingAngleWithNoBands, ReflectionShaderTests.EachTexelOfTheHalfResolutionTraceHoldsTheRayOfItsRepresentativePixel | 126.7 |
| S15 | src/Tertius.Rendering/Shaders/reflection.glsl:200 | traceRay: the level-0 stretch from the walk's s and inW, not the pixel's entry edge (the old rule) | survived |  | 67.7 |
| S16 | src/Tertius.Rendering/Shaders/reflection.glsl:206 | meetsPixelSurface: no beyondTheScreen guard | survived |  | 77.5 |
| S17 | src/Tertius.Rendering/Shaders/reflection.glsl:159 | tracedCellBounds: no beyondTheScreen guard | survived |  | 54.6 |
31 rows: killed 14, timeout 3, survived 14, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

The three timeouts (C07, C12, C16) are walks that stop moving, so RandomRays runs each ray to its 1,000,000 steps. The baseline of the same class takes 3.5 s.

Triage of the 14 survivors:
- C03, C04, S03, S04 (the shift's boundaries, exit <= s and entry > s): judgment call, pending ter-pywg. They change only an exact tie of s with a pixel's edge, where they put the walk in the pixel that the image leaves at s. The one tie that tells them apart is the defect ter-pywg (below). By a reading of the code (not run), C04 puts the walk there in the pixel that the march hits, and the code under test does not. So no test that agrees with the march can kill them now. The fix of ter-pywg decides the boundary and brings its test.
- C10, S06 (a descent to an entry at s also moves the pixel): equivalent. The walk's pixel always comes from PixelAt at the current s (the start pixel only at level 0, where no descent happens), and s changes only together with the pixel or to an entry at or below s. PixelAt at the same s gives the same pixel before the clamp, and the clamp into the child cell that holds the kept pixel gives the kept pixel back on each axis. So the decision "an entry at s keeps the pixel" has no behavior to lock in: it saves one PixelAt.
- S01, S02, S05 (pixelAt without the shift, without the clamp; the corner tie in cellExit): judgment call. Their C# twins C01, C02 and C08 are killed by ACoarseCrossingWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters, ASlabEntryWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters, ASlabEntryOnItsCellsExitStaysInTheCellAndMeetsThePixelThatTheImageLeavesThere and ARayThroughPixelCornersStepsDiagonallyAndNeverStandsInAPixelThatItTouchesOnlyAtACorner. Each case needs a ray within a rounding of a pixel's corner or edge, and no GPU harness runs a chosen ray through traceRay or compares hit pixels ray by ray: ReflectionShaderTests compares the colors of tile insides with a reach of 2 pixels, which a one-pixel slip at a corner does not change. A GPU harness for hand-built rays costs a new compute entry point and a readback of hit pixels, more than these rows are worth while the C# copy carries the cases.
- S12 (pixelEntry by a division, not the reciprocals): judgment call. The quotient and the product with the reciprocal differ by at most a rounding, which moves a pixel's entry off its neighbor's exit only for a ray within a rounding of an edge. No GPU harness traces such a ray, and the C# copy divides everywhere, so it has no twin.
- S13 (meetsPixelSurface's exit from the level-1 cell): judgment call. Its C# twin C17 is killed by ARayThatComesDownOntoAFloorSeenAtAGrazingAngleHitsItsFrontWhereItCrossesIt and the kind-1 regressions. On the GPU tests' scenes the longer stretch moves a hit by less than the tile reach.
- S15 (the level-0 stretch from the walk's s and inW, the rule before ter-cvyx): judgment call. Its C# twin C19 is killed by the three kind-1 regressions and by the new TheTracesHitIsWhatItsPixelAloneGivesHoweverTheWalkReachedThePixel. The kind-1 rays are C# rays in a C# scene, about 1 in 50,000 random rays, which no GPU frame of the tests reproduces.
- S16, S17 (no beyondTheScreen guard before a texelFetch in meetsPixelSurface and tracedCellBounds): judgment call. Without the guard, texelFetch reads outside the image, which the Vulkan spec leaves undefined without robustImageAccess. On this device the read gives no surface, so every test passes and no test can tell. The guard is there for the spec (CLAUDE.md: write to the Vulkan spec), and review keeps it.

New tests in ScreenRayTests, each red on its mutant first by the table:
- ASlabEntryWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters: the coarse-crossing ray of the review's finding 1, with an inverse depth that falls to the surface inside the 5e-8 sliver of pixel (8, 8). The descent's position rounds to y = 9. Kills C01 and C14.
- ASlabEntryOnItsCellsExitStaysInTheCellAndMeetsThePixelThatTheImageLeavesThere: exact binary fractions, the slab entry on a level-3 cell's exit. Kills C02.
- ARayThroughPixelCornersStepsDiagonallyAndNeverStandsInAPixelThatItTouchesOnlyAtACorner: 45° through exact corners, with surfaces on every pixel beside the diagonal. Kills C08.
- RandomRays.TheTracesHitIsWhatItsPixelAloneGivesHoweverTheWalkReachedThePixel: 100,000 random rays (seed 20261102), the hit's position and q equal MeetPixel of its pixel. Kills C19.

Defect found (not fixed, src unchanged): ter-pywg. A slab entry exactly on an edge between two pixels inside a cell puts the walk in the later pixel, while the march meets the earlier pixel at the end of its stretch. Reproduction in the ticket: the march gives (3, 4, 0), the trace leaves the segment after 6 steps. The test is not committed, because it is red on this branch.

Checked: dotnet build Tertius.slnx -c Checked, then dotnet test --solution Tertius.slnx -c Checked: 9,972 passed, 0 failed (Rendering 2,384, 1 skipped).

**2026-11-02T21:19:18Z**

Merged into main in c2645b0ac (orchestrator, 2026-11-02), after the hardening (8ce43c71c). tools/test-all.sh on the merge passed every module in Checked and Release. The hardening's defect, an exact tie of a slab entry with a pixel's edge inside a cell, is ter-pywg, which now blocks the epic ter-b2da. Closed.
