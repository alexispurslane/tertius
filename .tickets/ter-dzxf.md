---
id: ter-dzxf
status: open
deps: [ter-sftw]
links: [ter-gayt]
created: 2026-11-04T01:51:17Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, trace]
---
# Hill-climb the screen tracer's walk kernel on a fixed harness

The calibration spike (ter-3ug8, report docs/research/temporal-renderer/gpu-calibration.md on branch ter-3ug8-gpu-calibration until it merges) measured the lean walk at 2.3 to 2.7 times what the machine predicts: V5c took 2.67 to 2.81 ms on the deck at 1280 x 720 and 800 MHz, against a prediction of 1.05 ms. A rolled loop of dependent multiply-adds reaches only 0.41 of the FP32 peak, in native Metal and through MoltenVK alike. The walk serves both reflections and many-bounce light (ter-gayt), so it holds the largest known slack in our own code. The user chose on 2026-11-03 to climb it with a GPT 6.1 SOL agent.

Scope:
1. A harness that runs the walk kernel alone on fixed inputs: capture the walk's inputs once for the deck and floor views of lean-trace-spike.md (depth pyramid, normals, ray origins and directions), and record the current walk's hits as the reference.
2. A correctness gate, fixed before the climb starts: a variant must give the same hits as the reference, or stay within a tolerance that the ticket states.
3. An objective that the clock does not distort: GPU time per ray with dispatches batched so that the GPU reaches its top clock, gpu-clock (tools/gpu-calibration) beside each measurement, variants compared only at the same clock, under the GPU lock.
4. The climb: one change per step (for example an unrolled step loop, fewer live registers, subgroup-coherent stepping, the depth pyramid's layout), kept only if it passes the gate and is faster than the spread of the runs. A log of every attempt with its result.
5. A report: the best variant, its time against the machine's prediction, the log, and the change that ter-gayt takes over.

It starts after ter-sftw, because the profiling spike measures where the walk's time goes and tests the two-depth walk, and its answer picks the kernel that the climb starts from.


## Notes

**2026-11-04T03:25:10Z**

2026-11-03 (ter-3ug8's review fix): ter-scm7, "Unroll the lean walk's step loop two to four times, and measure it at a recorded clock", is folded into this ticket, whose step 4 already names an unrolled step loop. Its data, now from the variants of the native FMA probe (probes fma-variants, 7 rounds, docs/research/temporal-renderer/gpu-calibration.md, "The variants of the FMA probe"): with 4 float4 chains, 1, 2, 4 and 8 steps per pass of the loop reach 0.41, 0.77, 0.93 and 0.97 of the FP32 peak. A rolled loop with 2 to 16 float4 chains stays at 0.37 to 0.45, so more chains do not help. The first run's figures of 0.58, about 0.80 and 0.84 came from scratch kernels without logs and are withdrawn. The unrolled loop must not cost time on other Vulkan drivers. The lean walk V5c takes 2.4 to 3.3 times an estimate of its work at the rolled loop's rate (40 operations per step, ter-sftw's estimate). Time each variant at the clock over its timed span (ter-qk78).


**2026-11-04T12:20:46Z**

2026-11-04: with the user's 12 ms target (ter-89xq's note), the two trace dispatches get 1.36 to 2.05 ms if the rest of the frame stays as estimated, against V6's 2.92 to 3.16 ms at 10 % own rays at the windowed clock. So the climb's goal is a cut of about 35 to 55 % in the dispatches, at the correctness gate. Candidates named so far: the unrolled step loop (the calibration probe went from 41 % to 77 to 93 % of the FP32 peak), the 0.85 ms of our own code in V1's walk whose cause is unknown, GLSL forms that MoltenVK translates better (the words form recovered the record reads), persistent lanes, and whatever the profiling continuation leaves open of the level 0 layout and the diffuse cap with V6.
