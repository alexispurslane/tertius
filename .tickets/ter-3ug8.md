---
id: ter-3ug8
status: open
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

