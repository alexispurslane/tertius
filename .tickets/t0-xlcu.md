---
id: t0-xlcu
status: open
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

Read `docs/research/t0-mzhr-regional-test-cycle/README.md` first. It defines the terms, and its scratch patch holds the regional lag test with the box window of 18 draws ("region, window 18"). That probe uses a tolerance of 10 % of the fast sum, a tuned constant. It works in the films and settles the cabin fastest. But in a still state it keeps firing: the cabin's center region swings from 94 to 127 % of the reference before the switch, and the lamp hall's specks rise from 6.01 to 9.69.

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

In the scratch checkout of `73f4a6548` with the patch of the cycle measurement:

- A second ring per pixel for `F(t)`: the spatial dispatch sums this draw's ring layer over the taps, stores the sum in the layer of this draw, and reads the 18 layers for `R` and `d`. This costs 289 + 18 more loads per pixel, which a probe can pay.
- `TERTIUS_X_REGION_RULE=<0|1|2>` chooses T0, T1 or T2. The default 0 must repeat the cycle measurement.
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

### The three questions

1. In a still, how large is each derived tolerance against 10 %, and how often does the lag exceed it? Give the count per scene and jitter.
2. With a derived tolerance acting, does the still keep the fix's light (96.0 and 99.5 % in the lamp hall, decision Y5) and its specks, and does the cabin's center stop swinging before the switch?
3. Does a derived tolerance still see a real change? Give the bay film, the cabin's two measures and the bay-sun lag against T0 (bay film +0.43 / +0.18, cabin settle 39 / 25, bay-sun 8.41 draws) and against main.

If no derived rule keeps the films' gain, or each silences the test in the cabin, say so plainly with the numbers. That result is a question for Alexis, and the line orchestrator passes it up.

## Acceptance

- The control: rule T0 with action repeats the cycle measurement's printed numbers (lamp hall still 99.5 / 105.8, bay film +0.43 / +0.18, cabin settle 39 / 25). If it does not, stop and report that first.
- Tables of the count and of every take in a note on this ticket and in `docs/research/t0-xlcu-regional-tolerance/README.md`, with the three answers. Each answer gives the measured numbers apart from your reading.
- One chart of r11 over the cabin switch under today's jitter with the reference, the fix, T0, T1 and T2, in the style of `levelchart.py`. One crop sheet of the lamp hall's ceiling: the fix, T0, T1 and T2. Open each with the Read tool and describe it.
- The scratch patch and the scripts in the same folder. Reuse the scripts of `docs/research/t0-mzhr-regional-test-cycle/tools/` and copy only what you change.
- No change under `src`, `tests` or `tools`.

## Out of scope

- The remedy's build. Its ticket follows this result.
- Statistics at ray-block resolution. The remedy's design decides that. This ticket keeps the probe's taps, so that its numbers compare with the cycle measurement.
- The cause of the 18-draw window's light above the reference (`t0-y46f`), the long Halton jitter (`t0-f238`) and the 128-draw history (`t0-70v1`).
