---
id: t0-xlcu
status: in_progress
deps: []
links: [t0-acxm]
created: 2026-11-15T08:40:13Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Measure which measured spread gives the regional lag test its tolerance

An experiment with the scratch probe, for the remedy of the bay film's trail (t0-acxm). It changes nothing in src. Alexis's decision Y4 (docs/plans/bounce-light-confidence.md, section 2.3) asks for a tolerance with a reason, from a measured quantity, in place of the probe's tuned 10 %.


## Design (line orchestrator, 2026-11-15)

### Where this stands

Read two records first. `docs/research/t0-mzhr-regional-test-cycle/README.md` defines the terms and the measuring scripts. `docs/research/t0-y46f-window-18-excess/README.md` corrects it: the scratch patch of `t0-mzhr` never wrote the bounce light's 18-draw window, so its takes of "region, window 18" measured a bounce history held at 4 draws.

Build on the corrected patch, `docs/research/t0-y46f-window-18-excess/scratch-probe-on-73f4a6548.patch`, and set `TERTIUS_X_WINDOW_ALL=1` in every take of the regional test. That patch also holds a count of history lengths (`TERTIUS_X_LENGTHS`) and a cap of the bounce history's length (`TERTIUS_X_DIFFUSE_CAP`). Measure light in scene light with `scene.py` and `scenelight.py` of that folder, and give `light.py`'s shares beside it where an earlier number uses them.

What is known of the corrected test at a tolerance of 10 %, a tuned constant: only stills. The lamp hall gives 96.4 / 101.4 % under today's jitter and 97.8 / 101.9 % without it. But the still state keeps firing: on the lamp hall's walls, 25 % of the bounce pixel draws have a history below 8 draws, and the mean length is 22 to 23 draws in place of 31. No valid take of a film or a switch exists. So the takes of rule T0 below are the first valid takes of the regional test with the 18-draw window. They decide whether the test closes the bay film's trail at all.

Section 8.1 of `docs/plans/bounce-light-confidence.md` holds a warning from an earlier test. A tolerance that followed the noise of single ray streams silenced that test in the cabin, where the noise is large. So this ticket measures before the remedy is built: how large is each candidate tolerance in each scene, how often does a still exceed it, and does the test still act after a real change.

### The quantities, at one pixel in one draw

- A tap = one of the 17 × 17 pixels that lie 8 × (bx, by) render pixels from the pixel, for bx and by from -8 to 8. The test reads one tap per ray block.
- `x(q, t)` = the luminance of the sample that tap `q` took in draw `t`. The probe's ring `regionWindow` holds it for the last 18 draws.
- `F(t)` = the sum of `x(q, t)` over the taps: the region's light in the single draw `t`.
- `R` = the mean of `F(t)` over the last 18 draws. This is the probe's fast sum.
- `d` = the standard deviation of `F(t)` over the same 18 draws. It is the measured spread of the region's light from draw to draw. It holds the rays' noise and the jitter's cycle, as far as each reaches the whole region.
- `S` = the sum of the slow history's luminance over the taps. This is the probe's slow sum.
- `n` = the mean of the slow history's length over the taps, in draws. The slow history's alpha holds each length, and the probe already loads that texel.
- `lag` = |S − R| / R.
- `sr(q)` = the retained deviation of tap `q`: the standard deviation of that pixel's own luminance samples over its slow history's window. The diffuse fast history's alpha holds it (section 6 of the design doc).

### The candidate rules

The test fires where `lag` is larger than the tolerance. The anti-lag then shortens the history by the excess, as in the probe.

| Rule | Tolerance | The reason for its value |
|---|---|---|
| T0, the control | 0.10 | None. The probe's tuned constant |
| T1, the region's measured spread | 2 × (d / R) × sqrt(1/18 + 1/(2n − 1)) | `R` is a mean of 18 draws, so its variance is d²/18. `S` is a mean with the weight 1/n, so its variance is d²/(2n − 1). The tolerance is two standard deviations of their difference, the same two that the clamp's box uses (`ClampSigmas`, `RetainedSigmas`). The two means share samples, so the true spread of the difference is smaller, and the rule errs toward silence |
| T2, the stored spread | 2 × sqrt(sum over taps of sr(q)² × (1/18 + 1/(2 n(q) − 1))) / R | The same two standard deviations, from the spread that each history already stores. It needs no second ring. It takes the taps as independent, which the jitter's cycle violates, because the jitter is the same for every pixel of a draw |

For T1, a history of 32 draws gives a tolerance of 0.53 × d / R, and a history cut to 4 draws gives 0.89 × d / R. So the tolerance rises when a history is short and follows the cycle more. The probe's teeth come from exactly that state.

You can add one rule of your own if the count below points at one. Record it and its reason.

### The scratch additions

In the scratch checkout of `73f4a6548` with the corrected patch:

- A second ring per pixel for `F(t)`: the spatial dispatch sums this draw's ring layer over the taps, stores the sum in the layer of this draw, and reads the 18 layers for `R` and `d`. This costs 289 + 18 more loads per pixel, which a probe can pay.
- `TERTIUS_X_REGION_RULE=<0|1|2>` chooses T0, T1 or T2. The default 0 must repeat the control below.
- `TERTIUS_X_REGION_ACT=0` computes the test but does not shorten any history. The count below uses it.
- A count per draw, read back or written to an image that a script reduces: for the whole image and the survey's 3 × 3 regions, the median and the 90th and 99th percentile of `lag`, of T1 and of T2, and the share of pixels whose `lag` exceeds each of T0, T1 and T2.

How you store and read these is yours.

### The takes

Step 1, the count without action (`TERTIUS_X_REGION_ACT=0`, the fix's action otherwise):

| Take | Jitter | What the count shows |
|---|---|---|
| Stills `rect-lamps`, `bay-sunbeam`, `exit-cabin`, draws 100 to 300 | today's and none | A still has no change. So every pixel-draw above a tolerance is false evidence. Give each rule's share and each tolerance's size |
| Cabin switch, switch run | today's | After a real change: for the 60 draws after each switch, the share of pixels above each tolerance, per region. This is the rule's power to see the change |
| Film `rect-bay` | today's | The same over the film's frames |

Step 2, each rule acting (T0 as the control, then T1 and T2), with the scripts of the cycle measurement:

| Take | Jitter | Measures |
|---|---|---|
| Lamp hall still at 4.08 stops | today's and none | Light of ceiling and walls as % of the reference. Specks, blotch and daub at the automatic exposure |
| Film `rect-bay` | today's | Level and blotch, middle and last frame |
| Cabin switch | today's and none | For each switch: the level 60 draws after it as % of the reference's control (whole image and r11), and the draws until the slowest region, the whole image and r11 stay within 5 % of their own control. Also the lowest and highest level of r11 over frames 140 to 159, the still state before the switch |
| Lag film `bay-sun` | today's | Median lag and lag error |

The reference and the fix under each jitter are part of the set, because the build is new.

Two more variants belong to step 2:

- The fix with `TERTIUS_X_DIFFUSE_CAP=4` and no regional test: the bay film and the cabin switch under today's jitter. It must repeat the `t0-mzhr` numbers of the broken window (bay film +0.43 / +0.18, cabin settle 39 / 25). That confirms what those numbers measured, and it gives the films' result of a bounce history without a long memory.
- Rule T0, and the best derived rule, under the long Halton sequence (`TERTIUS_X_LONG_JITTER=1`): the lamp hall still and the cabin switch. The `t0-mzhr` takes of the 18-draw window under the long sequence are not valid, and the order of the long Halton jitter (`t0-f238`) depends on this result.

### The three questions

1. In a still, how large is each derived tolerance against 10 %, and how often does the lag exceed it? Give the count per scene and jitter.
2. With a derived tolerance acting, does the still keep the fix's light (96.0 and 99.5 % in the lamp hall, decision Y5) and its specks, and does the cabin's center stop swinging before the switch?
3. Does the corrected test close the bay film's trail, at 10 % and with a derived tolerance? Give the bay film, the cabin's two measures and the bay-sun lag for T0, T1 and T2 against the fix and against main (`t0-mzhr`'s table: main's bay film +1.18 / +0.53, the fix's +1.86 / +1.75).

If no derived rule keeps the films' gain, or each silences the test in the cabin, say so plainly with the numbers. That result is a question for Alexis, and the line orchestrator passes it up.

## Acceptance

- The control: rule T0 with action and the corrected window repeats the `t0-y46f` record's lamp hall still by `light.py`, 96.4 / 101.4 under today's jitter and 97.8 / 101.9 without jitter, with a mean bounce length near 28.6 / 22.8 draws without jitter. If it does not, stop and report that first.
- Tables of the count and of every take in a note on this ticket and in `docs/research/t0-xlcu-regional-tolerance/README.md`, with the three answers. Each answer gives the measured numbers apart from your reading.
- One chart of r11 over the cabin switch under today's jitter with the reference, the fix, T0, T1 and T2, in the style of `levelchart.py`. One crop sheet of the lamp hall's ceiling: the fix, T0, T1 and T2. Open each with the Read tool and describe it.
- The scratch patch and the scripts in the same folder. Reuse the scripts of `docs/research/t0-mzhr-regional-test-cycle/tools/` and copy only what you change.
- No change under `src`, `tests` or `tools`.

## Out of scope

- The remedy's build. Its ticket follows this result.
- Statistics at ray-block resolution. The remedy's design decides that. This ticket keeps the probe's taps, so that its numbers compare with the cycle measurement.
- Why a still's bounce light depends on the history's length (`t8-69v7`, its own diagnosis). Give each variant's mean bounce length beside its light, so that a reader can tell this effect from the test's own.
- A build of the long Halton jitter (`t0-f238`) and the 128-draw history (`t0-70v1`).

## Notes

**2026-11-15T09:36:31Z**

Step 1, the count without action (ticket lead, 2026-11-15).

Build: the corrected patch of t0-y46f on 73f4a6548, plus this ticket's switches (docs/research/t0-xlcu-regional-tolerance, README follows with step 2). TERTIUS_X_REGION_RULE=0|1|2|3 chooses T0, T1, T2 or T3. The derived rules act on the bounce light only. The reflections keep T0, because their fast history's alpha holds the hit distance, not a retained deviation. TERTIUS_X_REGION_ACT=0 computes the test and does not shorten. TERTIUS_X_REGION_COUNT=<folder> writes the lag, T1, T2 and T3 of every second render pixel of every second row in each counted draw.

Control: rule T0 with action, lamp hall still at 4.08 stops, light.py: 96.4 / 101.4 under today's jitter, 97.8 / 101.9 without jitter. Mean bounce length 27.09 / 21.97 and 28.63 / 22.81 draws. All equal the t0-y46f record. The second build (with T3) repeats both stills byte for byte. With ACT=0 the stills equal the fix's: specks 6.01 and 3.25, as in t0-mzhr. Rule 0 through the new path (rule 0 with the count on) gives 96.4 / 101.5. Its images differ in the last bits, mean 0.35 codes in the shown still, because the code moved.

T3 is my own rule: T1 with the spread of the successive differences of F (von Neumann), sqrt(sum (F(t) − F(t−1))^2 / (2 (N − 1))). Reason: T1's share fell after the cabin's lamps went off. A step in F raises the standard deviation of the window, but it adds only one term to the successive differences. Result: T3's median stays within 0.02 of T1's in every window of every take. In r11 after the lamps go off, 22.6 % exceed T3 against 6.3 % for T1. But that is still below T3's own 25.9 % before the switch. So T3 shows no more power than T1, and it gets no step 2 takes.

Bounce light, whole image, surface pixel draws. Tolerance percentiles 50 / 90 / 99, and the share of the pixel draws whose lag exceeds each tolerance. Stills: run draws 100 to 300, every 7th (29 draws), the fix's action, automatic exposure.

| Take | Jitter | lag p50/p90/p99 | T1 p50/p90 | T2 p50/p90 | over T0 | over T1 | over T2 | over T3 |
|---|---|---|---|---|---:|---:|---:|---:|
| rect-lamps | today's | 0.046/0.180/0.545 | 0.135/0.599 | 0.097/0.443 | 20.7 % | 6.4 % | 16.2 % | 7.1 % |
| rect-lamps | none | 0.041/0.169/0.630 | 0.119/0.482 | 0.098/0.489 | 19.1 % | 8.4 % | 14.4 % | 9.0 % |
| bay-sunbeam | today's | 0.029/0.066/0.108 | 0.079/0.121 | 0.080/0.112 | 1.5 % | 5.6 % | 5.3 % | 6.3 % |
| bay-sunbeam | none | 0.029/0.066/0.108 | 0.079/0.121 | 0.080/0.112 | 1.6 % | 5.6 % | 5.4 % | 6.4 % |
| exit-cabin | today's | 0.027/0.065/0.107 | 0.076/0.116 | 0.076/0.112 | 1.5 % | 5.6 % | 5.6 % | 6.5 % |
| exit-cabin | none | 0.027/0.064/0.103 | 0.075/0.116 | 0.076/0.112 | 1.2 % | 5.3 % | 5.2 % | 6.2 % |
| rect-lamps, filter off (diagnostic) | today's | 0.044/0.149/0.421 | 0.189/0.577 | 0.221/0.577 | 19.8 % | 3.1 % | 0.2 % | 3.5 % |

Cabin switch, today's jitter, only the switch run, every draw of film frames 140 to 419. d = film frame minus the switch's frame (160 lamps off, 360 lamps on). Shares over T0 / T1 / T2 / T3:

| Window | Whole image | r11 |
|---|---|---|
| before, frames 140 to 159 | 20.6 / 5.9 / 15.7 / 6.7 % | 25.9 / 21.6 / 32.6 / 25.9 % |
| off, d 0 to 5 | 20.3 / 4.7 / 11.0 / 5.5 % | 25.7 / 20.0 / 27.4 / 22.4 % |
| off, d 6 to 17 | 32.9 / 1.9 / 27.0 / 4.6 % | 46.4 / 6.3 / 56.0 / 22.6 % |
| off, d 18 to 35 | 24.0 / 5.8 / 12.7 / 7.0 % | 29.3 / 4.5 / 22.9 / 11.2 % |
| on, d 0 to 5 | 23.7 / 7.2 / 6.6 / 8.1 % | 29.2 / 24.3 / 12.6 / 25.6 % |
| on, d 6 to 17 | 26.0 / 4.1 / 8.7 / 5.9 % | 31.7 / 8.1 / 24.6 / 12.3 % |

The T1 median is 0.136 before the switch. It rises to 0.198 at off d 6 to 17 and to 0.190 at on d 6 to 17. The lag median rises from 0.045 to 0.070 and 0.051.

Film rect-bay, today's jitter, its 180 frames: lag 0.213 / 0.428 / 0.526, T1 median 0.084, T2 median 0.100. Over T0 / T1 / T2 / T3: 70.9 / 71.5 / 72.3 / 73.4 %. r11: 75.1 / 75.2 / 76.2 / 76.6 %.

Reflections (they keep T0): the share over T0 in the stills is 9.4 to 71.0 % of the whole image. The rect-lamps still gives 71.0 % under today's jitter and 47.8 % without.

My reading:
- In the bay and the exit cabin, T0 at 10 % is wider than every derived rule. Their medians are 0.075 to 0.080. They let 5 to 6 % of a still's pixel draws through, and T0 lets 1.2 to 1.6 % through. In the lamp hall and the cabin, the derived rules are wider than 10 %: T1's median is 0.12 to 0.14 with a p90 of 0.48 to 0.62. There they cut the still's false share from about 20 % to 6 to 8 % (T1) or 14 to 16 % (T2).
- Over the whole image, about 5 to 8 % of a still's pixel draws exceed T1 in every scene. Two standard deviations of a normal spread give 4.6 %, so the tails are a little heavier than normal. Per region it depends on the scene: over the lamp hall's 3 × 3 regions, 0.8 to 29.4 % exceed T1. In its r11 without jitter, 29.4 % exceed T1 against 7.6 % for T0. (Corrected after review, finding F1.)
- With the diffuse filter off, the still's lag stays (19.8 % over T0), so the filter's write-back does not cause it. T2 then falls to 0.2 %. So the filter narrows the retained deviation that T2 reads, and T2's false share in the lamp hall comes from that.
- Power after a real change: T1 has none in the cabin. In the first 6 draws after the lamps come on, its share rises a little (7.2 % against 5.9 %). From 6 draws after each switch, it falls below its still level. T1 rises more than the lag there. T3 rises the same way. So the spread of F from draw to draw rises after a switch, not only the step of the change. (Corrected after review, finding F3.) I did not measure why. T2 rises after the lamps go off (r11 56.0 % against 32.6 %) but falls after they come on (12.6 %). T0 rises after both, but only from 20.6 to 32.9 % over the whole image. In the bay film, all rules see the change at the same share, 71 to 73 %. The trail's lag (median 0.21) is about twice every tolerance there.
- No rule fails by its count alone in the sense of the brief: every rule sees the bay film. So step 2 takes T0, T1 and T2 in full. T3 gets none, as said above.

**2026-11-15T10:35:56Z**

Step 2 and the answers (ticket lead, 2026-11-15). docs/research/t0-xlcu-regional-tolerance/README.md holds every table, the code and the pictures. These are the first valid takes of the regional test with the 18-draw window in a film and in a switch.

Lamp hall still at 4.08 stops, light as % of the reference, ceiling / walls, and the mean bounce length on the walls:

| Variant | Today's jitter | Walls length | No jitter | Long sequence |
|---|---|---|---|---|
| fix | 96.0 / 99.5 | 31.26 | 96.9 / 99.9 | 96.9 / 99.4 |
| T0 | 96.4 / 101.4 | 21.97 | 97.8 / 101.9 | 96.7 / 99.1 |
| T1 | 96.6 / 100.7 | 29.26 | 97.4 / 101.0 | 101.2 / 108.1 |
| T2 | 99.4 / 105.1 | 29.27 | 97.3 / 100.3 | not taken |

Specks, today's jitter: the fix 6.01, T0 9.36, T1 7.34, T2 7.43.

Cabin switch, today's jitter: settle slowest off / on, level 60 after the lamps return in r11, and r11's range over frames 140 to 159.

| Variant | Settle | Level 60 on, r11 | Still range r11 |
|---|---|---|---|
| main (t0-mzhr) | 75 / 52 | 73.4 % | not computed |
| fix | 66 / none | 84.5 % | 98.7 to 100.0 % |
| T0 | 54 / 56 | 85.7 % | 100.7 to 117.8 % |
| T1 | 63 / 94 | 83.3 % | 101.7 to 102.4 % |
| T2 | 50 / 73 | 87.4 % | 110.5 to 113.6 % |
| cap 4 | 46 / 23 | 91.4 % | 94.0 to 128.1 % |

Long sequence: T0 84 / 72 with r11 95.0 to 102.2 %, T1 94 / 81 with r11 105.4 to 116.8 %.

Film rect-bay, level middle / last: main +1.18 / +0.53, the fix +1.86 / +1.75. T0 +1.36 / +0.64, T1 +1.41 / +0.60, T2 +1.43 / +0.62, cap 4 +0.43 / +0.18. bay-sun lag error: main 0.2644, the fix 0.2679, T0 0.2230, T1 0.2303, T2 0.2173, cap 4 0.0892.

Cap 4 repeats t0-mzhr's broken "region, window 18" in the bay film to every printed digit (+0.43 / +0.18, blotch 0.102 / 0.101). In the cabin, the whole image and r11 settle as there (20 / 15 and 20 / 18). The slowest region differs: 46 / 23 against 39 / 25. The review's fix took cap 4 with T0's test acting, so that the test acts only on the reflections, as in the broken build. It repeats t0-mzhr's cabin to every printed digit: 39 / 25. So the reflections' test causes the difference (finding F2).

Answers:
1. In the bay and the exit cabin, T1 and T2 have medians of 0.075 to 0.080. In the lamp hall, T1's median is 0.12 to 0.14 and T2's 0.10. Over the whole image, T1 lets 5 to 8 % of a still's pixel draws through in every scene and jitter. Per region, 0.8 to 29.4 % in the lamp hall. T2 lets 5 % through in the bay and the exit cabin and 14 to 16 % in the lamp hall. T0 lets 1 to 2 % through in the bay and the exit cabin and 19 to 21 % in the lamp hall.
2. Under today's jitter, T1 keeps the still's light within 1.2 points of the fix, and it stops the cabin center's swing before the switch. T2 holds the walls 5.1 points and the cabin center 10 to 14 % above the reference. Under the long sequence T1 loses both: 101.2 / 108.1, and r11 swings 105.4 to 116.8 %.
3. No. The corrected test does not close the bay film's trail at 10 % or with a derived tolerance. It brings the film from the fix's trail back to about main's, and the rules differ by at most 0.07 in level. The earlier film gain of "region, window 18" came from the bounce history held at 4 draws. In the cabin, T1 sees no light change: it settles the lamps-on window in 94 draws, main in 52. T2 sees the lamps go off but not come on. This is the question for Alexis: no rule of the test keeps the films' gain, and the best rule in the stills silences the test in the cabin.
