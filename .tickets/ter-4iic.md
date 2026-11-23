---
id: ter-4iic
status: open
deps: []
links: [t0-70v1, t0-o7gf, t0-zmjx, t0-gumm, t58-np60, t2b-d2m6, ter-6fka, t1-mybi, ter-13rm]
created: 2026-11-10T09:38:12Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Young histories after a start or a camera cut trace a ray at every pixel, so the deck's 95th percentile is 32.7 ms against a 24.5 ms median

Step 11 of /private/tmp/tertius-report-20261109/proposal.md. Deck trace with shared reflection rays at a lobe share of 0.5 (/private/tmp/ter-4oow/trace-lobe-0.5/passes.md, 152 frames, 1291 MHz): frame span median 24.50 ms, p95 32.70 ms; reflection rays median 2.65 ms, p95 9.52 ms; diffuse rays median 0.91 ms, p95 3.28 ms. The likely cause is the first draws, where every history is under FullRateFrames = 8 draws and keeps its own rays. Not yet shown: which frames make the p95, whether the survey's p95 counts them, and what one camera cut costs (view deck-cuts).

## Acceptance Criteria

The frames that make the deck's p95 are named from a per-frame trace. If they are the draws after a start or a cut, the p95 of exit-deck and deck-cuts falls to within 1 ms of the median, with no new blotch above 0.04 in the draws after a cut.


## Notes

**2026-11-10T09:48:17Z**

Correction from the frame-time study (/private/tmp/tertius-perf-plan-20261110/plan.md, research only, no new runs): the trace's p95 of 32.7 ms comes from its 32 warm-up draws, which the trace counts and the survey's timer leaves out. The same run's timer gives a median of 24.54 ms and a p95 of 25.13 ms. So the survey's p95 is not inflated by young histories. The cost stays real in a game window: about 10 ms more per draw for the 8 draws after a camera cut. The view deck-cuts does not measure a cut, because a headless draw after a camera switch is a still with 32 untimed warm-up draws. The acceptance needs a measure of the draws after a cut in a window, not the survey's p95.

**2026-11-22T21:05:14Z**

A structural candidate from the acceptance lead of the Design X line, 2026-11-22, passed to the orchestrator: a ray budget per draw. The plan ranks the pixels that want a ray of their own (young histories, by how young) and grants at most N own rays per block or per frame; the block's ray fills in for the others. Then no event raises a draw's ray cost above the budget: a light change, a start of a run, a camera cut. Evidence for the need: the start of a run costs 26.3 to 26.5 ms at draws 0 and 1 on the card and takes the scale to 0.771 (t58-np60); the variant "own rays wait for the evidence" of t0-zmjx still paid 4 draws at 22.8 to 25.3 ms after a lamp switch and moved the scale by 0.097. With a budget, the restart length after evidence (t0-zmjx: 8 draws) becomes free to choose again (t0-o7gf: 4 draws). Not designed and not measured; the orchestrator's review of the indirect lighting's approach may order it.

**2026-11-23T04:16:34Z**

Result of the catch-up ray experiment t0-gumm, 2026-11-22, with the orchestrator's decision. A catch-up ray is an own ray that the plan gives a pixel because its history is young. Three fixes of this ticket are out: no catch-up ray in the first 32 draws after a reset, the removal of the cliff (FullRateFrames as a ray rule), and one catch-up ray per restarted pixel spread over 32 draws (the share 1/32). Reason: in bay-sunbeam, where the bounce of one beam lights the room, today's cliff gives a smooth bounce light at draw 2 after a start and the three others give heavy blotches (docs/research/t0-gumm-catch-up-spread/pics/fair-bay-draw2.png on the branch t3/combined-renderer-check). Measured at the constant scale 2/3: boil of draws 1 to 8 1.370 % against 1.801 %, grain of draws 3 to 8 3.34 % against 7.31 %, and the same settle of 164 draws. In the lamp-lit cabin the catch-up rays buy about 5 % of the boil, which is why the first test missed it. The cliff's price stays: a fall of the card's render scale of about 0.16 at a start and 0.14 at a cut. Open: a sweep of shapes between the two ends (the same rays spread over more draws, or full rate for 3 draws only), and a second design that lowers the render scale for exactly the draws of the catch-up rays. The orchestrator chooses when the sweep reports.
