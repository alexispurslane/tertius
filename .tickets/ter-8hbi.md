---
id: ter-8hbi
status: in_progress
deps: [ter-toaw]
links: []
created: 2026-11-03T16:40:41Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, spike, performance]
---
# A spike of the leanest screen trace that is correct on average

The user's direction of 2026-11-03, after ter-toaw measured today's traceRay at 10 to 14.5 ms for the design's rays at 1280 x 720 (budget 0.8 to 1.7 ms): with temporal accumulation, each ray can be much simpler, so simplify the trace to the extreme, beyond SSSR and Frostbite. The rule that decides each simplification: accumulation averages random error (variance) but not systematic error (bias). So a simplification is allowed when its error has zero mean over frames, or when its systematic choice is made random per ray with the right distribution. Starts from ter-toaw's branch and harness, measures each variant's time per ray kind, hit rate and converged-image error against today's trace, and reports which variant fits the budget. ter-gayt (the screen tracer) waits for it.


## Notes

**2026-11-03T16:50:35Z**

2026-11-03: launched as workflow run 04d9d7c1-6c6e-4974-a015-593fdb398bf9, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (Opus high builder, Opus xhigh reviewer), branch ter-8hbi-lean-trace-spike from 923d9b1de: main at 0a4236c87 plus the earlier spike's code and run script (branch ter-8hbi-base).
