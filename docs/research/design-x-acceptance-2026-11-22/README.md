# Design X's last acceptance takes (t0-5tyj)

The record compares Design X with main `c4c1f1f2f`.

- M is main at `c4c1f1f2f`, with main's survey probe patch. Main has moved on to `a819b10b2` since. M stays `c4c1f1f2f` in this record.
- X is the line's head `57b4045ae` (branch `t7-3e22-retained-deviation`), with the line's survey probe patch after the fix of `tc-w3c0`. This checkout's later commits change only this record, the probe's lamp switch and tickets. They change nothing under `src` or `tests`.
- A is X with both history clamps off (`TERTIUS_A_NO_CLAMP=1`). A explains a fault. It is not the bar.

The bar is the user's: "acceptance is by the engine's stills and films. They must look good and show no blotches, daubs, bright specks (fireflies), trail or flicker. A reference explains a fault and is not the bar. A little lost light is acceptable. The bay film's bar is: no worse than main."

The user's sentence of 2026-11-22: "I think Design X is strictly better, since the 'worse' captures aren't visibly worse to me either."

Every take ran on this Mac (Apple M1 Max, MoltenVK) under `tools/gpu-lock.sh`, unless a row names the cloud's RTX 2000 Ada. A take with pictures held the lock in its shared mode. A timing run held it alone, at a one-minute load under 8.

## The decision table

"By the bar" says whether X is better than M, equal to M or worse than M, read from the pictures and the measures together. The measures come first in the sections below. Both commits are as above unless the row says otherwise.

| Scene and take | X against M by the bar | Pictures and videos | M | X |
|---|---|---|---|---|
| The survey's 90 stills | Equal or better. The measures call 21 worse, 36 equal and 33 better. No sheet shows a fault on X that M lacks. | `pics/still-<case>.png` (21 sheets) | `c4c1f1f2f` | `57b4045ae` |
| Light share of rect-lamps, bay-sunbeam, docking-bay | Better: X 97.9 to 100.4 % of A, M 81.6 to 102.8 % | none (numbers) | `c4c1f1f2f` | `57b4045ae` |
| Daub of the same three stills | Better: lower on X in all three | `pics/still-*.png` | `c4c1f1f2f` | `57b4045ae` |
| The 18 motion films | Equal or better. The measures call 4 worse (flicker of rect-bay and bay-sunbeam, level of docking-landing, blotch of exit-floor), 9 equal and 5 better. The sheets show no fault on X that M lacks. | `pics/film-{rect-bay,bay-sunbeam,docking-landing,exit-floor,rect-lamps}.png` | `c4c1f1f2f` | `57b4045ae` |
| Switch bay-sun-off | Better: settles in 24 / 17 draws against 50 / 38 | none | `c4c1f1f2f` | `57b4045ae` |
| Switch lamp-hall-16 | Equal: 35 / 25 against 36 / 25 | none | `c4c1f1f2f` | `57b4045ae` |
| Switch cabin, fair switch (glow off with the light), fixed scale 2/3 | Better: settles in 77 / 51 draws against 143 / 92. Both show grain and boil after off (a bug on both sides). | `pics/switch-cabin-fair-fixed-23.mp4`, `pics/boil-levels-fair-23.png` | `c4c1f1f2f` | `57b4045ae` |
| Lag films lamp-hall-16-moving and lamp-hall-64-spot-moving | Equal | none | `c4c1f1f2f` | `57b4045ae` |
| Walk cabin-walk, fixed scale 2/3 | **Worse by the trail**: the trail after the stop fades in 48 draws against 26. Both are inside the goal of 60. | `pics/walk-cabin.png`, `pics/walk-cabin-fixed-scale.mp4` | `c4c1f1f2f` | `57b4045ae` |
| Climb cabin-climb | Better: band peak 0.984 of the filter-off band against 1.012 | none | `c4c1f1f2f` | `57b4045ae` |
| Evidence census of four stills | Pass on X: no motion or light evidence | none | | `57b4045ae` |
| Switch cabin under the dynamic render scale, 16.67 ms (cloud) | Mixed: worse after off (182 against 148 draws), better after on (59 against 86). Both show grain. Old switch (glow stays). | `pics/switch-cabin-dynamic-16ms.mp4` | `c4c1f1f2f` | `57b4045ae` |
| Switch cabin under the dynamic render scale, 10 ms (cloud) | Equal to slightly better: 184 / 80 against 175 / 82 (slowest region), 115 / 34 against 127 / 51 (whole image). Old switch (glow stays). | `pics/switch-cabin-dynamic-10ms.mp4` | `c4c1f1f2f` | `57b4045ae` |
| Walk under the dynamic render scale, 16.67 ms (cloud and Mac) | **Worse by the trail**, as at the fixed scale: 44 against 24 draws on the card | `pics/walk-cabin-dynamic-16ms.mp4`, `pics/walk-cabin-dynamic-16ms-mac.mp4` | `c4c1f1f2f` | `57b4045ae` |
| The wobble under the dynamic render scale | Equal: an engine fault on both sides, a little larger on X because X's scale moves more | `pics/wobble-switch-dynamic.png` | `c4c1f1f2f` | `57b4045ae` |
| The cabin's window jambs | Equal: sunlight leaks onto them at some render sizes on both sides | `pics/jamb-*.png`, `pics/ramp-jambs-main-c4c1f1f2f.png` | `c4c1f1f2f` | `57b4045ae` |
| The frame's GPU span after a lamp switch | **Worse**: a block of about +5 ms over main's top for 10 to 13 draws after each switch, against about +2.5 ms for 7 draws on M. A cost of Design X. The variants XB and XC remove it. | `pics/cost-switch-fair-23.png`, `pics/switch-cabin-fair-fixed-23-x-xb-xc.mp4` | `c4c1f1f2f` | `57b4045ae`, XB and XC as scratch patches on it |
| The deck's GPU frame span | Cited: +0.254 ms (spread 0.145), within the +0.3 ms of point 13. T17: see "Frame time". | none | `c4c1f1f2f` | `57b4045ae` |
| Screenshots of the real renderer (T19) | Equal: both clean. X's bay walls are a little brighter. | `pics/screenshot-rect-bay.png`, `pics/screenshot-rect-lamps.png` | `c4c1f1f2f` | `57b4045ae` |

## The takes where X is worse

1. The walk cabin-walk: X's trail after the stop fades more slowly. The draws until the slowest region stays within 5 % of its control are 48 on X and 26 on M (goal 60). At 40 draws after the stop, the center region r11 is 4.3 % too bright on X and 2.6 % on M. Under the dynamic scale on the card it is 44 against 24 draws.
2. The frame's GPU span after a lamp switch: X costs about +5 ms more than main at the top of a block of 10 to 13 draws after each switch. Over the 60 draws after a switch, the mean excess is about +1.2 ms. This is a cost of Design X ("The frame's cost after a lamp switch"). Both variants of the experiment remove it ("The experiment on the block").
3. The switch cabin under the dynamic scale at 16.67 ms on the card, after off: the slowest region settles in 182 draws on X against 148 on M. After on, X is faster (59 against 86).
4. By the survey's measures only: 21 stills (14 by specks, 4 by level alone, 2 by blotch and level, 1 by blotch alone) and 4 films. In none of their sheets does X show a speck, blotch, daub or trail that M does not show. The user saw them and called them not visibly worse.

## Acceptance points 1 to 14

The points are those of `docs/plans/bounce-light-confidence.md`, section 22. "Pass" and "fail" are by the point's own words.

| Point | M | X | The numbers |
|---|---|---|---|
| 1. Light: every region's long mean at 97 to 102 % of the truth (A) | Fail | Pass | Long mean (settled) at the fixed exposure. rect-lamps ceiling / walls: M 102.8 / 102.8 %, X 100.4 / 100.3 %. bay-sunbeam walls / floor: M 88.3 / 81.6 %, X 98.8 / 99.3 %. docking-bay walls / floor: M 87.3 / 91.3 %, X 97.9 / 99.7 %. |
| 2. Blotch at most 0.04 in every still. Daub recorded against the bar 0.008 to 0.010 | Pass (largest 0.0397, hair-rugs) | Pass (largest 0.0388, hair-rugs) | Daub against A at the survey's exposure, rect-lamps / bay-sunbeam / docking-bay: M 0.0117 / 0.0311 / 0.0394, X 0.0088 / 0.0225 / 0.0291, A 0.0080 / 0.0148 / 0.0208. |
| 3. `t7-3e22` closes only at the daub's bar | | Open | X meets the bar in rect-lamps only. The bay and the docking bay stay above it. |
| 4. Specks: none above 1.0 where the control is at most 1.0, no other rise above 10 % | (control) | Fail by the letter | No still is above 1.0 on X (largest skin-hair, 0.9864, M 0.9332). 14 stills rise by more than 10 % and more than one speck: 1 to 9 more specks in the whole view, on hard edges against dark. |
| 5. Settle within 60 draws: cabin, bay, lamp hall 16, off and on | Fail (cabin 143 / 92) | Fail (cabin 77 / 51) | bay-sun-off M 50 / 38, X 24 / 17. lamp-hall-16 M 36 / 25, X 35 / 25. The cabin fails after off on both sides. |
| 6. Walk: every region within 5 % of its control within 60 draws | Pass (26) | Pass (48) | Per region M / X: r01 23 / 45, r10 26 / 48, r11 15 / 32, r12 1 / 21, r20 15 / 33. |
| 7. Stand film: r11 after tick 306 recorded | Recorded (cited) | Recorded (cited, X0) | `docs/research/t0-9fo0-room-after-shadow-fix`, table 3: r11 settles 0 draws after tick 306 on M and on X0. Not taken again on X. |
| 8. A settle case that fails only in the reflections goes to the user | Not measured | Not measured | This record did not split the switches by signal. |
| 9. Lag films: no median lag rises. The median lag error rises by at most 0.005 | Control | Pass within noise, fail by the letter | lamp-hall-16-moving: median lag 4.432 to 4.444 draws (+0.012 draws), error 0.02764 to 0.02736. lamp-hall-64-spot-moving: lag −0.049 to −0.330 draws, error 0.01064 to 0.01098 (+0.00034). bay-sun (cited, `t0-acxm-region-evidence`): lag 28.3 to 14.0 draws, error 0.270 to 0.150. |
| 10. The named tests green in Checked and Release | Not taken here | Not taken here | The merge's gate (`t0-iido`). |
| 11. Climb: band peak at most main's share (required), within 10 % (target) | Within 10 % | Pass, within 10 % | Band peak as a share of the same build's band with the filter off: M 1.012, X 0.984. |
| 12. Census: no motion evidence, at most 10^−4 light evidence | Not applicable | Pass | rect-lamps, exit-cabin, docking-bay and station-cabin, 329 draws each, 4,737,600 block-draws per signal: 0 motion and 0 light evidence in both signals. |
| 13. Frame time: at most +0.3 ms on the deck. The span after a switch is recorded | | Pass (cited). The span after a switch is recorded. | Cited: +0.254 ms (spread 0.145), the remedy's paired rounds (`t0-acxm-region-evidence`). T17 of this record: see "Frame time". |
| 14. Portability: no validation message on the Mac and on Linux, no new device feature, `-Werror` | Not taken here | Not taken here | T18 did not run. See "What was not measured". |

## Measured facts

### The stills

The survey's still part, 90 cases on each side. Group 7 holds X's retakes of seven stills whose first render failed to start (`vkCreateInstance` gave `ErrorIncompatibleDriver` while a nix store copy ran). station-cabin and windows-earth are black or unmeasured on both sides (`ter-cept`).

- A measure counts as worse on X where X's value passes M's by more than 10 % and by more than its floor: one speck (0.0049), 0.001 for the blotch, 0.002 for the level's magnitude.
- By their worst measure: 33 better, 36 equal, 21 worse.
- The 21 worse: 14 by specks (airlock-hull, earth-pad, lamp-hall-16-spot, lamp-hall-4-spot, lamp-hall-64-moving, lamp-hall-64-spot-moving, orbit-chase, orbit-umbra, rect-lights, reflections-front, spacewalk, station-watch, throw, windows-cabin). 7 others: by level doorway-outside (+3.0 %), yard-end (−3.6 %), stars-2 (−1.8 %) and danube-hold (+0.4 %), by blotch and level yard-ahead (0.0036 to 0.0068, +2.2 %) and push-astronaut (0.0101 to 0.0132, +2.1 %), by blotch meshes (0.0063 to 0.0095). airlock-hull is also worse by level (−0.3 %).
- `tables.txt` holds the whole table.

What the sheets show (opened with the Read tool, all 21): in none does X show a speck of light, a blotch or a daub that M does not show. X's extra specks lie on hard edges against dark: the planet's limb, box tops and pillar edges. The four stills worse by level look the same on both sides. M has a level of the same size on doorway-inside (+3.6 %).

### The light share and the daub

`light.py` and `daub.py` of `docs/research/design-x-acceptance-2026-11-14/tools`, on the stills of rect-lamps (4.08 stops) and of the bay and docking bay (−4.0 stops) at a fixed exposure, against A's long reference. "Shown" is the still at draw 132, "settled" is the side's own long reference.

| Still | Region | M shown / settled | X shown / settled | A shown / settled |
|---|---|---|---|---|
| rect-lamps | ceiling | 101.2 / 102.8 % | 98.9 / 100.4 % | 96.7 / 100.0 % |
| rect-lamps | walls | 101.0 / 102.8 % | 98.7 / 100.3 % | 96.0 / 100.0 % |
| bay-sunbeam | walls | 88.2 / 88.3 % | 98.2 / 98.8 % | 98.3 / 100.0 % |
| bay-sunbeam | floor | 81.1 / 81.6 % | 97.4 / 99.3 % | 96.9 / 100.0 % |
| docking-bay | walls | 87.3 / 87.3 % | 96.8 / 97.9 % | 97.4 / 100.0 % |
| docking-bay | floor | 91.5 / 91.3 % | 99.0 / 99.7 % | 98.8 / 100.0 % |

Daub and broad error, against A's long reference:

| Still | Exposure | M daub / broad | X daub / broad | A daub / broad |
|---|---|---|---|---|
| rect-lamps | survey's | 0.0117 / 0.0202 | 0.0088 / 0.0071 | 0.0080 / 0.0213 |
| bay-sunbeam | survey's | 0.0311 / 0.0583 | 0.0225 / 0.0374 | 0.0148 / 0.0329 |
| docking-bay | survey's | 0.0394 / 0.0564 | 0.0291 / 0.0462 | 0.0208 / 0.0265 |
| rect-lamps | fixed | 0.0102 / 0.0190 | 0.0081 / 0.0079 | 0.0079 / 0.0226 |
| bay-sunbeam | fixed | 0.0317 / 0.0999 | 0.0216 / 0.0363 | 0.0140 / 0.0374 |
| docking-bay | fixed | 0.0391 / 0.1109 | 0.0273 / 0.0499 | 0.0196 / 0.0320 |

X's bay-sunbeam still is its retake (`s7-X`, `tools/combine.sh`).

### The motion films

The survey's motion part, 18 films. Flicker and grain flicker (limits 0.006 and 0.05), and the blotch and level of the worst sampled frame (the film's frame against a still of the same state, limits 0.04 and 10 %). The same worse rule as the stills.

| Film | Flicker M / X | Blotch M / X | Level M / X | By the measures |
|---|---|---|---|---|
| rect-bay | 0.00204 / 0.00273 | 0.238 / 0.187 | +1.172 / +0.850 | flicker worse, blotch and level better |
| bay-sunbeam | 0.00393 / 0.00519 | 0.342 / 0.209 | −0.608 / −0.377 | flicker worse, blotch and level better |
| docking-landing | 0.00157 / 0.00151 | 0.182 / 0.154 | +0.014 / −0.037 | level worse, blotch better |
| exit-floor | 0.00393 / 0.00391 | 0.073 / 0.086 | −0.034 / −0.035 | blotch worse |
| lamp-hall-4-spot-moving | 0.01212 / 0.01171 | 0.090 / 0.077 | +0.002 / +0.001 | blotch better |
| rect-lamps | 0.00030 / 0.00027 | 0.014 / 0.010 | +0.008 / −0.008 | blotch better |
| pillar-room | 0.00060 / 0.00051 | 0.018 / 0.014 | −0.000 / +0.000 | blotch better |
| spot-box | 0.00069 / 0.00052 | 0.015 / 0.012 | −0.001 / −0.001 | flicker better |
| exit-cabin | 0.00104 / 0.00090 | 0.031 / 0.029 | +0.010 / −0.006 | level better |

The other nine films are equal. orbit-umbra is too dark for the survey on both sides. rect-bay's numbers equal those cited from `t0-acxm-region-evidence`.

What the sheets show: on bay-sunbeam, M's middle frame has a bright blotch on the left wall, and X's frames are closer to its stills. On docking-landing, both sides show the same fine grain beside the sunbeam's patch on the floor, in the films and in the stills. On exit-floor, the four columns look alike.

### The switches and the lag films

| Case | M off / on (whole image) | X off / on (whole image) |
|---|---|---|
| bay-sun-off | 50 / 38 (27 / 21) | 24 / 17 (13 / 8) |
| lamp-hall-16 | 36 / 25 (7 / 5) | 35 / 25 (7 / 5) |
| cabin, old switch (glow stays) | 143 / 92 (79 / 55) | 77 / 51 (43 / 31) |
| cabin, fair switch (glow off) | 143 / 92 (123 / 55) | 77 / 51 (67 / 31) |

Draws until the slowest of the 3 × 3 regions (and the whole image) stays within 5 % of its control, after off / on. The lag films are in point 9 above.

### The walk and the climb

- After the camera stops, both sides start 7 to 10 % too bright in the walked regions: the walk's trail. r11's excess over its control at 0, 20, 40, 60, 100 and 150 draws after the stop: M +6.8, +4.3, +2.6, +1.7, +0.8 and +0.3 %. X +7.7, +5.9, +4.3, +3.1, +1.9 and +1.0 %.
- X0, the line's head before the region's drift test (`631cc61cc`), settles in 54 draws against X's 48. Per region M / X / X0: r01 23 / 45 / 49, r10 26 / 48 / 54, r11 15 / 32 / 36, r12 1 / 21 / 30, r20 15 / 33 / 39. r11's excess at 0, 20, 40, 60 and 100 draws: X0 +8.1, +6.3, +4.7, +3.7 and +2.3 %. So X0 fades like X or slightly slower.
- r12 repeats exactly every 18 draws, the jitter's cycle at the scale 2/3 (`Fsr3Upscaler.JitterPhaseCount`: 8 × (1920 / 1280)² = 18). The control, which has no walk, shows the same pattern with the same spread (1.36 % against 1.35 % after the stop). The sunlit strip in r12 was not checked on a picture.
- Climb: the bounce band's peak over its light with the filter off is 1.012 on M and 0.984 on X. The plateaus are within 0.2 % of each other.

### The evidence census

| Still | Block-draws per signal | Motion evidence | Light evidence | Anchor-draws tested by the region test | Of them, fires |
|---|---|---|---|---|---|
| rect-lamps | 4,737,600 | 0 | 0 | 99.2 % | 1.95 % |
| exit-cabin | 4,737,600 | 0 | 0 | 100.0 % | 0.96 % |
| docking-bay | 4,737,600 | 0 | 0 | 97.1 % | 1.19 % |
| station-cabin | 4,737,600 | 0 | 0 | 100.0 % | 12.93 % |

The region test fires in stills, where no light changes. Ticket `t0-qvm1` holds the false fires at anchors whose light scatters. station-cabin is black (`ter-cept`).

### The fair cabin switch: grain and boil

The probe's lamp switch first turned off a lamp's light and left its panel's glow on (the "glow stays" case). Since `68239c0af` it turns the glow off with the light (`TERTIUS_PROBE_LAMP_GLOW=off`, the default). The selection `z:-12:4` switches 2 of the cabin's 6 lights: the near overhead lamp and, by inference from the camera-relative rule, cabin-lamp-3 behind the eye. The middle and far overheads stay on.

The boil is `tools/boilnoise.py`'s residual: per 16 × 16 block of three crops (floor, left wall, ceiling), b[k] − (b[k−1] + b[k+1]) / 2, as a share of the crop's light. A fade that is linear over three draws gives zero. Mean boil in % at the fixed 2/3, floor / wall / ceiling:

| Span | M | X |
|---|---|---|
| 18 draws before off | 0.02 to 0.04 | 0.02 to 0.04 |
| 0 to 60 after off | 0.24 / 0.23 / 0.29 | 0.26 / 0.29 / 0.32 |
| 60 to 200 after off | 0.07 / 0.07 / 0.09 | 0.06 / 0.04 / 0.07 |
| 0 to 100 after on | about 0.05 to 0.21 | about 0.05 to 0.21 |

The crops' levels fall smoothly after off on both sides (`pics/boil-levels-fair-23.png`). The largest rise against the fall is +0.18 %.

### The frame's cost after a lamp switch

The fair switch at the fixed 2/3, the GPU span of each draw from the time log of `scratch-dynamic-scale.patch` (`TERTIUS_X_TIME_LOG`), under the exclusive lock at a one-minute load under 8 (`tools/macdyn.sh cost`).

Mean of each code's runs, in ms. "Block" counts the draws in the 100 after a switch whose span lies more than 1 ms above the run's settled span.

| Code | Runs | Settled, lamps on (frames 40 to 159) | 0 to 59 after off | 100 to 199 after off | 0 to 59 after on | Peak after off / on | Block after off / on |
|---|---|---|---|---|---|---|---|
| M | 4 | 15.08 | 15.38 | 15.02 | 15.36 | 17.61 to 17.71 / 17.45 to 17.67 | 7 / 7 (draws 4 to 10) |
| X | 3 | 15.34 | 16.58 | 15.20 | 16.58 | 22.80 to 22.91 / 22.77 to 22.91 | 13 / 10 to 11 (draws 4 to 16 / 4 to 14) |
| XB | 2 | 15.30 | 15.27 | 15.19 | 15.39 | 15.55 to 15.68 / 15.66 to 15.67 | 0 / 0 |
| XC | 2 | 15.34 | 15.18 | 15.24 | 15.45 | 15.59 to 17.44 / 15.66 to 15.94 | 0 to 1 / 0 |

The excess over M per 10 draws after the switch, in ms:

| Code | After off: 0-9, 10-19, 20-29, 30-39 | After on: 0-9, 10-19, 20-29, 30-39 |
|---|---|---|
| X | +3.16, +3.44, +0.16, +0.06 | +3.19, +2.99, +0.34, +0.24 |
| XB | −0.97, −0.18, +0.07, +0.07 | −0.88, −0.08, +0.27, +0.25 |
| XC | −1.10, −0.20, −0.04, −0.02 | −0.92, −0.08, +0.39, +0.38 |

- The runs: M's rounds 1 and 2 (14:00 to 14:09) and its rounds s2 and s3, X's round 1 and its session rounds s1 and s2, and the session rounds of XB and XC (14:44 to 15:03). The one-minute load when a run got the lock was 2.6 to 7.0. M's session round s1 ran beside my own boil measure, gave a settled span of 18.21 ms, and is left out. s3 repeats it.
- XC's one draw above settled + 1 ms is a single draw 16 after off in round s2 (17.44 ms).
- In the settled draws, X, XB and XC cost 0.22 to 0.27 ms more than M.
The shape: draws more than 1 ms above the settled span start at the 4th draw after each switch on both sides. On M they last 7 draws (4 to 10) and peak at 17.6 to 17.7 ms. On X they last 13 draws after off (4 to 16) and 10 after on (4 to 13), as a flat plateau at 22.7 to 22.9 ms. So the cost is a block, not a curve that falls with the histories' age.

The code explains the block's start and length. This is a reading of the code, not a measurement:

```glsl
float antiLagged = max(fastCap, historyLength * (1.0 - min(1.0, lag / parameters.antiLagShare)));
if (evidence == LightEvidence)
    historyLength = fastCap;
else if (evidence == RegionEvidence)
    historyLength = min(antiLagged, FullRateFrames);
else
    historyLength = antiLagged;
```

(`src/Tertius.Rendering/Shaders/accumulate_spatial.comp` on X), and

```glsl
if (historyLengthOf(r, kind) >= FullRateFrames && fits(p, c, kind))
    continue;
```

(`screen_trace_plan.comp`: a pixel that does not `continue` here traces a ray of its own), where

- `historyLength` = the pixel's history length after this draw, in draws, which the slow history stores in its alpha.
- `fastCap` = the fast history's largest length, 4 draws (`SignalParameters.FastCap`), or the pixel's cap where that is lower.
- `lag` = how far the clamp moved the slow history, as a share of the light. `parameters.antiLagShare` = the lag at which the anti-lag cuts the history to `fastCap`.
- `evidence` = the evidence of this pixel in this draw: light evidence (a light change that lasted `LightChangeDraws` = 4 draws), region evidence (the region's drift test fired) or none.
- `FullRateFrames` = 8 draws (`sample_age.glsl`). A history shorter than this traces its own ray of the kind.
- `historyLengthOf(r, kind)` = the reprojected length of the pixel's history of the kind, read by the next draw's ray plan.
- `fits(p, c, kind)` = whether the block's chosen pixel's ray fits this pixel.

Light evidence holds a history at `fastCap` for the 7 light ages 0 to 6 (`RecentLightAge = 7u` in `sample_age.glsl`). The history then needs 4 more draws to pass 8. That gives about 11 draws of own rays, which fits the measured 10 to 13. Main's accumulator shortens a history only through the anti-lag, so only pixels whose clamp moved far go young.

### The experiment on the block: variants XB and XC

Two scratch variants of X, built in probe folders (`tools/playbuild.sh xb` and `xc`), never committed to `src`:

- XB (`scratch-light-evidence-cap.patch`): light evidence cuts a history to `min(antiLagged, FullRateFrames)`, as region evidence does, so a light change keeps the block's shared ray.
- XC (`scratch-plan-rate-at-fast-cap.patch`): the restart stays at `fastCap`, and the ray plan gives no own ray to a history of 3.5 draws or more. The plan reads a stored 4.0 as a weighted mean of four taps, which can round to just under 4.0, so the test is 3.5. A stored length is never between 3 and 4. This is wider than the design it stands for: it takes the own ray from every history of 4 to 7 draws, also from one that the anti-lag shortened.

Check of the builds: each Rendering assembly embeds 88 SPIR-V modules. XB differs from X in 4 modules (the variants of `accumulate_spatial`), XC in 3 (the variants of `screen_trace_plan`). No other module differs.

All four codes, the fair cabin switch at the fixed 2/3 on the Mac. The settle's cost is the draws more than X needs.

| Code | Settle, slowest region off / on | Whole image off / on | Settle's cost against X, slowest / whole | Frame span 0 to 59 after off / on | Peak after a switch | Block |
|---|---|---|---|---|---|---|
| M | 143 / 92 | 123 / 55 | +66 / +41, +56 / +24 | 15.38 / 15.36 ms | 17.7 ms | 7 draws |
| X | 77 / 51 | 67 / 31 | 0 | 16.58 / 16.58 ms | 22.9 ms | 10 to 13 draws |
| XB | 82 / 55 | 72 / 34 | +5 / +4, +5 / +3 | 15.27 / 15.39 ms | 15.7 ms | none |
| XC | 78 / 51 | 67 / 31 | +1 / 0, 0 / 0 | 15.18 / 15.45 ms | 15.7 ms (one draw 17.4) | none |

The boil without the fade, in %, floor / left wall / ceiling, from the source frames:

| Code | 18 draws before off | 0 to 60 after off | 60 to 200 after off | 0 to 100 after on |
|---|---|---|---|---|
| M | 0.02 / 0.03 / 0.04 | 0.24 / 0.23 / 0.29 | 0.07 / 0.07 / 0.09 | 0.05 / 0.21 / 0.13 |
| X | 0.02 / 0.02 / 0.04 | 0.26 / 0.29 / 0.32 | 0.06 / 0.04 / 0.07 | 0.05 / 0.20 / 0.14 |
| XB | 0.02 / 0.02 / 0.04 | 0.26 / 0.27 / 0.29 | 0.06 / 0.04 / 0.07 | 0.05 / 0.20 / 0.13 |
| XC | 0.02 / 0.02 / 0.04 | 0.27 / 0.30 / 0.34 | 0.06 / 0.04 / 0.07 | 0.05 / 0.21 / 0.15 |

How XB and XC differ from X per frame (frames 160 to 459, the crops' mean light, and the 99th percentile of the per-pixel difference in 8-bit steps):

- XB lags. Its crops are up to 8 / 12 / 14 % brighter than X's (floor / wall / ceiling) in the 60 draws after off, and up to 2 / 10 / 8 % darker in the 100 draws after on. The per-pixel difference reaches 9 of 255. The level chart shows XB's slower fade on the wall and the ceiling after both switches.
- XC differs from X by at most 1.7 % in any crop and 4 of 255. Its curves lie on X's.

The video: `pics/switch-cabin-fair-fixed-23-x-xb-xc.mp4`, three panes from left to right X, XB and XC, 1280 × 720 each (3840 × 720), 320 frames at 60 frames a second, one draw per frame, H.264 `yuv420p` at CRF 12, 6.2 MB. It runs from 20 draws before off to 100 draws after on, at the fixed exposure of 4.08 stops and the fixed scale 2/3, so no size is played back. A counter shows the draws since the last switch. The panes are 1280 × 720 because the render size at 2/3 is 1280 × 720, so a pane keeps the render's own detail and grain.

What I measured in the video:

- The fade: each pane's regions against its last frame, decoded against source, at 0 and 60 draws after off and 20 after on. They differ by at most 0.26 % of the light. r11 at 20 draws after on, source / video: X −14.37 / −14.13 %, XB −16.09 / −15.90 %, XC −14.09 / −13.87 %.
- The boil, per pane and crop, video against source (the ceiling's rows 100 to 179, under the labels). In the 60 draws after off: X floor 0.58 / 0.25, wall 0.40 / 0.29, ceiling 0.47 / 0.29. XB 0.58 / 0.26, 0.37 / 0.26, 0.42 / 0.28. XC 0.58 / 0.26, 0.40 / 0.29, 0.46 / 0.30. So the encode adds boil, most on the dark floor, and adds the same amount to each pane. A test at CRF 4 (42.5 MB) still gave 0.41 on X's floor, so most of the added boil comes from the 8-bit encode of dark tones, not from the CRF.
- Not checked: single specks over time, and whether the eye sees XC's 1.7 % difference.

Reading: both variants remove the block. Neither changes the grain or the boil by more than 0.03 points. XB pays with a slower fade (+3 to +5 draws of settle and a visible lag of up to 14 % in the first draws). XC keeps X's settle and fade, and its boil is 0.01 to 0.02 points higher in the 60 draws after off. The user decides between the designs.
### Frame time (T17)

T17 PENDING

## The remedy under the dynamic render scale

The game's window always runs the dynamic render scale, `FrameTimeTarget`. The survey's headless runs use a fixed 2/3. `scratch-dynamic-scale.patch` gives a headless run the window's dynamic scale (`TERTIUS_X_FRAME_TIME_TARGET=<ms>`), logs each draw's render size and the GPU span, and can play a logged scale back.

The size replay: a run that writes pictures cannot choose its own scale. The PNG writes stall the frames to a GPU span of about 47 ms, and the controller then holds the scale at its floor of 0.50. So each dynamic-scale video comes from two runs. A run without pictures logs the scale that the controller chose for each draw (`TERTIUS_X_SIZE_LOG`). The picture run then plays that scale back draw by draw (`TERTIUS_X_SIZE_REPLAY`). In every video of this record, the played-back render sizes equal the logged ones for every draw.

What the region test does under a moving size: it needs 36 valid draws at one render size, and every size change restarts its count (`t4f-71qp`). On the Mac at 16.67 ms the scale cycles between 0.50 and 0.58, the size changes about every 2 to 3 draws, and the test runs on 0 % of the anchor-draws. On the card at 16.67 ms it runs on 12.3 % of the anchor-draws in the 100 draws after off and 12.5 % after on.

| Run (cabin switch, old switch) | Scale | Size changes | Slowest region off / on | Whole image off / on |
|---|---|---|---|---|
| Mac, M, 16.67 ms | 0.500 to 0.531 | 28 / 25 / 9 (before / 100 after off / 100 after on) | 188 / not within 100 | |
| Mac, X, 16.67 ms | 0.500 to 0.581 | 71 / 40 / 43 | not within 200 / 98 | |
| Mac, X, 16.67 ms, repeat | 0.501 to 0.561 | 39 / 26 / 28 | not within 200 / 98 | |
| Mac, X, 8 ms | stays at 0.50 | 0 | 81 / 51 | |
| Card, M, 16.67 ms | 0.957 to 1.000 | 37 in the video's draws | 148 / 86 | 82 / 45 |
| Card, X, 16.67 ms | 0.825 to 0.993 | 101 in the video's draws | 182 / 59 | 93 / 26 |
| Card, M, 10 ms | 0.733 to 0.763 | 28 | 175 / 82 | 127 / 51 |
| Card, X, 10 ms | 0.643 to 0.756 | 84 | 184 / 80 | 115 / 34 |

X's scale drops to about 0.83 to 0.88 after each switch on the card at 16.67 ms. The cost block above is the likely cause, because the controller sees the frame's span rise.

The videos (one side per cloud job at 1280 × 720, composed on the Mac):

- `pics/switch-cabin-dynamic-16ms.mp4`: the cabin switch at 16.67 ms, M left, X right. The logged scale was played back draw by draw, and the played-back sizes equal the logged ones for all 493 draws.
- `pics/switch-cabin-dynamic-10ms.mp4`: the same at 10 ms. The logged scale was played back draw by draw, and the played-back sizes equal the logged ones.
- `pics/walk-cabin-dynamic-16ms.mp4`: the walk at 16.67 ms on the card. The logged scale was played back draw by draw. The played-back sizes equal the logged ones for all 533 draws. The scale does not move in the video's draws (M 0.964, X 0.956).
- `pics/walk-cabin-dynamic-16ms-mac.mp4`: the walk at 16.67 ms on the Mac at a load of 5.4 to 7.6. The logged scale was played back draw by draw, and the played-back sizes equal the logged ones. M stays at 0.658, X moves from 0.644 to 0.651 with 3 changes. The fade 40 draws after the stop: M 2.20 %, X 3.56 %.

Checks of the videos (measured in each video, not only by looking at frames):

- Each region's fade against the last frame, decoded against source, differs by at most 0.17 to 0.57 % of the light. So the codec keeps the fade.
- The wobble (`tools/wobble.py`): edge tracking shows that the picture's horizontal distances follow (h / w) · 16 / 9 of the rounded render size, with a correlation of 0.993 to 0.996. The range over the switch video is 11.9 px on M and 15.5 px on X at 16.67 ms, and 16.1 and 19.6 px at 10 ms, of about 1185 px. The vertical axis does not move. The cause is the projection's aspect, which comes from the rounded render size. The back-face line fixes it on main.
- Not checked: the motion of single specks over time, and the grain's own flicker frame by frame.

## Grain and boil per side, by the bar

Both are bugs by the bar ("look good", "no flicker"), on both sides, and ticket `tc-bece` holds them.

- M: after the lamps go off, the floor, walls and ceiling show grain and a boil of 0.23 to 0.29 % per draw (residual, 16 px blocks) for about 60 draws, then 0.07 to 0.09 %.
- X: the same, 0.26 to 0.32 % for about 60 draws, then 0.04 to 0.07 %. X's boil is a little higher in the first 60 draws and lower after.
- Before the switch both stay at 0.02 to 0.04 %.

The cause is open: young histories or rare ray hits in the darkened room, the class of `ter-fe63`.

## The jambs

Sunlight leaks onto the cabin's window jambs at some render sizes and not at others, on main without the back-face layer, on X and on A.

- At a constant 0.956 (the card's scale) M, X and A all have bright jambs (M 0.2730 with the lamps on). At 2/3 they are grey (0.0446). The fixed path and the replay give equal values. With the sun off the jambs are grey at 0.956 (0.0440).
- The size ramp on main `c4c1f1f2f` (`tools/sizeramp.sh`, `ramp-jambs-main-c4c1f1f2f.txt`): 74 of the 181 render sizes leak (a jamb mean above 0.08), from 1000 × 560 up. All 8 sizes of the exact 16:9 aspect are grey.
- The combined check (`docs/research/t0-gbgj-combined-check/README.md` on branch `t3/combined-renderer-check`, commit `3ddcc475a`) ran the same ramp: 32 of 181 sizes leak on main `a819b10b2` with the layer, 35 on the merged head, and 0 on the wobble fix's branch. A window of 1800 × 1008 at a render scale of 1 leaks the same on main and on the fix's branch (right jamb 0.0977). The back-face line found that the jamb light is one sawtooth curve of the render size's aspect error, (width / height) / (16 / 9) − 1, with teeth near −0.34 %, +0.45 % and +1.25 %.
- Reading: the wobble fix hides the leak at a 16:9 output and does not cure it. The cause is not measured. The back-face line's candidate is the texel grid of the sun's shadow cascades. Bug `t58-i3sn` holds it.

The line orchestrator's pictures of the jambs (commit `0abdcabd7` on the line, not copied here): `pics/line-jamb-sheet-switch-dynamic-16ms.png`, `pics/line-jamb-walk-fixed-mac.png` and `pics/line-frame-switch-dynamic-16ms-off60.png`.

## Other lines

- `tc-6v3d`: the left edge of the sun's patch on the bay's left wall is saw-toothed in the films and smooth in the stills. The teeth are 4.4 px deep (5th to 95th percentile, about 3 render pixels) and 18 to 19 output rows apart. In the stills the edge varies by 1.5 px (`pics/film-bay-sunbeam-slit.png`). Whether the teeth crawl is not known, because the survey keeps only the start, middle and end frames of a film.
- r12 in the walk: it repeats every 18 draws (the jitter's cycle at 2/3), the same in the control. The sunlit strip in it was not checked on a picture.
- X0's walk: 54 draws against X's 48 (see "The walk and the climb").

## The glow-stays switch

The switch takes before `68239c0af` kept a switched lamp's panel glow on (the "glow stays" case). These are:

- the survey's switch cabin on M, X and X0 (runs `w-cab-*`), and the dynamic-scale survey runs on the Mac (runs `d16-*`, `d8-X`, `dfix-X`);
- the cloud's switch videos at 16.67 and 10 ms and their region and settle numbers;
- the first boil measure (`pics/boil-switch.png`), which the residual measure replaces.

Which results depend on it: the whole image's settle after off (M 79 with the glow on, 123 with it off, X 43 and 67). The slowest region's settle is the same in both cases (143 / 92 on M, 77 / 51 on X). The combined check found the jambs and the floor equal bit for bit between the two cases.

## What holds at a fixed size, under a moving size, and what was not measured

At a fixed render size (2/3, the survey's): every table above except the dynamic-scale table. X settles the cabin switch in about half of main's draws, its stills and films carry no fault that main lacks, and its walk trail fades more slowly.

Under a moving size (the window's default): the region test rarely runs, so X's switch settle falls back to about main's: worse after off and better after on, on the card at 16.67 ms. The walk's scale does not move on either machine, so the walk behaves as at a fixed size of about 0.96 on the card and 0.65 on the Mac. The cost block after a switch lowers X's scale after each switch. The wobble moves the picture by up to 20 px on both sides.

Not measured:

- T18, the Linux check: no Linux GPU tests and no Linux switch on today's code. The cloud money rule of this ticket allows no more jobs on the present code.
- Point 10: the named tests in Checked and Release.
- Point 8: the switches split by signal.
- The stand film on X (cited on X0).
- The per-pass split of the cost block. MoltenVK times only sums of neighbouring passes, and a split needs a rebuild of M and X.
- The share of pixels that trace their own rays: no counter or readback exists.
- The sunlit strip in r12, the crawl of the sun patch's teeth, and the motion of single specks in the videos.

## Cloud money

$1.5700 of the ticket's $2.00, all through `tools/runpod/gpu_job.py`: the dynamic-scale videos, their sizes and region jobs, and one failed first attempt ($0.2784).

## Files

- `tables.txt`: every measure's output. `tools/measure.sh` prints it from the runs under `/private/tmp/t0-5tyj`.
- `scratch-dynamic-scale.patch`, `scratch-region-log.patch`, `scratch-render-scale-t0-gbgj.patch`, `scratch-light-evidence-cap.patch`, `scratch-plan-rate-at-fast-cap.patch`: scratch patches, never committed to `src`.
- `probe-main-2ffd7e66f-glow.patch`: main's probe patch with the lamp switch that turns the glow off.
- `tools/`: every script of the takes. Each starts with a comment that says what it does.
