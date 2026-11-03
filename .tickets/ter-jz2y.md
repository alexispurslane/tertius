---
id: ter-jz2y
status: closed
deps: []
links: []
created: 2026-11-03T16:06:43Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, design]
---
# The design's randomness rule gives a block ray and its pixel's own ray the same direction

Found in the review of the spike ter-toaw. Section 6 of docs/plans/temporal-renderer.md, "Randomness", draws every ray from pcg3d of the pixel, the draw index and a constant per kind and dimension. The spike's traceFrom (trace_spike.glsl on branch ter-toaw-trace-timing-spike) follows that rule for a block ray and for an own ray alike. So when the block's chosen pixel also gets an own ray of the same kind, the two rays have the same direction. The second ray costs a full trace and adds no information, and the pixel's own sample equals its block's shared sample. In the spike, this happens to about 10 % of the block rays at an own-ray share of 10 %. In the design, it happens to every chosen pixel of a new or disoccluded surface, because all four pixels of such a block trace own rays for 8 frames.

Two ways out, which the design doc must choose between before ticket 4 (ter-gayt) copies the spike's code:
1. Draw a block ray from a constant of its own, or key its draw by the block. The chosen pixel then has two independent samples, at the cost of two traces.
2. Let the plan give no own ray to the block's chosen pixel, which it can compute from the turn, and let the resolve use the block ray at weight 1 for that pixel. This saves a trace for each such pixel.

The spike's timing does not depend on the choice, because the spike traces both rays.


## Notes

**2026-11-03T16:48:59Z**

2026-11-03: the orchestrator settled it as way 2, under the user's rule for small choices inside a chosen design: the block's chosen pixel traces no own ray of that kind, and its own sample is the block ray at weight 1. It saves a trace, and the lean trace spike (ter-8hbi) uses the same rule. Section 6, 'Randomness', of docs/plans/temporal-renderer.md now says so.
