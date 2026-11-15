---
id: t0-mzhr
status: in_progress
deps: []
links: []
created: 2026-11-15T01:28:52Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Measure the regional lag test against the jitter's cycle of 18 draws, and a history cut to 18 draws

An experiment with the scratch probe, for the remedy of the bay film's trail (t0-acxm). It changes nothing in src. The parent orchestrator asked for it on 2026-11-14, before Alexis's choice between the regional lag test and ray evidence that reaches further.

## Design (line orchestrator, 2026-11-14)

### Why

The first probe of the regional lag test (t0-acxm, column "region 8") trailed less than main in the bay film, but the lamp hall's still lost half of the fix's gain in light. My reading was that the camera jitter's cycle of 18 draws fools the test, and that the long Halton jitter (t0-f238) cures it. The parent orchestrator doubts the cure, with this reason: the jitter is the same for every pixel in a draw, so a sub-pixel sill strip lights all of its blocks in the same draws, and a mean over a region does not average that out. A long sequence removes the period but not the fluctuation, and then no short window cancels it. A fast window of exactly 18 draws holds one full cycle of today's jitter, so the periodic part cancels exactly. Nobody measured either reading. This ticket measures them.

### Terms

- The fix: the branch as it is, where light evidence cuts a history to 4 draws and leaves its box wide. In the scratch probe it is `TERTIUS_X_LIGHT_ACTION=cut`.
- The reference: the same build with both history clamps off (`TERTIUS_A_NO_CLAMP=1`), taken with the same jitter as the variant that it judges.
- The regional lag test: at a pixel, the mean M_slow of the slow history's luminance and the mean M_fast of a fast estimate, over one tap per ray block within R blocks. Where |M_slow - M_fast| / M_fast exceeds a tolerance, the anti-lag shortens the history by the excess. The first probe used R = 8, a tolerance of 10 % and the 4-draw fast histories as the fast estimate.
- A ray block: the 8 x 8 render pixels that share one ray per draw.

### The scratch probe

`docs/research/design-x-acceptance-2026-11-14/scratch-probe-on-73f4a6548.patch` applies to commit `73f4a6548` and holds every switch so far (the folder's README lists them). Build it in a scratch checkout under `/private/tmp`, never in a tracked branch, and remove that checkout at the end. Add these switches there:

| Switch | Meaning |
|---|---|
| `TERTIUS_X_REGION_WINDOW=<N>` | The fast estimate of the regional lag test. 4, the default, is the fast histories as now. Another N is the mean of a ray block's samples over exactly the last N draws, a box window, not a running mean that only fades. The ticket needs N = 18. |
| `TERTIUS_X_CUT_LENGTH=<N>` | The length to which light evidence cuts a history. The default is 4, the fast cap. The ticket needs N = 18. |
| a long jitter sequence | Only if it is cheap in scratch: one Halton sequence with a period of 2^10 x 3^6 draws whose first 18 points are AMD's, while FSR keeps its phase count (docs/plans/sub-pixel-strips.md, sections 5.1 and 17, step 2). `TERTIUS_DEBUG_JITTER=none` already turns the jitter off. |

The box window needs the samples of the last N draws. A cheap scratch store is a ring of N layers at ray-block resolution (160 x 90 texels at the survey's render size) and their sum. How you store it is yours. The measured quantity is fixed: an exact mean over the last 18 draws.

The save: the updated patch replaces the old file in a new folder (below), with the README's switch table.

### Variants and takes

Three jitters: today's (18 phases), none, and the long sequence if it was cheap. For each jitter:

| Variant | Switches |
|---|---|
| reference | `TERTIUS_A_NO_CLAMP=1` |
| fix | `TERTIUS_X_LIGHT_ACTION=cut` |
| region, window 4 | fix + `TERTIUS_X_REGION=8 TERTIUS_X_REGION_TOL=10` |
| region, window 18 | region + `TERTIUS_X_REGION_WINDOW=18` |

Under today's jitter also: main (its own probe build, `tools/renderer-survey.py probe-build` in a scratch checkout of `ab54c785e`), and "cut 18" (fix + `TERTIUS_X_CUT_LENGTH=18`).

Takes, all with the survey tool (`tools/renderer-survey.py run <out> --parts ... --only ... --no-build --chunk 0`, tools/README.md, "The renderer survey"):

| Take | Survey part and case | Measures |
|---|---|---|
| Lamp hall still, light | `still`, `rect-lamps`, `TERTIUS_PROBE_EXPOSURE=4.08` | The long mean of the ceiling and of the walls as % of the reference (tools `light.py`). |
| Lamp hall still, noise | `still`, `rect-lamps`, automatic exposure | Specks, blotch, and the daub against the reference (`daub.py` with `REF`). |
| Bay film | `motion`, `rect-bay` | Level and blotch at the middle and at the last frame. Not needed for the reference. |
| Cabin switch | `switch`, `cabin` | Both measures below, for the whole image and the center region r11, and the slowest region's draws. |
| Lag films | `lag`, all three cases | Median lag in draws and median lag error. Only under today's jitter, for main, the fix, cut 18 and region window 18. |

The two measures of a switch take, for a region and for each switch (lamps off at film frame 160, lamps on at 360):

1. Settle: the draws until the region stays within 5 % of its own build's control (the survey's `offDraws`, `onDraws`, `offDrawsWhole`, `onDrawsWhole`).
2. Level at 60 draws: the region's light in the switch run 60 draws after the switch, divided by the reference's control with the lamps in the final state at the same frame. All series are at the probe's fixed exposure, so they compare across builds. Alexis reads this one first.

Also give, for the center region, the lowest level against the reference in the 60 draws after each switch. That number is the dip.

The measuring scripts of the first takes are in `docs/research/design-x-acceptance-2026-11-14/tools/` (tool code of a research take; `take.sh` and `common.sh` hold paths of that session, so adapt them). `ratio.py` prints a switch run against its own control. The level against the reference is new: write it.

Do the jitters in this order, so that an early stop still answers the first two questions: today's, none, long.

### The questions

1. Does the 18-draw window give back the lamp hall still's gain under today's jitter (the fix: 96.0 and 99.5 % of the reference, the first probe: 89.4 and 90.4 %), and its specks (the fix 6.01, the first probe 9.61 per 10,000 pixels)? Does it keep the bay film's gain (first probe: level +0.89 and +0.43, blotch 0.218 and 0.211, against main's +1.18, +0.53, 0.245, 0.231) and the cabin's lamps-on settle (51 draws)?
2. Does the long sequence help the regional test or hurt it, with the window at 4 and at 18? Does the jitter off remove the still's loss, which tells whether the jitter causes the loss at all?
3. Does a cut to 18 draws in place of 4 remove the cabin center's dip after a cut (the fix: 16 % below its own control 15 draws after the lamps return)? What does it cost in the lag films and in the bay film?

### Acceptance

- One table per take with every variant that ran, in a note of this ticket and in `docs/research/<this ticket's id>-regional-test-cycle/README.md`, with the updated scratch patch and the new scripts beside it.
- An answer to each question, with the measured facts apart from your reading of them, and what you did not take.
- Crops beside the reference for the lamp hall still of the fix, region window 4 and region window 18 under today's jitter, each opened and described (CLAUDE.md, "Testing", and the memory note `blind-test-image-metrics.md`).
- Each comparison on one machine in one session. A cloud job holds every variant that it compares.
- No change under `src`, `tests` or `tools` in the ticket's branch. A bug that you find gets a bug ticket.

Out of scope: a design of the remedy, the choice between the regional test and wider ray evidence (Alexis decides), a tolerance sweep (one more tolerance is welcome only if time is left), and the frame time (t0-89zi).

## Notes

**2026-11-15T03:09:44Z**

2026-11-14, the takes of this ticket. All ran on this Mac in one session (20:37 to 22:04 local time), under the shared GPU lock. No cloud job ran ($0). The scratch probe is docs/research/t0-mzhr-regional-test-cycle/scratch-probe-on-73f4a6548.patch. It adds three switches: TERTIUS_X_REGION_WINDOW (an exact box mean of the tap pixel's last N samples, per pixel, in place of the fast history), TERTIUS_X_CUT_LENGTH, and TERTIUS_X_LONG_JITTER (period 2^10 x 3^6). The README there holds the code, the terms, the crops, the charts and every table below with more columns.

Control runs with every new switch unset repeat the first session to every number: region 8's cabin switch (13,818 values), its still (both PNGs equal byte for byte), its bay film, the fix's cabin switch and main's cabin switch.

Variants: "window 4" and "window 18" are the regional lag test (radius 8 blocks, tolerance 10 %) with the fast history or the 18-draw box mean as the fast estimate. "Cut 18" is the fix with light evidence cutting to 18 draws. Jitters: today's (18 phases), none, long (the long Halton sequence). A dash means not taken.

Lamp hall still at 4.08 stops, long mean of ceiling / walls, % of the same jitter's reference:

| Variant | today's | none | long |
|---|---|---|---|
| main | 78.4 / 81.2 | - | - |
| fix | 96.0 / 99.5 | 96.9 / 99.9 | 96.9 / 99.4 |
| cut 18 | 96.0 / 99.5 | - | - |
| window 4 | 89.4 / 90.4 | 97.7 / 102.1 | 93.0 / 93.1 |
| window 18 | 99.5 / 105.8 | 100.8 / 106.5 | 99.0 / 102.5 |
| window 18, tolerance 20 | 99.3 / 105.6 | - | - |

Lamp hall still at the automatic exposure: specks per 10,000 px / blotch / daub against the same jitter's reference:

| Variant | today's | none | long |
|---|---|---|---|
| reference | 12.16 / 0.1064 / 0.0416 | 6.95 / 0.0536 / 0.0322 | 11.06 / 0.0712 / 0.0378 |
| main | 7.05 / 0.0530 / 0.0432 | - | - |
| fix | 6.01 / 0.0933 / 0.0403 | 3.25 / 0.0553 / 0.0322 | 5.50 / 0.0658 / 0.0380 |
| cut 18 | 6.01 / 0.0933 / 0.0403 | - | - |
| window 4 | 9.61 / 0.0581 / 0.0396 | 7.43 / 0.0491 / 0.0317 | 7.39 / 0.0535 / 0.0372 |
| window 18 | 9.69 / 0.0628 / 0.0448 | 7.51 / 0.0396 / 0.0356 | 8.91 / 0.1152 / 0.0414 |
| window 18, tolerance 20 | 8.63 / 0.0621 / 0.0460 | - | - |

Bay film rect-bay, level and blotch at the middle / last frame:

| Variant | today's | none | long |
|---|---|---|---|
| main | +1.18 / +0.53, 0.245 / 0.231 | - | - |
| fix | +1.86 / +1.75, 0.370 / 0.488 | +2.01 / +1.97, 0.372 / 0.502 | +1.84 / +1.72, 0.369 / 0.486 |
| cut 18 | +2.16 / +2.69, 0.391 / 0.484 | - | - |
| window 4 | +0.89 / +0.43, 0.218 / 0.211 | +0.92 / +0.43, 0.223 / 0.212 | +0.89 / +0.42, 0.218 / 0.212 |
| window 18 | +0.43 / +0.18, 0.102 / 0.101 | +0.43 / +0.18, 0.105 / 0.101 | +0.43 / +0.17, 0.100 / 0.101 |
| window 18, tolerance 20 | +0.43 / +0.18, 0.101 / 0.101 | - | - |

Cabin switch, lamps off at film frame 160 and on at 360. Settle = draws until the region stays within 5 % of its own build's control, off / on, "none" = not within the window. Level 60 = the light 60 draws after the switch as % of the reference's control in the new state, whole image / r11 (the center region). Dip on = r11's lowest level in the 60 draws after the lamps return. Own 15 = r11 15 draws after the lamps return against its own control.

| Jitter | Variant | Settle slowest | Settle whole | Settle r11 | Level 60 off | Level 60 on | Dip on r11 | Own 15 |
|---|---|---|---|---|---|---|---|---|
| today's | reference | none / 97 | 74 / 39 | 63 / 37 | 105.7 / 105.0 | 96.1 / 95.8 | 94.3 | -5.7 % |
| today's | main | 75 / 52 | 36 / 24 | 20 / 49 | 89.2 / 92.2 | 81.6 / 73.4 | 68.0 | -8.3 % |
| today's | fix | 66 / none | 12 / 27 | 56 / none | 96.0 / 95.2 | 90.4 / 84.5 | 77.6 | -16.3 % |
| today's | cut 18 | 96 / 85 | 51 / 40 | 27 / 72 | 99.3 / 102.4 | 90.0 / 87.8 | 85.6 | -8.1 % |
| today's | window 4 | 113 / 51 | 14 / 19 | 113 / 51 | 95.9 / 92.0 | 90.0 / 81.1 | 72.6 | -10.9 % |
| today's | window 18 | 39 / 25 | 20 / 15 | 20 / 18 | 104.5 / 120.7 | 93.5 / 91.5 | 76.2 | -6.5 % |
| today's | window 18, tolerance 20 | 40 / 24 | 20 / 15 | 20 / 18 | 104.4 / 120.7 | 93.6 / 91.5 | 76.2 | -6.5 % |
| none | reference | none / none | 78 / 34 | 186 / 91 | 106.0 / 112.7 | 96.4 / 93.5 | 90.3 | -9.3 % |
| none | fix | 122 / 27 | 44 / 9 | 122 / 27 | 100.0 / 110.7 | 95.6 / 97.1 | 89.2 | -7.2 % |
| none | window 4 | 94 / 34 | 26 / 9 | 94 / 30 | 100.8 / 111.1 | 96.7 / 97.8 | 89.6 | -7.6 % |
| none | window 18 | 46 / 26 | 19 / 10 | 34 / 26 | 100.3 / 109.0 | 98.8 / 103.1 | 91.3 | -8.5 % |
| long | reference | none / none | 86 / 48 | 104 / 81 | 106.5 / 107.0 | 95.7 / 93.9 | 92.0 | -7.8 % |
| long | fix | 171 / 92 | 92 / 16 | 171 / 92 | 102.0 / 112.8 | 92.4 / 87.0 | 83.9 | -11.3 % |
| long | window 4 | 108 / 68 | 63 / 21 | 108 / 68 | 99.0 / 98.3 | 92.0 / 84.9 | 76.0 | -10.4 % |
| long | window 18 | 39 / 27 | 19 / 16 | 25 / 27 | 97.2 / 88.5 | 94.3 / 88.7 | 72.8 | -8.9 % |

Window 18's r11 swings with the 18-draw cycle under today's jitter (README chart). Its mean over the cycle around 60 draws is 108.7 % after the lamps go off and 89.4 % after they return. Before the switch, its r11 stands at 110.0 % of the reference (today's), 107.5 % (none) and 112.9 % (long). A diagnostic with both clamps off still swings: window 18's r11 is 122.8 % in one frame and 111.2 % over the cycle, 60 draws after the lamps go off.

Lag films, today's jitter, median lag in draws / median lag error:

| Variant | bay-sun | lamp-hall-16-moving | lamp-hall-64-spot-moving |
|---|---|---|---|
| main | 28.55 / 0.2644 | 4.43 / 0.0276 | -0.06 / 0.0106 |
| fix | 26.93 / 0.2679 | 4.45 / 0.0275 | -0.28 / 0.0116 |
| cut 18 | 44.01 / 0.3015 | 4.45 / 0.0269 | 0.32 / 0.0096 |
| window 18 | 8.41 / 0.0910 | 4.46 / 0.0270 | -0.34 / 0.0114 |

Answers. The measured facts are above. My reading follows.

1. The 18-draw window gives back the ceiling's light (99.5 %) and overshoots the walls by 5.8 points (105.8 %). It does not give back the specks (9.69 against the fix's 6.01). It more than keeps the bay film's gain (+0.43 / +0.18, blotch 0.102 / 0.101) and settles the cabin's lamps-on in 25 draws (window 4: 51). But the test still follows the cycle. The box removes the cycle from the fast side. The slow history, an exponential mean with the weight 1/32, keeps about 9 % of the cycle's swing, and a history cut to 4 draws keeps about 64 %. The clamp is not the cause (clamps-off take). The light above the reference also appears without jitter, so a second effect is there. I did not measure its cause.
2. With the jitter off, window 4 keeps the still's light (97.7 / 102.1 %), so the jitter causes its loss. The long sequence helps window 4's still a little (93.0 / 93.1 %) and not window 18's. It hurts the cabin's lamps-off window: regular teeth become irregular jumps, and the fix settles 105 draws later after the lamps go off (171 against 66). After the lamps return, the fix settles in 92 draws, where under today's jitter it did not settle within 100. The long sequence removes the period but not the fluctuation.
3. A cut to 18 draws halves the dip after the lamps return (r11 -8.1 % against -16.3 % at 15 draws, main's size). It costs time after a change. bay-sun's lag error rises 0.037 against main, about 7 times acceptance 9's allowance of 0.005. The bay film reaches +2.69 at its last frame, and the whole image settles 39 draws later after the lamps go off.

Not taken: main, cut 18 and the lag films under no jitter and the long sequence (not required), and a control run of the long sequence. No take shows the first 18 draws after a reset, and the code shows that they are AMD's offsets. Bug tickets from this work: t1-zj18 (main's lag film lamp-hall-16-moving does not repeat in 11 of 245 values) and t1-ve0p (an rmdir error of tools/gpu-lock.sh).
