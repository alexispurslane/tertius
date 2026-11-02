---
id: ter-ipc1
status: in_progress
deps: []
links: []
created: 2026-11-02T16:37:04Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, bounce-light]
---
# One rule for where a depth sample's plane holds, for the reflections' pixels and the bounce light's near steps

Found in ter-eqtb's review (finding 3). Two rules now decide where the plane of a depth sample, through its point along its geometric normal, holds. The reflections' pixel surface (pixelPlane and pixelSurface in depth_pyramid.glsl, PixelPlane and PixelSurface in DepthPyramid.cs) extends a full-resolution pixel's plane toward an edge neighbor only where each plane holds the other's center within 1 % in inverse depth, and otherwise stops it at the center line; toward the sky it always stops. The bounce light's near steps (stepSurfacePoint and depthsAround in sectors.glsl, SectorSearch.SurfacePoint and DepthsAround) extend a half-resolution texel's plane to the step's exact place where the plane's depth lies between the depths of the texel and its two neighbors toward the place, widened by CenterBias, with sky at SkyDepth, so the plane reaches up to half a texel toward the sky. Both share kappa (ThicknessShare). A later change to one rule, such as ter-y1s7's impostor normal, a new tolerance or a new resolution, must find both. The readers differ: a pixel must bound pyramid cells over its whole square, and a near step reads one sub-texel place.

## Design

Design it twice: (1) the continuation test at the step's texel, so a near step extends the plane toward a neighbor only where the two planes continue each other; (2) the depths-around rule for a pixel's edges, so a pixel's plane reaches an edge where the plane's depth there lies between the depths around. Report the image changes of each on the step view, the bounce light's corner tests and demo-exit's deck, and the cost.

## Acceptance Criteria

One function per language decides where a depth sample's plane holds, and both readers call it; the docs (screen-space-reflections.md 'The pixel's surface', ARCHITECTURE.md) name one rule; the user chose between the two designs with images.


## Notes

**2026-11-02T18:02:21Z**

2026-11-02: launched as workflow run bebaa068-a164-4d94-a183-f53f31faa869, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-ipc1-one-plane-rule, base 6c71a2943 (brief /tmp/briefs/ipc1.txt): a probe of the ticket's two designs that ends at a fork for the user.

**2026-11-02T18:56:02Z**

Probe of the two designs, ending at a fork for the user (builder, 2026-11-02). Base 6c71a2943 (main), Release, headless, 1920 x 1080. Every GPU capture and timing run held /tmp/tertius-gpu-measure.lock for that run only, and no xctrace run was made. The evidence is in captures/ipc1/ of the worktree ter-ipc1-one-plane-rule (git ignores captures/). Its README.txt lists every script, patch and gallery, and all paths below are relative to it. No file under src, tests, shaders or tools changed in the commit.

The two probes. Each is a patch on main in both languages, built from an export in /private/tmp/ipc1 (probes/probe-build.sh).
- Design 1, probes/p-d1.patch: the continuation test for both readers. The one rule is `planesContinue` and `sampleSurface` in depth_pyramid.glsl, and `PixelPlane.ContinuesInto` and `PixelSurface.Over` in DepthPyramid.cs. A sample's surface is its plane over the part of its square, one unit of its own grid wide, that reaches each edge where the neighbor continues the plane, and keeps the plane's value past the part. A pixel builds its plane in pixels from the distance and normals targets and tests its four neighbors. A near step builds its texel's plane in texels from the half-resolution depth-and-normal copy (`texelPlane`, `SectorSearch.TexelPlane`), tests only the two neighbors toward its exact place, and stands on the exact ray at the surface's inverse depth there. Both readers read one surface. No branch on the reader: they differ only in the image that they read and the neighbors that they need. A neighbor beyond the image continues, and sky stops, for both.
- Design 2, probes/p-d2.patch: the depths-around rule for both readers. The one rule is `liesAmid` in depth_pyramid.glsl and `PlaneReach.LiesAmid` in DepthPyramid.cs: the plane's depth at a place lies between the least and the greatest depth of the samples around the place, widened by 10⁻⁵, sky at 10¹⁸ m. A near step calls it at its exact place, unchanged. A pixel calls it at the middle of each edge, where the samples around are the pixel and the neighbor across that edge (the step's triangle with sgn 0 on the other axis), and its part reaches that edge where the plane holds there. A neighbor beyond the image reads as the sample itself, the near steps' border read, for both. One function decides where the plane holds, with no branch on the reader. But what stands where it does not hold stays two models: the pixel keeps the plane's value at its center line, the step stands at P(t). A per-place test cannot serve the pixel: the trace's level-0 test reads the surface at the two ends of a stretch and takes it as affine between, so a surface that jumps back to w_c wherever a place fails the test breaks it.

Views (analysis/all-vs-base.txt, all06-vs-base06.txt: the share of pixels more than 4 levels off, and the largest difference). base2, the byte-copy control, is byte-identical to base on all 35 views it ran.

| View | Design 1 | Design 2 | Cause |
|---|---|---|---|
| pbr-spheres low, the step view | 0.000 %/2 | 0.012 %/56 | d2: scattered pixels in the gold spheres' reflections of the red sphere's edges |
| pbr-spheres front | 0.000 %/2 | 0.012 %/73 | d2: the same gold spheres |
| mirror-balls balls | 0.001 %/36 | 0.053 %/147 | d2: light seams on the ball rims, where a rim pixel's plane reaches toward the far floor |
| reflections front | 0.000 %/2 | 0.111 %/100 | d2: the reflected capsule's edge on the gold strip gains light dots |
| reflections low | 0.000 %/1 | 0.008 %/45 | d2: the same edge |
| floor film at 0.6 | 0.000 %/4 | 1.198 %/55 | d2: the outlines of the reflected far floor and walls move, with a new vertical cut at x about 605 |
| other films at 0.6 | at most 0.001 %/34 | 0.03 to 0.69 % | d2: the same reach at depth edges (brake-run 0.687 %, antispinward 0.584 %) |
| demo-surface (7 views) | at most 0.003 %/115 | byte-identical | d1: a few dozen pixels along thin dark rails, by the near steps |
| deck-late next | 0.001 %/48 | byte-identical | d1: a short run of pixels on a box's top edge, by the near steps |
| the 18 film views at 0.37 | at most 0.001 %/27 | byte-identical | d1: single pixels, by the near steps |
| bounce-raised-box, bounce-red-wall, hair-rugs, mirror-balls plate and belly, reflections edge | 0, largest 1 to 6 | 0, largest 0 to 2 | rounding |

So design 1 leaves every reflection as it is and moves the bounce light in a few dozen pixels per view, none visible at full size. Design 2 leaves the bounce light as it is and changes the reflections at every depth edge and silhouette.

Tests of both readers (probes/tests.sh: DepthPyramidTests, ScreenRayTests, PyramidRenderTests, GrazingReflectionRenderTests, ReflectionRenderTests, FlatFloorSectorsTests, ConvexEdgeSectorsTests, SectorSearchTests, AmbientOcclusionRenderTests, BounceLightShaderTests, OpenShareCreaseTests; Checked, 204 tests). Base: 204 passed.
- Design 1: 2 failed. `OnATiltedPlaneEveryStepOfEverySliceKeepsItsSurfacePointOnThePlane` fails only on precision: the C# step now runs on the float PixelPlane, as the GPU does, and its points lie 10⁻⁷ off the plane against a tolerance of 10⁻¹². It passes at 10⁻⁵. `AStepBetweenTheLastRowOfAFloorAndTheSkyKeepsItsSurfacePointBeyondThatRow` fails on the rule: toward the sky the floor's plane now stops at the last row's center line, so a step past it stands on its exact ray at that row's inverse depth, 0.28 m above the floor at 160 m (Y −0.716 for −1), half a texel of screen. It fails at 10⁻⁵ too. FlatFloorSectorsTests, ConvexEdgeSectorsTests, the impostor test of AmbientOcclusionRenderTests and the GPU-against-C# sector tests pass.
- Design 2: 9 failed. Behavior: `AMirrorFloorThatTheEyeSeesAtAGrazingAngleReflectsTheOutlineOfABallAgainstTheSkyWithNoStreak` (full and half: a streak at (305, 216) of 0.41, because the plane reaches toward the sky again) and `ACellAtADepthEdgeBoundsTheNearSurfaceOnlyToTheCenterLineOfItsLastRowWhereItsPlanePassesTheFarSurface` (far bound 0.19245 against 0.19566: the near panel's plane reaches the far wall, ter-eqtb finding 1's light bars). Unit tests of the old pixel rule: `TowardTheSkyAPixelsPlaneStopsAtItsCenterLine`, `APlaneStopsAtItsCenterLineTowardAnEdgeOnNeighbor`, `APlaneThatPassesTheCenterOfAFarNeighborByChanceStopsAtItsCenterLineTowardIt`, `AtTheScreensEdgeAPixelsPlaneReachesThePixelsEdge` (the border read stops it), and both off-plane cases of `ANeighborWithinOnePercentOfThePlaneInInverseDepthContinuesIt`. The bands test (a) and the dotted lines test (b) pass.

Cost (timing.txt, runs.txt). 20 interleaved rounds per window of base, base2 (the byte-copy control), d1 and d2 in a rotated order, each a locked run of 120 frames. The median of the round-by-round differences from base, the 10th to 90th percentile, and the rounds above 0. The four reflection passes are depth pyramid, radiance pyramid, reflection trace and reflections, summed by the renderer's timer.

| Window, 1-minute load | Pass | Base | Control | Design 1 | Design 2 |
|---|---|---|---|---|---|
| step view (pbr-spheres low), 4.6 to 15.5 | four reflection passes | 2.571 ms | +0.005 | −0.018 (−0.19 to +0.12) | −0.361 (−0.43 to −0.24, 0/20) |
| | depth pyramid | 0.602 | −0.009 | +0.003 | −0.135 (1/20) |
| | occlusion sectors | 1.098 | +0.001 | +0.421 (+0.33 to +0.49, 20/20) | −0.008 |
| | frame span | 9.023 | +0.018 | +0.483 (17/20) | −0.385 (3/20) |
| deck, 2.8 to 6.6 | four reflection passes | 2.110 | +0.007 | −0.065 (1/20) | −0.193 (0/20) |
| | depth pyramid | 1.059 | +0.000 | −0.061 (1/20) | −0.198 (0/20) |
| | occlusion sectors | 1.760 | +0.028 | +0.594 (+0.48 to +0.69, 20/20) | +0.048 |
| floor, 3.2 to 8.3 | four reflection passes | 2.368 | −0.007 | −0.065 (1/20) | −0.212 (0/20) |
| | depth pyramid | 1.276 | −0.016 | −0.059 (1/20) | −0.213 (0/20) |
| | occlusion sectors | 1.837 | −0.006 | +0.636 (+0.53 to +0.73, 20/20) | +0.030 |

Design 1's step reads the depth and normal of two neighbors and builds three planes, where the base read two depths: +0.42 to +0.64 ms on the sectors pass. Its depth pyramid pass gives byte-identical images but runs 0.06 ms faster on the deck and the floor, likely the compiler's output for the refactored shader. Design 2's pixel reads four neighbor distances and no neighbor normals, where the base built four neighbor planes: −0.13 to −0.21 ms on the pyramid pass and −0.19 to −0.36 ms on the four passes.

Sheets (before | design 1 | design 2 | the difference):
- sheets/shot-ball-rims.png: mirror-balls balls, a rim at 5x: design 2 puts light seams on the rim, design 1 matches the base.
- sheets/shot-capsule-edge.png: reflections front, the capsule's reflected edge at 4x: design 2 adds light dots along it.
- sheets/shot-floor06.png: the floor film at 0.6: design 2 moves the reflected outlines and adds a vertical cut, design 1 matches the base.
- sheets/shot-deck-bounce.png: deck-late next at 5x, where design 1 moves the bounce light most (48 levels on a box's top edge): no change to see at full size.
- sheets/shot-step-view.png: the step view at 5x: design 2 changes scattered pixels on the gold spheres, design 1 matches the base.

The fork, for the user's choice:
- Design 1, the continuation test for both readers (recommended). The reflections keep ter-eqtb's image and tests exactly. The near steps change in a few dozen pixels per view, none visible, and pass the convex-edge, flat-floor and impostor tests. One surface model serves both readers, so a later change (ter-y1s7's impostor normal, a tolerance, a resolution) has one place to go. Cost: +0.42 to +0.64 ms on the sectors pass in every frame with the bounce light on, unoptimized. Toward the sky a step now stops at its texel's center line and stands up to half a texel above the floor's line, where the base extended the floor's plane: the floor-to-sky test pins the old rule and must change.
- Design 2, the depths-around rule for both readers. The bounce light keeps its image exactly, and the reflections get 0.19 to 0.36 ms cheaper. But the reflections lose part of ter-eqtb's fix: a pixel's plane reaches half a pixel toward the sky and toward a far surface again, which brings back the streak against the sky (test (c) red), the near panel's reach across a depth edge (finding 1's light bars, red), light seams on mirror-ball rims and a reshaped floor film at 0.6. Two models of what stands past the plane remain (the center line for a pixel, P(t) for a step).
- No third design serves both better on this evidence. The one variant seen, design 1 with the step's old fallback P(t) past the part, trades the step that stands half a texel above the floor toward the sky for one that stands half a texel off its slice (ter-rxjr's fault), and keeps two models of what stands past the plane.

For the build after the choice: with design 1, the C# step's points carry float rounding (10⁻⁷), so the two SectorSearchTests tolerances, or a double copy of the plane, must change; the floor-to-sky test changes its expectation; and a per-texel continuation mask written by the occlusion depth pass can likely take back much of the +0.4 to +0.6 ms (not measured). With design 2, the reflection tests above go red by design, and the user's choice A of ter-eqtb would be partly undone.

**2026-11-02T19:39:11Z**

2026-11-02, the user's choice after the probe (run bebaa068, sheets in captures/ipc1/sheets): design 1, one rule for both readers, the continuation test of ter-eqtb's bounded planes. No visible change, every reflection byte for byte, +0.42 to +0.64 ms on the occlusion sectors pass before optimization. Rejected: design 2 (it undoes the user's choice A for ter-eqtb: the sky streak, rim seams, capsule dots, 9 tests red) and keeping the two rules. Settled by the orchestrator: try a per-texel continuation mask written by the occlusion depth pass, kept only for the same images at less cost; a step past its part stands on the clamped surface, not on P(t). Continuation launched as workflow run de392476-35cd-4b27-95d5-7ed4d6e020ee on this branch (brief /tmp/briefs/ipc1-cont.txt).

**2026-11-02T20:46:41Z**

The build of design 1, the user's choice of 2026-11-02 (builder, 2026-11-02). Commits on this branch: e02d76ead (merge of main), 484e21cb2 (the one rule), 1f117f383 (the continuation flags), and the docs and this note. Evidence in captures/ipc1/ of the worktree (git ignores it), listed at the end of its README.txt.

The change. One function per language decides each part of where a depth sample's plane holds, and both readers call it. A depth sample is a pixel of level 0 or a half-resolution texel, and each reader builds its sample's plane in its own grid.
- GLSL, depth_pyramid.glsl: `samplePlane` (the plane from a center, w_c, n · P, |P| and n · Δr), `planesContinue` (the continuation test, both directions), `sampleSurface` (the surface from the four answers), `surfaceW`. `pixelPlane` and `pixelSurface` call them for a pixel. texel_surface.glsl (new): `texelDepthAndNormal` (the depth pass's copy from the G-buffer), `texelPlane` (the texel's plane in the texel grid), `continuationFlags` (the four answers for a texel) and `texelSurface`. sectors.glsl's `stepSurfacePoint` stands the step on its exact ray at `surfaceW(texelSurface(...))` at its place.
- C#, DepthPyramid.cs: `PixelPlane.OfSample`, `PixelPlane.ContinuesInto`, `PixelSurface.Over`, `PixelSurface.InverseDepthAt`. `PixelPlane.Of` and `PixelSurface.Of` call them for a pixel, and `SectorSearch.TexelPlane` and `SectorSearch.TexelSurface` for a texel.
- No code branches on the reader. The readers differ only in the image that they read. A neighbor beyond the image continues the plane and sky does not, for both. Past its part a step stands on the clamped surface, not on P(t), so it always stays on its exact ray.
- Removed: `SectorSearch.SurfacePoint`, `SectorSearch.DepthsAround`, `depthsAround`, and the step's border read of the view depth.
- The continuation flags (the settled choice): the occlusion depth pass tests each texel's four neighbors once per frame and stores four bits in `OcclusionImages.Continuation`, R8_UINT, occlusion set binding 9. A step reads its texel's byte in place of two neighbors' copies and planes. R8_UINT storage: the spec's table "Mandatory format support: 1-3 component 8-bit" requires it on a device with `shaderStorageImageExtendedFormats`, which the renderer requires, and `OcclusionImages` checks it. The occlusion set now binds 10 storage images, as the depth pyramid's tail already does, so the device requirement does not change. Every texelFetch stays inside its image: a neighbor outside the half image reads nothing, and one inside has its pixel 2t inside the full image.

Tests (red on the code before this ticket, for the reason given):
- `ANearStepTowardAnEdgeNeighborWhosePlaneDoesNotContinueItsTexelsStopsAtTheTexelsCenterLine` (SectorSearchTests): planks one texel wide before a far wall. Red: steps stood at their plane's value at the exact place, off by up to 2 × 10⁻⁴ m⁻¹, against 2 × 10⁻⁶.
- `AStepBetweenTheLastRowOfAFloorAndTheSkyStopsAtThatRowsCenterLine` (replaces ...KeepsItsSurfacePointBeyondThatRow): red, steps stood at 223 and 237 m, beyond the last row's 160 m.
- `ANearStepStandsOnTheSurfaceThatThePixelOfItsGridHas`: the C# step and `PixelSurface` agree to 10⁻⁴ on a box, a floor that ends before the sky and the screen's edges. Red: differences of 0.057 to 0.125.
- `OnATiltedPlaneEveryStepOfEverySliceKeepsItsSurfacePointOnThePlane`: the plane's tolerance is now 10⁻⁵ m (was 10⁻¹²). I kept the float plane in C#, as the GPU builds it, so that one C# function decides, and a double copy would be a second rule. The slice-line tolerance stays 10⁻⁹.
- `AFlatFloorAloneBlocksNothingAwayFromItsOutlineAgainstTheSkyAndBouncesNothingOnItself` (BounceLightRenderTests, renamed): the probe did not run this class. Under design 1, 136 of 26,000 floor texels near the floor's outline against the sky block sectors before the denoise, the least open share 0.878, because the last row stands up to half a texel above the floor. The test now holds that no texel farther than the radius and a texel from the sky blocks, and that no texel bounces. This is the floor-to-sky consequence that the user accepted with design 1.
- OcclusionImagesTests: the continuation image's size and format, and 10 storage images.
- Removed with their functions: `AStepsSurfacePointIsWhereItsExactRayMeetsItsTexelsPlane` (now the tilted-plane test), `AStepWhosePlaneItsExactRayMeetsOnlyBehindTheEyeOrNeverHasNoSurfacePoint` (no longer possible: the surface is positive on the texel's square), `AStepWhosePlaneMeetsItsExactRayOutsideTheDepthsAround...StandsAtItsTexelsOwnPoint` (now the planks test), `AStepOnAWallSquareToTheViewKeepsItsSurfacePointThroughTheRoundingOfItsNormal` (the step is on its exact ray by construction, which the tilted-plane test checks). `AStepAtTheImagesCornerReadsNoTexelOutsideTheImage` holds under the new rule.
- FlatFloorSectorsTests, ConvexEdgeSectorsTests, BounceLightShaderTests (GPU against C#), AmbientOcclusionRenderTests (the impostor test), OpenShareCreaseTests and every reflection test pass unchanged.

Images (Release, headless, 1920 × 1080, galleries g/base, g/nomask, g/mask of pbr-spheres (the step view), mirror-balls, reflections, films, deck-late, demo-surface, bounce-raised-box, bounce-red-wall and hair-rugs: 39 views). nomask and mask are byte-identical to each other and to the probe's d1 on all 39 views. Against main: demo-surface at most 0.003 % of pixels and 115 levels (at-the-seat, a thin dark rail), deck-late-next 0.001 %/48 (a box's top edge), mirror-balls balls 0.001 %/36, every other view at most 27 levels on single pixels. All are bounce-light pixels at depth edges, where a step now stops at its texel's center line. None shows at full size (sheets/build/shot-at-the-seat.png, shot-deck-box-edge.png, shot-at-the-seat-full.png).
Reflections byte for byte: a throwaway test in the exports dumped the depth pyramid's levels 1 to 10, the hit depths and the half-resolution trace of the pyramid tests' two scenes, at half and full resolution. main, a second main run and the mask build are byte-identical. The reflection scenes' captures differ by at most 2 levels, from the bounce light that they show.

Cost (analysis/timing2.txt). 20 interleaved rounds per window in a rotated order of base (main), base2 (its byte copy, the control), nomask and mask, each a locked run of 120 frames, round 100 a trial. The median of the round-by-round differences from base, the 10th to 90th percentile, and the rounds above 0.

| Window, 1-minute load | Pass | Base | Control | Without the flags | With the flags |
|---|---|---|---|---|---|
| step view, 13.0 to 31.2 | occlusion sectors | 1.094 ms | −0.042 | +0.358 (+0.26 to +0.48, 20/20) | −0.029 (−0.16 to +0.07, 6/20) |
| | occlusion depth | 0.160 | +0.002 | −0.001 | +0.064 (+0.05 to +0.07, 20/20) |
| | depth + sectors | 1.254 | −0.034 | +0.361 (20/20) | +0.029 (14/20) |
| | depth pyramid | 0.595 | −0.005 | +0.005 | +0.006 |
| deck, 6.0 to 23.0 | occlusion sectors | 1.767 | +0.017 | +0.609 (+0.24 to +0.83, 20/20) | −0.025 (−0.23 to +0.21, 8/20) |
| | occlusion depth | 0.168 | −0.006 | −0.005 | +0.088 (+0.06 to +0.14, 20/20) |
| | depth + sectors | 1.927 | +0.007 | +0.609 (20/20) | +0.058 (13/20) |
| | depth pyramid | 1.062 | −0.036 | −0.101 | −0.028 |
| floor, 6.7 to 21.9 | occlusion sectors | 1.820 | +0.000 | +0.651 (+0.53 to +0.90, 20/20) | +0.013 (−0.25 to +0.10, 11/20) |
| | occlusion depth | 0.181 | +0.025 | +0.014 | +0.108 (+0.05 to +0.13, 20/20) |
| | depth + sectors | 2.000 | +0.034 | +0.686 (20/20) | +0.093 (15/20) |
| | depth pyramid | 1.264 | +0.024 | −0.035 | −0.022 |

With the flags against without them, the two passes together took −0.33, −0.47 and −0.61 ms (0 or 1 of 20 rounds above 0). So the flags are kept: the same images, and the sectors pass back at main's cost for +0.06 to +0.11 ms on the depth pass. The depth pyramid pass is unchanged within the control's noise. The machine was shared with other agents' builds, so the loads ran high, but the rounds interleave the builds and the control stays within ±0.04 ms.

Docs: docs/plans/screen-space-reflections.md, "The pixel's surface" (one rule for both readers, a table of the shared functions, the flags, the change for the bounce light). docs/plans/screen-space-illumination.md, "A near step" (the step's formula with S_t, "Where the texel's plane holds", the code, the safety table, the device requirements, the images and stocks tables, the known limit toward the sky, the tests and the cost). ARCHITECTURE.md (the depth pass item, the near step, the images and sets with 10 storage images and R8_UINT, the barriers, the pixel's surface, the safety table, the device features and the formats).

For the hardening: the changed lines are in Rendering (GLSL and C#), so they go to tools/mutate.py, not to Stryker. Places worth a mutant: `planesContinue`'s two directions, `sampleSurface`'s four flags and their bit order in `texelSurface` against `continuationFlags`' `EdgeNeighbors`, `continuationFlags`' outside-the-image and sky branches, `samplePlane`'s edge-on test, and the depth pass's sky case. No test reads the continuation image back. BounceLightShaderTests pins the steps that read it, end to end.

**2026-11-02T21:36:15Z**

The fix stage after the review (2026-11-02). The review passed correctness, fulfillment and design, with six low findings. Each finding and its resolution:
1. The image-border and sky policy sat at four reader sites. It now lives in the shared rule. GLSL `neighborContinues(plane, insideImage, seesSurface, other)` in depth_pyramid.glsl replaces `planesContinue`. C# `PixelPlane.ContinuesInto(insideImage, neighbor)` takes the same facts. The readers (`continuesPlane`, `continuationFlags`, `PixelSurface.Continues`, `SectorSearch.TexelSurface`) only say whether the neighbor lies inside their image and what it sees, and read no sample beyond the image. New tests `ANeighborBeyondTheImageContinuesASamplesPlane` and `ANeighborThatSeesTheSkyDoesNotContinueASamplesPlane` (DepthPyramidTests) were red at first because the rule had no `insideImage` parameter (CS1739). The behavior did not change, so the existing tests covered it before and after.
2. The type names PixelPlane and PixelSurface: ticket ter-ikm1 renames them to SamplePlane and SampleSurface after ter-cvyx merges, because the rename touches reflection.glsl and ScreenRay.cs.
3. `stepSurfacePoint` took one place in two units. It now takes only the exact place in the texel grid, `vec2(texel) + side * exactOffset`, as the C# copy does, and derives the ray from it: `gtaoViewPosition((2.0 * place + 0.5) / vec2(fullSize()), 1.0)`. The 39 views of g/fixA are byte-identical to g/mask (md5 of each PNG), so the change of rounding moves no byte.
4. The plan's tables: the depth and sectors rows of the cost table (the flags' 0.5 MB write, the neighbors' reads, 9.3 MB of flags in place of ter-y1s7's 74.6 MB), both barrier rows, and binding 9 in the set table. The new tests are in the plan's test list.
5. DescriptorLayouts.OcclusionSet's comment names binding 9, the continuation flags.
6. Tests of the flags: `TheDepthPassFlagsTheEdgeNeighborsThatContinueEachTexelsPlaneByTheSharedRule` (BounceLightShaderTests) reads the flags back with the new hook `VulkanRenderer.ReadOcclusionContinuation` (R8_UINT added to `ReadLevel`). It compares every texel's four bits with `PixelPlane.ContinuesInto` for the read-back copies, on the corner's floor that ends under the sky with the low box on it, and checks that a sky texel holds no flag. A neighbor within 10⁻⁴ of the 1 % cut is left out (`ScreenTesting.NearTheCut`, moved from PyramidRenderTests). The test requires more than 100 neighbors beyond the image, 100 that see the sky, 50 depth edges and 10,000 continuing planes. It passed on the first run that read the sky right. It is a lock-in test, not watched red on a production defect. A swapped bit order, a lost beyond-image or sky branch, or flags on a sky texel make it fail. The floor test now also holds that the least open share of a floor texel stays above 0.8. Correction: that share is 0.834, not the 0.878 of the build's note. I measured 0.834 both with the step change and without it, so it is the build's value.

Gaps of the review:
- The control: g/base2 now holds deck-late and hair-rugs, byte-identical to g/base, so deck-late-next's 48-level change is real.
- The reflections with the bounce light off: mirror-balls, pbr-spheres, reflections and films with --screen-space-light off, 26 views, byte-identical between main (g/base-nossl) and the fix (g/fixA-nossl).
- The evidence is copied to /Users/alexispurslane/Development/tertius/captures/ipc1 (git ignores it). README.txt lists the fix stage's folders.
- I did not time again. The fix adds no read and no pass: the step derives its ray from its place, where the caller derived it before, and the images are byte for byte the mask build's.

**2026-11-02T22:28:03Z**

Hardening of the build and the review's fixes (test agent, 2026-11-02). Worktree ter-ipc1-one-plane-rule, base f09e9afef.

Coverage of the changed lines in src (tests/Tertius.Rendering.Tests in Debug and Release, then tools/coverage/changed-lines.py --base f09e9afef): 96 of 96 lines and 34 of 34 branches. Every file is at 100 %: OcclusionImages.cs 11/11, OcclusionDepthPass.cs 1/1, OcclusionSectorsPass.cs 1/1, DepthPyramid.cs 31/31, SectorSearch.cs 43/43, VulkanRenderer.cs 9/9. Debug ran 2,370 tests and Release 2,369, with no failures.

Mutants: 34 listed mutants with tools/mutate.py, 24 in GLSL or in the passes and 10 in the C# copy (list /tmp/ter-ipc1-mutants.json). The table merges three runs. The first run held all 34. C05 did not compile there (IDE0060, an unused parameter), and its rewrite `<= 0f * EdgeOnShare * pointDistance` survived, because the edge-on test's plane distance is exactly 0. The final C05, `< 0f * ...`, counts no plane as edge-on, and it is killed. C08 survived the first run, and the new test below kills it (red on the mutant with --only C08). GLSL mutants reach the tests: 16 of the 24 shader and pass mutants are killed, among them S13, S16, S17 and S19 by the flags read-back test.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S01 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:92 | neighborContinues: only the plane holds the neighbor | killed | PyramidRenderTests.ACellAtADepthEdgeBoundsTheNearSurfaceOnlyToTheCenterLineOfItsLastRowWhereItsPlanePassesTheFarSurface, PyramidRenderTests.ThePyramidsFollowAResize, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3) | 24.3 |
| S02 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:92 | neighborContinues: only the neighbor holds the center | killed | GrazingReflectionRenderTests.AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines, PyramidRenderTests.ThePyramidsFollowAResize, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3) | 24.0 |
| S03 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:77 | planeHolds <= -> < | survived |  | 24.5 |
| S04 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:76 | planeHolds share 1 % -> 4 % | killed | GrazingReflectionRenderTests.AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines, PyramidRenderTests.ACellAtADepthEdgeBoundsTheNearSurfaceOnlyToTheCenterLineOfItsLastRowWhereItsPlanePassesTheFarSurface, PyramidRenderTests.ThePyramidsFollowAResize | 24.0 |
| S05 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:48 | samplePlane: never edge-on | survived |  | 26.0 |
| S06 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:49 | samplePlane: edge-on plane keeps its gradient | survived |  | 26.0 |
| S07 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:91 | neighborContinues: edge-on neighbor can continue | survived |  | 24.1 |
| S08 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:90 | neighborContinues: beyond image stops | killed | PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 4) | 24.3 |
| S09 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:100 | continuesPlane: sky pixel read as a surface | survived |  | 37.3 |
| S10 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:122 | sampleSurface: low reaches a whole unit | killed | PyramidRenderTests.ThePyramidsFollowAResize, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6) | 28.6 |
| S11 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:123 | sampleSurface: right and down swapped | killed | PyramidRenderTests.ACellAtADepthEdgeBoundsTheNearSurfaceOnlyToTheCenterLineOfItsLastRowWhereItsPlanePassesTheFarSurface, PyramidRenderTests.ThePyramidsFollowAResize, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3) | 26.5 |
| S12 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:142 | surfaceW: no clamp to the part | killed | BounceLightRenderTests.AFaceBesideAGrazingConvexEdgeBlocksNothingAndGetsNoBounce, GrazingReflectionRenderTests.AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines, GrazingReflectionRenderTests.AMirrorFloorThatTheEyeSeesAtAGrazingAngleReflectsTheOutlineOfABallAgainstTheSkyWithNoStreak(resolution: Full) | 28.0 |
| S13 | src/Tertius.Rendering/Shaders/texel_surface.glsl:13 | EdgeNeighbors: axes swapped | killed | BounceLightRenderTests.AFaceBesideAGrazingConvexEdgeBlocksNothingAndGetsNoBounce, BounceLightShaderTests.TheDepthPassFlagsTheEdgeNeighborsThatContinueEachTexelsPlaneByTheSharedRule | 19.4 |
| S14 | src/Tertius.Rendering/Shaders/texel_surface.glsl:40 | texelPlane: -4 t_y / H sign | killed | BounceLightRenderTests.ALitRedWallTintsTheFloorBesideItRed, BounceLightRenderTests.AFlatFloorAloneBlocksNothingAwayFromItsOutlineAgainstTheSkyAndBouncesNothingOnItself, BounceLightRenderTests.AWallBeyondTheRadiusGivesTheFloorNoBounceAndLeavesItOpen | 19.8 |
| S15 | src/Tertius.Rendering/Shaders/texel_surface.glsl:60 | continuationFlags: sky neighbor read as a surface | survived |  | 19.4 |
| S16 | src/Tertius.Rendering/Shaders/texel_surface.glsl:64 | continuationFlags: beyond image stops | killed | BounceLightShaderTests.TheDepthPassFlagsTheEdgeNeighborsThatContinueEachTexelsPlaneByTheSharedRule | 19.2 |
| S17 | src/Tertius.Rendering/Shaders/texel_surface.glsl:65 | continuationFlags: bit order reversed | killed | BounceLightRenderTests.AFaceBesideAGrazingConvexEdgeBlocksNothingAndGetsNoBounce, BounceLightShaderTests.TheDepthPassFlagsTheEdgeNeighborsThatContinueEachTexelsPlaneByTheSharedRule | 19.8 |
| S18 | src/Tertius.Rendering/Shaders/texel_surface.glsl:73 | texelSurface: decode order rotated | killed | BounceLightRenderTests.AFaceBesideAGrazingConvexEdgeBlocksNothingAndGetsNoBounce | 20.4 |
| S19 | src/Tertius.Rendering/Shaders/occlusion_depth.comp:32 | depth pass: sky texel flags 15 | killed | BounceLightShaderTests.TheDepthPassFlagsTheEdgeNeighborsThatContinueEachTexelsPlaneByTheSharedRule | 20.4 |
| S20 | src/Tertius.Rendering/Shaders/sectors.glsl:174 | stepSurfacePoint: flags ignored, plane everywhere | killed | BounceLightRenderTests.AFaceBesideAGrazingConvexEdgeBlocksNothingAndGetsNoBounce | 20.6 |
| S21 | src/Tertius.Rendering/Shaders/sectors.glsl:175 | stepSurfacePoint: no farthest-depth cut | survived |  | 20.6 |
| S22 | src/Tertius.Rendering/Shaders/sectors.glsl:178 | stepSurfacePoint: ray without the half pixel | killed | BounceLightRenderTests.AFlatFloorAloneBlocksNothingAwayFromItsOutlineAgainstTheSkyAndBouncesNothingOnItself, BounceLightShaderTests.UnderTheMapTheSectorPassTakesTheOpenPiecesShareOfTheMapsLightAsTheCSharpCopyDoes, BounceLightShaderTests.TheSectorPassGathersWhatTheCSharpCopyGathersForAHairReceiver | 20.2 |
| S23 | src/Tertius.Rendering/Passes/OcclusionSectorsPass.cs:40 | sectors pass: no barrier on the flags | killed | BounceLightRenderTests.ALitRedWallTintsTheFloorBesideItRed, BounceLightRenderTests.AGlowOfTheDefaultEntityBouncesBesideTheTestLamps, BounceLightRenderTests.AFlatFloorAloneBlocksNothingAwayFromItsOutlineAgainstTheSkyAndBouncesNothingOnItself | 27.0 |
| S24 | src/Tertius.Rendering/Frame/OcclusionImages.cs:180 | CheckDevice: R8_UINT storage not checked | survived |  | 10.2 |
| C01 | src/Tertius.Rendering/Shading/DepthPyramid.cs:155 | ContinuesInto: only the plane holds the neighbor | killed | DepthPyramidTests.APlaneThatPassesTheCenterOfAFarNeighborByChanceStopsAtItsCenterLineTowardIt, PyramidRenderTests.ThePyramidsFollowAResize, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3) | 16.9 |
| C02 | src/Tertius.Rendering/Shading/DepthPyramid.cs:154 | ContinuesInto: only the neighbor holds the center | killed | PyramidRenderTests.ThePyramidsFollowAResize, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6) | 13.0 |
| C03 | src/Tertius.Rendering/Shading/DepthPyramid.cs:150 | ContinuesInto: beyond image stops | killed | DepthPyramidTests.ANeighborBeyondTheImageContinuesASamplesPlane, DepthPyramidTests.AtTheScreensEdgeAPixelsPlaneReachesThePixelsEdge, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3) | 13.1 |
| C04 | src/Tertius.Rendering/Shading/DepthPyramid.cs:153 | ContinuesInto: edge-on neighbor can continue | killed | DepthPyramidTests.APlaneStopsAtItsCenterLineTowardAnEdgeOnNeighbor | 13.7 |
| C05 | src/Tertius.Rendering/Shading/DepthPyramid.cs:119 | OfSample: never edge-on | killed | DepthPyramidTests.AnEdgeOnPixelKeepsItsCentersInverseDepthOverItsWholeSquare | 12.9 |
| C06 | src/Tertius.Rendering/Shading/DepthPyramid.cs:207 | Over: left and up swapped | killed | SectorSearchTests.ANearStepTowardAnEdgeNeighborWhosePlaneDoesNotContinueItsTexelsStopsAtTheTexelsCenterLine, DepthPyramidTests.ANeighborWithinOnePercentOfThePlaneInInverseDepthContinuesIt(offPlane: -0.0120000001, continues: False), DepthPyramidTests.ANeighborWithinOnePercentOfThePlaneInInverseDepthContinuesIt(offPlane: 0.0120000001, continues: False) | 13.2 |
| C07 | src/Tertius.Rendering/Shading/DepthPyramid.cs:261 | InverseDepthAt: no clamp to the part | killed | SectorSearchTests.ANearStepTowardAnEdgeNeighborWhosePlaneDoesNotContinueItsTexelsStopsAtTheTexelsCenterLine, SectorSearchTests.AStepBetweenTheLastRowOfAFloorAndTheSkyStopsAtThatRowsCenterLine | 13.1 |
| C08 | src/Tertius.Rendering/Shading/SectorSearch.cs:224 | TexelSurface: left reads the right neighbor | killed | SectorSearchTests.ANearStepReachesTowardTheNeighborThatContinuesItsTexelsPlaneAndStopsTowardTheOtherThatDoesNot | 6.5 |
| C09 | src/Tertius.Rendering/Shading/SectorSearch.cs:247 | TexelPlane: x gradient sign | killed | SectorSearchTests.OnATiltedPlaneEveryStepOfEverySliceKeepsItsSurfacePointOnThePlane, BounceLightShaderTests.UnderTheMapTheSectorPassTakesTheOpenPiecesShareOfTheMapsLightAsTheCSharpCopyDoes, BounceLightShaderTests.UnderAMapWithASmallBrightLampTheSectorPassReadsEachOpenPieceAlongTheAnglesThatTheCSharpCopyReads | 6.7 |
| C10 | src/Tertius.Rendering/Shading/SectorSearch.cs:216 | TexelSurface: beyond image stops | killed | SectorSearchTests.ANearStepStandsOnTheSurfaceThatThePixelOfItsGridHas | 7.1 |
34 rows: killed 26, timeout 0, survived 8, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

The triage of each survivor:
- S03, planeHolds `<=` to `<` (depth_pyramid.glsl:77). Judgment call: the two differ only where a neighbor's inverse depth equals the plane's times 1.01 exactly in float. No rendered pixel can be aimed at that tie, and the GPU-against-C# tests leave out the neighbors within 10⁻⁴ of the cut by design (ScreenTesting.NearTheCut).
- S05, samplePlane never edge-on (depth_pyramid.glsl:48). Judgment call: the flag changes the surface only where n · P is exactly 0, because any other plane distance gives a gradient that no neighbor's plane holds, and the part then keeps the center's value. On the GPU, that needs a stored octahedral normal exactly at a right angle to the pixel's ray, which no test scene renders. The C# copy's twin, C05, is killed by AnEdgeOnPixelKeepsItsCentersInverseDepthOverItsWholeSquare.
- S06, an edge-on plane keeps its gradient (depth_pyramid.glsl:49). Judgment call, for the reason of S05: with no neighbor that continues an edge-on plane, the surface reads the gradient only at the center, except at n · P = 0, where it gives NaN.
- S07, an edge-on neighbor can continue a plane (depth_pyramid.glsl:91). Judgment call, for the reason of S05: no GPU scene has an edge-on pixel. Its C# twin, C04, is killed by APlaneStopsAtItsCenterLineTowardAnEdgeOnNeighbor.
- S09, a sky pixel read as a surface in continuesPlane (depth_pyramid.glsl:100). Equivalent: pixelPlane leaves a sky pixel's plane at 0 inverse depth with no gradient, and that plane holds no center of a surface, whose inverse depth is positive.
- S15, a sky texel read as a surface in continuationFlags (texel_surface.glsl:60). Equivalent for every surface nearer than about 10¹³ m: the sky texel's plane lies at 10⁻¹⁸ m⁻¹, and a plane that is not edge-on has a plane distance above 10¹² m, so its gradient stays below about 10⁻¹⁴ m⁻¹ per texel and never holds the surface's center. The reviewer named this one as near-equivalent.
- S21, no farthest-depth cut in stepSurfacePoint (sectors.glsl:175). Equivalent: a point at a view depth of center.z + radius or more lies at least the radius from the center, so the caller's distance test drops it. The surface's inverse depth is positive on the texel's square, so the cut never drops a point that the distance test keeps.
- S24, CheckDevice without R8_UINT (OcclusionImages.cs:180). Judgment call: Vulkan requires R8_UINT storage on every device with shaderStorageImageExtendedFormats, which the renderer requires, so no device that a test can open fails the check.

New test (SectorSearchTests): ANearStepReachesTowardTheNeighborThatContinuesItsTexelsPlaneAndStopsTowardTheOtherThatDoesNot. Planks two texels wide recede to the right before a wall, so each plank texel has one neighbor along its row that continues its plane and one that does not. A step toward the plank's other texel stands on the plane at its place, and a step toward the wall stands at the plane's value on the texel's center column. It kills C08, where the left answer read the right neighbor. The existing tests missed C08: the planks of one texel have two neighbors that break, and the box scene's planes have almost no slope across a row.

The decisions of the ticket, and the tests that hold them through behavior. No other new test was needed:
- The pixel and the near step read one surface: ANearStepStandsOnTheSurfaceThatThePixelOfItsGridHas (C#), and the GPU-against-C# tests of both readers (PyramidRenderTests, BounceLightShaderTests).
- A neighbor beyond the image continues a plane: AtTheScreensEdgeAPixelsPlaneReachesThePixelsEdge for a pixel (it kills C03), ANearStepStandsOnTheSurfaceThatThePixelOfItsGridHas for a texel (it kills C10), and the flags read-back test on the GPU (it kills S16). Pixels on the GPU: S08 is killed by EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt.
- A sky neighbor does not continue a plane: TowardTheSkyAPixelsPlaneStopsAtItsCenterLine, AStepBetweenTheLastRowOfAFloorAndTheSkyStopsAtThatRowsCenterLine and the flags test (sky texels hold no flag, which kills S19). On the GPU, the sky mutants S09 and S15 are equivalent, as above.
- A near step past its part stands on the clamped surface: the planks test and the floor-to-sky test (they kill C07), the new test (it kills C08), and AFaceBesideAGrazingConvexEdgeBlocksNothingAndGetsNoBounce (it kills S12 and S20).
- The flags equal the shared rule: TheDepthPassFlagsTheEdgeNeighborsThatContinueEachTexelsPlaneByTheSharedRule kills S13, S16, S17 and S19. The decode order S18 is killed by the convex-edge render test.
- The barrier on the flags before the sectors pass: S23 is killed only as a validation failure. Without the barrier, the synchronization layer reports SYNC-HAZARD-READ-AFTER-WRITE on binding 9 in occlusion_sectors, and the tests that require silent validation fail. All 42 failed assertions were the validation check. No image assertion failed.

The full Checked run after the new test: 9,973 tests, 9,959 passed, 0 failed, 14 skipped.
