---
id: ter-sftw
status: closed
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

**2026-11-04T02:49:12Z**

2026-11-03: the spike's report is docs/research/temporal-renderer/profiling-spike.md. The fix stage of 2026-11-03 rewrote this note's results after the review. The spike code is commit d71b549a0 (V6, the split mode, the dump of a frame's rays), after the merge of main in 7dd247c3d. The fix stage added commit a129e2761: the split walk's words form, and the finest level as the pass DepthFinestPass. The tool code is commits bdaf279fb, 34125e9b9, 8d7c812b1, 6e658a994 and 6860e9216. None of it merges into main.

Results:
- V6, the two-depth walk, takes 5.09 to 5.12 ms on the deck and 4.73 to 4.90 ms on the floor. These times have miss reads at 10 % own rays. V1 takes 5.93 to 5.96 ms and 5.91 to 5.92 ms. At full length V6 finds V1's hits. Its tile bias against V0 is −6.2 % / −7.1 % on the deck and +1.6 % / +0.7 % on the floor. Its steps behind slabs on the deck fall from 43 % / 23 % to 4.2 % / 2.4 %. Its diffuse rays are slower in every run: by 0.31 to 0.56 ms on the deck at 25 % own rays, where its sum equals V1's in one round.
- A plain buffer load comes back into MoltenVK's loop at each use. The words form (the record as uvec4 words through uintBitsToFloat) keeps the reads out of the loop. MoltenVK's dump of the split kernels shows 0 record reads in a loop, against 36 or 37 before. The walk's buffer read limiter fell from 28 % / 57 % to 1.2 % / 3.0 %.
- The walk alone takes 93 to 109 % of the whole trace. The setup and the shading add 0.6 to 0.9 ms as passes of their own, and almost nothing inside the whole trace.
- The SOL walk in its batched form (the GPU busy, one clock level): 2.88 ms in hand MSL, 3.57 ms through MoltenVK in the words form, 5.48 ms in the plain form. MoltenVK's share of the words form is 19 %: 0.17 ms runtime and 0.52 ms generated code. The reads in the loop cost the plain form 1.91 ms.
- On the frames of the dump, V1's walk alone takes 4.42 ms, so our code adds about 0.85 ms, 19 % of the walk. Its cause is not occupancy and is not found. The first version's claim that our code adds nothing is withdrawn.
- The GPU's clock level changes between runs. The finest-level pass shows it: 0.019 ms in the 10-frame runs, 0.024 to 0.029 ms in the 120-frame runs, which run about 1.3 times slower.
- The trace's reflection kernel and FSR's accumulate allow 512 threads per threadgroup and run at about 18 % occupancy. FSR's accumulate is at the ALU limit (92 %, F32 42 %). The direct accumulation (archive of ter-nmbm-direct-light at af4cef60d) allows 1024 threads, runs at 48 % occupancy, and has no limiter above 44 %. No single cause covers the three passes. FSR's 8 per-pass times add up to its whole upscale to 0.07 ms.
- Metal GPU captures of V1's trace, the direct accumulation and the SOL walk are in captures/sftw/gputrace, for Xcode's shader profiler. FSR has none, for lack of disk space.

Recommendation: ter-gayt takes V6's step for the reflection rays and reads its per-ray inputs in the words form. It keeps the walk in one kernel, and it finds our code's 0.85 ms by steps from the SOL walk. FSR stays as ter-7m38 left it. Two choices are open for the user. The first is V6 for the diffuse rays too, or V1's step for them. The second is whether to install Xcode's offline Metal toolchain for register and spill counts. The budget decision before ticket 4 remains, with V6 at 4.3 to 4.7 times the 1.1 ms. Its choices are fewer rays, a larger share of the frame, or the bias of caps and the vote.

**2026-11-04T12:03:08Z**

2026-11-04: the user did not pick the diffuse walk. They asked for V1 against V6 at the GPU's top clock and for a measured explanation of why the two-depth walk helps reflection rays but not diffuse rays. Continuation run 8e00fb0c-11e1-4439-982e-94c073115992 (version 869b0b07) on the same branch measures it in the batched form with the clock recorded, tests the two candidate causes alone, and compares at equal image quality. The Metal toolchain choice is settled: it is installed (approved in ter-3ug8).

**2026-11-04T14:18:57Z**

2026-11-04: the continuation measured V1 against V6 at the GPU's top clock and explained the reflection and diffuse difference (docs/research/temporal-renderer/profiling-spike.md, the first three sections of 'Result'). Its spike and tool code is commit c988232d3: the repeated trace (--trace-spike-repeat), V6's images in the ray dump, and the SOL forms v1-two-channel, v6 and v6-one-image. None of it merges into main.

Results:
- At the top clock (span clock 1285 to 1296 MHz, recorded by main's gpu-clock beside every run, 8 alternating rounds under the GPU lock, 4 to 7 kept runs per setting), with miss reads: V6 takes 3.93 / 5.38 ms on the deck at 10 / 25 % own rays and 3.50 / 4.77 ms on the floor. V1 takes 4.57 / 6.38 and 4.42 / 6.10 ms. V6's reflection rays are 20 to 32 % faster. Its diffuse rays are 0.025 to 0.057 ms slower, 2 to 4 %. The first run's diffuse differences of up to 0.56 ms came from runs at changing clock levels.
- Why: time is SIMD steps times ns per SIMD step. V6's step costs 3 to 10 % more than V1's in the trace, in both kinds. The reflection rays save 24 to 37 % of their SIMD steps. The diffuse rays save none, because the diffuse cap of 64 sets the longest walk of 76 to 88 % of the subgroups with V1 and V6 alike.
- The two causes alone, in the SOL walk on dumped frames of both views and shares, each pair checked to take the same steps and find the trace's hits on every ray: (a) the two-channel read and V6's extra tests cost 7 to 10 % of a step, (b) level 0 in a separate image costs 5 to 7 %. The rest of V6's step costs 0.09 to 0.23 ns per SIMD step and is not split further. In the SOL walk, V6's step costs 20 to 29 % more than V1's, against 3 to 10 % in the trace.
- Equal quality: V6 with a diffuse cap of 54 has V1's deck diffuse tile bias at 64, -9.5 %. Its diffuse rays then take 0.10 to 0.19 ms less than V1's, 9 to 10 %. That match holds on the deck only (see the fix stage's note below).

Recommendation, revised: one walk with V6's step for both kinds. The split walk buys at most 0.057 ms against V6 at 64, and at a cap of 62 V6 gives V1's image in both views at V1's cost. The diffuse cap, 64 for V6's better deck image, 62 for V1's image in both views, or 54 for V1's deck image and a worse floor at 0.15 to 0.22 ms less than V6 at 64, belongs to the budget decision before ticket 4. V6 takes 3.0 to 4.9 times the 1.1 ms of the two trace dispatches at the top clock. Level 0 in one RG32F image is a candidate for ter-gayt to measure in the real tracer. The offline Metal toolchain is installed (ter-3ug8), and the continuation did not use it.

Checks: the full Checked and Release suites passed on c988232d3, except GpuTimingTests.APassThatRecordsNothingAddsNearlyNoTime..., the flaky test of ter-abun, which failed in two of three Checked runs of the Rendering project under other sessions' GPU load and passed in the third.

**2026-11-04T15:40:00Z**

2026-11-04, the continuation's fix stage, after the review of 0b4dc80b7. The report's tables and claims changed where the review found them wrong:
- Equal quality holds per view. V6 at a diffuse cap of 54 matches V1's tile bias on the deck only. On the floor, it raises the diffuse tile bias from V1's +0.6 % to +1.4 %, 4 times the floor's noise of +-0.2 %. New converge runs at 58, 60 and 62 steps show that 62 is the lowest cap at which V6 is as good as V1 in both views. At 62, V6's diffuse rays take V1's time at the top clock, -0.5 to +1.2 % (8 alternating rounds under the GPU lock, median span clock 1285 to 1292 MHz). So the split walk buys nothing at equal quality, and the recommendation stays one walk with V6. The diffuse cap (64, 62 or 54) belongs to the budget decision before ticket 4.
- V5c at the top clock: 2.07 ms on the deck and 2.24 ms on the floor at 10 % own rays, 1.9 to 2.0 times the 1.1 ms (the first run's clock gave 2.4 to 2.9 times). V6's cost per ray at the top clock: 8.9 / 4.0 ns per reflection / diffuse ray on the deck, so the 1.1 ms covers 28 % of the rays there and 31 % on the floor.
- The kept-run rule (within 2 % of the fastest run) is now one module, tools/sftw/kept.py, used by top-table.py, top-steps.py and sol-forms.py. The SOL tables now apply it, which moves cause (a) to 7 to 10 % and the SOL premium of V6's step to 20 to 29 %. One run of the new batch was dropped because its pass stamps moved, and top-table.py prints dropped runs.
- TraceSpikeSolTool keeps one table entry per SOL form (name, macros, pyramid), and an unknown TERTIUS_SOL_WALK fails before the device starts. The repeat test also compares the own-ray images.
- Merge note: main holds a note of 2026-11-04T12:03:08Z that this branch lacks. The merge must keep it beside this branch's notes.

**2026-11-04T16:30:00Z**

2026-11-04: closed, report only. The report, its images and tools/sol-walk/loop-reads.py went to main in the close commit (the commit "ter-sftw closed: the profiling spike's report"). The spike code (tools/sftw, tools/trace-spike, the rest of tools/sol-walk, TraceSpike and TraceSpikeSolTool, captures/) stays on the branch ter-sftw-profiling-spike. The decision on the budget and on the diffuse cap goes to the user before ter-gayt.
