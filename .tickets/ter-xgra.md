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

Method and gallery (builder, 2026-11-01, revised in the fix stage). Base 79d27a68 (main after ter-pjza), Release, headless, 1920 x 1080, Apple M1 Max. Every GPU run held the GPU lock. Git ignores captures/, so every file below lives in this worktree's captures/xgra/ until the orchestrator copies it to the main checkout. All paths in these notes are relative to captures/xgra/.
- Scripts are in scripts/. films.json is captures/pjza/demo-exit-films.json at 1920 x 1080. It adds a second capture one tick after each of the nine films (<film>-next) and keeps every other tick. deck-late.json is ter-k96l's late.json with the captures deck-late and deck-late-next. The five review scenes are the scripts/ files at 1920 x 1080: cabin-light, bounce-raised-box, bounce-red-wall, demo-surface and mirror-balls.
- The galleries have one folder per script:
  - default/: the default setting.
  - ssl-off/: --screen-space-light off, the light-off twin.
  - ref-16x16/: 16 slices and 16 steps per side, the most that the setting allows. The slices came from a temporary edit of VisibilityBitmaskLight.Default, the steps from --screen-space-light-steps 16.
  - ref-16x3/: 16 slices and 3 steps per side, films and deck-late only.
  - default-nobloom/: --bloom off. It is the twin of the ratio and lobe probes, which ran with --bloom off.
- sheets/full-<script>_<view>.png shows each view as default, ref-16x16 and off, at half size. The artifact notes name the crops and the probe sheets. Pixel positions are in the 1920 x 1080 image, as x0,y0-x1,y1.
- probes/ holds each probe as p-<name>.patch, with the build and capture scripts and the sheet tools. probe-build.sh <name> <patch>... applies one or more patches, builds Release, saves the build in /tmp/xgra/builds/<name>, and restores and touches the files. When the build fails, it stops with exit 2 and saves nothing. It takes the repository root from git. The saved builds live in /tmp and are not kept. capb.sh runs one capture of a saved build under the GPU lock. queue.sh runs a list of captures in order and skips the finished ones. A fix ticket can reuse them. The probes:
  - noM: the net bounce without M, the map's light along the blocked directions.
  - noG: the net bounce without G, the bounce.
  - hemi: no M, and V scales the map's diffuse term, the model of decision 31.
  - nodenoise: the denoise passes its input through.
  - nearest: the upsample takes the texel under the pixel.
  - rough1 and rough0: every M read at roughness 1 or 0.
  - roughfloor05 (lobe floor): every M read at roughness 0.5 or more.
  - ratio: a false-color view of the ratio M / (c_diff D), where D is the map's diffuse term. The view takes the largest of the three channels' ratios. Red is 1 or more in at least one channel, where the clamp at 0 binds in that channel unless G gives light back. Yellow is 0.5 to 1, green 0.25 to 0.5, blue 0.1 to 0.25, gray less.
  - kappa01: the slab's thickness share is 0.01 in stepRun, not 0.1.
  - facediffuse: the environment faces keep only the diffuse part of the direct light.
  - clampviz: a first view of the clamp, spoilt by bloom. The ratio probe replaces it.
  - The fix stage added these probes:
    - bruteD: In the ambient pass, D(n) is the cosine-weighted mean of the specular map over the hemisphere of n. It takes 256 reads of a fixed Hammersley set at roughness 0.2. It replaces the band-2 spherical harmonics. It costs 13.4 ms in the ambient pass, so it shows the image only.
    - subM: each M read is 8 reads spread over its piece, each with the lobe of an eighth of the piece. So no lobe reaches more than a few percent past its piece, and none reaches far below the receiver's plane. It clips the lobe tails of the first item of "Known limits".
    - slices16 and slices6: 16 or 6 slices in VisibilityBitmaskLight.Default.
    - Combinations, built with several patches: ratio16 (ratio at 16 slices), ratio16-bruteD, ratio16-subM, ratio16-bruteD-subM, bruteD16, subM16, bruteD-subM and bruteD-subM16. probe-bruteD-off/ is the bruteD build with the light off, the light-off twin of bruteD.
    - The candidate probes: outlier, onefactor, boundM, denoise4 and steps8. The candidate notes say what each one does.
- Measures in analysis/:
  - noise.py and noise.txt: the noise measure of the review. It is the mean absolute difference between luma and its 9 x 9 box blur over a fixed box. The unit is levels of 255 times 100. Edges and texture also count, so only a run's difference from the light-off twin of the same box means noise. Its second table gives the share of the excess noise, default minus off, that each probe removes.
  - masks.py and deck-late-masks.txt: on deck-late, the face mask is the cabin's base face from 2 to 15 px above the crease (7,358 px). The band mask is the floor from 1 to 20 px below the crease (11,320 px), in the columns x 1100-1900. The crease comes from the light-off twin. The script gives the share of red pixels of each ratio view and the mean color of each image over each mask.
  - timing2-summary.txt: the timing of the candidates (see the summary note).
- Consecutive frames. In every artifact region, the two frames differ by at most 4 levels with the light on and 1 level with it off. The mean difference is 0.04 to 0.33 levels, because those cameras are still. So the pattern does not flicker under a still camera. A moving camera moves surfaces under the fixed noise, as "The noise" of the plan says. Beside the moving box of antispinward, 5.5 % of the pixels change by more than 4 levels with the light on. With it off, 4.2 % change.

**2026-11-01T17:10:05Z**

Artifact 1: the specks and blotches of M (builder, 2026-11-01, revised in the fix stage). This is ter-k96l's artifact. M, the map's light along the blocked directions, is noisy from two sources. Some views show one source, and some show both.
- What shows: dark texels that the denoise spreads into blotches 5 to 20 px wide. They lie along creases and on surfaces with a few blocked sectors. The light-off twins show none of them.
- The two noise sources:
  - Lobe noise. Decision 42 reads each newly blocked piece with a lobe as narrow as the piece, α = 0.121 w. With 3 steps per side, a step usually blocks a few sectors, so most reads are almost sharp. The map has small bright parts: the cabin's lit interior, the lamps' lit ceilings and the blue opening. A sharp read lands on a bright part in some texels and misses it in their neighbors. A wider lobe, the lobe floor or rough1, removes this source.
  - Slice noise. Each texel has 3 slices, at fixed angles that change from texel to texel. M is then a 3-sample estimate in angle, the same mechanism as the noise of G in artifact 3. More slices remove this source, and a wider lobe does not.
- Each view, with its noise measure (analysis/noise.txt) and the share of the excess over the light-off twin that each probe removes:

| View and box | Crop | off | default | lobe floor | rough1 | 16 x 3 | noM | Source |
|---|---|---|---|---|---|---|---|---|
| deck-late, the floor along the cabin's base, 1000,600-1920,780 (measured in 1100,640-1700,760) | sheets/deck-late-crease-3x.png | 260 | 279 | 246 | 230 | 244 | 250 | Lobe noise: the lobe floor removes all of it. The box holds the crease's edges, so the numbers are mostly edges. |
| deck-late, the foot of the blue pillar, 940,600-1150,760, and the far wall's base, x 0-700, y about 575 | sheets/deck-late-pillar-upsample.png | | | | | | | Lobe noise, by sight |
| unbelt, the light-blue wall, 150,180-470,330 (measured in 160,190-300,250), and 0,380-320,560 | sheets/unbelt-candidates.png, sheets/unbelt-left-3x.png | 39 | 107 | 63 (65 %) | 65 (62 %) | 56 (76 %) | 40 | Mostly lobe noise, some slice noise |
| films/floor, the floor at the foot of the stacked boxes, 1400,860-1520,960, with a dark speck near 1437,893 | sheets/floor-boxes-foot-3x.png | 45 | 69 | 58 (45 %) | 58 (47 %) | 56 (57 %) | 49 | Both |
| cabin-light cabin, the dark wall right of the window, 1500,740-1720,850 (measured in 1550,760-1700,840) | sheets/cabin-wall-probes.png, sheets/cabin-wall-candidates.png | 172 | 214 | 197 (40 %) | 193 (50 %) | 16 x 16: 181 (78 %) | 186 (66 %) | Both. The first notes put this instance under artifact 3. noM removes the blotches and noG keeps them, so it belongs here. |
| brake-run, round dark dots along the left crease, 300,560-620,740 (measured in 320,600-600,720) | sheets/brake-run-left-3x.png, sheets/brake-run-candidates.png | 69 | 83 | 80 (23 %) | 77 (40 %) | 78 (34 %) | 68 | Both. The lobe floor shrinks the dots, and they stay. |
| chute-top, grain on the slide's side face, 1150,780-1470,960 (measured in 1160,800-1460,950) | sheets/chute-top-candidates.png | 39 | 53 | 48 (34 %) | 50 (19 %) | 44 (64 %) | 40 | Mostly slice noise |
| spinward, grain under the ceiling crease, 100,260-420,440 (measured in 120,300-400,420) | sheets/spinward-candidates.png, sheets/spinward-noise-probes.png | 50 | 81 | 78 (9 %) | 71 (32 %) | 67 (45 %) | 49 | Slice noise. rough1 and the lobe floor keep the blotches. |
| cabin-light cabin and demo-surface at-the-seat, dark blotches at the base of the gray ceiling panel, 1800-1920, 470-490 | sheets/cabin-ceiling-panel-probes.png, sheets/cabin-ceiling-panel-candidates.png, sheets/seat-ceiling-panel-3x.png | | | | | | | Slice noise, by sight: noM removes them, the lobe floor keeps them, and ref-16x16 turns them into a smooth contact line. The measure fails here, because the box holds the panel's edge. |

- Stage: the sectors pass, in the reads of M (mapLightOf in occlusion_sectors.comp). The denoise spreads the outliers. The upsample, the depth pass and the bounce do not cause them.
- Probe evidence: sheets/deck-late-probes-3x.png, brake-run-left-probes.png, unbelt-probes.png, chute-top-noise-probes.png, spinward-noise-probes.png, cabin-wall-probes.png and cabin-ceiling-panel-probes.png.
  - noM removes every one of them. noG leaves each one as it is.
  - hemi gives a smooth mild darkening. So V and G are not noisy enough to make them. M is.
  - nodenoise shows the raw texels: single dark texels, salt and pepper, dense near the crease. The two blurs turn each into a soft blotch.
  - nearest shows the same blotches in 2 x 2 blocks. So the upsample only carries them.
  - rough0, every M read sharp, makes the deck-late blotches black blobs. rough1 removes the lobe noise and leaves a faint smooth darkening (sheets/deck-late-lobe-ratio.png). On spinward and chute-top, rough1 keeps most of the blotches.
- Noise, not bias. ref-16x3 has 16 slices and the same pieces, and it averages the blotches into a nearly smooth band (sheets/deck-late-refs.png, sheets/brake-run-refs.png). In the brake-run, unbelt, chute-top and spinward regions, its mean equals the default's mean to 0.1 level (the review measured it). ref-16x16 is not a fair reference for M. Its 16 steps make the pieces narrower and the reads sharper, so the brake-run dots get rounder and darker there. steps8 shows the same: at 3 slices and 8 steps, the noise grows on unbelt (134) and spinward (93). The band that ref-16x3 keeps along the deck crease is artifact 2.
- Fault or limit, for each source:
  - Lobe noise: a fault of the design's choice. The lobe as narrow as each piece misbehaves where a correct estimate of M does not, because a converged M has no specks. Decision 42 names this risk. Sharp reads "carry a small bright part of the map, such as a lamp panel, into the fixed noise". It picked the 5 % lobe against that risk. But at 3 steps the pieces are narrow, and the 5 % lobe is close to sharp.
  - Slice noise: the same call as artifact 3. By this ticket's rule, it is a fault of the default sample count, because a converged estimate shows no blotches. The design accepts it ("Compared with a design from scratch", decision 3), so the code is not at fault.
- Candidate fixes. Each changes the image, so each is a design decision for the user. The costs come from the deck window, with the method of the summary note. The noise shares are those of analysis/noise.txt.
  - A. A floor under the lobe of the M reads, for example roughness 0.5 or more (probe roughfloor05). It removes the lobe noise: 65 % of the excess on unbelt, all of it on deck-late. It removes little slice noise: 9 % on spinward and 34 % on chute-top. Images: probe-roughfloor05/, and every candidate sheet of the table. Cost: about +0.01 ms (first timing round, 6 rounds against a control). Price: a wider lobe takes in more map light beyond the piece's ends, the first item of "Known limits". On the deck's cabin base face, it keeps the face near black (artifact 2). A fix ticket reruns AFloorPointBesideAGlowingWallThatTheMapAlsoHoldsGetsTheWallsLightOnce and its dark-wall twin, and it reports the new leak.
  - B. One M read per slice for its whole blocked run, with the lobe of the run's width. It takes fewer reads and wider lobes. Not probed. It breaks decision 42's rule that a read stands for an unbroken piece. So a read can point into a gap that a nearer step already counted.
  - C. More slices. These remove both sources. 6 slices (probe slices6) remove 10 % to 116 % of the excess: 49 % on unbelt, 18 % on spinward, 38 % on chute-top, 51 % at the boxes' foot. Cost: +0.99 ms, all in the sectors pass. 16 slices (ref-16x3) remove 34 % to 76 %, at about +5 ms. Images: probe-slices6/, ref-16x3/ and the candidate sheets.
  - D. A spatial outlier clamp before the denoise (probe outlier). The first denoise dispatch clamps each texel's net bounce to the range of its 8 neighbors. It removes 0 % to 2 % of the excess, and the images show no change (sheets/spinward-candidates.png, unbelt-candidates.png). The blotches are clusters of texels, not single outliers, so the clamp does not reach them. Cost: +0.10 ms. Rejected by this evidence.
  - E. Two more dispatches of the first, stronger denoise (probe denoise4, 4 dispatches in place of 2). It removes 17 % to 101 % of the excess: 64 % on unbelt, 34 % on spinward, 55 % on chute-top. The blotches get softer and wider, and they stay. Cost: +0.11 ms in total. It softens real detail and contact shadows too.
  - The clip of the lobe tails of artifact 2 (probe subM) makes this noise worse: unbelt 111, the boxes' foot 83. Its sub-reads are sharper than the read that they replace. So it needs A, C or E with it.

**2026-11-01T17:10:39Z**

Artifact 2: the dark brown crease of the deck, where M exceeds the map's diffuse term D (builder, 2026-11-01, revised in the fix stage). It shows on deck-late only, after the blue light floods the deck. It stays after the noise of artifact 1 is averaged out: ref-16x3 and ref-16x16 keep it with the same hue (sheets/deck-late-refs.png). The deck film, at tick 769 before the flood, shows a smooth gray darkening and no band (sheets/deck-crease-3x.png). It has two parts with different mixes of cause.
- What shows (sheets/deck-late-artifact2-candidates.png, 2x crops of 1250,630-1550,715):
  - 2a, the face: the cabin's base face just above the crease, x 1100-1900, about 2 to 15 px above the crease (the face mask of analysis/masks.py). It is near black and brown: mean (13.8, 7.5, 6.4) against (23.5, 17.6, 20.0) in the light-off twin.
  - 2b, the band: the floor from 1 to 20 px below the crease. It is dark, with brown blotches at its darkest: mean (58.4, 59.4, 72.2) against (98.2, 111.1, 148.3) off. At 1400,686: (29, 23, 23) against (108, 125, 170).
- Stage: M in the sectors pass, D in the ambient pass, and the clamp at 0 of occludedAmbient in lighting.glsl. The denoise and the upsample do not cause it.
- Why the clamp binds. M and D integrate the same map from the same point, and M covers a subset of D's directions. So a converged M never exceeds a converged D, wherever the map was drawn from. Two things break that here. Both are items of "Known limits":
  - Limit 2: D is the map's irradiance in band-2 spherical harmonics, and M comes from the specular map.
  - Limit 1: a lobe of M puts up to 5 % of its weight beyond either end of its piece. The lowest piece ends at the receiver's plane, so its lobe reaches below the plane into map directions that D does not hold.
- Probe evidence. The ratio views ran at 16 slices, so the slice noise does not decide them. sheets/deck-late-ratio16-factorial.png shows the five ratio views. The red shares over the masks (analysis/deck-late-masks.txt):

| Ratio view | Face red | Band red |
|---|---|---|
| 3 slices, SH D, lobe M (the default) | 81.2 % | 35.6 % |
| 16 slices, SH D, lobe M | 81.2 % | 38.4 % |
| 16 slices, brute-force D (bruteD), lobe M | 44.7 % | 19.3 % |
| 16 slices, SH D, clipped lobes (subM) | 81.2 % | 21.7 % |
| 16 slices, bruteD and subM | 12.0 % | 4.9 % |

  - On the face, limit 2 is the larger cause. The SH D of the face is too low by itself: its light-off twin with SH D is (23.5, 17.6, 20.0), and with brute-force D it is (31.9, 32.6, 39.7). So the face's blue irradiance from the spherical harmonics is half of the brute-force value. bruteD cuts the face's red from 81 % to 45 %. Then subM cuts the rest to 12 %. subM alone changes nothing on the face, because SH D stays too low.
  - Limit 1 grows with the lobe's width. rough1 makes the face black, (9.1, 8.4, 9.1), and the lobe floor keeps it near black, (12.7, 7.7, 7.5). sheets/deck-late-lobe-ratio.png shows rough1 turning the face black.
  - On the band, both causes are about equal. bruteD halves the red, subM nearly halves it, and both together leave 4.9 %.
  - The remaining 12 % on the face lies in the last pixels above the crease. The fourth item of "Known limits", a pixel's normal against its half-resolution texel's, can explain it. No probe tested it.
- The images with both causes removed (bruteD-subM16, 16 slices): the face is (25.7, 24.2, 25.2) against its light-off twin (31.9, 32.6, 39.7), a neutral darkening. The band is a soft blue-gray darkening, (66.5, 72.3, 93.7) against (98.4, 112.4, 150.3), with no brown. hemi, the model of decision 31, gives the band (75.6, 85.4, 117.6). At 3 slices (bruteD-subM), the band keeps the blotches of artifact 1, now blue. Full frame: sheets/deck-late-artifact2-full.png.
- The hue. The first notes said that the clamp per channel makes the brown, because the blue channel reaches 0 first. The probe onefactor tested that: it scales the net bounce by one factor for all channels, and the band turns olive, not blue (sheets/deck-late-artifact2-candidates.png). The blue channel still goes to 0 first, because M is bluer than D there. So the brown comes from M being too large in blue, through limits 1 and 2, and the clamp only stops it at 0.
- Corrections to the first notes. The first notes named limit 3, the map drawn from the camera, as the most likely cause, and they ruled out limit 1. Limit 3 cannot make the clamp bind, for the reason above. It can only make D − M close to 0. "rough0 makes the band darker" holds for single dark pixels only. Over the band mask, rough0 is lighter than the default: (66.4, 68.9, 85.1) against (58.4, 59.4, 72.2).
- The darkening that remains with both causes removed is not a structure: it has the hue of the light-off twin, and it fades smoothly from the crease. It can be a correct contact shadow, or partly limit 3. A probe that draws the map from a point on the floor beside the crease settles that part. Not run.
- Fault or limit: a limit by this ticket's rule, because limits 1 and 2 of "Known limits" name both causes. But limit 2 says "a few percent of D can remain". Here the SH D of the face is half the brute-force value in blue. So the plan understates the size of limit 2 for a map with a small bright source, and the user decides whether that is acceptable. Limit 1 is as the plan states at the default lobe, and it grows with candidate A of artifact 1.
- Candidate fixes. Each changes the image, so each is a design decision for the user. Images: sheets/deck-late-artifact2-candidates.png. Costs from the deck window (see the summary note).
  - A. The lobe floor of artifact 1. The band is a thin dark line, (67.7, 72.8, 94.8). The face stays near black, (12.7, 7.7, 7.5), because a wider lobe grows limit 1. Cost: about +0.01 ms.
  - B. The one-factor clamp (probe onefactor). The band turns olive, (62.2, 63.1, 72.3), and the face stays dark, (19.0, 10.9, 6.6). It does not keep D's hue, because the blue channel still stops at 0. Cost: no measurable change (−0.014 ms, inside the control's spread). Rejected by this evidence.
  - C. Bound M at each texel by the decision 31 value, (1 − V) D(n) (probe boundM). The face is (33.4, 29.1, 28.2) and the band (80.0, 91.7, 127.3), with no brown and a lighter crease. Cost: +0.044 ms, the sectors pass +0.054 ms. It takes back part of decision 41: a wall that the map also holds, brighter than the mean, then counts partly twice again. It also uses the SH D, so it inherits limit 2 in another form.
  - D. One source for D and M, which makes M ≤ D hold by design (the class-level candidate). D comes from an irradiance cube level, a cosine convolution of the same specular map. The ambient pass reads it at the pixel's full-resolution normal, so decision 42's reason, the normal map's detail, still holds. The image: probe bruteD stands for the convolved cube. With the lobe clip (bruteD-subM16) the brown goes from the face and the band. With bruteD alone, the face's red falls from 81 % to 45 %. Cost, not measured: the read per pixel replaces the nine SH coefficients and is about the same. The convolution runs once per map update. For scale, the whole prefilter of every level takes 0.28 ms median per frame on the deck window (timing2 logs, "GPU environment prefilter"). A small irradiance level, for example 16 x 16 per face, is a fraction of that. The bruteD probe itself costs 13.4 ms in the ambient pass and does not measure this candidate. bruteD changes D everywhere: 96 % of the deck-late pixels differ, by at most 73 levels. So a fix ticket compares every view. This candidate fixes the class: wherever D and M come from one map representation, D − M cannot go below 0 except by the lobe tails.
  - E. The open directions carry the map's light: the sectors pass reads the map along the open pieces as well, and the ambient pass uses that in place of D − M. No subtraction remains, so no clamp binds and no hue changes. It moves the map's diffuse term to half resolution and loses the normal map's detail, which is decision 42's reason for D at the pixel. Its reads grow by the open pieces of each slice. Not probed. It is the second design for the user beside D.
  - The lobe clip (probe subM) completes D or E against limit 1. It costs +0.32 ms with 8 reads per piece, and it makes the noise of artifact 1 worse (see artifact 1). A cheaper clip, for example one read whose lobe narrows near the plane, is not probed.

**2026-11-01T17:10:39Z**

Artifact 3: bright blotches of the bounce beside lit surfaces (builder, 2026-11-01, revised in the fix stage).
- What shows: soft bright blotches 5 to 15 px wide, where the bounce of a bright blocker reaches a darker receiver. ref-16x16 shows each as a smooth glow, and the light-off twins show no glow.
  - brake-run: the upper side of the lit streak on the right wall, 1600,720-1920,880 (sheets/brake-run-right-3x.png, sheets/brake-run-right-candidates.png).
  - demo-surface terminal: a bright cloud on the floor beside the glowing door, 1000,440-1320,620 (sheets/terminal-door-probes.png, sheets/terminal-door-candidates.png).
  - mirror-balls balls: mottled light on the wall behind the balls, 960,560-1280,700 (sheets/balls-wall-probes.png, sheets/balls-wall-candidates.png).
  - cabin-light cabin: bright grain on the shadowed floor beside the sunlit floor, 560,680-880,860 (sheets/cabin-left-probes.png, sheets/cabin-left-candidates.png).
  - The first notes also listed the dark wall right of the cabin's window, 1380,660-1700,840. Its probes show M, so it moved to artifact 1.
- Stage: G in the sectors pass, the light of the blockers, at 3 slices and 3 steps per side. The denoise works as XeGTAO's does. Its two 3 x 3 dispatches leave what they cannot average.
- Probe evidence. On each of the four views, noG removes the blotches, and noM and the lobe floor keep them. ref-16x16 smooths them (sheets/brake-run-noise-probes.png, terminal-door-probes.png, balls-wall-probes.png, cabin-left-probes.png). On the balls wall, noG leaves a much fainter mottle. So it is noise of G, not bias: the reference keeps the glow and loses the blotches. The noise measure does not separate these blotches from the strong gradients of the glows, so the evidence here is by sight.
- Fault or limit: a fault of the default sample count by this ticket's rule, because a converged estimate shows no blotches. The design accepts it. "Compared with a design from scratch" names the cost of fixed noise and blur only (decision 3): "a fixed pattern that the blur does not remove in full". Part 7 picked 3 steps as "the strongest setting without visible grain" at full size. At 3x the grain shows. The code does what the design says, so it is not a code fault.
- Candidate fixes. Each is a default change or costs GPU time, so each goes to the user. Images: the four candidate sheets above, with off, default, 6 slices, 4 denoise dispatches, 3 x 8 steps, the outlier clamp and ref-16x16.
  - A. More slices (probe slices6). 6 slices make the brake-run blotches fewer and smaller. The terminal cloud stays. Cost: +0.99 ms, all in the sectors pass. 16 slices cost about +5 ms.
  - B. More denoise dispatches (probe denoise4, two more of the first kind). The blotches get softer and wider on every view, and they stay. Cost: +0.11 ms for the two, so about +0.06 ms each. A wider blur also softens real detail and contact shadows.
  - C. More steps at 3 slices (probe steps8, --screen-space-light-steps 8). The terminal cloud is nearly smooth, but the brake-run blotches stay and get brighter. It also makes the noise of M worse (artifact 1). Cost: +1.44 ms, all in the sectors pass.
  - The outlier clamp of artifact 1 does not change these blotches either.

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

Summary (builder, 2026-11-01, revised in the fix stage). The gallery covers the nine films of demo-exit, deck-late, and the five review scenes. Paths are relative to captures/xgra/, as in the first note.

| Artifact | Views | Stage | Fault or limit | Candidates, with measured cost |
|---|---|---|---|---|
| 1. Specks and blotches of M | Lobe noise: deck-late, unbelt. Slice noise: spinward, chute-top, the ceiling panel of the demo-surface cabin. Both: brake-run, the boxes' foot on films/floor, the cabin wall of cabin-light | Sectors pass, the M reads, spread by the denoise | Lobe noise: a fault of decision 42's lobe at 3 steps. Slice noise: a fault of the default sample count, which the design accepts, as for artifact 3 | Lobe floor (+0.01 ms, lobe noise only). 6 slices (+0.99 ms). Two more denoise dispatches (+0.11 ms, softer, not gone). The outlier clamp (+0.10 ms) does nothing |
| 2. Dark brown crease where M exceeds D | deck-late after the flood: 2a the cabin's base face, 2b the floor band | M in the sectors pass, D in the ambient pass, the clamp at 0 | Limit by the plan's list: limit 2 (D in band-2 spherical harmonics, M from the specular map) and limit 1 (lobe tails). The size of limit 2 here exceeds what the plan states | D and M from one source (not measured, about the cost of a small prefilter level). The bound by (1 − V) D (+0.044 ms). The lobe floor leaves the face near black. The one-factor clamp turns the band olive |
| 3. Bright blotches of G | brake-run, the terminal of demo-surface, the balls of mirror-balls, the cabin floor of cabin-light | Sectors pass G at 3 slices, the denoise | A fault of the default sample count, which the design accepts | 6 slices (+0.99 ms). Two more denoise dispatches (+0.11 ms). 8 steps (+1.44 ms, helps the terminal only) |
| 4. Dark streak behind a box | antispinward | Sectors pass, the slab of κ z, made stronger by M | Limit: κ z is one thickness for everything (limit 8) | A thinner slab for the near steps (design change, no cost) |

- How the costs were measured. Each round ran the 9 builds of timing2/ in a rotated order: base, a copy of the base as the control, outlier, onefactor, boundM, slices6, denoise4, steps8 and subM. The runs used the deck window: time-deck.json, 120 frames from frame 399, --video none. The renderer's timer gave the sum of the five passes. The tables give the median over 4 rounds of each build minus the base of its round. The control's median was −0.014 ms, range −0.040 to +0.106. In round 3, the base ran about 0.45 ms slower than the rest. So every build of that round shows a negative difference, and the median is the figure to read. The 1-minute load was 8 to 11 during these runs, because other builders ran at the same time. Per pass and per round: analysis/timing2-summary.txt. The lobe floor's cost comes from the first timing round (timing/summary.txt, 6 rounds, load 2.5 to 5.1).
- Each candidate changes a default or the image, so each goes to the user as two designs with captures. No candidate is a plain bug fix.
- The captures for the user's comparison:
  - Artifact 1: sheets/spinward-candidates.png, chute-top-candidates.png, unbelt-candidates.png, cabin-wall-candidates.png, cabin-ceiling-panel-candidates.png, deck-late-candidates.png and brake-run-candidates.png.
  - Artifact 2: sheets/deck-late-artifact2-candidates.png, deck-late-ratio16-factorial.png and deck-late-artifact2-full.png.
  - Artifact 3: sheets/brake-run-right-candidates.png, terminal-door-candidates.png, balls-wall-candidates.png and cabin-left-candidates.png.
  - Full frames are in default/, ssl-off/, ref-16x3/, ref-16x16/ and the probe-<name>/ folders.
- Not found: no fault of the depth pass and no tile or block edges. The upsample makes no halo or dark line at depth edges: the nearest probe shows the same structures (sheets/deck-late-pillar-upsample.png). No light leaks through a wall. The bounce has the right color: bounce-red-wall tints the floor red.
- Not an artifact: ref-16x16 darkens some creases more than the default, for example spinward's ceiling crease. That is the seventh item of "Known limits": light passes between two steps. It is a difference of strength, not a structure.
- All four artifacts come from the screen-space light. Artifacts 1 and 2 come from its decision 42: the reads of the environment map along the blocked directions. Artifact 2 also comes from how the environment map represents D. So a fix touches the environment map's side of the estimate too.
- The probes are not in the commits: each patch was applied for one build and then restored and touched. git diff --stat 79d27a68..HEAD lists only .tickets/ter-xgra.md.

**2026-11-01T18:19:07Z**

Fix stage: how each review finding was resolved (builder, 2026-11-01). The notes above are revised in place. Every probe of this stage ran under the GPU lock, and each patch was restored and touched after its build.
- Finding 1, high (limit 3 cannot make the clamp bind, and limit 2 is missing). Accepted. The probe bruteD replaces D(n) by a brute-force hemisphere integral of the specular map. At 16 slices, it cuts the red of the ratio view from 81 % to 45 % on the base face. On the band, it cuts it from 38 % to 19 %. So limit 2 is a main cause. The artifact 2 note now names limits 1 and 2 as the causes, and it explains why limit 3 cannot make the clamp bind. The map-from-a-floor-point probe stays as the open question for the remaining smooth darkening only.
- Finding 2, medium (the face and the band need separate causes, and limit 1 holds on the face). Accepted. Artifact 2 is now 2a, the face, and 2b, the band, with their own masks and numbers. The probe subM clips the lobes to their piece. With bruteD, it cuts the face's red from 45 % to 12 %, so limit 1 is the second cause on the face. Candidate A now states that it keeps the face near black.
- Finding 3, medium (part of artifact 1 is slice noise). Accepted. Artifact 1 now names two sources, lobe noise and slice noise, and gives each view its source with the noise measure of the review. Spinward and chute-top have lobe-floor crops in their new candidate sheets. The slice-noise part takes artifact 3's call. Its candidates are more slices and the outlier clamp, and both were measured.
- Finding 4, medium (no probes on the review scenes). Accepted. noG, noM, the lobe floor and rough1 ran on cabin-light, demo-surface and mirror-balls. The cabin wall right of the window responds to noM and not to noG, so it moved to artifact 1. The terminal, the balls wall and the cabin floor respond to noG, so they stay in artifact 3.
- Finding 5, medium (cheap candidates have no capture and no cost). Accepted. Each cheap candidate now has a probe build and captures on the views of its artifact. Its cost comes from 4 interleaved rounds with a control. The candidates are the outlier clamp, the one-factor clamp, the bound by (1 − V) D, 6 slices, two more denoise dispatches, and 8 steps at 3 slices. Two of them fail by their own captures: the outlier clamp changes nothing visible, and the one-factor clamp turns the band olive.
- Finding 6, medium (the candidates of artifact 2 patch the instance). Accepted. Candidate D takes D and M from one source, an irradiance cube convolved from the specular map, which makes M ≤ D hold by design. Candidate E is the second design: the open directions carry the map's light. D has a probe capture (bruteD and bruteD-subM). Its cost is an estimate, because the convolution is a new pass. The note gives the prefilter's measured 0.28 ms as the scale.
- Finding 7, low (two instances of artifact 1 are missing). Accepted. The boxes' foot on films/floor and the ceiling panel of the cabin camera are in the artifact 1 table, with crops. The ceiling panel is slice noise: noM removes it, and the lobe floor keeps it.
- Finding 8, low (probe-build.sh reports success after a failed build). Accepted. When dotnet build fails, the script now stops with exit 2 and saves nothing. It takes the repository root from git rev-parse --show-toplevel and accepts several patches. The method note says that the saved builds in /tmp/xgra/builds are not kept.
- The two gaps of the correctness review are also closed. The method note now says that the ratio view takes the largest channel's ratio. The artifact 2 note, and the caption of sheets/deck-late-lobe-ratio.png in the report, now say that rough1 makes the cabin base face black.
- One claim of the first notes was wrong, and the artifact 2 note corrects it. The brown hue does not come from the per-channel clamp alone. M is bluer than D there, so a one-factor clamp still stops the blue channel at 0.
