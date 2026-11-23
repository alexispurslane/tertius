---
id: t0-ygp1
status: closed
deps: []
links: [t58-3n6v, t58-phgr]
created: 2026-11-23T14:27:27Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, temporal]
---
# After a start on a cold card, the render scale of the dear draws after each camera cut stays at 0.54 to 0.65 for about 8 cuts

Measured on 2026-11-23 (card job cold1, RTX 2000 Ada, main 2e8fdaed2; the record is docs/research/t58-3n6v-cold-start when the lead's commit is merged, and the numbers are in t58-3n6v's note). The run's first draw costs 229.0 ms against 29.4 ms in a warm run, because the GPU starts it at its idle clock (210 MHz against 2475 MHz; every pass is 8 to 30 times dearer, and draw 1 is normal). RenderScaleController takes that draw as the first measurement of the catch-up kind, so it becomes the kind's whole model. The catch-up request at the first catch-up draw of the next three cuts is 0.538, 0.571 and 0.653 against 0.770, 0.787 and 0.805 warm, and those draws take 10.4 to 15.0 ms against a budget of 15 ms: they are drawn smaller than needed. The class of the bug: a measurement that describes the machine's state and not the scene enters a cost model of the scene.

## Design

Design A, chosen by the orchestrator on 2026-11-23 over a median of a kind's first three measurements (design B knows nothing of the cause, costs every start about 4 catch-up draws at the wrong scale, and brings the number 3). The model: a measurement means the cost of this scene at this render area at the GPU's working speed. A draw that the GPU began from rest is no such measurement, in any kind. The rule: the run's first draw is no measurement. In RenderScaleController, Measure changes nothing for the first draw that Drew recorded in the controller's life: no model is fitted from it and no request moves. No call changes its signature, and the rates and the band stay as they are (ter-5kz6). The controller's documentation names the rule with its measured reason, and AssertInvariants covers the new fact. Left out on purpose: a draw from rest later in a run (a hidden window, a pause). No take shows that case. If one does, the same rule extends to it: a draw after a pause of the GPU's work is no measurement.

## Acceptance Criteria

A test, red first, pins that the timings of the controller's first recorded draw fit no model and move no request, in either kind, and a second test pins that the second recorded draw is the first fit. On the card, one cold job (the first GPU run of the job, no warm-up, a start and three cuts, then a warm run): after the cold start the catch-up request at the first cut is within 0.02 of the warm run's. Checked and Release pass. ARCHITECTURE.md states the rule where it describes the render scale's controller.


## Notes

**2026-11-23T14:48:03Z**

2026-11-23, lead. Built on branch t0-ygp1-first-draw-no-measurement. RenderScaleController keeps the frame number of the first draw that Drew recorded (firstDrawn, -1 before it), and Measure returns for that frame, so no model is fitted from it and no request moves, in either kind. The controller is the right place: it is where a measurement becomes a model, and the renderer and the history do not need to know. AssertInvariants adds: a kind has a fit only after a measurement of a later draw than the first (measured > firstDrawn >= 0). Red first: TheRunsFirstDrawFitsNoModelAndMovesNoRequestInEitherKind (both kinds, 0.875 against 1 before the change) and TheSecondRecordedDrawIsTheFirstFit (0.781 against 0.887). Hardening: TheFirstRecordedDrawIsNoMeasurementWhateverItsFrameNumber (first draw at frame 5). The rule applied to the old tests: a test whose subject is a measurement of the first recorded draw now gets one draw before it (RenderScaleTesting.AfterItsFirstDraw, a dear first draw at frame 0) and draws its frames from 1: seven tests of RenderScaleTests and three of DrawCostScaleTests. The GPU tests: DelayedGpuFeedbackCountsTheScaleThatWasDrawn measures the second draw; CatchUpDraws.AWindowDrawsTheCatchUpDraws... now expects draw 1 at the full size and draws 2 to 7 small. The loops that start at frame 0 measure the ordinary kind after 8 catch-up draws, so they did not change.

Mutants (tools/mutate.py, docs/research/t0-ygp1/mutants.json):

| id | line | change | result | answer |
|---|---|---|---|---|
| Y01 | 41 | firstDrawn starts at 0 | killed | TheFirstRecordedDrawIsNoMeasurementWhateverItsFrameNumber |
| Y02 | 90 | every draw becomes the first | killed | ALaterResetAsksForTheFittedCatchUpScaleInItsFirstDraw, ARepeatedMeasurement..., TheFirstRecordedDraw... |
| Y03 | 90 | < 0 to <= 0 | killed | ALaterResetAsks..., AnOrdinaryDrawsMeasurement..., ARepeatedMeasurement... |
| Y04 | 91 | firstDrawn = 0 | killed | TheFirstRecordedDraw... |
| Y05 | 105 | == to <= | survived | Equivalent: a frame before the first recorded draw was never recorded, so the slot check returns first. |
| Y06 | 105 | == to != | killed | ADearCatchUpDraw..., ALaterResetAsks..., TheFirstRecordedDraw... |
| Y07 | 105 | the rule removed (never equal) | killed | ALaterResetAsks..., AnIncompletePassSpan..., TheFirstRecordedDraw... |
| Y08 | 200 | the new invariant removed | survived | Judgment: only a second defect can reach the state that it checks, and no test can drive a valid controller there. |

**2026-11-23T16:47:18Z**

2026-11-23, 11:47 New York time, line orchestrator of the Design X line. The fix is merged into the line and the ticket closes. The gates: the lead's branch was clean at f10346c0e; Checked and Release passed (Rendering 3,499 and 3,498 tests, 0 failed); the merge's own commit ran the whole Rendering project again. 8 mutants: 6 killed, Y05 equivalent, Y08 a judgment (the invariant's check). The card, job ygp1 on 3e3cc076f, cold first with no warm-up: the catch-up request at the three cuts is 0.766, 0.779 and 0.792 against 0.781, 0.795 and 0.808 warm, all within 0.02 (before the fix: 0.538 against 0.770 at the first cut). Draw 0 cost 334.0 ms cold against 28.8 ms warm. I opened the chart docs/research/t58-3n6v-cold-start/cold-scale-fix.png: the cold run follows the warm run at each cut. I read the source diff: the rule lives in the controller, which keeps its first recorded draw's frame number, and the documentation names the measured reason and what is left out on purpose (a draw from rest later in a run). What remains is not this bug: the cold process's ordinary request rests at 0.948 against 0.992 (t1-80jd), and the catch-up kind's slow follow of a cheaper scene (t58-3n6v).
