---
id: ter-4iic
status: open
deps: []
links: []
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
