---
id: ter-bcwr
status: in_progress
deps: []
links: [t1-zeyk, ter-4iic]
created: 2026-11-23T04:35:54Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, temporal]
---
# The render scale dips for the dear draws after a reset and returns at once, in place of a fall that takes 40 to 60 draws to climb back

After a start or a camera cut every bounce-light history is young, so the plan traces an own ray at nearly every pixel for 8 draws (ter-4iic). The catch-up ray experiment (t0-gumm on the Design X line) settled that these rays must stay and must come early: in the bounce-lit bay no spread over more draws and no shorter burst keeps the picture. Their price on the card is a render scale that falls by 0.14 to 0.23 and stays more than 0.02 under its rest for 41 to 62 draws. About 8 of those draws are dear. The rest is the controller's return at 5 % of the gap per measured draw. An estimate from the card's logs gives 8 to 10 draws at a scale of about 0.75 to 0.79 if the scale returned at once (marked as an estimate there: it leaves out the cost that does not grow with the pixels, the start's own dip, FSR's history across two sudden size changes and the controller's band).

After the 8-draw restart for light and region evidence (t0-zmjx), the only events that make the whole picture young are resets of the frame history, and the CPU knows them (FrameHistory.Begin with reset). So the cause is known ahead of the cost, and the controller need not discover it.

## Design

Systems view. RenderScaleController owns the requested scale and a cost model (Fixed, PerArea) that Measure fits from finished frames. FrameHistory owns the draw index since the last reset. Today the controller sees the dear draws only as measurements, some frames late, and answers with its normal fall and its slow return.

One model for both kinds of draw: a draw has a cost kind, ordinary or catch-up, and the controller asks for the scale that fits the budget under the model of the next draw's kind. The kind is a derived read of the frame history (draws since the reset below the catch-up length), not a stored mode.

Design 1, two fitted models (my lean). Drew records the kind with the scale. Measure fits the model of the measured frame's kind and moves only that kind's request. RequestedScale takes the next draw's kind. Sketch:
    internal enum DrawCost { Ordinary, CatchUp }
    public float RequestedScaleFor(DrawCost next);
    public void Drew(long frame, float realizedScale, DrawCost cost);
The first reset of a run has no catch-up model yet and behaves as today. Later resets dip to the fitted scale at once and return to the ordinary request at once, because the ordinary request was never moved by the dear draws. Nothing to tune.

Design 2, a fixed factor. The controller holds its request through the dear draws (Measure skips them), and the renderer draws them at the request times a constant. Less code, one constant that depends on the scene and the card.

Compare them on: the scale log of a start and a cut on the card, the number of size changes, and the picture of the first 20 draws under each (a video per event, each code on its own scale log). FSR's behaviour across the two sudden size changes is the main risk: check for a visible pop or blur at the return.

Length of the catch-up kind: FullRateFrames (8 draws) plus the frames by which the timings arrive late, or 5 if the delayed shape of t0-gumm's run 91 holds and is adopted.

Out of scope: the controller's rates and band (ter-5kz6, which another session holds; this ticket does not edit it), the request that never arrives at a target above it (t1-zeyk), and the start's own dip of about 50 draws from the dear first draws of a run (t58-np60).

## Acceptance Criteria

On the card, after a camera cut and after a start, the render scale is back within 0.02 of its rest within 12 draws of the event, and the number of size changes in the log does not rise against today. The picture of the first 20 draws is not worse than today's by the film measures, and a side-by-side video per event shows no pop at the return. Tests pin that a dear draw's measurement does not move the ordinary request, and that the kind is derived from the draws since the reset.


## Notes

**2026-11-23T04:35:59Z**

2026-11-22, orchestrator. No build before the Design X line merges into main (its merge ticket t0-iido lives on the line's branch, so this ticket holds no dependency on it) and before run 91 of t0-gumm sets the catch-up length. Related and not linked, because another session holds uncommitted changes to it: ter-5kz6.

**2026-11-23T05:01:01Z**

2026-11-23, orchestrator. The catch-up length is settled at 8 draws: run 91 of t0-gumm (own rays only in draws 3 to 7) keeps 87 to 94 % of the grain gain but only 36 % of the boil gain on narrow surfaces, and with the 36-tap fix of short histories (t0-rs55 on the Design X line) the rays of draws 0 to 2 buy grain again. That fix adds up to 27 texture reads per young pixel in draws 1 and 2, inside the dip of this ticket, so its measured cost belongs in this ticket's comparison.

**2026-11-23T10:45:48Z**

2026-11-23, 05:45 New York time, line orchestrator of the Design X line. The ticket starts on the line's freed slot (the register guard t2-x53j is merged). Its lead is the lead of the catch-up ray experiment and of the wide fix, in its worktree t3-52bfcd29-81249085ce80dd9f, branch ter-bcwr-announced-dip from 852556185. Both conditions of the first note hold: Design X is on main, and the catch-up length is 8 draws.

My decisions for the build (answer 99, reported to the orchestrator, who can reverse them):
1. The build is design 1, two fitted models, the orchestrator's lean. Design 2 is built only if design 1 fails its acceptance, because its factor is a tuned constant that depends on the scene and the card.
2. The wide fix (t0-rs55) was not merged, so its cost is not in this ticket's comparison.
3. The acceptance's "after a start" and the design's "the first reset of a run has no catch-up model yet and behaves as today" disagree. The camera cut after a fitted model is the case that the acceptance holds the build to. The lead measures the start too and reports it. A seed for the first reset is a fork, not a part of this build.
4. Card money for this ticket: at most $1.00, through tools/runpod/gpu_job.py only. The line then stands at no more than $3.55 of $5.00.

**2026-11-23T13:10:57Z**

2026-11-23, lead of ter-bcwr (branch ter-bcwr-announced-dip). The build is design 1: two fitted cost models, one per cost kind of a draw (DrawCost). The record is docs/research/ter-bcwr-announced-dip/README.md.

**Result on the card** (job cut1, $0.0511, 3 rounds, today c776bed47 against the build 2cd409b7a, FrameTimeTarget(16.67, 0.5, 1)):

| Event | Code | Back within 0.02 of the rest (draws) | Size changes in the 60-draw window / in the run |
|---|---|---|---|
| cut | today | 47, 47, 45 | 22 to 25 / 57 to 61 |
| cut | build | 8, 8, 8 | 2 / 23 to 24 |
| start | today | 59, 56, 54 | 30 to 32 / 32 to 34 |
| start | build | 28, 28, 39 | 15 to 21 / 15 to 22 |

- The cut meets the acceptance (8 ≤ 12, fewer size changes). The dip's draws 0 to 7 cost 15.5 to 16.7 ms on the GPU, against 19.9 to 27.8 ms today.
- The start does not meet 12 draws, and by decision 4 it need not: the first reset has no catch-up model. Draw 8 is its first ordinary measurement and is still dear (t0-xwdz), so the ordinary request falls once to 0.92 to 0.96. That is the start's own dip, t58-np60.
- Film measures under each code's own scale log, from t0-gumm's events.py:
  - cut: the slowest region settles in 137 draws (today 156); the boil is 0.095 % (today 0.107 %); the grain is 0.92 % (today 0.91 %);
  - start: the slowest region settles in 151 draws (today 156); the boil is 0.100 % (today 0.107 %); the grain is 0.91 % (today 0.89 %).
- The videos (pics/*-today-vs-build.mp4) show no pop at the return. The region means rise smoothly from draw 141 to 143.
- At 3 ×, the window's top edge shows a faint row of dark dots in draws 141 and 142 that today does not show. It is near invisible at 1:1 and gone at draw 143.

**Mutants** (tools/mutate.py, docs/research/ter-bcwr-announced-dip/mutants.json; 23 rows: 20 killed, 1 timeout, 2 survived with a judgment):

| id | change | result |
|---|---|---|
| C01 | every kind reads the ordinary fit | killed |
| C02 | a kind without a fit asks for the largest scale | killed |
| C03 | the fallback's kind test inverted | timeout (endless recursion): killed |
| C04 | Drew forgets the kind | killed |
| C05 | the blend starts from the ordinary fit | killed |
| C06 | the move starts from the ordinary request | killed |
| C07 | the fit goes to the ordinary kind | killed |
| C08 | the band's cost is linear in the scale | killed by the new ARequestBelowTheLargestScaleWhoseCostLiesInsideTheBandStaysExactlyWhereItIs (survived first) |
| C09 | inside the band the request jumps to the largest | killed |
| C10 | the down and up rates swapped | killed |
| C11 | the kind's invariant removed | survived. Judgment: a contract check of a state that no call can produce, because the renderer passes the pending draw's kind. Tests check behavior, not contracts. |
| C12 | the request's invariant removed | survived. Judgment: Moved clamps every request into the limits, so no call reaches the state. The check only guards against a later bug. |
| F01 | the catch-up kind one draw longer | killed |
| F02 | the catch-up kind one draw shorter | killed |
| F03 | the length is FullRateFrames + 1 | killed |
| F04 | CostOf ignores the reset | killed |
| F05 | a draw of index 1 resets too | killed |
| F06 | a reset draw gets index 1 | killed |
| F07 | the pending kind one draw ahead | killed |
| F08 | a still's first draw no longer resets | killed |
| V01 | every draw asks for the ordinary scale | killed by the new GPU test UpscalingRenderTests+CatchUpDraws |
| V02 | the renderer records every draw as ordinary | killed |
| V03 | every draw asks for the catch-up scale | killed |

C04, F04, F08 and V01 first failed to build, because of the analyzers' rule against unused parameters, and were rewritten to keep the parameter used.

**Coverage** of the changed lines (Rendering, Debug, against c776bed47): 54 of 54 lines are covered, and 31 of 36 branches. The 5 partial branches are the failing sides of contracts (RenderScaleController.cs 122 and 181 to 183, FrameHistory.cs 204).
