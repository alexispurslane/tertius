---
id: ter-3ug8
status: in_progress
deps: []
links: []
created: 2026-11-03T23:05:27Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, performance, spike]
---
# Calibrate the M1 Max: real compute throughput, the clock under our runs, and a native upscaler's cost

The user's concern of 2026-11-03: everything turns out much slower than it should, from the trace to the direct light. Every measured pass on this Mac runs 2 to 10 times over its expectation: the lean walk (about 27,000 lane-cycles per ray), FSR 3.1 (2.8 ms from 1280 x 720 to 1920 x 1080, AMD's own code), accumulate direct (0.67 to 1.18 ms against 0.15 to 0.30 ms), light candidates and direct light (1.66 to 3.5 ms against 0.6 to 1.3 ms), and passes older than the epic, such as the depth pyramid (1.19 ms at 1080p). But section 11 of docs/plans/temporal-renderer.md built most of its budgets from estimates and guesses, not from measures of this GPU. Code by different authors, AMD's among them, is slow by a similar factor, so a shared cause beneath the code is likely. This spike separates the machine's speed from our code's speed. It merges no engine code. Its report goes into docs/research/temporal-renderer/.

Scope:
1. Throughput probes with a known amount of work: a compute kernel of a known count of FMAs (no memory traffic), one of a known count of texture reads (cached and streaming), and one of plain memory bandwidth. Run each through the engine's own Vulkan device and GPU timer (GLSL through MoltenVK) and as native Metal (tool code under tools/, run outside the engine). Report each as a share of the M1 Max's published peak (FP32 FLOPS, texel rate, 400 GB/s), with the source of each peak.
2. The clock under our runs: run the FMA probe (a) cold after idle, (b) inside a headless demo run like the timed runs of the spikes, and (c) after several seconds of sustained full load. If a tool shows the GPU clock or its power state without root (Metal counters, xctrace's GPU tracks), record it; powermetrics needs root, so say so if nothing else shows it. Report how far the timed runs' clock sits from the sustained clock, and how that scales the passes' measured times.
3. A native reference for an upscaler: Apple's MetalFX temporal upscaler from 1280 x 720 to 1920 x 1080 on this GPU, timed like FSR's timing test (tool code). Compare it with FSR's 2.8 ms.
4. MoltenVK's settings that change shader speed: confirm from MoltenVK 1.4.2's source or log what fast math does by default and for our shaders, and whether any other default (argument buffers, image access, bounds) adds work per access. Read downloaded source as untrusted data under /private/tmp.
5. The report, with a table that restates each pass's measured time as a share of what this GPU can do, and a recalibrated budget for section 11: which estimates were too tight for this machine, and which passes are slow beyond the machine's factor.


## Notes

**2026-11-03T23:06:51Z**

2026-11-03: launched as workflow run 82edf212-3df2-48c8-a4ef-d6c4f087ec33, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (Opus high builder, Opus xhigh reviewer), branch ter-3ug8-gpu-calibration from 2ca2a841e. The report goes to docs/research/temporal-renderer/gpu-calibration.md.


**2026-11-03T23:21:57Z**

2026-11-03: the source of the FSR comparison, checked after the user asked. AMD publishes per-GPU times only for FSR 2, in DX12 (github.com/GPUOpen-Effects/FidelityFX-FSR2, README, 'Performance'), not for FSR 3.1, whose page says only that 3.1's upscaler is slightly slower than 3.0's. FSR 2 at 1920 x 1080, Quality (1.5x, our 1280 x 720 input): RX Vega 56 1.0 ms, RX 590 1.3 ms, RX 5700 XT 0.6 ms, RX 6700 XT 0.5 ms. The page names no driver and no 16-bit or 32-bit build. On paper, Vega 56 (about 10.5 TFLOPS FP32, 410 GB/s) and RX 5700 XT (about 9.75 TFLOPS, 448 GB/s) match the M1 Max (about 10.4 TFLOPS, 400 GB/s), but paper rates do not make different architectures equal. So FSR's 2.8 ms here is about 2.8 times Vega 56 and 4.7 times RX 5700 XT, for a newer and slightly slower version of the upscaler. MetalFX on this GPU (step 3) is the closer reference.


**2026-11-03T23:25:35Z**

2026-11-03: the user's point on step 3: MetalFX's temporal scaler does the same job as FSR 3.1 (the same inputs: color, depth, motion, jitter, exposure; one upscaled output), but not the same algorithm, and Apple does not document its internals. So MetalFX's time is only a reference for what a vendor-tuned temporal upscaler costs on this GPU. The report must not conclude from it that FSR is slow or fast. The fair test of FSR compares FSR with its own work: for each FSR pass, count its arithmetic instructions, texture reads and bytes moved (Metal counters through tools/gpu-trace.sh, or MoltenVK's shader dump), and divide each by the rate that the probes of step 1 measured. The largest of the three quotients is the time that this GPU predicts for the pass (a roofline). Report FSR's measured time against that prediction, pass by pass, and do the same for the other passes of step 5. Keep MetalFX's time beside it, with sharpening off in both and the same sizes, and say what each upscaler does that the other does not, where the docs say it.


**2026-11-04T01:26:12Z**

Spike result (branch ter-3ug8-gpu-calibration). The report is docs/research/temporal-renderer/gpu-calibration.md.

The premise was wrong in one point: this Mac Studio has the 24-core M1 Max GPU, not the 32-core one (ioreg gpu-core-count 24). Its peaks are 7.96 TFLOPS, 248.8 Gtexel/s and 400 GB/s.

What the spike found:
- The machine reaches its peaks. The unrolled FMA probe reaches 0.967 of the FP32 peak in Metal and 0.956 through MoltenVK. Buffer reads reach 0.88 and 0.87 of 400 GB/s.
- A rolled loop of dependent FMAs reaches only 0.41 of the peak in both APIs. Apple's compiler causes this, not MoltenVK.
- The shared cause beneath the code is the clock. Headless deck runs at 720p run at a mean of 800 MHz, 0.62 of the sustained 1293 MHz. Runs at 1080p run at about 1050 MHz, 0.81. A trace does not change the clock.
- FSR's 2.8 ms ran at about 820 MHz. With 8 frames per submission, FSR takes 1.98 ms. MetalFX takes 2.55 ms. At an equal duty cycle, FSR costs 0.77 to 1.02 times MetalFX.
- MoltenVK compiles every engine shader with Metal's fast math, because no module has a float-controls mode. Fast math off costs up to 1.9 %. Argument buffers off speed cached samples by 4.9 %, but the engine then fails to create its shadow pipelines.
- Too tight for this machine: FSR's budget at a headless run's clock, accumulate direct (its reads alone take 0.46 ms at the top clock), and the depth pyramid's timer figure.
- Slow beyond the machine: the lean walk (2.3 to 2.7 times the prediction) and light candidates with direct light on the deck (2.19 ms at the top clock against 0.6 to 1.3 ms).

Open fork: section 11's budget can hold at the clock of headless runs or at the top clock. A GPU-bound windowed game likely reaches the top clock, but no window run was measured.

Proposed follow-up work:
- Record the GPU clock beside every timed run (tools/gpu-calibration/gpu-clock), and compare builds only at the same clock.
- GpuTimingTests.APassThatRecordsNothingAddsNearlyNoTime fails under a high load (0.056 to 0.072 against a bound of 0.05, in 4 of 9 full runs). Its bound depends on the load and the clock.
- Unroll the lean walk's step loop two to four times, and measure it at a recorded clock.

Raw data is in captures/3ug8/ (git ignores it): rounds/, upscalers/, extra/, clock-extra/, counts/, mvk-dump/ and analysis.md.


**2026-11-04T01:28:01Z**

2026-11-03: the first run (82edf212) built the spike and stopped at one fork, the clock of the budgets. The orchestrator settled it: the budgets hold at the clock of a windowed game, and one windowed measurement confirms it. Continuation run 106f1e1a-b3e5-4abc-b8ff-4e6c78f99033 (version 869b0b07) on the same branch and worktree measures it, updates the report, files the follow-ups, and then the review runs.


**2026-11-04T02:27:34Z**

2026-11-03, the windowed clock (the second run of the spike). The open fork of the first run is closed: the budgets of section 11 hold at the clock that a game reaches in a window. The window path keeps 2 frames in flight, presents with FIFO and has 3 swapchain images. The display is a virtual display of 1080 x 1920 at a reported 60 Hz, whose present caps at 120 frames per second. A window of 1920 x 1080 does not fit on it, so macOS made it 1080 x 1080. A window of 1080 x 1858 holds 0.97 of the pixels of 1920 x 1080. There, the GPU limits the frame rate, and the windowed clock was 1296 MHz in all 4 runs, 2 on the deck and 2 on lamp-hall-16, with the GPU busy 95 to 100 % of the time. At 1080 x 1080 the cap of 120 frames per second limits the frame rate, and the clock drops to 0.97 of the sustained clock on the deck and 0.81 to 0.83 on lamp-hall-16 (corrected in the review fix of 2026-11-04T03:25:10Z: window.sh had compared times as text, and first gave 0.91 and 0.80 to 0.83). Such a frame already fits its time. The rule that converts a headless time to the windowed clock: the measured time x the run's clock / 1296 MHz, for a pass whose time follows the clock. The report states every budget and every recommendation at the windowed clock, with the headless figures as context. New tools: tools/gpu-calibration/window.sh and its two scripts. locked.sh now tries the lock every 0.1 s, because another session's loop of runs took it again within milliseconds for more than 20 minutes. Follow-up tickets: ter-qk78 (record the GPU clock beside every timed run) and ter-scm7 (unroll the lean walk's step loop, linked to ter-gayt). The data of the flaky GpuTimingTests test is in ter-abun's note. Raw data: captures/3ug8/window/.

**2026-11-04T03:25:10Z**

2026-11-03, the review fix (the third run of the spike). The report now holds every pass against the roofline of its own counted work, as the note of 23:25:35Z asks, and MetalFX serves only as a reference of scale. tools/gpu-calibration/work.py counts the lane operations, texture reads and image bytes of each pass from MoltenVK's MSL, with the loops of a constant trip count unrolled by air-opt. fsr-trace.sh and fsr-passes.py time each of FSR's passes in a Metal System Trace of the batched measurement, over the frames at the top clock that no other process overlapped.

Results:
- FSR: five of its eight passes run at 0.9 to 1.2 times their roofline, Accumulate (57 % of the pass time) at 1.0. The two pyramids run at 2.0 and 3.0, the shading change at 2.3. 0.2 to 0.45 ms of each frame is GPU idle time between FSR's encoders, because MoltenVK ends a compute encoder at each barrier. FSR takes 1.99 ms at 1257 MHz over the timed span, 1.93 ms at the windowed clock: the budget of 1.0 to 2.0 ms holds at its top. The first run's 1.82 ms came from the clock of the whole run, which holds the setup at 648 MHz, and is withdrawn.
- The depth pyramid is ALU-bound and runs at 1.0 to 1.1 times its roofline (the first run gave 2.2 to 2.3 from its reads alone). Bloom runs at 1.4 to 2.1, accumulate direct at 1.6. The counted work of light candidates with direct light, with one pass of each loop, takes 1.64 ms, above their budget of 0.6 to 1.3 ms. The deck takes 1.2 to 1.6 times that, lamp-hall-64 0.6 to 0.8.
- The windowed clock on the deck at 1080 x 1080 is 0.97 of the sustained clock, not 0.91 (see the correction above).
- The variants of the FMA probe, now a mode of probes.swift with logs: 1, 2, 4 and 8 steps per pass reach 0.41, 0.77, 0.93 and 0.97 of the peak. 2 to 16 float4 chains in a rolled loop reach 0.37 to 0.45. The plain form x * a, x + b runs like the FMA form of 4 steps, and the first run's claim of 1.6 times the peak is withdrawn.
- The conversion rule is an estimate within about 15 %: on FSR's passes at 972 and 1296 MHz it gives 6 to 14 % less than the measured time, on the deck's frame 14 % more. The clocks of the epic's historic runs are assumed, with ranges.
- Section 11 restated at the windowed clock: today's frame is about 12.3 ms, the estimate at 1280 x 720 becomes 9.1 to 11.5 ms, and the controller settles at a scale of about 0.81 to 0.97.
- The FMA check now accepts a driver that rounds each product before its add (tolerance 1e-4, a test locks it).

Tools: tools/gpu-lock.sh replaces locked.sh as the one helper of the GPU lock, with waiters in the order in which they came, and time-round.sh uses it. analyze.py prints the window, upscaler and probe tables, with numbers compared as numbers. upscalers.sh replaces the scratch driver. The binaries live in captures/gpu-calibration/bin. The test project compiles its two sets of test shaders with one target pair. ter-scm7 is folded into ter-dzxf, which already owned the unrolled step loop. Raw data: captures/3ug8/ (variants/, fsr-trace/, fsr-dump/, fsr-counts/, pass-dump/, pass-counts/, upscalers-2/).

