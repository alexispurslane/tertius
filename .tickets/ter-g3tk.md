---
id: ter-g3tk
status: in_progress
deps: []
links: []
created: 2026-11-10T09:38:12Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# After a walk stops in the cabin, the light is 13 % to 47 % too bright and drains over 100 to 200 draws

Survey walk part, cabin-walk (scene orbit, camera person), main with the light-change record's repair (011c56360): /private/tmp/tb8zew/sv-fix/results/walk/cabin-walk.json. Exposure fixed at 4.08 stops; the end still comes about 360 draws after the stop with the history kept. At the stop every one of the 10 regions is 1.13 to 1.47 times its end-still level; at 60 draws 1.04 to 1.29; at 120 draws 0.96 to 1.08; at 199 draws 0.99 to 1.06. The whole image settles within 5 % in 94 draws, and two regions never do in 200. So motion makes the image too bright and a long history drains the excess; it is not a lag. The cut on evidence (ter-wkvw) cannot fix it, because a walk past still lamps changes no direct light. Open: which signal carries the excess, and whether the light is already too bright before the walk (the walk starts about 90 draws after the load). The research plan is at /private/tmp/tertius-walk-overshoot-20261110/plan.md.

## Acceptance Criteria

The signal that carries the excess is named, with a split by light signal. After the fix, every region of cabin-walk stays within 5 % of the walk's control at the same tick (tools/README.md, the walk case) in at most 60 draws after the stop, and no other survey view gets worse.

## Notes

**2026-11-11T17:06:43Z**

Single bounce for the diffuse rays: measured and dropped (Alexis, 2026-11-11).

Probe on main 587195441 (with the guided diffuse rays), patch /private/tmp/g3tk/hit-bounce-v2.patch, results in /private/tmp/g3tk/ab2. A: a diffuse ray's hit returns factors.diffuse times its direct light plus glow, with no bounce history and no closed form (hitLightOf in screen_trace.glsl). Reflection rays keep main's hit light. B: as A, and screen_trace_resolve.comp multiplies the diffuse sample by 1 / (1 - min(rho c, 0.9)), with rho the receiver's diffuse color and c one less the open-sky share of the irradiance cube at its normal. A first B in accumulate_temporal.comp had no effect, because the accumulator's pipelines get no specialization constants (ticket t2-3unu).

| Measure | main | A | B |
|---|---|---|---|
| Walk, whole image, draws within 5 % | 110 | 56 | 65 |
| Walk, slowest region | 180 | never in 200 | never in 200 |
| Cabin lamp switch, off / on | 98 / 59 | 100 / 29 | 92 / 57 |
| rect-lamps blotch / specks | 0.051 / 0.79 | 0.070 / 3.18 | 0.065 / 8.62 |
| bay-sunbeam blotch / specks | 0.041 / 0.05 | 0.066 / 0.08 | 0.071 / 0.04 |
| exit-deck blotch / specks | 0.0067 / 0.02 | 0.0053 / 0.005 | 0.0105 / 0.005 |
| orbit cabin (tick 600) blotch / specks | 0.043 / 0.68 | 0.060 / 1.47 | 0.056 / 7.94 |
| Mean bounce light against main: orbit cabin, exit-deck, rect-lamps, bay-sunbeam, lamp-hall-16 | 1 | 0.54, 0.86, 0.50, 0.65, 0.89 | 2.63, 4.57, 2.44, 3.70, 0.89 |

Images (main, A, B side by side, each at its own auto exposure): /private/tmp/g3tk/report/orbit-cabin-main-A-B.png, exit-deck-main-A-B.png, rect-lamps-main-A-B.png, bay-sunbeam-main-A-B.png.

Reason for the drop: the later bounces are the smooth part of the bounce light. Without them A raised blotches and specks (rect-lamps specks 0.79 to 3.18) and lost 11 % to 50 % of the light. B's albedo fill assumes a closed room of the receiver's albedo, so it washes out pale surfaces (2.4 to 4.6 times main's bounce light, about twice the clamp-off reference of 1.28 to 1.35 times main). The lamps-off fall stayed near 100 draws in every variant, so the feedback loop does not cause it.

Experiment 1 (2026-11-10, main 6c220857a era, /private/tmp/g3tk/e1): the bounce light carries about 4/5 of the walk's excess at the stop (+22 of +28 points, whole image), the reflections the rest, the direct light about 0. The repeat run is bit for bit the same. A stand control (the walk's script without the key press) is as bright: its center region's bounce light is 40 % to 70 % above the end still for about 240 film draws. A clock experiment (/private/tmp/g3tk/e1/results/clock) shows that this start-up excess follows world ticks, not draws: 300 extra ticks before the first draw remove most of it, 300 extra still draws do not. A state dump shows that no entity in the ship moves but the inner airlock door, which opens beside the person by tick 160. Open: why the light after a load falls over about 400 ticks, and what holds the cabin's old light for about 100 draws after the lamps go off.

**2026-11-12T01:17:43Z**

Cause 2, the cabin's lamps-off fall (2026-11-11). Reflections settle slowest; bounce settles in 20-57 draws. Switch split of the reflections signal alone, lamps off at film frame 160 (survey switch case cabin, settle = draws until every region stays within 5 % of the lamps-off control):

| Reflection history | Slowest region | Highest jump after lamps off | rect-lamps blotch / specks | bay-sunbeam blotch / specks |
|---|---:|---:|---|---|
| main (precise, uncapped, cut rule with 25 % share) | 128 | +72 % | 0.051 / 0.79 | 0.041 / 0.055 |
| clamp off | none in 200 | +117 % | - | - |
| capped (TERTIUS_SPECULAR_HISTORY=precise-capped) | 92 | +77 % | 0.050 / 2.23 | 0.040 / 0.059 |
| cut rule without the share condition | 126 | +42 % | 0.051 / 0.77 | 0.041 / 0.055 |
| both | 87 | +43 % | 0.050 / 2.24 | 0.040 / 0.059 |

Reading: four draws after lamps off the age cut restarts the history; the share condition lets only samples of at least 25 % of the history cut it, so the restart keeps the bright samples (the upward jump). The long tail is the uncapped history averaging the reflected bounce light while that bounce drains: the light age sees only direct-light changes, so nothing cuts the history again. Capping shortens the tail but nearly triples rect-lamps' specks (fails). Scratch: /private/tmp/g3tk/cut, /private/tmp/g3tk/noise.

**2026-11-12T06:57:08Z**

Cause 1, the bounce light's start-up excess after a load (2026-11-12). It is not a start-up of the renderer: fresh 33-draw stills of the cabin walk's view read r11 bounce 0.148 up to world tick 300 and 0.121 from tick 306 on (a world loaded at tick 330 reads the same as the continuous run), and the stand film's r11 excess (+54..+67 % against the film's end) ends right after film draw 240 = world tick 300. The scene changes nothing inside the ship then (dumps of tick 296..306: lamps, doors, person and crates hold their poses); only the sun turns against the ship. Sun off: no step (r11 0.0675 at both ticks).

The sun's bounce comes from sunlit strips on the window sills that are thinner than a render pixel. With TERTIUS_DEBUG_JITTER=none the sun adds nothing to r11 (single bounce 0.0147, sun off 0.0143). The unbiased mean of the single-bounce samples (diffuse filter off, clamp off, 12 continued stills of 33 draws) falls from r11 0.067 at tick 300 to 0.038 at tick 306 (-43 %) while the direct sunlight on the strips rises 2 % (437 changed pixels, all on sills and jambs). So the screen tracer's expected light depends on which jitter positions land on the strips: a sampling fault, not a change of light. The light-age record sees only those few sill pixels, so neither the clamp gate of ter-18hc nor ter-wkvw's bounce cut covers the step.

The diffuse filter's write-back adds a second fault: at a fixed state the single-bounce r11 climbs from 0.029 to 0.108 (about 1.6 times the reference) over about 200 draws, collapses, and climbs again, as a bright band where the ceiling meets the far wall. Filter off: flat after about 66 draws. Not the cause: the clamp (clamp off still climbs), the guided rays, the ray plan (full rate everywhere), the environment map (no map light: same step), the sun's disc (not in view). The gated clamp (t7-3e22's probe, TERTIUS_T7_GATE=1) keeps r11's peak excess at +78 % (main +77 %) and drains slower after the step (+34 % against +16 % at film draws 320..360). Scratch: /private/tmp/g3tk/look (seeds, climb, loop, truth, dgate takes).

**2026-11-12T07:13:33Z**

The honest walk measure (commit ab2cd43e3): the walk stands 180 frames, walks 120, stops, films 200; a control walks right after the load without drawing (the person ends at the same place to 9e-15 m), then draws through the same world ticks, and the settle compares frame by frame. On this branch's renderer (= main): whole image 33 draws, regions [r00..r22] 61, 18, 36, 156, 38, 143, 25, 15, 28; slowest 156. r10 and r12 settle by frame 30-60 and then wander up to +5 % / -5 % against the control at frames 120-150: they hold the side windows' sub-pixel sill strips, so that is cause 1's sampling fault, not the walk. Images: /private/tmp/g3tk/report2 (sun-share.png: the sun's bounce and its direct light on screen at tick 300 and 306; single-step.png: single bounce at tick 300 and 306 and the drop; climb-map.png: the band at the far ceiling edge at still 1, 6 and 7 and the change). Results: /private/tmp/g3tk/walk2/results/walk/cabin-walk.json.

**2026-11-12T10:22:54Z**

2026-11-12 overnight. (1) The honest walk measure merged into main on its own (641450420) after a review; the review's fixes are 92f69310a (both runs hold the key alike, own render folders). (2) The cut rule without its 25 % condition is built on branch ter-g3tk-cut-rule, ticket cr-pkzd, ready to merge: films show no regression, the reflections' jump after lamps off falls from +72 % to +42 %, the tail stays at about 126 draws. (3) The climb test on new main with the thin diffuse filter (ter-hxhl, 6e3ba0641): the far-ceiling band still climbs, r11 at tick 300 from 0.154 to 0.277 over 198 draws, collapses to 0.233 and climbs to 0.284; the old kernel (TERTIUS_DIFFUSE_FILTER=wide) on the same binary climbs from 0.148 to 0.253 as before. Picture: /private/tmp/g3tk/report2/newmain-band.png. Region means of every take: /private/tmp/g3tk/look/region-means.json (raw frames deleted).

**2026-11-12T12:43:59Z**

2026-11-12, Alexis's decision: no fix here for the two remaining parts. The reflections' slow fall after the cabin's lamps go out (126 to 128 draws, the jump now +42 % with cr-pkzd merged) and the sunlit-sill step of the bounce light at tick 303 wait for Design A of t7-3e22 (an always-on clamp widened by each pixel's stored noise, plus a test over 8 x 8 blocks that counts a regional bounce change as a change), which is meant to cover both. The measurements to judge Design A by are listed in t7-3e22's note of the same day. The ticket stays in progress until Design A lands and the survey's walk and switch parts are measured again.
