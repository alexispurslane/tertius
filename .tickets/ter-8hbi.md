---
id: ter-8hbi
status: closed
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

**2026-11-03T22:35:11Z**

The result, after the review fix: no variant fits the budget. The report is docs/research/temporal-renderer/lean-trace-spike.md. This note replaces the first note of 19:09, whose budget left out the misses' map reads. The spike code is in the commits 1ebcac5fb, e998174ce, 7c9461a1f, a35f0a7ff, cc14a71c7, a6607f258 and 7fc6164b7 on the branch ter-8hbi-lean-trace-spike, never merged. ter-gayt takes the walk from lean_trace.glsl at 7fc6164b7.

- The two trace dispatches get about 1.1 ms of the tracer's 1.7 ms. The plan takes 0.19 to 0.26 ms alone, and the resolve an estimated 0.3 ms. The timed runs now read the environment map for each miss, with --trace-spike-miss-reads on. The design's hits read more values than the spike's hits, which the times leave out.
- The fastest variant, V5c with miss reads, takes 2.67 to 3.14 ms at 10 % own rays, 2.4 to 2.9 times the 1.1 ms. At 25 % it takes 3.86 to 4.24 ms. Today's trace takes 9.0 to 9.1 ms without miss reads.
- V1, the lean walk, takes 5.7 to 5.8 ms, and 5.9 to 6.5 ms with miss reads. Its tile bias against today's trace is −6.5 % and −9.5 % on the deck and +1.7 % and +0.6 % on the floor. On the deck, its flat slab gives −18.2 % and −10.4 %, and its facing test +12.0 % and +4.1 %. The step caps of V1 and V0 give the rest. The facing test removes most of the flat slab's hits on steep pixels.
- Every later saving is systematic: the step caps (V3), the coarse finest level (V4) and the vote exit (V5c). The random thickness (V2) is systematic too, and slower. The random start (V3) changes nothing. R16F (V5a) holds the slab only to 1,700 km. Persistent lanes (V5b) give the same images 7 to 8 times slower. The spike did not find the cause, so persistent lanes stay untested.
- V5c's vote now counts every lane that walks, and ends the rough walks at an eighth of the subgroup's started walks.
- The time goes into idle lanes and steps behind slabs. V5c's lanes do their own step in 53 to 58 % of the subgroup's lane steps, and V1's in 27 to 44 %. 43 % and 23 % of V1's steps on the deck cross cells behind a slab.
- Recommendation for ter-gayt: V1's walk with a fixed thickness of 0.1 and its facing test, over DepthPyramid as the one pyramid. Measure next a walk over near and far bounds, and a working refill of idle lanes. The budget needs a decision first: fewer rays (about half of the block rays fit), a larger share of the frame, or the bias of caps and the vote.
- The full Checked suite passed on a6607f258: 10,014 tests, 9,999 passed, 15 skipped, 0 failed.

**2026-11-03T22:46:52Z**

2026-11-03: closed. The report, docs/research/temporal-renderer/lean-trace-spike.md with its five image sheets, came to main without the spike code. The code stays on the branch ter-8hbi-lean-trace-spike at 9e0f48be1, which the profiling spike (ter-sftw) starts from and the screen tracer (ter-gayt) takes its walk from. Keep that branch.
