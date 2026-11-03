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
