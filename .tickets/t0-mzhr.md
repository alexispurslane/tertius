---
id: t0-mzhr
status: open
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
