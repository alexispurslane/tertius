---
id: ter-wwu7
status: closed
deps: [ter-gmv8]
links: [ter-cikj, ter-dgen]
created: 2026-11-03T23:39:41Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, portability]
---
# GPU pass timings on KosmicKrisp are impossible for four passes and zero for eighteen

Found on 2026-11-03 while ter-gmv8 ran the demo on KosmicKrisp (Mesa 26.2.4) with VK_ICD_FILENAMES set to its ICD: dotnet run --project src/Tertius.Demo -c Release -- --script scripts/lamp-hall-16.json --headless --out <folder> --video none. The run reached the end with exit code 0, but the GPU timings of four passes are impossible: occlusion upsample and reflection trace about 7.686e14 ms, ambient light and bloom about 3.392e9 ms, over 301 frames. Seven passes read plausible times: environment faces 1.42 ms, G-buffer 0.93 ms, direct light 3.49 ms, tone map 0.15 ms and the three SMAA passes 0.08 to 0.12 ms. The frame span reads 8.27 ms. At KosmicKrisp's timestampPeriod of 41.67 ns, 7.686e14 ms is about 1.8e19 ticks, near 2^64, so the end timestamp of such a pass is probably a little earlier than its begin timestamp and the difference wraps. Sort it as ter-cikj sorts KosmicKrisp failures: a defect of ours against the Vulkan spec (for example a timestamp written at a stage or in an order that the spec does not promise), or a KosmicKrisp defect with a minimal reproduction.

## Notes

**2026-11-04T00:13:59Z**

2026-11-03 (review of ter-gmv8): the other 18 of the 29 passes read a mean of exactly 0.000 ms over the 301 frames, so their timings are wrong too. They are deformation, shadow maps, local shadows, environment prefilter, surface test, depth pyramid, occlusion depth, occlusion sectors, occlusion denoise, scatter rows, scatter columns, radiance pyramid, reflections, haze, transparent, exposure, outlines and capture (/private/tmp/ter-gmv8-kk/gpu-timings.json). An earlier MoltenVK run on the same Mac, at the same 1280 x 720 over 301 frames (/private/tmp/ter4xw0-shots/gpu-timings.json, script not known), reads a time above zero for every pass, for example 2.01 ms for the depth pyramid, 0.79 ms for the radiance pyramid and 2.41 ms for the local shadows. One cause to test: KosmicKrisp writes a timestamp at a coarser point than the stage that the renderer asks for, such as the boundary of a Metal encoder. Then passes inside one encoder read one value (the zeros), and a pass whose begin and end fall on different boundaries in the wrong order wraps (the four huge values).

**2026-11-04T02:26:05Z**

2026-11-03 (ter-cikj): the cause is found, and it is a defect of KosmicKrisp, not of the renderer. ter-dgen holds the minimal reproduction, docs/research/kosmickrisp/timestamps.c. KosmicKrisp records every stamp outside a render pass into its open Metal compute encoder, with Metal's relaxed timestamp granularity. In one such encoder, every stamp reads the tick of the encoder's start, so a run of compute passes reads 0.000 ms each and the next render pass takes their time (the 18 zeros). From the fifth stamp in one encoder on, a stamp reads 0 and is marked available. A pass that ends at such a stamp wraps to about 7.686e14 ms, and the pass after it reads the whole GPU clock, 3.392e9 ms, which is this Mac's 39.3 days of uptime at 24 MHz. The spec forbids the 0s: a timestamp write that happens after another in the same submission must not have a lower value. The renderer's stamps, one at all commands before the first pass and after each pass, follow the spec, and MoltenVK gives sane values for the same commands. So the renderer does not change. This ticket can close with ter-cikj, and ter-dgen tracks the report to Mesa.

**2026-11-04T14:00:40Z**

2026-11-04: closed with ter-cikj, merged into main as a6aedea6c. The cause is KosmicKrisp's timestamps (ter-dgen).
