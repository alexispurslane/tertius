---
id: ter-eqtb
status: open
deps: [ter-rjts, ter-y8z0, ter-f3g2, ter-8bda]
links: [ter-f3g2]
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
