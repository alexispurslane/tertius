---
id: ter-y8z0
status: in_progress
deps: [ter-rjts]
links: []
created: 2026-11-01T16:01:19Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections]
---
# Reflections: find and attribute every artifact on demo-exit and the review scenes

Diagnosis ticket of docs/plans/screen-space-artifacts.md (section 'What a diagnosis delivers'). Known artifacts: the blocky fragments of the greenhouse's reflection on the deck (captures/artifacts-2026-11-01/deck-reflection-blocks.png), and the stair steps, hard cut lines and floating green line on the floor film (ter-eqtb, captures/artifacts-2026-11-01/floor-reflection-steps.png). Starts after ter-rjts merges, because ter-rjts changes the reflection passes. Build a gallery of captures at 1920 x 1080 (frames of each of demo-exit's nine films, and the review scenes of docs/plans/screen-space-reflections.md: reflections, mirror-balls, pbr-spheres, demo-surface, hair-rugs), find every artifact of the reflections, and attribute each to its stage (depth pyramid, radiance pyramid, half-resolution trace, upsample, own rays, cone, fallback) with probes and the A/B switches. No fix goes into the code.

## Acceptance Criteria

A ticket note lists every artifact found, with its crop, its stage, the probe evidence, whether it is a fault or a limit of the method, and the candidate fixes with their cost. The probes are not in the commits.


## Notes

**2026-11-01T17:30:42Z**

2026-11-01, from ter-xgra's review: structures that also show with the screen-space light off, so they probably belong to the reflections: bright dotted specks along the far creases of the floor and chute films, and sawtooth reflections on the floors of brake-run and floor. Not confirmed. ter-xgra's light-off gallery (captures/xgra/ssl-off after the merge) shows them.

**2026-11-01T23:47:03Z**

Method and gallery (builder, 2026-11-01). Base bd13d8c9 (main after the ter-rjts merge), Release, headless, 1920 x 1080, Apple M1 Max. Every GPU run held /tmp/tertius-gpu-measure.lock for that run only. Git ignores captures/, so every file below lives in this worktree's captures/y8z0/ until the orchestrator copies it to the main checkout. All paths in these notes are relative to captures/y8z0/. README.txt there lists every script, tool and probe.
- Scripts (scripts/): films.json is ter-xgra's copy of captures/pjza/demo-exit-films.json: the nine films of demo-exit, each with a second capture one tick later (<film>-next). deck-late.json is ter-k96l's late.json with deck-late and deck-late-next. The five review scenes are the scripts/ files of the repository: reflections, mirror-balls, pbr-spheres, demo-surface, hair-rugs. All runs use --size 1920x1080. The 200 timing frames of the reflections scene were deleted after each run.
- Galleries, one folder per run:
  - default/: the default setting. refl-off/: --reflections off, the twin of every view. ssl-off/: --screen-space-light off (films, deck-late, demo-surface, mirror-balls).
  - References: ref-full/ (TraceResolution.Full), ref-steps1024/ (MaxSteps 1024), ref-full1024/ (both), each from a temporary edit of ConeTracedReflections.Default. probe-lobe/ is the converged-lobe reference: at full resolution, 64 rays per pixel whose half vectors sample GGX of the pixel's alpha, each read at the cone of its own footprint only, and their mean result. It keeps the trace, the slab and the fallback, and replaces only the one-ray cone. Its fine speckle is its own sampling noise (64 rays, a hash rotation per pixel).
  - Probes: probe-<name>/, one per patch of probes/p-<name>.patch (README.txt says what each one does). probes/probe-build.sh applies a patch, builds Release into /tmp/y8z0/builds/<name>, restores and touches the files. The saved builds live in /tmp and are not kept. A fix ticket can reuse every patch.
- Sanity check of the lobe reference: on mirror-balls (smooth mirrors) it matches the default (sheets/mirror-balls-lobe-sanity.png). So the probe is correct where one ray stands for the whole lobe.
- The default against the references (analysis/refdiff.txt): in every film and review view, at most 0.54 % of the pixels differ from ref-full1024 by more than 4 levels, and the mean difference is at most 0.18 levels. Against ref-steps1024 alone it is at most 0.28 %. So the half-resolution trace, the upsample, the own rays and the 256-step limit cause no visible artifact here. Every artifact below shows in ref-full1024 as in the default.
- Consecutive frames (analysis/framediff.txt): with a still camera (unbelt, deck, spinward, antispinward) the two frames differ by more than 4 levels in at most 0.14 % of the pixels, the same as their reflections-off twins. So no pattern flickers. The floor film's camera moves: 2.92 % change with reflections against 1.49 % off. The extra change lies on the band edges of artifact 1, which move with the camera (sheets/floor-framediff-x16.png).
- docs/plans/screen-space-reflections.md has no section "Known limits". The fault-or-limit calls below use the epic's rule (docs/plans/screen-space-artifacts.md, "Goal") and the doc's decisions.

**2026-11-01T23:47:28Z**

Artifact 1: stair steps and sawtooth ramps where a ray hits a surface that the eye sees at a grazing angle (builder, 2026-11-01). This is ter-eqtb's "stair steps", and part of its "hard cut lines".
- What shows: horizontal bands several pixels tall. Inside each band the reflection fades from full to almost nothing, and at the next band it jumps back to full. So the bands read as stairs or as a sawtooth.
  - films/floor: the floor's reflection of the far floor where it curves up, x 450-1400, y 560-720 (sheets/floor-steps-refs-2x.png, shot-floor-steps.png). The band edges near y 565-590 are the "hard cut lines" of ter-eqtb.
  - films/chute-top: the slide's reflection, x 820-1160, y 560-860 (sheets/chute-top-slide-1.5x.png).
  - films/deck and films/unbelt: the same ramps inside the reflected greenhouse and cabin frame (sheets/deck-vizclass-1.5x.png, sheets/unbelt-lobe-1x.png, vizclass panel). There artifact 3 dominates.
- Stage: the half-resolution trace's level-0 hit test and its entry share q (reflection.glsl, traceRay, the branch "meetsSlab && level == 0u"). The same code runs at full resolution. Mechanism: a level-0 pixel is a slab of one inverse depth, its center's, from w down to w / (1 + kappa) (decision 16). A surface that the eye sees at a grazing angle spans a wide range of depths inside one pixel. A ray that crosses such a surface enters the next row's flat slab partway, so q at the pixel's edge rises from 0 to 1 over a band of neighboring origin rows. Then the rays pass that row and hit the next row with q = 0. The weight 1 - q makes the ramp, and the row change makes the jump.
- Probe evidence:
  - vizclass shows the ramp of 1 - q inside each band and its jump at each band edge (sheets/floor-steps-vizclass-4x.png).
  - vizpos shows that each band is one hit row (G constant per band, same sheet).
  - noq (q = 0 everywhere) removes the steps (sheets/floor-steps-refs-2x.png).
  - ref-full, ref-steps1024 and ref-full1024 keep them unchanged (same sheet). So neither the half-resolution trace nor the step limit causes them. vizclass shows no ray that runs out of steps there (no red).
  - kappa001 makes them finer and darker, with gaps (same sheet). So the slab model of the pixel is involved, not the thickness's size.
  - plane0, which treats each level-0 pixel as its plane from the G-buffer's geometric normal, removes them at both resolutions (sheets/floor-steps-plane0-2x.png). vizclass-plane0 shows q = 0 on almost every such hit (sheets/floor-cutlines-plane0-2x.png).
- Noise or bias: bias. It is the same in every run and at every reference.
- Fault or limit: a fault of the design's level-0 model, which the code implements as designed. The screen holds the information that removes it: the G-buffer's geometric normal gives the plane of each pixel. So it is not a limit of screen-space methods. A converged estimate of the design's quantity has no bands.
- Candidate fixes. Each one changes the image, so each is a design decision for the user. Costs: the four reflection passes' summed mean by the renderer's timer, 120 frames, interleaved rounds with a control build (base2, a byte copy of base), probes/timing.sh. Deck window, 1-minute load about 10: base 3.682, 3.668, 3.669 ms; control 3.691, 3.675, 3.651 ms. Floor window, load 11 to 22: base 3.529, 3.713, 3.375 ms; control 3.425, 3.475, 3.394 ms.
  - A. Level-0 pixels as planes (probe plane0): the hit test, the hit position and q use the pixel's plane over the ray's stretch inside the pixel. It removes the steps. Cost of the unoptimized probe: deck 4.448, 4.512, 4.467 ms, about +0.80 ms. Floor 3.931, 4.080, 3.914 ms, about +0.45 ms. It reads the normal at every level-0 step. A fix ticket can read it only when the flat slab overlaps. Side effect to fix: thin vertical streaks on the floor film's rising floor, x about 840 and 940, y 0-280 (probe-mirror0-plane0/films/floor.png, sheets/floor-framediff-x16.png, plane0 panel). The coarse levels keep the flat bounds, so a coarse cell can still skip a plane that leans out of its bounds. The probe did not test that.
  - B. q against the plane at the hit only (probe planeq): the steps become fainter and stay (sheets/floor-steps-planeq-2x.png). Cost: deck 3.752, 3.760 ms, about +0.09 ms. Floor 3.516 ms (one round). Not sufficient alone.
  - C. A fade by the hit surface's facing to the eye (probe grazefade): smoothstep(0.05, 0.2, |n_hit · view|). It removes the steps by dropping these hits to the map (sheets/floor-steps-grazefade-2x.png). Cost: inside the spread (deck 3.736 and 3.991 ms against base 3.721 and 3.983 ms in the same rounds, load about 20). Price: it loses the far floor's reflection, and it adds hard-edged regions where the fade cuts (chute-top slide, sheets/chute-top-slide-1.5x.png, grazefade panel). It also removes artifact 2.
  - D. No entry fade (probe noq): it removes the steps but gives a ray that passes behind an object full weight. It undoes decision 23's reason, so it is not a candidate on its own.
- Images the user needs for A against C: sheets/shot-floor-steps.png (default, full1024, plane0, off), sheets/floor-steps-grazefade-2x.png, sheets/chute-top-slide-1.5x.png.

**2026-11-01T23:47:54Z**

Artifact 2: teeth along the reflected edge of a surface that the eye sees almost edge-on (builder, 2026-11-01). This is the "sawtooth reflection on the floor of brake-run" of ter-xgra's review note.
- What shows: films/brake-run, the floor's reflection of the far floor and wall on the left, x 330-620, y 880-960. Regular teeth about 15 px apart. Each tooth has a vertical edge and a ramp, and above the teeth the reflection stops (sheets/brake-run-teeth-3x.png, sheets/brake-run-floor-1x.png).
- Stage: the trace's level-0 hit, on a surface whose screen rows each span a long stretch of depth. vizpos shows that each tooth is the hits on one pixel row of the hit surface (G constant per tooth). mirror0 shows the teeth as a hit/miss boundary: above each tooth the rays miss. So a ray that passes over one row of that surface finds no next row near it, and misses.
- Probe evidence: ref-full1024 keeps the teeth. plane0 keeps them, sharper. kappa001 thins them and keeps them. vizclass shows a q ramp in each tooth. grazefade removes them (sheets/brake-run-teeth-grazefade-2x.png): the hit surface faces the eye at a cosine below 0.2. The converged-lobe reference shows no teeth and almost no reflection there (sheets/brake-run-floor-1x.png, lobe64 panel).
- Fault or limit: a limit of screen-space methods, by my judgment. The depth image holds only one sample per pixel row of that surface, and the reflected ray sees the surface from an angle at which it covers many more pixels. So the information that would close the gaps between rows is not on the screen. I am not certain: a level-0 model that joins neighboring rows into one continuous surface could close the gaps. A probe that tests the ray against the triangle of the pixel and its neighbors, as the screen-space light's step does (docs/plans/screen-space-illumination.md, the triangle of texels), settles it. Not run.
- Candidate fixes. Each one changes the image, so each is a design decision for the user.
  - A. The grazing fade of artifact 1, candidate C: the teeth become the map's light. Cost inside the spread (see artifact 1). Price: the hard-edged regions where the fade cuts.
  - B. A continuous level-0 surface from the pixel and its neighbors. Not probed. Cost not measured. It reads two more depths per level-0 step.
  - If the user calls it a limit, ARCHITECTURE.md records it with films/brake-run.

**2026-11-01T23:47:54Z**

Artifact 3: a rough surface's reflection keeps sharp outlines, so it breaks into blocky fragments (builder, 2026-11-01). This is the "blocky fragments of the greenhouse's reflection on the deck" of the epic's table, and the largest artifact by area.
- What shows: on the demo's painted surfaces (roughness 0.38) the floor shows a clear image of the frames, pillars and windows with hard edges. Inside each fragment the color is soft, but the outlines between fragments are as sharp as a mirror's.
  - films/deck: x 940-1400, y 640-900 (sheets/shot-deck-fragments.png, sheets/deck-blocks-lobe-1.5x.png).
  - films/unbelt: the cabin floor, x 560-1500, y 520-1080 (sheets/unbelt-lobe-1x.png).
  - films/chute-top: the slide, x 820-1160, y 560-860 (sheets/chute-top-slide-1.5x.png).
  - deck-late, demo-surface at-the-seat, in-cabin and apron, and the pbr-spheres floor show the same (default against refl-off in each folder, sheets/pbr-floor-lobe-1x.png).
  - films/floor: the cut line beside the boxes, x 450-640, y about 855, is this artifact. It is the sharp outline of the reflected ceiling lamp (sheets/floor-lamp-cut-2x.png). vizpos shows the rays inside the rectangle spread over the lamp panel, and the rays outside it in bands on the ceiling (sheets/floor-lamp-cut-vizpos-2x.png). The remaining cut lines of the floor film with plane0, the vertical edges at x about 585 and 760, y 560-600, are outlines of reflected ceiling structures of the same kind (sheets/floor-cutlines-plane0-2x.png).
- Stage: the cone (docs/plans/screen-space-reflections.md, "6. The cone", decisions 3, 19 and 20, open fork 1). One ray along the dominant direction decides which surface a pixel reflects. One trilinear read at that hit blurs the color around the hit on the screen. So the blur covers the color inside a fragment, and the outline between two hit surfaces, or between a hit and a miss, stays where the single ray puts it. The design names this ("each cone blurs only its own surroundings, so no cone can place the outline between them").
- Probe evidence: the converged-lobe reference (64 GGX rays per pixel, the same trace, slab and fallback) shows a soft glow with no outline on the deck, on unbelt, on the chute-top slide and on the pbr-spheres floor. On the smooth mirrors of mirror-balls it matches the default. ref-full1024 keeps every fragment, so resolution and steps do not cause it. vizclass shows the fragments' hit/miss boundaries (sheets/deck-vizclass-1.5x.png). plane0 keeps the fragments and removes only their inner ramps (sheets/deck-blocks-lobe-1.5x.png).
- Noise or bias: bias. The default is noiseless and its outlines are wrong: the reference has none.
- Fault or limit: a fault of the design's one-ray cone at this roughness. It is not a limit of screen-space methods, because the reference uses the same screen. The code implements the design. Through the greenhouse's glass, which the depth image does not hold, the rays also enter the interior that the eye sees. That part is the screen-space limit "behind the nearest surface", and the lobe reference shows that it needs no outline either.
- Candidate fixes. Each one changes the image or costs GPU time, so each is a design decision for the user. None of them accumulates over frames.
  - A. Several rays per pixel over the lobe, in a fixed pattern per pixel, with a spatial filter: the method of the reference. Cost: the 64-ray probe at full resolution took 580 ms per frame (frame span on films). One full-resolution ray costs about 3 ms more than the half-resolution default (ARCHITECTURE.md, "The cost"). So 4 half-resolution rays cost about +3 to +4 ms. Not measured.
  - B. Uludag's march of circles along the ray's image (open fork 1): it reads several levels along the ray and weights them by coverage, so the outline blurs with the cone. Not probed. Cost not measured. It adds a few reads per pixel.
  - C. Ray reuse in the upsample: each pixel blends the hits of its neighboring texels inside the lobe's projected footprint on the receiver, weighted by the BRDF (Stachowiak 2015, without the temporal filter). Deterministic, one pass. Not probed. Estimated 0.2 to 0.5 ms.
  - D. A lower MaxRoughness default, below 0.38: the demo's paint keeps only the environment map's reflection, which refl-off shows. ARCHITECTURE.md measured the reflections at 0.25 ms per frame with the maximum at 0.3 (the pass alone, before ter-rjts). The four reflection passes take about 3.7 ms on the deck window at the default by this ticket's timing (artifact 1). The deck and unbelt of refl-off are its image. Smooth surfaces keep their reflections.
- Images the user needs: sheets/shot-deck-fragments.png (default, converged lobe, off), sheets/unbelt-lobe-1x.png, sheets/pbr-floor-lobe-1x.png.

**2026-11-01T23:48:16Z**

Artifact 4: reflections stretched by the slab's thickness, tombstones and capsules (builder, 2026-11-01).
- What shows:
  - pbr-spheres front: each red sphere's reflection in the floor is a tombstone with straight sides and square shoulders, x 290-1000, y 720-870, where a mirror image of a sphere is round (sheets/shot-pbr-tombstones.png, sheets/pbr-floor-probes-1x.png).
  - reflections front: the blue sphere's reflection is a long capsule that reaches far below the sphere, x 1600-1920, y 400-620 (sheets/reflections-front-sphere-2x.png).
  - The fragments of artifact 3 change shape with the thickness too (sheets/floor-steps-refs-2x.png and the deck panels of probe-kappa001), so part of their outline is this artifact.
- Stage: the trace's slab thickness, kappa = 0.1 (decision 16, ThicknessShare in depth_pyramid.glsl). A ray that passes behind the sphere's front surface by less than a tenth of its depth counts as a hit on the sphere's pixel. vizclass shows those hits with q near 0, so they get full weight.
- Probe evidence: kappa001 (0.01 in the trace only) turns the tombstones round and shortens the capsule (sheets/pbr-floor-probes-1x.png, sheets/reflections-front-sphere-2x.png). ref-full1024 keeps both shapes. The converged-lobe reference keeps the capsule on the mirror floor of the reflections scene, because it uses the same slab. plane0 and noq keep the tombstones.
- Fault or limit: a limit of screen-space methods. Nothing behind the nearest surface is on the screen, so the trace must guess the thickness, and any single kappa is wrong for some object. The design chose 0.1 and left the check to Part 6's captures. The price of a thinner slab shows on films/floor: at 0.01 the far floor's reflection breaks into finer bands with gaps (sheets/floor-steps-refs-2x.png, kappa001 panel). With the plane model of artifact 1, a thinner slab can work on such floors. A probe of plane0 with kappa 0.02 settles that. Not run.
- Candidate fixes. Each one changes a default or costs GPU time, so each is a design decision for the user.
  - A. A smaller kappa, for example 0.02, together with artifact 1's candidate A. No cost of its own. Not probed together.
  - B. A back-face depth layer: a second depth image of the back faces gives each pixel its real thickness. Cost: one more depth pass of the scene (the G-buffer pass takes 0.35 ms on the deck window), and an R32F image of 8.3 MB at 1080p. Not probed.
  - C. Keep kappa = 0.1 and record the limit in ARCHITECTURE.md with pbr-spheres and the reflections scene.

**2026-11-01T23:48:16Z**

Structures that are not reflection artifacts (builder, 2026-11-01).
- The floating green line of ter-eqtb, films/floor at x 735-850, y 497: scene geometry. It shows at the same pixels with --reflections off and with --screen-space-light off (sheets/floor-green-line-6x.png). It is a thin green object on the far floor where the floor curves up, most likely the flat green plank that films/spinward shows on the floor at x 480-590, y 740-790. So ter-eqtb's green line needs no fix in the reflections.
- The bright dotted specks along the far creases (films/floor along the left crease, x 400-760, y 120-320; also chute-top, chute and brake-run): ter-kn1d's sun leak through the cascade shadow filter. They show the same with --reflections off. ter-kn1d's after-capture of the floor film, which has reflections on, shows none (sheets/floor-crease-specks-kn1d-3x.png against /Users/alexispurslane/.t3/worktrees/tertius/ter-kn1d-environment-map-flip/captures/kn1d-fix/after/films/floor.png). The reflections add no specks of their own there.
- The dark speckled band along the creases of brake-run and the deck, and the speckle on the floor under the bar of the reflections scene, show with reflections off too. They belong to the screen-space light (ter-xgra, ter-k96l).

**2026-11-01T23:48:33Z**

Summary (builder, 2026-11-01). Four reflection artifacts, each with its stage and probe evidence in its own note above. No change to src, tests, shaders or tools. The probes are only in captures/y8z0/probes/.

| Artifact | Views | Stage | Fault or limit | Candidates |
|---|---|---|---|---|
| 1. Stair steps and sawtooth ramps on surfaces seen at a grazing angle | floor (ter-eqtb's steps and cut lines), chute-top, inside deck and unbelt | Level-0 hit test and entry share q: a pixel is a flat slab at its center depth | Fault of the design's level-0 model (the G-buffer normal holds the plane) | A pixel planes, +0.80 ms deck, +0.45 ms floor (unoptimized). B q at the plane only, +0.09 ms, partial. C grazing fade, about 0 ms, drops the hits to the map |
| 2. Teeth on the reflected edge of a surface seen almost edge-on | brake-run | Level-0 hit on rows that each span a long depth | Limit by judgment, not certain: a continuous-surface probe settles it | A grazing fade (as 1C). B continuous level-0 surface, not probed |
| 3. Rough reflections with sharp outlines, blocky fragments | deck (the epic's blocks), unbelt, chute-top, deck-late, demo-surface, pbr-spheres, the lamp cut on floor | The cone: one ray decides the surface, one blurred read colors it | Fault of the one-ray cone at roughness 0.38 (the converged-lobe reference has no outlines) | A several rays per pixel, about +3 to 4 ms. B Uludag's cone march, not measured. C ray reuse in the upsample, about 0.2 to 0.5 ms estimated. D MaxRoughness below 0.38, saves most of 3.7 ms |
| 4. Tombstones and capsules from the slab's thickness | pbr-spheres, reflections front | The slab thickness kappa = 0.1 | Limit of screen-space methods (nothing behind the nearest surface) | A kappa 0.02 with 1A. B back-face depth layer, about +0.35 ms. C record the limit |

- Stages with no artifact: the depth pyramid, the radiance pyramid, the half-resolution trace, the upsample, the own rays and the step limit. The default differs from ref-full1024 in at most 0.54 % of the pixels of any view, and every artifact shows in ref-full1024 as in the default. The fallback to the environment map works as designed, and candidates 1C, 2A and 3D lean on it.
- Not reflections: ter-eqtb's floating green line is scene geometry. The crease specks are ter-kn1d's sun leak. See the note above.
- ter-eqtb's scope: it covers artifact 1 (its stair steps) and its hard cut lines, which are band edges of artifact 1 near y 565-590, and outlines of artifact 3 (the lamp cut at y about 855, and the reflected ceiling edges that remain with plane0). Its green line is not a reflection, and its title should drop it. Its acceptance ("the cause of each artifact named") is now met. Its fix scope is too narrow for artifact 1: the same fault makes the chute-top slide's steps and the ramps inside the deck's and unbelt's reflections. Artifact 3 needs its own design ticket, because each of its candidates changes a default or costs GPU time and goes to the user as two designs. Artifacts 2 and 4 each need a decision: fix or record as a limit.
- docs/plans/screen-space-reflections.md has no "Known limits" section. The fix stage, or the limit records of the epic, add it with the scenes above.
- Two designs for the user, from these notes: for artifact 1, A (planes, keeps the far reflection, about +0.5 to 0.8 ms before optimization) against C (grazing fade, free, drops it). For artifact 3, C or B (blur the outline, about 0.2 to 0.5 ms or not measured) against D (no screen-space reflection on the demo's paint, saves GPU time). Captures: sheets/shot-floor-steps.png, floor-steps-grazefade-2x.png, chute-top-slide-1.5x.png, shot-deck-fragments.png, unbelt-lobe-1x.png, pbr-floor-lobe-1x.png, shot-pbr-tombstones.png.
