# Finish the renderer

Epic: `ter-8hyd`. This doc came from ticket `ter-6k3k`, the survey of every scene, on 2026-11-09. It says what "finished" means for the renderer and how the survey measures it. It also gives the baseline's results and the order in which the epic fixes what fails. The proposal of 2026-11-09 (`/private/tmp/tertius-report-20261109/proposal.md`) holds the full reasoning and its evidence files. This doc keeps its conclusions and the user's decisions on them.

## Goal

The user set the goal on 2026-11-09:

> just keep working until our whole rendering system essentially is reasonable. We don't have terrible noise or blotches and updates are within 60 frames or so and generally we can render everything that we were rendering before in a reasonable way under 12 milliseconds. ... Don't do re-ster though, that was a failure.

The epic reads this as three measurable conditions and one rule:

1. No scene that `scripts/*.json` renders shows terrible noise or blotches, in a still or in motion.
2. The light settles within about 60 draws after a change: a lamp switch, a moving sun, moving lamps, and the surfaces that a walk reveals.
3. Every scene that rendered before still renders, with a GPU frame span of at most 12 ms at the budget's configuration. Decision 5 below names the exceptions.
4. No ReSTIR of any kind, for direct light or for bounce light.

## Terms

- Draw: one rendered frame.
- View: one camera position and moment of the survey. The survey has 90 views.
- Film: a run of up to 180 consecutive draws of one scene, one tick apart. The survey has 18 films.
- Still: the image of a view after 132 draws of an unchanged state.
- Direct light: light that reaches a surface straight from a lamp or the sun. The survey counts the emission of glowing surfaces with it.
- Bounce light: light that reaches a surface from another surface. The code calls it indirect diffuse light.
- Reflections: the mirror-like light of a surface. The code calls it indirect specular light.
- Light signal: one of direct light, bounce light and reflections.
- History: a running average of one pixel's light over past draws.
- Budget's configuration: the Release demo on the development Mac (Apple M1 Max), with 1920 × 1080 output. FSR 3.1 upscales it from 1280 × 720 (`docs/plans/rendering-performance.md`, decision 1).
- GPU frame span: the time from the start of a frame's first pass to the end of its last pass.
- p95: the value that 95 % of the frames stay under.
- Windowed clock: the GPU clock of a window that draws steadily, 1296 MHz on the development Mac (`tools/README.md`, "The GPU clock of a timed run").

## Decisions

### Made by the user

1. Blotches: the diffuse rays aim half by angle and half at the bright lit pixels of the draw (guided diffuse rays, ticket `ter-05wb`). Screen probes stay the fallback. This reverses a part of the choice of 2026-11-08, which wanted a spatial filter "not a clamp and not importance-sampled rays" (`.tickets/ter-9371.md`). (User, 2026-11-09)
2. Slow updates: the bounce-light history is cut on evidence only. A ray brings back the light-change age of the pixel that it hit. The history then drops the draws from before that change (ticket `ter-wkvw`). Hits keep reading the filtered light. The fresh hit light, in which a hit reads the fast history again, is not built. (User, 2026-11-09)
3. Shared reflection rays: if the block's reflection ray lies inside a pixel's own reflection lobe, the pixel uses that ray (sharing by lobe fit). A lobe is the cone of directions from which a surface reflects light toward the eye. Sharing by a fixed roughness is not built. (User, 2026-11-09)
4. Diffuse filter: the thinner kernel, six steps of a 3 × 3 grid in place of five steps of a 5 × 5 grid. The coarse steps at half size are not built. (User, 2026-11-09)
5. Frame time: a GPU frame span of 16.7 ms, one frame at 60 frames per second, is acceptable for a named list of heavy views. The 12 ms budget holds for every other view. Step 10 of the order of work names the list from measured numbers. (User, 2026-11-09)
6. The survey's limits stay as they are. They were set by eye, with the lamp hall as clean and the bay's moving sunbeam as bad. Part "Measures and thresholds" gives each one. (User, 2026-11-09)
7. No ReSTIR of any kind. The ReSTIR for direct light was 3.2 ms slower than the exact loop on the deck, and it left grain on floors and glass. So it was removed (`ARCHITECTURE.md`, "Light sampling"). (User, 2026-11-09)

Two earlier decisions still hold. The budget is 12 ms at the budget's configuration (`docs/plans/rendering-performance.md`, decision 1). A pass over its share first gets optimizations that keep the image. Then its default can drop quality, and the full quality stays a setting (decision 2 there).

### Settled by this design

8. The survey (`tools/renderer-survey.py`) is the measure of the goal. A ticket of the epic that changes the image or the frame time runs the parts of the survey that it touches. It compares the result with the baseline below. The rules of `tools/README.md`, "The renderer survey", say how to run it.
9. If an Android emulator runs, a timing does not count. On 2026-11-09, 39 of the 90 timing runs of the first survey ran beside an Android emulator. The emulator put the GPU in a slow mode. For example, the p95 of `bay-sunbeam` was 33.8 ms against a median of 23.8 ms. So the survey writes no timing while an emulator runs, before or during the run. The tool sees only emulators. Before a timing run, make sure that no other program uses the GPU.
10. A failure is assigned to a fix only after a split by light signal shows which signal carries it. The first diagnosis of the blotches covered 2 of the 9 failing films. Part "The split by light signal" gives the split of every failing film.
11. Every step uses only the device features that the renderer already requires (`ARCHITECTURE.md`, "Required device features"). The times in this doc hold for the M1 Max only, because no tracer pass has a Linux run yet (ticket `ter-cgif`).

## Measures and thresholds

The survey measures each view, film and change at the budget's configuration. `tools/README.md`, "The renderer survey", defines each measure exactly. This table gives the limits and their reasons.

| Measure | What it measures | Limit | Reason |
|---|---|---|---|
| Frame time | The p95 of the GPU frame span over 120 draws, scaled to the windowed clock | 12 ms, or 16.7 ms for the named heavy views | Decisions 5 and the budget of `rendering-performance.md` |
| Specks | Groups of pixels more than twice as bright as their 7 × 7 neighborhood, which the long reference does not hold, per 10,000 visible pixels | 1.0 | Set by eye: the lamp hall is clean, and specks show above this count |
| Blotch score | The 90th percentile of the relative difference to a long reference of the same state, after a blur of 4 render pixels | 0.04 | Set by eye: the lamp hall stays under it, and the bay's moving sunbeam is far above it |
| Level | A still's whole-image light against its reference | 5 % | The settle tolerance: a light that still changes at the still's draw |
| Motion level | A film frame's whole-image light against a still of the same state | 10 % | The error of a history that lags 60 draws behind a light that changes by 10 % in 60 draws |
| Flicker | The median over a film's frames of the relative second difference of the blurred light from draw to draw | 0.006 | Set by eye: the lamp hall's quiet films stay under it, and the moving lamps' films show a visible pumping above it |
| Grain flicker | The same without the blur | 0.05 | Set by eye: no film shows visible grain under it |
| Settle count | The draws after a change until every changed region stays within 5 % of a control run, or of a still at the end for a walk | 60 draws | The goal |
| Lag error | The median error of a moving light's film against stills of the same states | 10 % | The motion level's reason |

A settle that is still outside the 5 % at the end of its window has no count. The survey reports it as "not settled within N draws", and it fails. The lamps-off window is 200 draws and the lamps-on window 100 draws. The first survey of 2026-11-09 had a lamps-off window of 100 draws and reported the window's length as the count. So its "100 draws" for the cabin means "not settled within 100 draws".

The limits come from the eye, and two scenes anchor them: the lamp hall as clean and the bay's moving sunbeam as bad. The user accepted them on 2026-11-09 (decision 6). The blotch score compares the renderer with itself. So it cannot see an error that every draw repeats, such as a bias of the clamp. The parent of the epic also looks at frames and films before it calls a scene clean.

## The baseline

The baseline measures main at commit `fc1c51476`, on 2026-11-09 and 2026-11-10. It ran before steps 3 and 4 merged. The survey's results are in `/private/tmp/tertius-survey-baseline-20261109`, and `report.md` there holds the full tables.

### Frame time

Every one of the 90 views ran at a GPU clock of 1295 to 1296 MHz, with no Android emulator. The table counts the views by the p95 of their GPU frame span.

| Band of p95 | Views |
|---|---:|
| At most 12 ms | 22 |
| 12 to 14.2 ms | 23 |
| 14.2 to 16.7 ms | 11 |
| 16.7 to 21 ms | 12 |
| Above 21 ms | 22 |

So 22 views meet the 12 ms budget, and 56 views stay within 16.7 ms. The six slowest views are `exit-cabin` (35.3 ms), `docking-cabin` (33.3 ms), `spin-standing` (31.0 ms), `exit-deck` (30.8 ms), `deck-cuts` (30.6 ms) and `docking-bay` (30.3 ms). The deck's p95, the view `exit-deck`, is 30.8 ms.

The 22 views above 21 ms are the candidates for the list of heavy views of decision 5: `airlock-door`, `bay-sunbeam`, `box-cabin`, `cabin-light-cabin`, `deck-cuts`, `docking-bay`, `docking-cabin`, `exit-cabin`, `exit-chute`, `exit-deck`, `exit-floor`, `exit-stack`, `floor`, `lamp-hall-16-hair`, `pillar-room`, `rect-bay`, `rect-lamps`, `spin-standing`, `station-cabin`, `surface-cabin`, `surface-terminal` and `terminal`. Step 10 names the list from the times after the plan.

The first survey gave `bay-sunbeam` a p95 of 33.8 ms beside the emulator. Without it, the view takes 24.3 ms. The view `spacewalk` fell by 4.2 ms in the same way. The other 88 views moved by at most 0.7 ms.

### Stills

5 of 90 stills fail.

| Still | Specks | Blotch score | Fails on |
|---|---:|---:|---|
| `rect-lamps` | 1.34 | 0.156 | Specks and blotches |
| `bay-sunbeam` | 0.04 | 0.085 | Blotches |
| `docking-bay` | 0.24 | 0.081 | Blotches |
| `terminal` | 0.01 | 0.056 | Blotches |
| `surface-terminal` | 0.01 | 0.056 | Blotches |

A full disk stopped one chunk of the still part, so 8 stills have no baseline result: `launch-climb`, `meshes`, `shuttle`, `surface-terminal`, `surface-apron`, `surface-cabin`, `surface-overview` and `doorway-inside`. The table and the count take their values from the first survey, which measured the same source code. Only `surface-terminal` of them fails. The still `lamp-hall-16-hair` now passes with 0.91 specks, and `rect-lamps` now fails with 1.34 specks. The first report's values came from the still part with bloom, before the part turned bloom off. Nobody measured how much the speck count varies between two runs of the same code.

### Films

9 of 18 films fail.

| Film | Camera | Flicker | Blotch score | Motion level | Fails on |
|---|---|---:|---:|---:|---|
| `bay-sunbeam` | Fixed | 0.0078 | 0.421 | −71 % | Blotches, level, flicker |
| `rect-bay` | Fixed | 0.0024 | 0.354 | +238 % | Blotches, level |
| `exit-floor` | Moving | 0.0040 | 0.298 | +12 % | Blotches, level |
| `docking-landing` | Moving | 0.0033 | 0.208 | +7 % | Blotches |
| `rect-lamps` | Fixed | 0.0034 | 0.178 | −3 % | Blotches |
| `lamp-hall-4-spot-moving` | Fixed | 0.0124 | 0.116 | 0 % | Blotches, flicker |
| `lamp-hall-64-moving` | Fixed | 0.0036 | 0.069 | +1 % | Blotches |
| `lamp-hall-16-moving` | Fixed | 0.0029 | 0.046 | +2 % | Blotches |
| `rigs-crowd` | Fixed | 0.0020 | 0.043 | 0 % | Blotches |

The other 9 films pass: `exit-cabin`, `exit-deck`, `hair-bent-cards`, `orbit-umbra`, `pillar-room`, `rect-lights`, `reflections`, `skin-head` and `spot-box`.

The review of this ticket found three faults in the motion part, after the baseline ran. The tool now has the repairs, but the film table above still holds the faults:

- The film used one fixed exposure, and each still of the same state used its own auto exposure. The tone curve is not linear, so the exposure gap shows as level and as blotch. The gap was ±0.57 stops for `bay-sunbeam` and ±0.54 stops for `rect-bay`, so their levels of −71 % and +238 % are mostly exposure. The gap was at most ±0.16 stops for the other films.
- The film `orbit-umbra` runs 6 ticks per script frame, so each of its frames was a fresh still, not a film. Every visible pixel of its view also lies near a bright pixel, so it measured nothing. Its pass means nothing.
- The probe kept the history across a step of time, so each film's last reference started from the history of the middle frame's state. This bias is small for a light that settles within 100 draws.

The split by light signal below renders each film and its stills at one fixed exposure, so its numbers do not have the first fault. Step 10 runs the motion part again with the repaired tool.

### Settle counts

| Change | Lamps off | Lamps on | Source |
|---|---:|---:|---|
| Cabin, lamp switch | 114 draws | 66 draws | Baseline |
| Bay with the sun off, lamp switch | 48 draws | 34 draws | Baseline |
| Lamp hall, lamp switch | 36 draws | 25 draws | First survey |

The cabin fails in both directions. With the window of 200 draws, its lamps-off count is 114 draws, where the first survey reported "100", the length of its window. The lamp hall's switch was not run again on the user's order of 2026-11-10 (see below). The first survey measured it on the same source code, with a lamps-off window of 100 draws. Its count of 36 lies inside that window.

The first survey also holds the moving lights and the walk, on the same source code:

- `bay-sun`, the moving sun in the bay: a median lag of 18 draws and a median error of 19 % against the limit of 10 %. It fails.
- `lamp-hall-16-moving` and `lamp-hall-64-spot-moving`: errors of 3.6 % and 0.9 %. Both pass.
- `cabin-walk`: the whole image settles 94 draws after the walk stops. Two of the 9 regions were not settled within 200 draws, and the other changed regions need 56 to 142 draws. It fails. Ticket `ter-g3tk` follows it.

On 2026-11-10 the user stopped the baseline's remaining switch, lag and walk cases, so that steps 2 and 5 got the GPU first. Step 10 surveys them again on the final main.

## The split by light signal

The survey's tool split each failing film and the still `rect-lamps` by light signal (`tools/README.md`, "The split by light signal"). Each case renders once whole and once for each signal, at one fixed exposure and in scene luminance. A part's blotch score is the error of that part alone, relative to the whole image's light. So the part with the largest score carries the failure, and a part above 0.04 fails on its own. The results are in `/private/tmp/tertius-split-20261109/results/split`.

| Case | Sample | Whole | Direct | Bounce | Reflections | Carried by |
|---|---|---:|---:|---:|---:|---|
| `rect-lamps` (still) | Shown | 0.103 | 0.008 | 0.103 | 0.012 | Bounce |
| `rect-lamps` | Middle | 0.093 | 0.005 | 0.099 | 0.020 | Bounce |
| `rect-lamps` | Last | 0.117 | 0.005 | 0.117 | 0.011 | Bounce |
| `bay-sunbeam` | Middle | 0.201 | 0.005 | 0.171 | 0.040 | Bounce, reflections at the limit |
| `bay-sunbeam` | Last | 0.149 | 0.009 | 0.114 | 0.066 | Bounce and reflections |
| `rect-bay` | Middle | 0.118 | 0.006 | 0.121 | 0.017 | Bounce |
| `rect-bay` | Last | 0.105 | 0.004 | 0.073 | 0.031 | Bounce |
| `exit-floor` | Middle | 0.017 | 0.005 | 0.011 | 0.007 | Passes |
| `exit-floor` | Last | 0.152 | 0.004 | 0.253 | 0.028 | Bounce |
| `docking-landing` | Middle | 0.127 | 0.010 | 0.079 | 0.105 | Reflections and bounce |
| `docking-landing` | Last | 0.122 | 0.017 | 0.095 | 0.059 | Bounce and reflections |
| `lamp-hall-4-spot-moving` | Middle | 0.075 | 0.058 | 0.025 | 0.007 | Direct |
| `lamp-hall-4-spot-moving` | Last | 0.060 | 0.054 | 0.018 | 0.008 | Direct |
| `lamp-hall-64-moving` | Middle | 0.042 | 0.038 | 0.007 | 0.004 | Direct |
| `lamp-hall-64-moving` | Last | 0.033 | 0.029 | 0.007 | 0.003 | Direct, under the limit |
| `lamp-hall-16-moving` | Middle | 0.028 | 0.025 | 0.005 | 0.003 | Direct, under the limit |
| `lamp-hall-16-moving` | Last | 0.026 | 0.023 | 0.007 | 0.002 | Direct, under the limit |
| `rigs-crowd` | Middle | 0.024 | 0.025 | 0.006 | 0.004 | Direct, under the limit |
| `rigs-crowd` | Last | 0.028 | 0.029 | 0.008 | 0.004 | Direct, under the limit |

The flicker of the films with moving lamps, split the same way:

| Film | Whole | Direct | Bounce | Reflections |
|---|---:|---:|---:|---:|
| `lamp-hall-64-moving` | 0.0188 | 0.0189 | 0.0004 | 0.0004 |
| `lamp-hall-16-moving` | 0.0075 | 0.0075 | 0.0003 | 0.0002 |
| `lamp-hall-4-spot-moving` | 0.0060 | 0.0059 | 0.0004 | 0.0003 |
| `bay-sunbeam` | 0.0055 | 0.0011 | 0.0051 | 0.0008 |

The light after the composite, from glass, particles and haze, stays at 0.015 or less in every case, so the table leaves it out.

The split gives four groups:

1. The bounce light carries the failure of `rect-lamps` (still and film), `bay-sunbeam`, `rect-bay` and `exit-floor`. In `rect-lamps` the levels stay within 6 %, so the error is shape: the blobs on the ceiling. The guided diffuse rays (step 5) aim at this. In `bay-sunbeam` and `rect-bay` the bounce light also trails the change of light, by −31 % and +84 % at the middle frame. The history cut of step 6 aims at this lag. In `exit-floor` the error appears only at the last frame, after the camera moved, where the bounce light is 73 % too bright. That is the walk's failure (ticket `ter-g3tk`).
2. The reflections fail on their own in `docking-landing` (0.105 and 0.059) and in `bay-sunbeam` (0.066 at the last frame), beside the bounce light. No step of the plan aims at this. Ticket `t3-5r0z` follows it, after step 2 changes the reflection rays.
3. The direct light carries the failure of the films with moving lamps or moving figures: `lamp-hall-4-spot-moving`, `lamp-hall-64-moving`, `lamp-hall-16-moving` and `rigs-crowd`. It also carries their flicker. The renderer's direct light has no history, so the split ran `lamp-hall-4-spot-moving` again without FSR 3.1 (`--native`). Without FSR, the direct light's blotch falls from 0.058 to 0.000 and its flicker from 0.0059 to 0.0003. So FSR's own history causes this group's failure, and no step of the plan touches it. Ticket `t3-5jws` follows it.
4. In the split's measure, `lamp-hall-16-moving` and `rigs-crowd` stay under the blotch limit, at 0.028 and 0.029 at most. The survey's display measure fails them at 0.046 and 0.043. They are borderline cases of group 3.

So the guided diffuse rays and the history cut address group 1 only. The proposal's assignment of all 9 films to the diffuse rays holds for 4 films and a still.

The split measures in scene luminance, not in display luminance. The tone curve compresses bright light, so the split's scores differ from the survey's. For example, the flicker of `lamp-hall-16-moving` is 0.0075 in the split and 0.0029 in the survey. The split of `rect-lamps` (the still) ran before a correction of the measure. That correction divides each part by its own level, not by the whole image's. The still's whole level is −1.4 %, so the correction changes its parts by less than 0.002.

## The problems and their fixes

This part gives each problem in a few lines. Sections 2 and 3 of the proposal hold the measurements, the two designs of each problem and the sources.

### Blotches

Bright and dark patches, 20 to 60 pixels wide, sit in the bounce light and move from draw to draw. The cause is a count of rays. One diffuse ray serves a block of 2 × 2 pixels and aims by angle only. Take the bay's sunbeam and the cabin walk. There, 0.13 to 2.1 % of the rays hit a small sunlit patch about 100 times brighter than the walls. Those rays carry 36 to 86 % of the bounce light (`.tickets/ter-05wb.md`). No history and no filter averages so few hits without a wider blur or a longer delay.

The fix is decision 1. Half of the diffuse rays pick a target pixel in proportion to its direct light in this draw. Each ray's weight divides by the chance of its direction under both rules together (multiple importance sampling, [Veach 1997]). Nothing is kept between pixels or draws, so this is not ReSTIR. The prototype cut the small blotches in the bay from 1.5 to 0.4 % of the lit pixels. It cost 0.35 to 0.39 ms. It also lowered the lamp hall's mean bounce light by 4.8 % through the clamp. The builder of step 5 tries a guided share of a quarter against that loss.

### Slow updates

After a change, the bounce light keeps moving for one to three seconds. A ray's hit reads the filtered light of the last draw, which follows the slow history of up to 32 draws. Each bounce passes through that history again. Let g be the share of its light that a room sends back per bounce. The settle then stretches by about 1 / (1 − g) (`docs/plans/temporal-renderer.md`, "The bounce model").

The fix is decision 2. The renderer records for each pixel how many draws ago its direct light changed. A diffuse ray brings back that age from its hit, and the bounce-light history then holds only the draws after the change. The branch `ter-wkvw-diffuse-light-age` measured five of six settle counts better, at 0.04 ms. The repair of the light-change record (step 4) comes first. A walk past still lamps changes no direct light, so this fix cannot help the walk. The split of the walk by signal decides its fix.

### Frame time

The deck's GPU frame span rose from 13.4 ms on 2026-11-05 to about 30.9 ms (ticket `ter-b4ux`). A Metal System Trace attributes the rise:

| Cause | Added time on the deck | Fix |
|---|---|---|
| A reflection ray at every smooth pixel in every draw | 7.40 ms | Step 2: shared reflection rays by lobe fit |
| The diffuse filter | 4.21 ms | Step 7: the thinner kernel, about 2 ms back by estimate |
| The 13 lamps became rectangle lights | 1.83 ms | A quality feature that the user chose. It stays |
| The direct pass grew with the tube light's merge | 0.76 ms | Step 3: one light loop per light shape |
| 16 shadow taps under FSR | 0.29 ms | A quality feature that the user chose. It stays |
| Other passes | 0.81 ms | None yet |
| Frame span outside single passes | 1.57 ms | Step 9 attributes it |

The forecast after the plan is about 20 to 21 ms for the deck, so the ship interiors stay above 12 ms. Decision 5 answers that: the heavy views get 16.7 ms. Views with many lamps also get the light budget per screen tile (ticket `ter-69s8`).

### Light lost to the clamp

The clamp keeps the slow history inside the range of the fast histories of the 3 × 3 pixels around it. A rare bright sample lies outside that range, so the clamp cuts real light: 22 to 27 % of the cabin's bounce light. The user keeps the clamp, because it makes a lamp switch settle 1.5 to 3 times faster (`.tickets/ter-18hc.md`). The guided rays shrink the jumps, and the prototype's cabin read 23 % brighter. Step 12 measures the loss again.

### Specks and grain

89 of 90 stills pass the speck limit, and no film fails the grain flicker. The thinner filter is the main risk to both, so its acceptance guards the speck count.

## The order of work

| Step | Work | Ticket | Depends on |
|---|---|---|---|
| 1 | Commit the survey, repair its two faults, run the baseline, split each failure by light signal, write this doc | `ter-6k3k` | Nothing |
| 2 | Shared reflection rays by lobe fit, as the default | `ter-4oow` | Nothing |
| 3 | One light loop per light shape (merged 2026-11-10) | `t4-6p04` | Nothing |
| 4 | The repair of the light-change record alone (merged 2026-11-10) | `tb-8zew` | Nothing |
| 5 | Guided diffuse rays: the clock, the guided share, the hardening, the merge | `ter-05wb` | Step 1 |
| 6 | Cut the bounce-light history on evidence, with the fix of its blotches after a cut | `ter-wkvw` | Steps 4 and 5 |
| 7 | The thinner diffuse filter | New, child of `ter-8hyd` | Step 5 |
| 8 | The light budget per screen tile | `ter-69s8` | Nothing |
| 9 | Attribute the 1.57 ms outside single passes, and the 5.9 ms of shadow tiles in `rect-bay` | `ter-b4ux` | Steps 2 and 3 |
| 10 | Run the survey again, name the heavy views of decision 5, set the defaults | `ter-6k3k` notes, `ter-odli` | All above |
| 11 | If `deck-cuts` fails: a ray limit after a camera cut | New | Step 10 |
| 12 | Close `ter-12xp`, measure `ter-18hc` again, update `ARCHITECTURE.md` | `ter-12xp`, `ter-18hc` | Step 10 |

The split by light signal found two failures that no step covers yet. FSR 3.1's history smears the direct light of moving lamps and figures (ticket `t3-5jws`). The reflections fail on their own in two films (ticket `t3-5r0z`). Both come before step 10.

Steps 2, 3 and 4 are quick fixes that no later step changes, so they run first and in parallel. Steps 5 to 7 form one chain. The noise that the guided rays leave decides how much the history cut and the filter can do. Step 6 of the proposal had two parts. Decision 2 removes its first part, the test of fresh hit light.

Three things stay out, because the goal does not need them yet:

- A world-space light cache (ticket `ter-13rm`). No measured failure needs light from surfaces behind the camera. If the split shows light that left the screen as a cause, it comes back.
- A filter for the reflections. The uncapped reflection history does that work.
- Screen probes. If the guided rays fail, they are the fallback for the blotches.

## Acceptance of the steps

- Step 2: a Metal System Trace of the deck shows the reflection rays at most 2.7 ms and the frame span lower by at least 7 ms. The reflection plate test of 2026-11-07, with the uncapped history, gives rough noise at most 7.7 W/(m² sr) and smooth noise unchanged at 1.0. No measure of `reflections-front`, `mirror-balls`, `exit-deck`, `exit-floor` or the film `reflections` moves from pass to fail. Sharing by lobe fit has no measured gain yet, so its trace decides whether it meets the 7 ms.
- Step 3: the deck's direct pass falls by 0.5 ms or more, and the image stays the same.
- Step 5: long stills of the bay, the cabin walk and the lamp hall stay within 1 % of the rays aimed by angle only. The lamp hall's mean bounce light stays within 1 % of main. The stills `docking-bay`, `surface-terminal` and `bay-sunbeam` have a blotch score of at most 0.04. No still's speck count rises above 1.0.
- Step 6: every changed region of the cabin, the bay and the lamp hall settles within 60 draws after a lamp switch. The film `bay-sunbeam` and the cabin walk show no new blotches from false cuts.
- Step 7: a trace of the deck shows the diffuse filter at most 2.3 ms. No still's speck count rises above 1.0, no still's blotch score rises by more than 0.005, and each region's mean light stays within 1 %.
- Step 10: every view meets its frame time limit, every still and film passes, and every change settles within 60 draws.

## Dead ends

Do not try these again without a new reason. Section 5 of the proposal names the record of each.

| Tried | Why it failed |
|---|---|
| ReSTIR for direct light | Grain on floors and glass, and 3.2 ms slower than the exact loop on the deck |
| ReSTIR for bounce light | Ruled out by the user on 2026-11-09 |
| A cap of 24 lights for the whole view | Far lamps lost the light on their own walls, and the exposure jumped |
| Light lists per tile by reach | Every lamp's reach covers the whole hall, so no tile drops a lamp |
| A spatial filter with AMD's 7 × 7 pattern, before the histories | Specks and patches stayed, and the user rejected the image |
| A bounce-light history limited by the neighbors' median | It removes real highlights |
| Hits read the raw fast history | Single bright pixels came back on the cabin floor (`ter-4kws`) |
| Cheap switches against blotches: no anti-lag, no clamp, more own rays, a longer history, a wider clamp box | Each one loses light or gains under 12 % |
| The SVGF paper's own weighting in the filter | It created light, about 20 % in the cabin |
| Removing the clamp | A lamp switch settles 1.5 to 3 times slower |
| The debug switch `TERTIUS_SPECULAR_HISTORY=original` as a default | It shares the rays but also brings back the short reflection history |

## References

- [Veach 1997] E. Veach, 1997, "Robust Monte Carlo Methods for Light Transport Simulation", PhD thesis, Stanford, section 9.2.4.
- [Stachowiak 2015] T. Stachowiak, 2015, "Stochastic Screen-Space Reflections", SIGGRAPH course Advances in Real-Time Rendering. The source of shared reflection rays.
- [Schied 2017] C. Schied and others, 2017, "Spatiotemporal Variance-Guided Filtering", High Performance Graphics. The source of the diffuse filter.
- The proposal of 2026-11-09 and its evidence: `/private/tmp/tertius-report-20261109/proposal.md`, `report.md`, `compare*.md`.
- The first survey: `/private/tmp/tertius-survey-20261109/report.md`.
