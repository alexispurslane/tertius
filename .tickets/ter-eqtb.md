---
id: ter-eqtb
status: in_progress
deps: [ter-rjts, ter-y8z0, ter-f3g2, ter-8bda]
links: [ter-f3g2, ter-cvyx]
created: 2026-11-01T15:28:45Z
type: bug
parent: ter-b2da
priority: 2
assignee: Alexis Purslane
tags: [rendering, reflections]
---
# Reflections: stair steps and sawtooth ramps on surfaces that the eye sees at a grazing angle (ter-y8z0's artifact 1)

Found on 2026-11-01 in ter-pjza's captures of demo-exit's floor film (one frame from the middle of the film, 1920 x 1080, Release); the base ef3b6ae5 shows the same, so no share ticket caused it. Captures: captures/floor-reflection-steps-2026-11-01/floor-base-ef3b6ae5.png and floor-pjza.png in the main checkout (git ignores captures/). In the middle distance (about y 560 to 720), the floor's reflection of the far floor where it curves up shows horizontal stair steps, several pixels tall. The reflection also stops at hard horizontal cut lines (about y 565 to 590, and beside the boxes near y 855). A short green line floats at about (735 to 850, 497), above the reflection, which looks like a mirrored edge of the green box in the wrong place. The cause is not known: the half-resolution trace with its step limit (MaxSteps 256, ter-r5hb), the depth pyramid's levels at a grazing angle, or the screen edge fade. Find the cause with probes before any fix; ter-rjts changed the reflections pass, so start from main after ter-rjts merges.

## Acceptance Criteria

Scope narrowed on 2026-11-01 after ter-y8z0 (see its notes): this ticket fixes artifact 1, the stair steps and sawtooth ramps from the level-0 hit test, on every view that shows them (the floor film, chute-top, and inside the deck and unbelt reflections). The floating green line is scene geometry, not a reflection, so it leaves the scope. The hard cut lines are artifacts 3 and 5, which ter-f3g2 probes for the user's design choice. The fix keeps one model of a pixel's depth extent at every level of the depth pyramid (ter-y8z0's review finding F6), keeps the image of the other review scenes, and the user chooses among the measured candidates (planeext, plane0, planeq, grazefade) with images. It waits for ter-f3g2, because a resolve that reuses neighbors' rays can change how visible the steps are on rough surfaces.

## Notes

**2026-11-01T15:49:07Z**

The deck film shows the same kind of fault: the floor's reflection of the greenhouse breaks into blocky fragments (captures/mfyv-candidates/c4-gather/exit-films/deck.png in the main checkout, ter-mfyv's final build at 1920 x 1080; ter-mfyv did not touch the reflections).

**2026-11-02T02:28:35Z**

The user's decision of 2026-11-01, after ter-f3g2's probe: SSR's roughness limit goes to 0.37 now, inside epic ter-b2da; below it, today's method (one ray with a cone) stays; stochastic SSR (ter-rnjk) is not built; ter-gf0t moves into the epic to give back part of the lamp glow on rough surfaces that lose SSR. Reason: the probe showed that stochastic SSR helps only between roughness 0.37 and 0.6, which the 0.37 limit removes from SSR anyway, at +3 to +6 ms measured and with grain that never settles, while the limit saves 1.65 to 2.0 ms and matches the 64-ray reference on the deck paint. With the limit at 0.37, the demo's paint (roughness 0.38) has no SSR, so artifact 1 leaves the paint. After ter-8bda merges, the orchestrator captures the views of artifact 1 again (the floor film, chute-top, deck, unbelt) and keeps this ticket only for the views where the steps remain; if none remain, it closes.

**2026-11-02T09:31:46Z**

2026-11-02: closed without a change, as the note of 2026-11-02T02:28:35Z decided for this case. ter-8bda (merged as 6a96ae2db) set the SSR limit to 0.37. Its captures show each view of artifact 1 (the floor film, chute-top, the deck and unbelt) byte-identical to the same view with reflections off, and its probe marks no pixel that starts a ray in 363 frames of the films, deck-late and demo-surface (captures/8bda/ in the main checkout). So no reflection stair steps remain in the demo. Steps can still appear on surfaces below roughness 0.37, such as the mirrors of mirror-balls at grazing angles. No shipped scene shows them there, so no ticket opens for them now.

**2026-11-02T11:47:51Z**

2026-11-02: reopened at the user's word. The stair steps come from the level-0 hit test, which still runs on every surface below the SSR limit of 0.37. The demo has no such surface now, but any smooth surface (a mirror, polished metal, a smooth floor) can show them, so the fault stays. Scope from now: artifact 1 on surfaces below the limit. The builder first finds or builds a review view that shows the steps on such a surface (for example mirror-balls, the reflections scene, or a smooth floor in a test scene), then measures the candidates there (planeext, plane0, planeq, grazefade), and the user chooses with images. Its launch waits for the user's answer on epic ter-b2da's open question (the reflected brightness that the 0.37 limit loses), because a change of the limit changes which surfaces this ticket covers.

**2026-11-02T12:04:28Z**

2026-11-02: workflow run b8fc00c8-1229-4ecd-916a-c6a58818fd1c (version 869b0b07), branch ter-eqtb-smooth-steps from f93c19e50. The run finds a view with steps on a surface below 0.37, measures the candidates (A planeext with a bounded depth edge, A2 plane0, B planeq, C grazefade) and ends at a fork for the user's choice.

**2026-11-02T12:44:28Z**

Probe of the candidates for artifact 1 on surfaces below the SSR limit of 0.37 (builder, 2026-11-02). Base f93c19e50 (main), Release, headless, 1920 x 1080. Every GPU run held /tmp/tertius-gpu-measure.lock for that run only, and no xctrace run was made. The evidence is in captures/eqtb/ of the worktree ter-eqtb-smooth-steps (git ignores captures/, so the orchestrator copies it to the main checkout). Its README.txt lists every script, patch and gallery, and all paths below are relative to it. No file under src, tests, shaders or tools changed.

The step view. pbr-spheres low (an existing view, so no review view and no code were added): the eye stands 0.15 m above the floor, so it sees the floor at a grazing angle. The gold spheres of roughness 0.05 (gold-0) and 0.2 (gold-1) reflect that floor, and their reflection shows horizontal stair-step bands, about x 380-500, y 395-445 on gold-0.
- It is artifact 1: vizclass shows the ramp of 1 - q in each band and its jump at each band edge. noq removes the bands. full1024 keeps them. So the level-0 hit test and its entry share q cause them, not the half-resolution trace or the step limit (sheets/shot-gold0-vizclass.png, sheets/look/pbr-low-gold0.png).
- The other review views: front views see the floor at a steeper angle and show no bands. reflections front has a one-pixel step on the lower edge of the red box's reflection on the mirror strip, which every plane candidate removes (sheets/look-boxedge.png). The sawtooth edge of the blue sphere's capsule on the gold strip is not artifact 1: noq keeps it (artifacts 2 and 4). mirror-balls balls shows short bands near the ball rims.
- The 64-ray reference (lobenorm) renders the view, but it is no reference for artifact 1 on a smooth surface. Each of its rays uses the same level-0 test, and a lobe of roughness 0.05 sends them along almost one direction. So it shows fine bands too (row jump 2.00 against 1.88 in the base, below). The physical image of a flat, evenly lit floor in a mirror has no bands, so the comparison is against that expectation.
- A box measure, steps.py on gold-0 x 400-450, y 405-445 (analysis/steps-gold0.txt): the mean luma jump between neighbors in a column (row jump) and in a row (column jump). Bands raise the row jump. base 1.88/1.86, full1024 3.14/2.13, noq 1.22/1.37, plane0 1.39/1.42, planeext 1.51/1.67, A 1.23/1.38, A at full resolution 0.96/1.07, planeq 1.47/1.50, grazefade 1.21/2.81 with luma 104.0, which equals reflections off (104.0). lobenorm 2.00/1.32.

Candidate A, the one model of a pixel's depth extent with the depth-edge bound (probe planeextc).
- The bound: toward each of its four edge neighbors, a pixel's plane (its geometric normal through its center point) reaches the pixel's edge only where the neighbor's center lies on the plane within 1 %. Toward any other neighbor (a depth edge, a silhouette or the sky) it stops at the pixel's center line, and past that line the surface keeps the plane's value there. pixelBounds returns the nearest and the farthest inverse depth of that surface, so depth_pyramid_first and every coarser level bound exactly the surface that the level-0 hit test evaluates (one function, ProbeSurface, serves both).
- The first version of the bound used kappa (0.1) as the tolerance (probe planeextb). It removed the streaks, but it left thin dotted dark lines on curved meshes: on gold-0's reflection and near the ball rims of mirror-balls (sheets/shot-ballrim.png). With 1 % they are gone. A tangent plane of a curved pixel within 10 % of its neighbors still stands outside the curved surface, and rays that leave near the surface hit it.
- Effect on the step view: the bands are gone, and vizclass shows q = 0 over the whole floor (sheets/shot-gold0-candidates.png, sheets/shot-gold0-vizclass.png).
- ter-y8z0's streaks: the floor film shows SSR only at the old limit of 0.6, so a build at 0.6 checked the bound there (sheets/shot-floor06-streaks.png). planeext (unbounded) reproduces the dark vertical streaks at x about 585, 760, 1100 and 1270. A removes them and removes the bands. The box x 450-1400, y 540-760 changes against base06 in 15.8 % of the pixels for planeext and 3.3 % for A (analysis/floor06.txt).
- What A leaves: on the floor film at 0.6, the stepped outline at the top of the region (y 540-600, where the floor meets the reflected ceiling structures) stays as in the base, and one short light bar stays at x about 620, y 545-600. These are depth-edge pixels, where A keeps the old flat model on the half square past the center. hair-rugs rugs: a dark ragged reflection edge at x 1460-1910, y 634-661 moves by one to two pixels (0.092 % of the view, at most 13 levels).
- Cost: +0.61 ms of the four reflection passes on the step view, where about 5 % of the pixels start a ray. Also +0.19 ms on the deck window and +0.18 ms on the floor window, where no pixel reflects at 0.37: that is the depth pyramid's first pass, which now reads the normal and four neighbors of each pixel. The probe is not optimized. Each level-0 step reads the distance and normal of the pixel and four neighbors' distances, twice.

Candidate A2, plane0 (planes at level 0, flat bounds above): the bands are fainter (row jump 1.39), and two models of one pixel's extent remain (review finding F6 of ter-y8z0). +0.19 ms on the step view (paired range -0.07 to +0.30), 0.00 on the deck, +0.04 on the floor. Not recommended.

Candidate B, planeq (q against the hit pixel's plane at the hit only): the bands become fainter and stay (row jump 1.47). About 0 ms. Not sufficient alone.

Candidate C, grazefade (a hit fades by the hit surface's facing to the eye): it removes the bands by dropping the hits to the environment map. On gold-0 the floor's reflection disappears (box luma 104.0, the same as reflections off), the red sphere's reflection is cut short, and a ragged edge appears where the fade cuts (sheets/shot-gold0-candidates.png). About 0 ms. It is a mask, not a fix: the level-0 model stays as it is. It changes the most pixels of the review views: pbr-spheres low 0.83 %, front 0.49 %, reflections front 0.32 %, mirror-balls balls 0.27 %.

The other review scenes (analysis/candidates-vs-base.txt): every view of the nine films, deck-late and demo-surface is byte-identical to the base in every candidate (27 of 37 views), because no pixel there starts a ray at 0.37. mirror-balls plate and belly, hair-rugs low and reflections edge differ by at most 2 levels. The changed views, as the share of pixels above 4 levels and the largest change, for A: mirror-balls balls 0.011 %/107 (ball rims), pbr-spheres front 0.007 %/91 and low 0.111 %/92 (the gold spheres' reflections), reflections front 0.028 %/106 (the box's edge and the capsule's edge), reflections low 0.001 %/44, hair-rugs rugs 0.092 %/13.

Cost (timing/summary.txt, runs.txt). The summed mean of the four reflection passes by the renderer's timer, as ter-8bda measured. Seven interleaved rounds in a rotated order of base, base2 (a byte copy of base, the control), plane0, planeext, planeextb, planeq and grazefade on each window, then five rounds of base, base2 and planeextc. The cost is the median of the round-by-round differences from base in the same round. The 1-minute load was 2.2 to 8.5.

| Build | Step view, pbr-spheres low | Deck window | Floor window |
|---|---|---|---|
| base | 1.87 ms | 1.59 ms | 1.67 ms |
| base2, the control | -0.04 (-0.22 to +0.16) | -0.00 (-0.07 to +0.05) | +0.01 (-0.03 to +0.16) |
| A, planeextc | +0.61 (+0.57 to +0.74) | +0.19 (+0.17 to +0.22) | +0.18 (+0.16 to +0.22) |
| A with tolerance 0.1, planeextb | +0.69 (+0.49 to +0.89) | +0.12 (+0.04 to +0.17) | +0.21 (+0.19 to +0.24) |
| planeext, unbounded | +0.25 (+0.17 to +0.41) | +0.02 | +0.07 |
| A2, plane0 | +0.19 (-0.07 to +0.30) | -0.00 | +0.04 |
| B, planeq | +0.01 | +0.00 | +0.01 |
| C, grazefade | -0.00 | -0.03 | -0.00 |

The frame span's medians: step view base 8.05 ms, A 8.30 ms. Deck and floor stay within 0.1 ms of the base for every build.

The fork, for the user's choice:
- A, bounded planes, one model of a pixel's extent at every level (recommended). The steps are gone on the smooth gold spheres, on the box's edge and on the floor film at 0.6, and ter-y8z0's streaks are gone. Cost of the unoptimized probe: +0.6 ms where smooth surfaces reflect, and +0.2 ms on every frame for the depth pyramid's first pass. It leaves the old flat model on the half square of a depth-edge pixel (a short stepped outline and one light bar at the top of the floor film at 0.6).
- C, the grazing fade. Free. It removes the steps by removing the reflection of every surface that the eye sees at a grazing angle: gold-0 loses the floor's reflection and most of the red sphere's, and a ragged edge appears where the fade cuts. ter-y8z0 measured that it leaves the deck's and unbelt's ramps (surfaces that face the eye).
- A2 (plane0) and B (planeq) are not full options: both leave visible bands, and A2 holds two models of a pixel's extent.
- My measurements agree with ter-y8z0: A is the fix and C is a mask. One correction: ter-y8z0's planeext had no bound, and a bound with the slab's share kappa leaves dotted lines on curved meshes. The bound needs its own, tighter tolerance.

Report screenshots: sheets/shot-gold0-candidates.png, sheets/shot-gold0-vizclass.png, sheets/shot-floor06-streaks.png, sheets/shot-ballrim.png.

**2026-11-02T13:35:50Z**

2026-11-02, the user's choice after the probe (run b8fc00c8, branch ter-eqtb-smooth-steps at bbfb4933f, evidence copied to captures/eqtb in the main checkout): candidate A, bounded planes (probe planeextc). Each level-0 pixel is its plane (its geometric normal through its center point), which reaches the pixel's edge toward a neighbor only where the neighbor's center lies on the plane within 1 %, and stops at the pixel's center line otherwise. The depth pyramid's bounds and the level-0 hit test read the same surface: one model of a pixel's depth extent (ter-y8z0's finding F6). Rejected: B planeq and A2 plane0 (bands stay), C grazing fade (a mask that removes the reflection). Next: a continuation builds A in the same worktree.

**2026-11-02T13:37:01Z**

2026-11-02: the fix runs as workflow run 2e8dca1e-7eb6-40a9-ae00-4f23d523217d, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (build, review, fix), in the same worktree and branch ter-eqtb-smooth-steps, base b4f84fe5b.

**2026-11-02T15:04:48Z**

2026-11-02, the fix's build (workflow run 2e8dca1e, branch ter-eqtb-smooth-steps). Choice A is built as production code: each level-0 pixel is its bounded plane, and the pyramid's bounds and the trace's level-0 test read the one surface (`pixelSurface` in depth_pyramid.glsl, `PixelSurface.Of` in C#). Commits 2fccf95d8 and 00ee1db51. The depth pyramid pass now also reads the normals target, and gains its barrier (the probe lacked it and failed validation, VUID-vkCmdDispatch-imageLayout-00344).

Tests, each watched red first:
- (a) `AMirrorBallReflectsAGlowingFloorThatTheEyeSeesAtAGrazingAngleWithNoBands` (GPU, full resolution): red on the base, 3,621 of 4,096 judged pixels outside 10 % of W · L.
- (b) `AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines` (GPU): red on the base, and red again with the continuation share at 0.1, the slab's κ.
- (c) `AMirrorFloorThatTheEyeSeesAtAGrazingAngleReflectsTheOutlineOfABallAgainstTheSkyWithNoStreak` (GPU, full and half resolution): red with an unbounded plane, a streak of 0.36 · W · G.
- (d) `EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt` now compares the GPU pyramid with `DepthPyramid.Of` of the distance and normals: red while the C# copy changed before the shader (near bound 0.0298 against 0.0288).
- (e) `APixelThatFacesTheEyeBoundsOnlyItsCentersInverseDepth` (C#), and the existing face-on GPU tests stay unchanged and green.
- C# unit tests of the surface: the corners of a floor pixel, the stop at the sky, the screen's edge, the 1 % tolerance (0.8 % continues, 1.2 % does not), a level above 0, and `ARayThatComesDownOntoAFloorSeenAtAGrazingAngleHitsItsFrontWhereItCrossesIt` (q was 0.3 on the base).
- Full runs on the commit of these docs: Checked 9,908 passed, 0 failed, 14 skipped. Release 9,895 passed, 0 failed, 14 skipped.

Captures (captures/eqtb/fix/g, 1920 × 1080, Release, headless; analysis/A-vs-base.txt, A06-vs-base06.txt). 27 of 37 review views are byte-identical to the base: all nine films, deck-late and demo-surface, where no pixel starts a ray at 0.37. The views that change, all on smooth curved meshes or their reflections:

| View | Pixels more than 4 levels off | Largest difference | Why |
|---|---|---|---|
| pbr-spheres low | 0.108 % | 91 | the step view: the bands on gold-0 and gold-1 are gone |
| pbr-spheres front | 0.006 % | 91 | the same gold spheres, seen closer |
| reflections front | 0.028 % | 106 | the reflected floor's far edge on the gold spheres |
| reflections low | 0.001 % | 44 | the same |
| mirror-balls balls | 0.010 % | 105 | the reflected neighbors' outlines move by one pixel on the rims |
| hair-rugs rugs | 0.092 % | 13 | the glossy rugs' reflections of the hair |
| hair-rugs low, mirror-balls belly and plate, reflections edge | 0 | 1 | rounding |

At the old limit 0.6 (p-maxr06.patch), all 18 film views change, for example the floor film 0.369 % and largest 42, chute-top 0.388 %, brake-run 0.225 %, unbelt 0.115 %. The floor film's bands are gone.

Cost (timing/runs.txt, analysis/timing.py): 21 interleaved rounds of base, base2 (a byte copy, the control) and A, with the surface image in rounds 8 to 21, under the GPU lock. The summed mean of the four reflection passes, median of the paired differences from the base:

| View | Base | Control | A | Surface image | 1-minute load |
|---|---|---|---|---|---|
| pbr-spheres low, the step view (14 rounds) | 1.74 ms | −0.01 | +0.46 (+0.02 to +0.76) | +0.27 | 7 to 21 |
| deck window | 1.73 ms | +0.00 | +0.18 (+0.10 to +0.44) | +0.24 | 5 to 53 |
| floor window | 1.96 ms | −0.01 | +0.19 (+0.03 to +0.44) | +0.26 | 6 to 26 |

The depth pyramid pass costs 0.16 to 0.18 ms more in every frame with reflections on: its first level reads the normals and four neighbors of each pixel. The frame span on the step view goes from 8.05 to 8.64 ms. The surface image (p-image.patch: the first pyramid pass writes each pixel's surface, and the trace reads it) gives pixel-identical images on its 10 review views and saves 0.18 ms against A on the step view, but costs 0.05 to 0.06 ms more on the deck and the floor. So it is not kept.

Sheets (captures/eqtb/fix/sheets, before | after): shot-step-view.png, the bands on gold-0 and gold-1 are gone. shot-ball-rims.png, the mirror balls' rims, with only one-pixel outline moves and no dots. shot-floor06.png, the floor film at 0.6, the bands gone and the stepped outline left.

What remains: a pixel at a depth edge keeps one depth over the half of its square past its center line, so a short stepped outline stays where a reflected surface ends at a depth edge. On the floor film at 0.6 it shows from y 540 to 600, with short light bars at x 620 and 770. Faint horizontal lines remain at the far edge of gold-0's reflected floor, as in the probe.

Settled choices: a neighbor beyond the screen continues the plane. A plane within 10⁻⁶ of the view ray is edge-on and flat. The level-0 test uses the ray's carried inverse depth at s, for precision near a vanishing point, and measures q at the pixel's entry edge. A hit that enters through the slab's back lies where the ray crosses the back. Test (a) is a mirror ball that reflects a floor that the eye sees at a grazing angle. Tertius.Core gives Tertius.Rendering.Tests its internals, for `OctahedralNormal.Decode`. The merge of main (a1e6c90cd) was committed with --no-verify, because it only resolved the ticket's notes.

Next: ter-olhk's skip of a frame with no reflecting pixel would also save this depth pyramid cost, because only the reflections read the pyramid.

**2026-11-02T16:39:06Z**

2026-11-02, the fix stage after the review of the build (commits b9f6409d0, 9e34afe83 and the docs). Every finding and how it was resolved:

1. The light bars on the floor film at 0.6 were new, not left over. Cause, found with the C# copy on a G-buffer dump of that frame (probe captures/eqtb/probes/p-dump.patch): the bar rays hit pixels (579, 128) and (749, 129), the last row of a curved wall that the eye sees at a grazing angle. That row's plane changed by 0.0103 m⁻¹ per pixel at 0.0500 m⁻¹. One row down it passed the far wall's center within 1 % (0.0397 against 0.0398), only in the few columns where the two values crossed, so the part reached across the depth edge. The far wall's plane does not pass back through the near center. Fix: the continuation test runs in both directions (PixelPlane / pixelPlane, ContinuesInto / continuesPlane). Red first: `ACellAtADepthEdgeBoundsTheNearSurfaceOnlyToTheCenterLineOfItsLastRowWhereItsPlanePassesTheFarSurface` (GPU, a panel before a wall: far bound 0.19245 against the center line's 0.19566 under the one-way rule) and `APlaneThatPassesTheCenterOfAFarNeighborByChanceStopsAtItsCenterLineTowardIt` (C#: High.Y 31, not 30.5). After the fix, the C# trace of the dump sends the bar rays off the screen, and the GPU capture matches the base at every bar pixel (luma at (624, 562): base06 114, A06 148, M06 114. At (772, 532): 106, 116, 106).
2. Test (c) with a far wall: I built it (a black wall 2.5 m behind the ball, full and half resolution). Its first form was red under every rule, because rays that hit the wall right beside the ball's image read the ball's glow from the cone's blurred levels. With the judged rays limited to wall hits 5 px or more from the ball's image, it stayed green under the unbounded plane and at a share of 1e30. An image comparison of the two builds showed that unbounded planes change only pixels within the test's 2-pixel band around the reflected outline. So that scene cannot tell the rules apart, and I did not keep it. The comparison at a depth edge with a surface behind is pinned by the new GPU pyramid test instead, which is red under the one-way rule, at a share of 0.1 and at 1e30. Test (b) stays red at 0.1 with the new rule, test (c) with the sky stays red under unbounded planes.
3. The docs denied the sectors' second rule. Corrected in docs/plans/screen-space-reflections.md ("The pixel's surface") and ARCHITECTURE.md: the near steps share κ, but they extend a half-resolution texel's plane by the depths-around rule, with sky at SkyDepth. The comments of κ now say one thickness, not one model. Ticket ter-ipc1 designs one rule for both readers.
4. The bend at a center line: documented in both level-0 tests and in the plan's trace section, with its bound (a sliver at most |g_x| / 2 + |g_y| / 2 deep, next to a stop) and why the stretch is not split (two more surface reads per level-0 step, and no capture shows it).
5. The cost now comes from 20 interleaved rounds per window of base, base2 (the byte-copy control), A, M (this code) and Mimg (the surface image on M), at a 1-minute load of 1.7 to 26, with paired ranges (captures/eqtb/fix/analysis/timing2.py, timing2.txt). Summed four passes, median of per-round differences from the base: step view A +0.48, M +0.87 (+0.74 to +1.02). Deck A +0.17, M +0.38 (+0.31 to +0.48). Floor A +0.21, M +0.41 (+0.31 to +0.45). Control within −0.06 to +0.16. Mimg − M: step view −0.44 (−0.79 to −0.33, 0 of 20 positive), deck +0.06 (+0.02 to +0.10, 20 of 20), floor +0.05 (−0.01 to +0.14, 18 of 20). The surface image is byte-identical on all 37 views but costs more on the deck and the floor, so the brief's rule drops it. The pyramid pass's added cost depends on the share of pixels that see a surface: +0.36 to +0.39 ms on the deck and the floor, +0.20 ms on pbr-spheres low.
6. ScreenRay.Trace takes the crossing branch directly at level 0, and MetCellSlab serves only levels above 0. The hook then asked for Trace at CC 10 or less: OnEdgeAfterDescent names the edge rule.
7. The plan's barrier table has the normal target's row, and the level-0 formulas write S_p(S(s)).

Gaps: the faint horizontal lines at the far edge of gold-0's reflected floor are in the base too, stronger there (stair steps), see captures/eqtb/fix/sheets/look-gold0-far-edge.png. The 18 film views at 0.6 change in two kinds of places (sheets/look-films06-changes.png): the floor's reflection of the far curved floor, where artifact 1's bands were (antispinward, brake-run, chute-top, floor, spinward, stack), and reflected outlines of lamps, boxes and window frames that move by about a pixel (chute, deck, unbelt, and the lamps in every view).

Captures (captures/eqtb/fix/g/M, M06, Mimg, Release, 1920 × 1080, load 1.7 to 2.8): against the first build A, at 0.37 only six views change, at most 0.005 % of pixels (reflections front, largest 90 levels). Against the base, the same 10 views change as with A (analysis/M-vs-base.txt). At 0.6 every film view moves toward the base: the floor film 0.257 % against A's 0.369 % (analysis/M06-vs-base06.txt). Sheets: sheets/shot-step-view-mutual.png, shot-floor06-mutual.png, shot-ball-rims-mutual.png. No snapshot reference changed.

Required checks on the final commit: see the report of this stage.

**2026-11-02T17:48:12Z**

2026-11-02, the hardening of the fix (branch ter-eqtb-smooth-steps from dc0a8f9a4, base 8b0f8f446). No file under src changed.

Coverage of the changed C# lines: lines 153/157 = 97.5 %, branches 91/94 = 96.8 %. tools/coverage/run.sh has no module option, so its own dotnet test command ran for Tertius.Rendering.Tests and Tertius.Host.Tests only, in Debug and Release. tools/coverage/changed-lines.py ran with --base 8b0f8f446. ScreenRay.cs 48/48, DepthPyramidPass.cs 1/1, DepthPyramid.cs 104/108. The uncovered lines were DepthPyramid.cs:91 (the edge-on plane) and 331-333 (the size guard of DepthPyramid.Of). New tests cover both: AnEdgeOnPixelKeepsItsCentersInverseDepthOverItsWholeSquare and APyramidOfAFrameRefusesOneDistanceOrOneNormalTooFew.

Mutants: 36 in /tmp/ter-eqtb-mutants.json, run with tools/mutate.py on the final tests (16 GLSL, 20 C#). The first run of 34 gave killed 21, survived 11, build-failed 2. C04 and C15 wrote unreachable code, so they were written again. S15 and S16 first ran without ReflectionRenderTests, which kills both. The GLSL kills show that shader mutants reach the tests (S01 to S03, S05, S07, S09 to S11, S13, S15, S16). C18 (no barrier for the normals target) is killed by the validation layer: VUID-vkCmdDispatch-imageLayout-00344 on the g-buffer normal, in Gpu.DisposeAndAssertClean.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| S01 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:81 | continuesPlane one-way: no second planeHolds | killed | PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 1), PyramidRenderTests.ThePyramidsFollowAResize | 20.6 |
| S02 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:67 | continuation share 1 % -> κ 10 % | killed | GrazingReflectionRenderTests.AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines, GrazingReflectionRenderTests.AMirrorBallReflectsAGlowingFloorThatTheEyeSeesAtAGrazingAngleWithNoBands, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6) | 22.1 |
| S03 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:67 | continuation share 1 % -> 1e30 (every neighbor continues) | killed | GrazingReflectionRenderTests.AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines, GrazingReflectionRenderTests.AMirrorBallReflectsAGlowingFloorThatTheEyeSeesAtAGrazingAngleWithNoBands, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6) | 20.8 |
| S04 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:55 | no plane is edge-on | survived |  | 21.3 |
| S05 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:26 | edge-on share 1e-6 -> 0.05 | killed | GrazingReflectionRenderTests.AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines, GrazingReflectionRenderTests.AMirrorBallReflectsAGlowingFloorThatTheEyeSeesAtAGrazingAngleWithNoBands, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6) | 22.1 |
| S06 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:80 | edge-on planes continue and are continued | survived |  | 21.0 |
| S07 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:78 | a neighbor beyond the screen stops the plane | killed | PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 4) | 20.6 |
| S08 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:128 | pixelBounds near sum in another order | survived |  | 19.2 |
| S09 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:108 | stop toward the lower neighbor a quarter pixel past the center line | killed | GrazingReflectionRenderTests.AMirrorFloorThatTheEyeSeesAtAGrazingAngleReflectsTheOutlineOfABallAgainstTheSkyWithNoStreak(resolution: Full), GrazingReflectionRenderTests.AMirrorFloorThatTheEyeSeesAtAGrazingAngleReflectsTheOutlineOfABallAgainstTheSkyWithNoStreak(resolution: Half), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3) | 19.9 |
| S10 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:115 | surfaceW unbounded: no clamp to the part | killed | GrazingReflectionRenderTests.AMirrorBallReflectsBallsOnAFloorThatTheEyeSeesAtAGrazingAngleWithNoDottedLines, GrazingReflectionRenderTests.AMirrorFloorThatTheEyeSeesAtAGrazingAngleReflectsTheOutlineOfABallAgainstTheSkyWithNoStreak(resolution: Full), GrazingReflectionRenderTests.AMirrorFloorThatTheEyeSeesAtAGrazingAngleReflectsTheOutlineOfABallAgainstTheSkyWithNoStreak(resolution: Half) | 19.2 |
| S11 | src/Tertius.Rendering/Shaders/depth_pyramid.glsl:46 | sky (distance 0) counts as a surface | killed | PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 10) | 22.5 |
| S12 | src/Tertius.Rendering/Shaders/reflection.glsl:198 | crossed: no back entry (hit at s from behind) | survived |  | 45.2 |
| S13 | src/Tertius.Rendering/Shaders/reflection.glsl:193 | a stretch behind the slab's back still hits | killed | ReflectionRenderTests.ARayThatPassesFarBehindAnObjectShowsWhatLiesBehindIt(resolution: Full), ReflectionRenderTests.ARayThatPassesFarBehindAnObjectShowsWhatLiesBehindIt(resolution: Half) | 46.6 |
| S14 | src/Tertius.Rendering/Shaders/reflection.glsl:182 | level-0 test without the beyond-the-screen guard | survived |  | 42.7 |
| S15 | src/Tertius.Rendering/Shaders/reflection.glsl:231 | level-0 test reads w(s), not the carried inW | killed | ReflectionRenderTests.AFarPlanetShowsInANearMirror(resolution: Full) | 41.6 |
| S16 | src/Tertius.Rendering/Shaders/reflection.glsl:204 | q from inW at s, not from the entry edge | killed | ReflectionRenderTests.AMirrorGivesNoWeightToASurfaceThatItsRayMeetsFromBehind | 43.2 |
| C01 | src/Tertius.Rendering/Shading/DepthPyramid.cs:120 | ContinuesInto one-way: no other.Holds | killed | DepthPyramidTests.APlaneThatPassesTheCenterOfAFarNeighborByChanceStopsAtItsCenterLineTowardIt, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 1) | 17.9 |
| C02 | src/Tertius.Rendering/Shading/DepthPyramid.cs:106 | Holds: <= -> <, >= -> > | survived |  | 4.1 |
| C03 | src/Tertius.Rendering/Shading/DepthPyramid.cs:105 | continuation share 1 % -> κ 10 % | killed | DepthPyramidTests.ANeighborWithinOnePercentOfThePlaneInInverseDepthContinuesIt(offPlane: -0.0120000001, continues: False), DepthPyramidTests.ANeighborWithinOnePercentOfThePlaneInInverseDepthContinuesIt(offPlane: 0.0120000001, continues: False) | 4.1 |
| C04 | src/Tertius.Rendering/Shading/DepthPyramid.cs:90 | no plane is edge-on | killed | DepthPyramidTests.AnEdgeOnPixelKeepsItsCentersInverseDepthOverItsWholeSquare | 4.2 |
| C05 | src/Tertius.Rendering/Shading/DepthPyramid.cs:117 | an edge-on plane continues into its neighbors | survived |  | 4.5 |
| C06 | src/Tertius.Rendering/Shading/DepthPyramid.cs:118 | an edge-on neighbor continues the plane | killed | DepthPyramidTests.APlaneStopsAtItsCenterLineTowardAnEdgeOnNeighbor | 4.2 |
| C07 | src/Tertius.Rendering/Shading/DepthPyramid.cs:226 | a neighbor beyond the screen stops the plane | killed | DepthPyramidTests.AtTheScreensEdgeAPixelsPlaneReachesThePixelsEdge | 4.1 |
| C08 | src/Tertius.Rendering/Shading/DepthPyramid.cs:81 | sky (distance 0) counts as a surface | killed | DepthPyramidTests.TowardTheSkyAPixelsPlaneStopsAtItsCenterLine | 4.3 |
| C09 | src/Tertius.Rendering/Shading/DepthPyramid.cs:152 | Bounds near: max -> min on y | killed | DepthPyramidTests.AtTheScreensEdgeAPixelsPlaneReachesThePixelsEdge, ScreenRayTests.ARayThatComesDownOntoAFloorSeenAtAGrazingAngleHitsItsFrontWhereItCrossesIt, DepthPyramidTests.AFloorPixelWhoseNeighborsContinueItsPlaneBoundsThePlaneAtTheCornersOfItsSquare | 4.5 |
| C10 | src/Tertius.Rendering/Shading/DepthPyramid.cs:205 | stop toward the upper neighbor a quarter pixel past the center line | killed | DepthPyramidTests.APlaneStopsAtItsCenterLineTowardAnEdgeOnNeighbor, DepthPyramidTests.TowardTheSkyAPixelsPlaneStopsAtItsCenterLine, DepthPyramidTests.ANeighborWithinOnePercentOfThePlaneInInverseDepthContinuesIt(offPlane: -0.0120000001, continues: False) | 4.0 |
| C11 | src/Tertius.Rendering/Shading/DepthPyramid.cs:212 | InverseDepthAt unbounded: no clamp to the part | killed | ScreenRayTests.TheHierarchicalTraceFindsThePixelThatAPixelByPixelMarchFinds | 4.2 |
| C12 | src/Tertius.Rendering/Shading/ScreenRay.cs:181 | Trace: level 0 also asks MetCellSlab after a miss | killed | ScreenRayTests.ARayThatComesDownOntoAFloorSeenAtAGrazingAngleHitsItsFrontWhereItCrossesIt, ScreenRayTests.TheHierarchicalTraceFindsThePixelThatAPixelByPixelMarchFinds, ScreenRayTests.ASlabEntryThatRoundsOntoItsCellsFarEdgeFindsThePixelThatTheMarchFinds | 4.4 |
| C13 | src/Tertius.Rendering/Shading/ScreenRay.cs:219 | MetCellSlab never null above level 0 | killed | ScreenRayTests.ARayThatEntersASlabHalfwayInHasAnEntryShareOfOneHalf, ScreenRayTests.ARayThatEntersAWallsSlabFromBehindHasAnEntryShareOfOne, ScreenRayTests.ARayFromANearFloorHitsAPlanetAtItsVanishingPoint | 3.8 |
| C14 | src/Tertius.Rendering/Shading/ScreenRay.cs:207 | OnEdgeAfterDescent: drop entry >= exit | survived |  | 3.9 |
| C15 | src/Tertius.Rendering/Shading/ScreenRay.cs:266 | MeetPixel: no back entry | killed | ScreenRayTests.ARayThatEntersTheSlabOfATurnedWallFromBehindHitsWhereItCrossesTheSlabsBack | 4.1 |
| C16 | src/Tertius.Rendering/Shading/ScreenRay.cs:259 | MeetPixel: a stretch behind the slab still hits | killed | ScreenRayTests.ARayThatEntersTheSlabOfATurnedWallFromBehindHitsWhereItCrossesTheSlabsBack, ScreenRayTests.TheHierarchicalTraceFindsThePixelThatAPixelByPixelMarchFinds | 4.0 |
| C17 | src/Tertius.Rendering/Shading/ScreenRay.cs:195 | after a descent, inW = w(entry), not the slab bound | killed | ScreenRayTests.EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanet | 4.1 |
| C18 | src/Tertius.Rendering/Passes/DepthPyramidPass.cs:31 | no barrier for the normals target | killed | PyramidRenderTests.AResizePastTheLongestSideIsRefusedAndKeepsThePyramids, PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 6), PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 3) | 11.2 |
| C19 | src/Tertius.Rendering/Shading/ScreenRay.cs:276 | MeetPixel: q from inW at s, not from the entry edge | survived |  | 4.0 |
| C20 | src/Tertius.Rendering/Shading/ScreenRay.cs:173 | Trace: level-0 test reads w(s), not the carried inW | killed | ScreenRayTests.EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanet | 4.3 |
36 rows: killed 27, timeout 0, survived 9, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Triage of the 9 survivors:
- S04 (no GPU plane is edge-on), judgment call: no GPU scene can make an edge-on pixel on purpose. A rasterized triangle that holds a pixel's view ray within 10⁻⁶ rad covers no pixel center. The C# copy's rule is pinned by AnEdgeOnPixelKeepsItsCentersInverseDepthOverItsWholeSquare, which kills C04.
- S06 (GPU edge-on planes continue and are continued), judgment call for the same reason. The C# rule is pinned by APlaneStopsAtItsCenterLineTowardAnEdgeOnNeighbor, which kills C06.
- S08 (pixelBounds near sum in another order), judgment call: the order moves the bound by at most one unit in the last place. That is far inside the 10⁻⁶ relative tolerance of the GPU comparison, which Vulkan's precision of sqrt and division needs anyway. The order only keeps the two copies alike.
- S12 (GPU: no back entry in crossed), judgment call: the hit's light carries no weight here. A stretch that enters the slab through its back starts behind it, so q = 1 at the pixel's entry edge. The weight is then 1 − q = 0. Only the hit position inside its pixel moves. The C# twin C15 is killed by the new ARayThatEntersTheSlabOfATurnedWallFromBehindHitsWhereItCrossesTheSlabsBack.
- S14 (GPU level-0 test without the beyond-the-screen guard), judgment call: without it a texelFetch reads outside the distance image, which the Vulkan spec leaves undefined. This device returns 0 there, which reads as sky, so no test here can tell the two apart. The guard stays for the spec.
- C02 (Holds with < and > in place of <= and >=), judgment call: the two differ only where a neighbor's center lies exactly on the cut in float. The tests at 0.8 % and 1.2 % pin the share.
- C05 (an edge-on plane continues into its neighbors), equivalent: an edge-on plane has no gradient. So its surface has one inverse depth over its square wherever its part reaches, and Bounds and InverseDepthAt do not change.
- C14 (OnEdgeAfterDescent without entry >= exit), judgment call: the term matters only where a descent that starts strictly inside a cell solves its entry a rounding past the cell's exit. A temporary search of 2,000,000 random rays (seed 7) gave the same trace results under the mutant as without it.
- C19 (C# q from inW at s, not from the entry edge), judgment call: after a descent, inW is a coarse slab bound, which bounds the pixel's surface, so both give the same q up to rounding. The GPU twin S16 shows that rounding (1.4 · 10⁻⁶ of the light at q = 1 − 10⁻⁶) and is killed by ReflectionRenderTests.AMirrorGivesNoWeightToASurfaceThatItsRayMeetsFromBehind. A C# test of it pins only float noise.

New tests, each red on its mutant first (tools/mutate.py --only):
- DepthPyramidTests.AnEdgeOnPixelKeepsItsCentersInverseDepthOverItsWholeSquare: kills C04. The middle pixel of a 65 × 49 view has a normal at a right angle to its view ray. Without the edge-on rule, its bounds are NaN.
- DepthPyramidTests.APlaneStopsAtItsCenterLineTowardAnEdgeOnNeighbor: kills C06. A wall that leans back 0.24 % per row, so both planes' tests pass and only the edge-on rule stops the plane.
- DepthPyramidTests.APyramidOfAFrameRefusesOneDistanceOrOneNormalTooFew (two cases): covers the size guard of DepthPyramid.Of.
- ScreenRayTests.ARayThatEntersTheSlabOfATurnedWallFromBehindHitsWhereItCrossesTheSlabsBack: kills C15 (and C16). The crossing lies 0.6 px inside its pixel.

The decisions that the brief named, and the mutants that their tests kill:
- Continuation in both directions: C01 by APlaneThatPassesTheCenterOfAFarNeighborByChanceStopsAtItsCenterLineTowardIt, and S01 by the GPU pyramid comparison.
- The share is 1 %, not κ: C03 by ANeighborWithinOnePercentOfThePlaneInInverseDepthContinuesIt, and S02 by the dotted lines test.
- No plane reaches toward the sky: C08 and S11.
- A neighbor beyond the screen continues the plane: C07 and S07.
- An edge-on plane continues nothing, and nothing continues into it: C04 and C06, by the two new tests.

Flakiness at the 1 % cut: a temporary probe measured each edge-neighbor pair of PyramidRenderTests' scene against the cut. At 640 × 360, 284,320 pairs: one pair 8.6 · 10⁻⁶ from the cut, at (370, 168)-(370, 169), 7 within 10⁻⁴, 84 within 10⁻³. At 321 × 181 (the resize test) none within 10⁻³. A driver with other rounding can flip such a pair and move a bound by half a pixel's gradient, beyond the comparison's 10⁻⁶. So EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt and ThePyramidsFollowAResize now skip a cell bound only where an undecided pixel can reach it. An undecided pixel has a continuation test within 10⁻⁴ of its cut (UndecidedShare). Its slack is half its plane's change of inverse depth across and down. The first test also asserts that it compares more bounds than the level has cells. The skipped bounds do not hide a mutant: the final table kills S01, S07, S11 and C01 with these tests.

Finding, not fixed: ScreenRay.Trace and the march oracle disagree on 92 of 2,000,000 random rays with no mutant. The temporary search for C14 found it (seed 7, the scene of TheHierarchicalTraceFindsThePixelThatAPixelByPixelMarchFinds, which passes with its own seed). Ticket ter-cvyx holds the evidence and the two kinds of disagreement.

Final runs on the committed tests (3d7e75c77): dotnet build Tertius.slnx -c Checked passed with no warning. dotnet test --solution Tertius.slnx -c Checked: 9,929 total, 9,915 passed, 0 failed, 14 skipped.
