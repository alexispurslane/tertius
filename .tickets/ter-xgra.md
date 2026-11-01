---
id: ter-xgra
status: open
deps: []
links: []
created: 2026-11-01T16:01:19Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, screen-space-light]
---
# Screen-space light: find and attribute every artifact on demo-exit and the review scenes

Diagnosis ticket of docs/plans/screen-space-artifacts.md (section 'What a diagnosis delivers'). Known artifact: the band of dark brown specks along the floor's creases on the deck (ter-k96l, captures/artifacts-2026-11-01/deck-crease-specks.png and captures/deck-fringe-2026-10-31/). Build a gallery of captures at 1920 x 1080 (frames of each of demo-exit's nine films, and the review scenes of docs/plans/screen-space-illumination.md: cabin-light, bounce-raised-box, bounce-red-wall, demo-surface, mirror-balls), find every artifact of the screen-space light, and attribute each to its stage (depth pass, sectors, denoise, upsample, ambient pass, bounce) with probes and the A/B switches. No fix goes into the code.

## Acceptance Criteria

A ticket note lists every artifact found, with its crop, its stage, the probe evidence, whether it is a fault or a limit of the method, and the candidate fixes with their cost. The probes are not in the commits.


## Notes

**2026-11-01T17:09:39Z**

Method and gallery (builder, 2026-11-01). Base 79d27a68 (main after ter-pjza), Release, headless, 1920 x 1080, Apple M1 Max. Every GPU run held the GPU lock. Git ignores captures/, so every file below lives in this worktree's captures/xgra/ until the orchestrator copies it to the main checkout. All paths in these notes are relative to captures/xgra/.
- Scripts are in scripts/. films.json is captures/pjza/demo-exit-films.json at 1920 x 1080. It adds a second capture one tick after each of the nine films (<film>-next) and keeps every other tick. deck-late.json is ter-k96l's late.json with the captures deck-late and deck-late-next. The five review scenes are the scripts/ files at 1920 x 1080: cabin-light, bounce-raised-box, bounce-red-wall, demo-surface and mirror-balls.
- The galleries have one folder per script:
  - default/: the default setting.
  - ssl-off/: --screen-space-light off, the light-off twin.
  - ref-16x16/: 16 slices and 16 steps per side, the most that the setting allows. The slices came from a temporary edit of VisibilityBitmaskLight.Default, the steps from --screen-space-light-steps 16.
  - ref-16x3/: 16 slices and 3 steps per side, films and deck-late only.
  - default-nobloom/: --bloom off. It is the twin of the ratio and lobe probes, which ran with --bloom off.
- sheets/full-<script>_<view>.png shows each view as default, ref-16x16 and off, at half size. The artifact notes name the crops and the probe sheets. Pixel positions are in the 1920 x 1080 image, as x0,y0-x1,y1.
- probes/ holds each probe as p-<name>.patch, with the build and capture scripts and the sheet tools. probe-build.sh applies a patch, builds, saves the build, restores and touches the files, and prints git status. A fix ticket can reuse them. The probes:
  - noM: the net bounce without M, the map's light along the blocked directions.
  - noG: the net bounce without G, the bounce.
  - hemi: no M, and V scales the map's diffuse term, the model of decision 31.
  - nodenoise: the denoise passes its input through.
  - nearest: the upsample takes the texel under the pixel.
  - rough1 and rough0: every M read at roughness 1 or 0.
  - roughfloor05: every M read at roughness 0.5 or more.
  - ratio: a false-color view of M over c_diff D, the map's diffuse term. Red is 1 or more, where the clamp at 0 binds unless G gives light back. Yellow is 0.5 to 1, green 0.25 to 0.5, blue 0.1 to 0.25, gray less.
  - kappa01: the slab's thickness share is 0.01 in stepRun, not 0.1.
  - facediffuse: the environment faces keep only the diffuse part of the direct light.
  - clampviz: a first view of the clamp, spoilt by bloom. The ratio probe replaces it.
- Consecutive frames. In every artifact region, the two frames differ by at most 4 levels with the light on and 1 level with it off. The mean difference is 0.04 to 0.33 levels, because those cameras are still. So the pattern does not flicker under a still camera. A moving camera moves surfaces under the fixed noise, as "The noise" of the plan says. Beside the moving box of antispinward, 5.5 % of the pixels change by more than 4 levels with the light on. With it off, 4.2 % change.

**2026-11-01T17:10:05Z**

Artifact 1: the specks and blotches of M (builder, 2026-11-01). This is ter-k96l's artifact, and the same cause makes four more.
- What shows: dark texels that the denoise spreads into blotches 5 to 20 px wide. They lie along creases and on surfaces with a few blocked sectors. The light-off twins show none of them.
  - deck-late: the brown band along the cabin's base, x 1000-1920, y 600-780 (sheets/deck-late-crease-3x.png).
  - deck-late: the foot of the blue pillar, 940,600-1150,760 (sheets/deck-late-pillar-upsample.png), and the base of the far wall, x 0-700, y about 575.
  - brake-run: round dark dots along the left crease, 300,560-620,740 (sheets/brake-run-left-3x.png).
  - unbelt: blue speckle on the light-blue surfaces, 150,180-470,330 and 0,380-320,560 (sheets/unbelt-wall-3x.png, sheets/unbelt-left-3x.png).
  - chute-top: grain on the slide's side face, 1150,780-1470,960 (sheets/chute-top-slide-3x.png).
  - spinward: grain under the ceiling crease, 100,260-420,440 (sheets/spinward-wall-3x.png).
- Stage: the sectors pass, in the reads of M (mapLightOf in occlusion_sectors.comp). The denoise spreads the outliers. The upsample, the depth pass and the bounce do not cause them.
- Probe evidence: sheets/deck-late-probes-3x.png, brake-run-left-probes.png, unbelt-probes.png, chute-top-noise-probes.png and spinward-noise-probes.png.
  - noM removes every one of them. noG leaves each one as it is.
  - hemi gives a smooth mild darkening. So V and G are not noisy enough to make them. M is.
  - nodenoise shows the raw texels: single dark texels, salt and pepper, dense near the crease. The two blurs turn each into a soft blotch.
  - nearest shows the same blotches in 2 x 2 blocks. So the upsample only carries them.
- The lobe decides it (sheets/deck-late-lobe-ratio.png). rough0, every M read sharp, makes the blotches black blobs. rough1, every read at the widest lobe, removes them and leaves a faint smooth darkening. Decision 42 reads each newly blocked piece with a lobe as narrow as the piece, α = 0.121 w. With 3 steps per side, a step usually blocks a few sectors. So most reads are almost sharp. The map has small bright parts: the cabin's lit interior, the lamps' lit ceilings and the blue opening. A sharp read lands on a bright part in some texels and misses it in their neighbors.
- Noise, not bias, for the blotches. ref-16x3 has 16 slices and the same pieces, and it averages them into a nearly smooth band (sheets/deck-late-refs.png, sheets/brake-run-refs.png). ref-16x16 is not a fair reference for M. Its 16 steps make the pieces narrower and the reads sharper, so the brake-run dots get rounder and darker there. The band that ref-16x3 keeps is artifact 2.
- Fault or limit: a fault. The design's choice, a lobe as narrow as each piece, misbehaves where a correct estimate of M does not. A converged M has no specks. Decision 42 names the risk: sharp reads "carry a small bright part of the map" into the fixed noise. It picked the 5 % lobe against that risk. But at 3 steps the pieces are narrow, and the 5 % lobe is close to sharp.
- Candidate fixes. Each changes the image beyond the image rule, so each is a design decision for the user.
  - A. A floor under the lobe of the M reads, for example roughness 0.5 or more (probe roughfloor05). The images are probe-roughfloor05/, sheets/deck-late-candidates.png and sheets/brake-run-candidates.png. The blotches go. A thin smooth dark line stays along the deck crease, and the brake-run dots almost go.
  - A, cost. The runs used the deck window: time-deck.json, 120 frames from frame 399, --video none. Each of 6 rounds ran the base, a copy of the base as the control, and the probe, in a rotated order. The renderer's timer gave the sum of the five passes. The probe took +0.018 ms over the base in the median round, range +0.004 to +0.060. The control took +0.0085 ms, range -0.004 to +0.024. So the probe costs about +0.01 ms, within the spread. The load was 2.5 to 5.1 (timing/summary.txt).
  - A, pixels against the base. deck-late: 45.6 % of the pixels differ, 106,096 px by more than 2 levels, largest 113. unbelt: 47,700 px by more than 2 levels. chute: 141,945 px by up to 64 levels, almost all from a 0.01-stop change of the exposure. The other films: 700 to 33,500 px.
  - A, the price. A wider lobe takes in more map light beyond the piece's ends, the first item of "Known limits". The tests of a wall that the map holds measure 2 % today. A fix ticket reruns AFloorPointBesideAGlowingWallThatTheMapAlsoHoldsGetsTheWallsLightOnce and its dark-wall twin, and it reports the new leak.
  - B. One M read per slice for its whole blocked run, with the lobe of the run's width. It takes fewer reads and wider lobes. Not probed. It breaks decision 42's rule that a read stands for an unbroken piece. So a read can point into a gap that a nearer step already counted.
  - C. More slices. ref-16x3 gives a nearly smooth band. Its sectors pass took 6.7 ms against 1.5 ms (2 capture frames, the renderer's timer), about +5 ms. That is not a practical default.
  - D. A spatial outlier clamp before the denoise. The first denoise dispatch clamps each texel's net bounce to the range of its 3 x 3 neighbors. It keeps no state between frames, so decision 8 holds. Not probed. Its cost is about the reads that the denoise already does, less than 0.05 ms by the denoise's 0.107 ms for two dispatches. It hides outliers and does not remove their cause. It also clamps real detail of one texel.

**2026-11-01T17:10:39Z**

Artifact 2: a dark band with a brown hue where M exceeds the map's diffuse term (builder, 2026-11-01). The band stays after the noise of artifact 1 is averaged out.
- What shows. It shows on deck-late only, after the blue light floods the deck. A dark band 10 to 20 px wide lies along the cabin's base and the far wall's base, x 1000-1920, y 600-780. It is brown at its darkest. The cabin's base face is near black, where the light-off twin shows dark purple. ref-16x3 and ref-16x16 keep a smooth band in the same place with the same hue (sheets/deck-late-refs.png). The deck film, at tick 769 before the flood, shows a smooth gray darkening and no band (sheets/deck-crease-3x.png).
- Stage: M in the sectors pass and the clamp at 0 in the ambient pass (occludedAmbient in lighting.glsl). The denoise and the upsample do not cause it.
- Probe evidence.
  - On deck-late, the ratio view shows M at or above c_diff D (red) over the cabin's whole base face. It also shows red patches in the floor crease (sheets/deck-vs-late-ratio.png, full frames in probe-ratio/). In the deck film the ratio is only 0.25 to 1, green and yellow.
  - Where the ratio is red, D − M + G is below 0 in some channel unless G gives the light back. G is the dark cabin base's direct light, and noG leaves the band as it is.
  - The clamp max(..., 0) works on each channel apart. The map's light is blue, so the blue channel reaches 0 first. The red and green that remain read as brown.
  - rough1 nearly removes the band (sheets/deck-late-lobe-ratio.png). So the band follows what the sharp reads see in the map.
  - The lobe tails below the horizon, the first item of "Known limits", are not the cause. rough0 has no tails and makes the band darker.
  - The floor's glossy highlight is not the cause. The map's faces hold it toward the camera, and G holds only diffuse light (decision 16). But facediffuse, faces with diffuse light only, leaves the band as it is (sheets/deck-late-facediffuse.png, light-off twin in probe-facediffuse-off/).
- Fault or limit: not sure.
  - The darkness is most likely the third item of "Known limits": the map is drawn from the camera. From the camera, the near-horizontal directions toward the cabin see the lit interior through the glass. From the floor at the crease, the same directions hit the dark base. So M takes light that the blocker hides from nobody.
  - The evidence that settles it: a probe that draws the map from a point on the floor beside the crease, or that dumps the map's faces. It compares the map's radiance along the crease texels' blocked directions with the blockers' bounce source there.
  - The hue is a fault of the clamp's form, in my view. A correct estimate never goes below 0, so it never changes the hue. "Known limits" names the clamp but not the change of hue.
- Candidate fixes. Each changes the image, so each is a design decision for the user.
  - A. Candidate A of artifact 1, the lobe floor, shrinks the band to a thin line (sheets/deck-late-candidates.png). It costs about +0.01 ms.
  - B. Keep the hue. Where D − M + G goes below 0 in a channel, scale the net bounce by one factor for all channels. The term then stops at 0 in its lowest channel and keeps D's color in the others. The result is a dark blue band, not a brown one. The cost is a few operations per pixel in the ambient pass, too small to measure. Not probed.
  - C. Bound M at each texel by the decision 31 value, (1 − V) D(n), so that the clamp never binds. It needs D(n) at the texel in the sectors pass, one spherical-harmonics evaluation per texel, not measured. It takes back part of decision 41: a wall that the map also holds, brighter than the mean, then counts partly twice again.

**2026-11-01T17:10:39Z**

Artifact 3: bright blotches of the bounce beside lit surfaces (builder, 2026-11-01).
- What shows: soft bright blotches 5 to 15 px wide, where the bounce of a bright blocker reaches a darker receiver. ref-16x16 shows each as a smooth glow, and the light-off twins show no glow.
  - brake-run: the upper side of the lit streak on the right wall, 1600,720-1920,880 (sheets/brake-run-right-3x.png).
  - demo-surface terminal: a bright cloud on the floor beside the glowing door, 1000,440-1320,620 (sheets/terminal-door-3x.png).
  - mirror-balls balls: mottled light on the wall behind the balls, 960,560-1280,700 (sheets/balls-shelf-3x.png).
  - cabin-light cabin: bright grain on the shadowed floor and wall beside the sunlit floor, 560,680-880,860 and 1380,660-1700,840 (sheets/cabin-left-3x.png, sheets/cabin-wallbase-3x.png).
- Stage: G in the sectors pass, the light of the blockers, at 3 slices and 3 steps per side. The denoise works as XeGTAO's does. Its two 3 x 3 dispatches leave what they cannot average.
- Probe evidence (sheets/brake-run-noise-probes.png): noG removes the blotches. noM, rough1 and hemi keep them. ref-16x16 smooths them. So it is noise, not bias: the reference keeps the glow and loses the blotches.
- Fault or limit: a fault of the default sample count by this ticket's rule, because a converged estimate shows no blotches. The design accepts it. "Compared with a design from scratch" names the cost of fixed noise and blur only (decision 3): "a fixed pattern that the blur does not remove in full". Part 7 picked 3 steps as "the strongest setting without visible grain" at full size. At 3x the grain shows. The code does what the design says, so it is not a code fault.
- Candidate fixes. Each is a default change or costs GPU time, so each goes to the user.
  - A. More slices. 16 slices with 3 steps cost about +5 ms in the sectors pass (artifact 1, candidate C). 6 slices cost about +1.2 ms by proportion (5.2 ms for 13 more slices), not measured.
  - B. A third denoise dispatch, or a 5 x 5 kernel in the last one. It costs about +0.05 ms per dispatch, by the two dispatches' 0.107 ms. Not probed. A wider blur also softens real detail and contact shadows.
  - C. Whether more steps alone reduce the noise of G was not measured. ref-16x16 changes the slices and the steps together.

**2026-11-01T17:10:52Z**

Artifact 4: a dark streak behind a box on the floor (builder, 2026-11-01).
- What shows. On antispinward, a thin dark wedge about 150 px long lies on the floor, 1600,770-1920,900 (sheets/antispinward-box-3x.png). It starts at the foot of the orange box at the right and runs away from the camera, behind the box's left edge. ref-16x16 keeps it as a smooth wedge, so it is not noise. The light-off twin has none.
- Stage: the slab of the steps in the sectors pass (stepRun in sectors.glsl). M then darkens the directions that the slab blocks.
- Probe evidence (sheets/antispinward-box-probes.png):
  - kappa01, a slab of 0.01 z in place of 0.1 z, removes the wedge except a faint dashed line at its root.
  - noM removes it. hemi leaves only a small contact shadow at the box's foot.
  - So the box's slab reaches 0.1 z behind each of its pixels along the view ray. It blocks directions of floor points behind the box that nothing blocks. M takes the map's light of those directions away, far more than V alone (hemi).
- Fault or limit: a limit, the eighth item of "Known limits": κ z is one thickness for everything (decision 13, the user's). The M of artifacts 1 and 2 makes the streak stronger than the crease shadowing alone. Candidate A of artifact 1 can weaken it too, not measured here.
- Candidate fixes. Both are design changes for the user.
  - A thinner slab for the near steps than for the reflections, at no cost. It splits decision 13's one model of what is solid. The probe kappa01 shows the image (probe-kappa01/).
  - A pass of back faces for a real thickness. It is a second geometry pass, which the design left out ("Compared with a design from scratch").

**2026-11-01T17:13:09Z**

Summary (builder, 2026-11-01). The gallery covers the nine films of demo-exit, deck-late, and the five review scenes. Paths are relative to captures/xgra/, as in the first note.

| Artifact | Views | Stage | Fault or limit | Best candidate and cost |
|---|---|---|---|---|
| 1. Specks and blotches of M | deck-late, brake-run, unbelt, chute-top, spinward | Sectors pass, the M reads, spread by the denoise | Fault: the lobe as narrow as each piece is almost sharp at 3 steps | Lobe floor, roughness 0.5 or more: about +0.01 ms |
| 2. Dark brown band where M exceeds D | deck-late after the flood | Sectors pass M, and the clamp at 0 in the ambient pass | Not sure: the darkness is most likely limit 3 (the map drawn from the camera). The brown hue is a fault of the clamp per channel | Lobe floor shrinks it. A clamp by one factor keeps the hue, at no measurable cost |
| 3. Bright blotches of the bounce | brake-run, terminal, mirror-balls, cabin | Sectors pass G at 3 slices, the denoise | Fault of the default sample count, a cost that the design accepts | More slices (+1.2 ms for 6) or one more denoise dispatch (+0.05 ms) |
| 4. Dark streak behind a box | antispinward | Sectors pass, the slab of κ z, made stronger by M | Limit: κ z is one thickness for everything (limit 8) | A thinner slab for the near steps (design change, no cost) |

- Each candidate changes a default or the image, so each goes to the user as two designs with captures. No candidate is a plain bug fix.
- The captures for the user's comparison of artifact 1's candidates: sheets/deck-late-candidates.png and sheets/brake-run-candidates.png show the default, the lobe floor, 16 slices and the light off. Full frames are in default/, probe-roughfloor05/, ref-16x3/ and ssl-off/.
- Not found: no fault of the depth pass and no tile or block edges. The upsample makes no halo or dark line at depth edges: the nearest probe shows the same structures (sheets/deck-late-pillar-upsample.png). No light leaks through a wall. The bounce has the right color: bounce-red-wall tints the floor red.
- Not an artifact: ref-16x16 darkens some creases more than the default, for example spinward's ceiling crease. That is the seventh item of "Known limits": light passes between two steps. It is a difference of strength, not a structure.
- All four artifacts come from the screen-space light. Artifacts 1 and 2 come from its decision 42: the reads of the environment map along the blocked directions. So a fix touches the environment map's side of the estimate too.
- The probes are not in the commits: each patch was applied for one build and then restored and touched. git diff --stat 79d27a68..HEAD lists only .tickets/ter-xgra.md.
