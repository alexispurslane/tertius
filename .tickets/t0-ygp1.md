---
id: t0-ygp1
status: open
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

