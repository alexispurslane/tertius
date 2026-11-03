---
id: ter-sftw
status: in_progress
deps: [ter-8hbi]
links: []
created: 2026-11-03T19:35:47Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, performance, spike]
---
# The trace and FSR profiling spike: where the GPU time goes, and the two-depth walk

The user's request of 2026-11-03 ("I like all your profiling ideas, crucial work. And the two-depth back face fix seems very important."). A spike on a branch of its own, from the final head of the lean trace spike (ter-8hbi, branch ter-8hbi-lean-trace-spike) after its fix stage. It merges no code. Its report goes into docs/research/temporal-renderer/.

Why: the lean trace spike's fastest walk costs about 5 ns per reflection ray on the M1 Max. That is about 27,000 lane-cycles per ray, or roughly 200 to 300 instructions per step, while a pyramid step needs one texel read and a few dozen arithmetic operations. FSR 3.1 takes 2.8 ms from 1280 x 720 to 1920 x 1080 (ter-7m38), several times AMD's published times for desktop GPUs of similar power. Both are compute shaders that MoltenVK translates to Metal, so a shared cause is possible. The review of ter-8hbi also found that a pyramid of the nearest depth alone makes a ray that passes behind a surface crawl one pixel per step.

Scope:
1. Split the trace's time: the walk alone (no hit or miss shading), the ray setup alone, and the shading of hits and misses alone, with the misses' environment map reads included. Do it for V1 and the fastest variant of ter-8hbi's final report.
2. Profile the compiled code: capture the Metal shaders that MoltenVK makes, and use Xcode's GPU frame capture and shader profiler (or the Metal counters that tools/gpu-trace.sh reads) for one walk step and one FSR pass (accumulate, its heaviest). Report the instructions per step, the registers, the occupancy (threads per SIMD group and per core), any spills, and the lines that cost the most.
3. A speed-of-light walk: a minimal pyramid walk over the same pyramid, with fixed ray directions and no shading, in GLSL through MoltenVK and in native Metal (tool code outside the engine), on equal inputs. The difference between the two is MoltenVK's share. The difference from the trace's walk is our code's share.
4. The two-depth walk (V6): V1's walk over a pyramid whose cells hold the nearest depth and the farthest back of the slabs under them, as today's min-max pyramid does (tracedCellBounds of V0). A ray that passes behind every slab of a cell skips the cell. Keep V1's facing test and κ = 0.1. Measure its time, its steps (with the steps behind slabs counted), its hit share, its tile bias and FLIP against V0 and V1, as ter-8hbi measures. Follow the review's design finding: the tracer keeps one owner of the pyramid, so V6 extends today's DepthPyramid (a finest level and the near channel) instead of a second pyramid.
5. Report with a recommendation for the screen tracer's traversal (ter-gayt) and, if the profile finds a shared cause, for FSR.

The GPU lock (mkdir /tmp/tertius-gpu-measure.lock) holds for every timed run. Persistent lanes stay untested (ter-8hbi's review), and this spike leaves them out.


## Notes

**2026-11-03T20:45:23Z**

2026-11-03: add the direct signal's temporal accumulation (accumulate_temporal.comp, the pass "accumulate direct") to step 2's profile in Xcode, beside the walk step and FSR's accumulate pass. The direct light's build (branch ter-nmbm-direct-light) measured it at 0.67 ms on lamp-hall-64 and 1.15 ms on demo-exit's deck at 1280 x 720, against a budget of 0.15 to 0.30 ms. That is the same pattern as FSR (2.8 ms against AMD's under 1 ms) and the trace: a compute pass at 2 to 4 times its expected cost on this Mac. If the profile shows one cause for all three, the ticket for the direct signal's cost (ter-9nrc, on that branch) uses it.

**2026-11-03T22:48:42Z**

2026-11-03: launched as workflow run ab3b5db2-ede2-4783-9178-8555202ba759, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (Opus high builder, Opus xhigh reviewer), branch ter-sftw-profiling-spike from 9e0f48be1, the lean trace spike's last commit. The builder first merges main (ac3370c14 or later) for FSR's host and the motion vectors. The report goes to docs/research/temporal-renderer/profiling-spike.md.
