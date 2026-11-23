---
id: ter-bcwr
status: open
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
