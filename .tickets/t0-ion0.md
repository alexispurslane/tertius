---
id: t0-ion0
status: open
deps: []
links: [t0-acxm]
created: 2026-11-15T10:49:32Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Measure the region's drift as light evidence: a cut to the fast cap while the region's light moves

An experiment with the scratch probe, for the remedy of the bay film's trail (t0-acxm). It changes nothing in src. It follows the tolerance measurement (t0-xlcu), which showed that the regional lag test brings the bay film only back to about main's trail.


## Design (line orchestrator, 2026-11-15)

### What the tolerance measurement showed

Read `docs/research/t0-xlcu-regional-tolerance/README.md` first. It holds the terms, the corrected probe with its two rings, and the takes.

| Build | Bay film level, middle / last | bay-sun lag error | Cabin settle, slowest off / on | Lamp hall still, ceiling / walls |
|---|---|---|---|---|
| main | +1.18 / +0.53 | 0.2644 | 75 / 52 | 78.4 / 81.2 |
| the fix | +1.86 / +1.75 | 0.2679 | 66 / none | 96.0 / 99.5 |
| regional lag test, tolerance 10 % (T0) | +1.36 / +0.64 | 0.2230 | 54 / 56 | 96.4 / 101.4 |
| regional lag test, derived tolerance (T1) | +1.41 / +0.60 | 0.2303 | 63 / 94 | 96.6 / 100.7 |
| bounce history held at 4 draws, no test | +0.43 / +0.18 | 0.0892 | 46 / 23 | not a still that passes: the cabin center swings from 94 to 128 % |

In the film, every rule sees the change at 71 to 73 % of the pixel draws. So the test's power to see is not the limit. Its action is. The test compares the histories' sum `S` with the samples' 18-draw mean `R`, and shortens a history only while the two differ. `R` itself lies about 9 draws behind a light that keeps moving. When `S` meets `R`, the action stops, and the histories grow long again inside the change. The histories then follow `R`, not the light.

### The model of this experiment

Alexis's decision Y3 (design doc, section 2.3) asks for one evidence value per block with one reader. Today the reader is the light evidence of a ray: it cuts the history's length to the fast cap of 4 draws and leaves the box wide, for as long as the evidence lasts. The regional lag test is a second reader beside it, with its own action.

This experiment makes the region a second source of the same evidence instead. The region does not ask whether the histories lag. It asks whether the light of the region moves. Where it moves, each block of the region gets light evidence, and the one reader cuts. While the light keeps moving, the evidence lasts, and the histories stay at the fast cap, as under the cap of 4 draws that closed the film. When the light stands still, the evidence ends, and the histories grow long.

- The state that the test reads is the samples alone: the ring of the region's light per draw, `F(t)`, which the probe already stores. The histories are not part of the test. So the clamp's loss, the store's loss (`t8-69v7`) and the filter's write-back cannot hold the test open in a still.
- What causes evidence: the region's mean light over the last 18 draws differs from its mean over the 18 draws before them. Each of the two windows holds one full cycle of the jitter, so the cycle cancels in each.
- What the evidence does: the block takes `LightEvidence`, as from a ray. Nothing else changes in the reader.

### The quantities, at one pixel in one draw

- `F(t)` = the sum over the 17 × 17 taps of the samples' luminance in draw `t`, as in the tolerance measurement.
- `Rnew` = the mean of `F` over the last 18 draws. `Rold` = the mean of `F` over the 18 draws before those. This needs a ring of 36 draws of `F`, or a ring of 18 past values of `Rnew`. The choice is yours.
- `dold` = the standard deviation of `F` over the older window of 18 draws.
- `drift` = |Rnew − Rold| / max(Rold, least luminance).

### The candidate rules

| Rule | Evidence where | The reason for its value |
|---|---|---|
| Q0, the control of the method | drift > 0.10 | None. The tuned constant of the earlier probes |
| Q1, the older window's spread | drift > 2 × (dold / Rold) × sqrt(2 / 18) | `Rnew` and `Rold` are each a mean of 18 draws with the variance dold² / 18, and they share no draw. The tolerance is two standard deviations of their difference. The spread comes from the older window, so a step inside the new window does not raise the tolerance. The tolerance measurement found that a spread over the window that holds the step silenced its rule in the cabin |

You can add one rule of your own if the count points at one. Record it and its reason.

For both rules, a block with regional evidence takes the reader's light action: the bounce history's length becomes the fast cap, and the box stays wide. Take the reflections with the same rule from their own samples, as the earlier probes did, and say where you differ.

### The store

Take every variant with the nearest-half store of the diagnosis `t8-69v7` (`TERTIUS_X_HALF_NEAREST=1`, `docs/research/t8-69v7-length-light/scratch-probe-on-73f4a6548.patch`). The line's branch gets that fix now, and with it a history's light no longer depends on its length. So a still's light then judges the test alone. Bring that switch into your patch. The reference, the fix and the cap of 4 draws need new takes with it, because every earlier number of them has the old store.

### The takes

Step 1, the count without action, with the nearest-half store. Stills `rect-lamps`, `bay-sunbeam` and `exit-cabin` under today's jitter, no jitter and the long Halton sequence: the share of pixel draws with evidence under Q0 and Q1, for the whole image and the 3 × 3 regions, and the median and 90th percentile of the drift and of Q1's tolerance. A still has no change, so every such pixel draw is false evidence. Then the cabin switch and the film `rect-bay` under today's jitter: the share with evidence over the frames, per region. Commit the table as a note before step 2.

Step 2, each rule acting, against the reference, the fix and the cap of 4 draws, all with the nearest-half store:

| Take | Jitter | Measures |
|---|---|---|
| Lamp hall still at 4.08 stops | today's, none, long | Light of ceiling and walls in scene light and by `light.py`, the bounce history's mean length and share below 8 draws, specks, blotch, daub |
| Film `rect-bay` | today's | Level and blotch, middle and last frame |
| Cabin switch | today's, none, long | The level 60 draws after each switch as % of the reference's control (whole image and r11), the draws until the slowest region, the whole image and r11 stay within 5 % of their own control, and r11's lowest and highest level over frames 140 to 159 |
| Lag films `bay-sun`, `lamp-hall-16-moving`, `lamp-hall-64-spot-moving` | today's | Median lag and lag error |

Main's numbers are those of the earlier records. They carry the old store. Say so beside each comparison with main.

### The three questions

1. In a still, how often does each rule give false evidence, under each of the three jitters? The earlier test gave 20 % in the lamp hall at 10 %.
2. With a rule acting, does the film reach the cap of 4 draws (+0.43 / +0.18, bay-sun 0.0892), and does the cabin settle like it (46 / 23), without that cap's swing of the cabin center before the switch (94 to 128 %)?
3. Does the still keep the fix's light, specks and daub, and its history length?

If a rule passes all three under today's jitter, say what the long Halton sequence does to it. That decides the order of `t0-f238`.

## Acceptance

- The control: with every new switch at its default and the old store, the build repeats the tolerance record's T0 lamp hall still (96.4 / 101.4 under today's jitter).
- Tables of the count and of every take in a note on this ticket and in `docs/research/t0-ion0-region-drift/README.md`, with the three answers. Each answer gives the measured numbers apart from your reading.
- One chart of r11 over the cabin switch under today's jitter with the reference, the fix, the cap of 4 draws, Q0 and Q1. One sheet of film frames of `rect-bay` at the middle and the last frame: the still of the same state, the fix, Q1 and the cap of 4 draws. One crop sheet of the lamp hall's ceiling: the fix, Q0, Q1. Open each with the Read tool and describe it.
- The scratch patch and the scripts in the same folder. Reuse the scripts of the earlier folders and copy only what you change.
- No change under `src`, `tests` or `tools`.

## Out of scope

- The remedy's build. Its ticket follows this result.
- Statistics at ray-block resolution. This ticket keeps the probe's taps.
- A graded cut. The reader cuts to the fast cap or does nothing. If the count shows that a graded cut is needed, say so in the notes.

## Notes

**2026-11-15T11:27:07Z**

Step 1: the count of the drift test without action (ticket lead).

The build. The scratch probe is `docs/research/t0-ion0-region-drift/scratch-probe-on-73f4a6548.patch`. It holds the t0-xlcu probe and the nearest-half store of t8-69v7 (`TERTIUS_X_HALF_NEAREST=1`, constant 22). It also holds the drift test with the constants 23 to 28 (`TERTIUS_X_DRIFT`, `_RULE`, `_ACT`, `_COUNT`, `_TOL`, `_SIGMAS`). The drift ring holds 36 draws of `F` after the window ring's layers. Until 36 draws exist, the test gives no evidence. The spread `dold` takes the divisor 17. The reflections use the same rule with their own samples.

The control. Every new switch at its default with the old store repeats the T0 lamp hall still: 96.4 / 101.4 (ceiling / walls, % of the reference). The ported store repeats t8-69v7 in scene light: the reference with the store gives 102.7 / 105.8 % of the reference without it, and the cap of 4 draws with the store gives 100.0 / 100.1 % of the reference with it. The second build (below) repeats the first build's T0 still and its count still byte for byte.

A change of the design: one test per block, at its center pixel. The first build gave a block evidence when any of its 64 pixels fired. In the lamp hall, Q1 then fired at 9.9 % of the pixel draws but flagged 75.6 % of the block draws. The tests of a block's pixels are almost independent, so "any of 64" multiplies the false evidence. The second build tests only the center pixel, local pixel (4, 4), and its block takes that result. All shares below are of this rule. `tables-step1.txt` also holds each pixel's own share and the first build's block shares.

The count, the bounce light, % of counted pixel draws with block evidence, whole image. Stills count run draws 100 to 300, every 7th. A still has no change, so all its evidence is false.

| Take | Drift p50 / p90 | Q1 tolerance p50 / p90 | Q0 | Q1 | Q2 |
|---|---|---|---:|---:|---:|
| Lamp hall, today's jitter | 0.058 / 0.266 | 0.167 / 0.740 | 30.5 | 9.2 | 3.8 |
| Lamp hall, no jitter | 0.056 / 0.251 | 0.147 / 0.592 | 30.5 | 12.6 | 5.6 |
| Lamp hall, long Halton sequence | 0.067 / 0.318 | 0.157 / 0.708 | 34.1 | 14.3 | 7.7 |
| Bay still, each jitter | 0.034 / 0.089 | 0.098 / 0.150 | 7.3 to 7.6 | 7.0 to 7.3 | 1.1 |
| Exit cabin, each jitter | 0.032 / 0.086 | 0.094 / 0.144 | 6.2 to 6.3 | 6.0 to 6.1 | 0.9 |
| Cabin before the switch, frames 140 to 159 | 0.057 / 0.266 | 0.168 / 0.735 | 30.4 | 9.8 | 3.5 |
| Cabin, lamps off, d 0 to 5 | 0.056 / 0.265 | 0.168 / 0.738 | 31.9 | 12.7 | 6.8 |
| Cabin, lamps off, d 6 to 17 | 0.095 / 0.283 | 0.166 / 0.768 | 46.8 | 18.7 | 9.2 |
| Cabin, lamps off, d 18 to 35 | 0.150 / 0.304 | 0.175 / 0.742 | 69.6 | 34.2 | 15.1 |
| Cabin, lamps off, d 36 to 59 | 0.071 / 0.282 | 0.193 / 0.769 | 37.6 | 8.8 | 2.7 |
| Cabin, lamps on, d 6 to 17 | 0.085 / 0.305 | 0.178 / 0.644 | 42.5 | 16.2 | 7.3 |
| Cabin, lamps on, d 18 to 35 | 0.119 / 0.349 | 0.175 / 0.638 | 54.8 | 29.1 | 14.4 |
| Film rect-bay, 180 frames | 0.205 / 0.299 | 0.104 / 0.173 | 86.7 | 84.1 | 71.0 |

`d` = the film frame minus the switch frame. The reflections give 34 to 53 % (Q0), 13 to 23 % (Q1) and 8 to 17 % (Q2) in the lamp hall, 1 to 13 %, 4 to 7 % and 0.5 to 2.6 % in the two other stills, and 82 / 83 / 71 % in the film.

A rule of my own, Q2: Q1 at 3 standard deviations, `TERTIUS_X_DRIFT_SIGMAS=3`. The reason: under a normal distribution, a two-sided test at 2 standard deviations fires at 4.6 % of the draws. In the lamp hall Q1 fires at 9 to 14 %, so the drift's tails are heavier. At 3 standard deviations, a normal distribution gives 0.3 %. The measured share is 3.8 to 7.7 % in the lamp hall and about 1 % in the two other stills. The film keeps 71 %.

Facts that bear on step 2:

- The test sees a switch late. Under Q1 the cabin's share rises from 9.8 % before the switch to 12.7 % at d 0 to 5 and peaks at d 18 to 35 (34.2 %). The new window must fill with changed draws before `Rnew` moves, and `Rold` holds the old light until d 18.
- The lamp hall and the cabin's still state have the same tolerance and drift, because the cabin's view is the lamp hall's.
- In the film, the drift's median 0.205 is about 2 times its Q1 tolerance. In the stills, the median drift is about a third of the tolerance.

My reading. Q0 fails the still at 30 % in the lamp hall, so it stays only as the control of the method. Q1 and Q2 separate the film (84 and 71 %) from the stills (1 to 14 %), but no rule reaches zero false evidence in the lamp hall. Step 2 must show whether 4 to 9 % false evidence costs the still its light, specks or length. The long Halton sequence raises the lamp hall's false evidence under each rule (Q1 9.2 to 14.3 %, Q2 3.8 to 7.7 %).

Step 2 takes Q0, Q1 and Q2 acting, with the reference, the fix and the cap of 4 draws, all with the nearest-half store.

Files: `tables-step1.txt` (made by `tools/measure-1.sh`), `tools/queue-c.sh`, `tools/queue-1.sh`, `tools/queue-1c.sh`, `tools/driftcount.py`.

**2026-11-15T13:15:09Z**

Step 1 again, with the ticket's rule Q2 and the wait before the first cut (ticket lead, after the line orchestrator's two additions).

Names. The ticket now gives the name Q2 to the rule of differences at equal jitter phase. My rule of the first step 1 note, Q1 at 3 standard deviations, is now Q3. Its numbers do not change.

The third build. It adds Q2 (`TERTIUS_X_DRIFT_RULE=2`) and three count layers. For m = 3 and 6, the shader takes |the mean of the last m differences F(t − k) − F(t − k − 18)| / Rold, and Q2 fires where any of the three lengths passes its tolerance. Its tolerance for m is Q1's tolerance times sqrt(18 / m). The length m = 18 is Q1's own test. The controls: the third build repeats the second build's Q1 lamp hall still (step 2's `t-Q1-e`) byte for byte, and its count repeats the second build's five count layers byte for byte. The shader's Q2 block flag equals the center rule that `driftcount.py` derives (14.3 % against 14.3 % in the lamp hall).

False evidence in the stills, the bounce light, % of counted pixel draws whose block has evidence (center rule), whole image:

| Still | Q0 | Q1 | Q3 | Q2 | Q2, m = 3 alone | Q2, m = 6 alone |
|---|---:|---:|---:|---:|---:|---:|
| Lamp hall, today's jitter | 30.5 | 9.2 | 3.8 | 14.3 | 5.5 | 6.2 |
| Lamp hall, no jitter | 30.5 | 12.6 | 5.6 | 20.4 | 8.5 | 9.5 |
| Lamp hall, long Halton sequence | 34.1 | 14.3 | 7.7 | 21.9 | 8.2 | 10.5 |
| Bay still, each jitter | 7.3 to 7.6 | 7.0 to 7.3 | 1.1 | 14.2 to 14.4 | 5.9 to 6.0 | 6.0 to 6.2 |
| Exit cabin, each jitter | 6.2 to 6.3 | 6.0 to 6.1 | 0.9 | 12.6 to 12.7 | 5.0 | 5.3 to 5.4 |
| Cabin before the switch | 30.4 | 9.8 | 3.5 | 15.6 | 5.9 | 7.0 |
| Film rect-bay, 180 frames | 86.7 | 84.1 | 71.0 | 86.0 | 30.0 | 54.0 |

The wait before the first cut. "Changed region" = the union of the regions that the survey counts as changed in the cabin switch (all but r01), and the whole image in the film. The number is the first draw after the change at which half of the changed region's counted pixel draws have block evidence. In brackets: the share in % at d = 0, 3, 5, 10, 15, 20 and 30.

| Change | Q0 | Q1 | Q3 | Q2 |
|---|---|---|---|---|
| Cabin, lamps off | d 13 [29 32 33 34 62 77 69] | never [15 14 13 11 28 45 33] | never [7 8 7 7 14 25 12] | d 21 [19 19 21 27 37 49 35] |
| Cabin, lamps on | d 17 [34 38 37 37 45 56 59] | never [7 9 10 11 19 31 33] | never [2 3 4 4 9 16 18] | never [14 14 19 20 28 38 38] |
| Film rect-bay, from frame 0 | frame 3 [n/a 51 24 15 18 37 63] | frame 28 [n/a 17 20 11 9 23 60] | frame 38 [n/a 2 6 2 1 6 29] | frame 23 [n/a 20 27 17 20 36 66] |

Film frame 0 is run draw 32. The test is valid from run draw 35, frame 3. At frame 3 the older window holds the run's first 18 draws, so the first frames carry the start of the run as well as the film's change.

My reading:

- Q2 does not shorten the wait in these changes. The cabin's switch changes the light by about 18 % (the survey's change −0.176). At m = 3, the tolerance is Q1's times 2.45, a median of about 0.41, so a step of 18 % does not pass it. Q2 then fires mostly through m = 6 and 18, from d 6 on.
- Q2 doubles the false evidence of Q1 in the bay and exit cabin stills (14 % against 7 %), because it adds two more tests per draw.
- No rule gives half of the cabin's changed region evidence before d 13. Q1 never gives it to half. In the film, Q1 reaches half only at frame 28, and Q2 at frame 23.

Step 2 adds Q2 acting with all takes of Q0, Q1 and Q3, the film's level at frames 10, 20 and 30 (`tools/filmearly.py`), and the cabin's level 5, 10 and 20 draws after each switch (`tools/switchearly.py`).

**2026-11-15T16:58:34Z**

Resume note: the lab stopped on the line orchestrator's stop order (2026-11-15, about 12:00). Step 2 and the strips check are measured and recorded. Not done: a few takes, the ticket's step 2 note, the review and the fix stage.

Where things are:
- Worktree `/Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144`, branch `t3/measure-regional-lag-tolerance`. HEAD is the commit that holds this note. The review base is `33c6e2952`.
- Record folder `docs/research/t0-ion0-region-drift/`: `README.md` (the answers, every table, the strips check), `tables-step1.txt`, `tables.txt` (every measured number of step 2 and the strips check), `pics/` (4 pictures, each opened and described in the README), `scratch-probe-on-73f4a6548.patch` (the fourth build: drift test Q0 to Q3, the nearest-half store, `TERTIUS_NO_SUN`), `tools/`.
- Scratch folder `/private/tmp/t0-ion0`: only the run folders' small result files (`runs/*/results`, logs, `take.txt`). Every probe build, every frame and the scratch checkout are deleted. No process of this ticket runs. No process of this ticket holds the GPU lock. No cloud job ran.

What is done and proven (numbers in `tables.txt` and the README):
- Controls: each of the four builds repeats the one before byte for byte (`z-Q1-e`, `y-Q1-e` against `t-Q1-e`; `mt-rect-lamps` against `kt2-rect-lamps`).
- Step 1 with Q0, Q1, Q3 (my rule, Q1 at 3 standard deviations, first named Q2) and the ticket's Q2, and the wait before the first cut (the `m*` and `k*` takes, `tables-step1.txt`).
- Step 2 under today's jitter for all six variants (`t-*`, `sc-*`, the lag films `t-*-lag`, the early film levels `t-*-fe`).
- Step 2 without jitter (`n-*`) for the reference, the fix, Q1, Q3 and Q2, and the cap of 4 draws' switch.
- Step 2 under the long Halton sequence (`l-*`): the stills at 4.08 stops and the cabin switch.
- The strips check, questions 1 and 2 (`s-*`, `ss-*`, `s-t-*-nosun-sw`, `strips.py`).

The answers in short (the README's first table): every rule brings rect-bay to the cap of 4 draws' level (+0.43 to +0.47 / +0.18 to +0.23, against main's +1.18 / +0.53 with the old store). No rule settles the cabin like the cap of 4 draws (46 / 23). Q2 comes closest (37 / 69). Q1 and Q3 keep the still's light under today's jitter, but Q2 loses 2 to 4 points. Under the long sequence, every rule loses 2 to 8 points. The strips check: without the sun, window 4 keeps the still's light (100.3 / 100.2 % against 90.7 / 92.8 %), and the cap of 4 draws' cabin center no longer swings (102.0 to 102.3 % against 91.2 to 124.3 %). Without jitter, r11 is 46 % darker than the jitter's cycle mean ("never").

Not done:
1. The lamp hall stills at the automatic exposure under the long sequence (`l-*-s`): stopped before the first one finished. The README says "not taken". They are optional.
2. The step 2 note on this ticket with the three answers. Copy it from the README's first section, "The answers in short".
3. The review: one fresh reviewer (providerInstanceId "claudeAgent", model "claude-opus-5-5", options {"effort": "high"}, role "review", isolation "shared") for the diff from `33c6e2952` to HEAD. Then the fix stage if the review has findings. Then the final JSON report to the line orchestrator.

The exact next step: write the step 2 note (`tk add-note t0-ion0`), commit it, and start the review with `delegate_task` as above. A take needs the probe again. To build it, run these commands:

    git worktree add --detach /private/tmp/t0-ion0/src 73f4a6548
    cd /private/tmp/t0-ion0/src && git apply /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/t0-ion0-region-drift/scratch-probe-on-73f4a6548.patch
    df -g /System/Volumes/Data   # start no build below 40 GiB free
    nix develop /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144 --command bash -c 'dotnet build src/Tertius.Demo -c Release -v quiet -nologo -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o /private/tmp/t0-ion0/out-z/probe-bin'

Then run the queue, for example `docs/research/t0-ion0-region-drift/tools/queue-3.sh`. It keeps the finished runs and starts at `l-A-s`. After the takes, remove the scratch worktree with `git worktree remove --force /private/tmp/t0-ion0/src`.

**2026-11-21T16:21:09Z**

Step 2: each rule acting, and the strips check (ticket lead, written 2026-11-21 after the lab resumed). The record is docs/research/t0-ion0-region-drift/README.md, and every number is in tables.txt. All variants have the nearest-half store. Main's numbers come from the earlier records and carry the old store.

| Question | Measured |
|---|---|
| 1. False evidence in a still | Lamp hall, today's jitter: Q0 30.5 %, Q1 9.2 %, Q3 3.8 %, Q2 14.3 % of the block draws. Bay and exit cabin: Q1 6 to 7 %, Q3 1 %, Q2 13 to 14 %. No jitter and the long Halton sequence raise the lamp hall's numbers (Q1 12.6 and 14.3 %). |
| 2a. The film rect-bay, middle / last frame | The fix: +1.86 / +1.91. Cap 4: +0.43 / +0.18. Q0 and Q1: +0.44 / +0.19. Q3: +0.47 / +0.23. Q2: +0.43 / +0.19. Main (old store): +1.18 / +0.53. |
| 2b. The cabin's settle, slowest region off / on | The fix: 74 / none. Cap 4: 46 / 23. Q0: 134 / 42. Q1: 77 / 90. Q3: 76 / none. Q2: 37 / 69. Main (old store): 75 / 52. |
| 2c. The cabin center before the switch, lowest / highest | The fix: 99.1 / 100.3 %. Cap 4: 91.2 / 124.3 %. Q0: 102.1 / 124.3 %. Q1: 106.0 / 107.4 %. Q3: 104.3 / 105.5 %. Q2: 101.4 / 103.3 %. |
| 3. The still: light, specks, length | Light at 4.08 stops, ceiling / walls: the fix 96.0 / 99.7 %, Q1 96.7 / 100.3 %, Q3 96.5 / 100.2 %, Q2 94.0 / 96.0 %. Specks: the fix 5.96, Q1 7.99, Q3 6.27, Q2 9.50. Mean length of the ceiling's bounce history: the fix 32.0, Q1 26.1, Q3 29.4, Q2 22.3. |

My reading:

- Every rule closes the film's trail to the level of the cap of 4 draws, well below main's. The film is no longer the limit.
- No rule settles the cabin like the cap of 4 draws in both switches. Q2 comes closest (37 / 69). Q1 and Q3 settle no faster than the fix.
- Q1 and Q3 keep the still's light. Q1 costs 34 % more specks, and Q3 costs 5 % more. Q2 loses 2 to 4 points of the still's light under today's jitter and costs 59 % more specks.
- So no rule passes all three questions. Q3 comes closest on questions 1 and 3, and fails the cabin's settle of question 2. Under the long Halton sequence, every rule loses the still's light: Q3 94.9 / 95.6 %, Q1 94.0 / 93.9 %, Q2 92.2 / 91.4 %, against the fix's 96.8 / 99.6 %.
- The strips check shows that the sun causes the jitter's cycle in the cabin center and window 4's loss of the still's light. Without the sun, both are gone.

The long Halton sequence: a rule that passes under today's jitter loses the still's light under the long sequence. Q3, the closest rule, falls from 96.5 / 100.2 % to 94.9 / 95.6 %, and the cabin center before the switch swings from 89.2 to 99.6 %. So the long sequence comes after a remedy for the sun's strips, not before it.

Not taken, by the line orchestrator's decision of 2026-11-21: the lamp hall stills at the automatic exposure under the long sequence. The room holds the sun's shadow leak, and every acceptance number of that room gets new takes after the shadow fix.

**2026-11-21T16:28:38Z**

Step 2's tables, and the review's corrections (ticket lead, 2026-11-21). The acceptance asks for every take's table in a note, so they follow, copied from the README after the review's fixes. The review changed four readings of the step 2 note above. Q3 settles no faster than the fix, and Q1 is faster only on the lamps-on switch (90 against none). Every rule closes rect-bay's trail to the cap's level, but in bay-sun the rules stay about 14 % above the cap (0.102 to 0.103 against 0.0897), and Q3's last frame keeps more blotch (0.148 against 0.101). Under the long sequence, the cabin center swings 10 to 17 points, and only Q1 settles faster than under today's jitter on both switches. The strips check's "never" reading is now marked unverified: the whole image holds within 0.2 % without jitter, so light moves between regions. A graded cut is not needed: the cabin fails by the wait before the first cut, not by the cut's depth.

The lamp hall still, today's jitter:

| Variant | Light at 4.08 stops, ceiling / walls (%) | Scene light, no jitter, ceiling / walls (%) | Specks | Blotch | Daub | Ceiling's mean length / share below 8 draws |
|---|---|---|---:|---:|---:|---|
| Reference | 100.0 / 100.0 | 100.0 / 100.0 | 12.08 | 0.1159 | 0.0420 | not counted |
| The fix | 96.0 / 99.7 | 98.7 / 100.0 | 5.96 | 0.1027 | 0.0406 | 32.0 / 0.0 % |
| Cap 4 | 94.5 / 99.2 | 98.4 / 99.9 | 6.85 | 0.0607 | 0.0434 | 4.0 / 100 % |
| Q0 | 96.0 / 100.5 | 98.5 / 99.9 | 8.78 | 0.0647 | 0.0437 | 12.5 / 50.5 % |
| Q1 | 96.7 / 100.3 | 98.7 / 100.1 | 7.99 | 0.0722 | 0.0417 | 26.1 / 11.2 % |
| Q3 | 96.5 / 100.2 | 98.7 / 100.1 | 6.27 | 0.0748 | 0.0405 | 29.4 / 4.8 % |
| Q2 | 94.0 / 96.0 | 98.6 / 99.9 | 9.50 | 0.0653 | 0.0424 | 22.3 / 18.3 % |
| Main (old store) | 78.4 / 81.2 | | | | | |

No jitter and the long Halton sequence:

| Variant | No jitter, light | No jitter, length | Long sequence, light | Long sequence, length |
|---|---|---:|---|---:|
| The fix | 96.9 / 100.0 % | 32.0 | 96.8 / 99.6 % | 32.0 |
| Q1 | 96.8 / 100.1 % | 21.5 | 94.0 / 93.9 % | 21.5 |
| Q3 | 96.8 / 100.1 % | 26.9 | 94.9 / 95.6 % | 26.0 |
| Q2 | 96.7 / 99.9 % | 15.7 | 92.2 / 91.4 % | 16.0 |

No jitter and the long Halton sequence:

| Variant | Specks | Blotch | Daub | Daub's broad part |
|---|---:|---:|---:|---:|
| Reference | 6.90 | 0.0644 | 0.0324 | 0.0541 |
| The fix | 3.19 | 0.0662 | 0.0325 | 0.0876 |
| Q1 | 5.03 | 0.0445 | 0.0351 | 0.1045 |
| Q3 | 3.87 | 0.0457 | 0.0340 | 0.0982 |
| Q2 | 7.33 | 0.0483 | 0.0365 | 0.1108 |

No jitter and the long Halton sequence:

| Variant | No jitter: settle off / on | No jitter: r11 before, lowest / highest | Long sequence: settle off / on | Long sequence: r11 before, lowest / highest |
|---|---|---|---|---|
| Reference | none / none | 100.0 / 100.0 % | none / none | 100.0 / 100.0 % |
| The fix | 112 / 66 | 100.8 / 100.8 % | 168 / none | 98.1 / 99.3 % |
| Cap 4 | 49 / 26 | 104.1 / 104.8 % | not taken | not taken |
| Q1 | 84 / 41 | 103.6 / 104.2 % | 65 / 38 | 87.3 / 99.4 % |
| Q3 | 83 / 42 | 102.9 / 103.4 % | 103 / 55 | 89.2 / 99.6 % |
| Q2 | 66 / 39 | 103.8 / 104.5 % | 83 / 40 | 84.3 / 101.5 % |

The film rect-bay and the lag films, today's jitter:

| Variant | rect-bay middle / last | Blotch middle / last | bay-sun lag / lag error | lamp-hall-16-moving | lamp-hall-64-spot-moving |
|---|---|---|---|---|---|
| The fix | +1.86 / +1.91 | 0.383 / 0.484 | 29.72 / 0.2808 | 4.44 / 0.0277 | −0.17 / 0.0107 |
| Cap 4 | +0.43 / +0.18 | 0.102 / 0.101 | 8.34 / 0.0897 | 4.47 / 0.0271 | −0.34 / 0.0115 |
| Q0 | +0.44 / +0.19 | 0.105 / 0.101 | 8.56 / 0.1027 | 4.39 / 0.0276 | −0.35 / 0.0118 |
| Q1 | +0.44 / +0.19 | 0.105 / 0.111 | 8.66 / 0.1026 | 4.38 / 0.0275 | −0.30 / 0.0116 |
| Q3 | +0.47 / +0.23 | 0.122 / 0.148 | 8.22 / 0.1019 | 4.46 / 0.0275 | −0.29 / 0.0114 |
| Q2 | +0.43 / +0.19 | 0.102 / 0.108 | 8.78 / 0.1029 | 4.48 / 0.0274 | −0.33 / 0.0121 |
| Main (old store) | +1.18 / +0.53 | | 28.55 / 0.2644 | | |

The cabin switch, today's jitter:

| Variant | Settle, slowest off / on | r11 settle off / on | Level 60 draws after, off / on, whole (%) | Level 60 draws after, off / on, r11 (%) | r11 before the switch, lowest / highest (%) | r11 at d 5 / 10 / 20 after off (%) | r11 at d 5 / 10 / 20 after on (%) |
|---|---|---|---|---|---|---|---|
| Reference | none / 99 | 66 / 43 | 105.8 / 96.0 | 105.1 / 95.5 | 100.0 / 100.0 | 107.8 / 107.7 / 107.0 | 94.5 / 94.3 / 94.5 |
| The fix | 74 / none | 74 / none | 95.3 / 89.4 | 94.3 / 82.6 | 99.1 / 100.3 | 105.8 / 94.9 / 95.2 | 84.7 / 79.5 / 80.2 |
| Cap 4 | 46 / 23 | 20 / 18 | 102.0 / 91.1 | 115.1 / 87.0 | 91.2 / 124.3 | 131.2 / 117.8 / 128.3 | 78.1 / 75.5 / 85.9 |
| Q0 | 134 / 42 | 134 / 42 | 100.8 / 89.9 | 109.7 / 84.1 | 102.1 / 124.3 | 127.5 / 114.1 / 118.5 | 78.1 / 75.2 / 81.3 |
| Q1 | 77 / 90 | 77 / 90 | 96.7 / 89.5 | 97.3 / 82.3 | 106.0 / 107.4 | 112.0 / 99.5 / 96.0 | 81.4 / 77.2 / 78.8 |
| Q3 | 76 / none | 76 / none | 96.3 / 89.4 | 96.8 / 82.7 | 104.3 / 105.5 | 110.3 / 98.3 / 95.7 | 82.7 / 78.1 / 79.3 |
| Q2 | 37 / 69 | 37 / 69 | 95.7 / 89.2 | 94.4 / 81.1 | 101.4 / 103.3 | 108.0 / 96.4 / 94.3 | 77.6 / 74.3 / 76.8 |
| Main (old store) | 75 / 52 | | | | | | |

Question 1: always or never:

| Run | Region | Today's jitter: darkest / median / brightest phase | Cycle mean | No jitter |
|---|---|---|---:|---:|
| Reference | whole image | 0.7881 / 0.7893 / 0.7911 | 0.7896 | 0.7913 |
| Reference | r11 | 0.7991 / 0.8073 / 0.8124 | 0.8069 | 0.4389 |
| Cap 4 | whole image | 0.7109 / 0.7308 / 0.7450 | 0.7291 | 0.7683 |
| Cap 4 | r11 | 0.6310 / 0.7169 / 0.7816 | 0.7139 | 0.4436 |

Question 2: how much of the cycle is the sun's:

| Jitter | Measure | With the sun | Without the sun |
|---|---|---|---|
| Today's | Light at 4.08 stops | 90.7 / 92.8 % | 100.3 / 100.2 % |
| Today's | Scene light | 95.4 / 94.1 % (whole image 92.9 %) | 100.1 / 100.1 % (whole image 100.1 %) |
| None | Light at 4.08 stops | 96.1 / 99.6 % | 100.3 / 100.2 % |
| None | Scene light | 98.3 / 99.7 % (whole image 96.3 %) | 100.1 / 100.1 % (whole image 100.1 %) |

Question 2: how much of the cycle is the sun's:

| | With the sun | Without the sun |
|---|---|---|
| r11, lowest / highest | 91.2 / 124.3 % | 102.0 / 102.3 % |
| Settle, slowest off / on | 46 / 23 | 50 / 34 |
| r11 per jitter phase in the lamps-on control, darkest / brightest, against the cycle mean | −11.6 / +9.5 % | −0.9 / +0.4 % |
