# Spike: what the development Mac's GPU can do, and at which clock our runs measure it (ter-3ug8)

This spike separates the speed of the machine from the speed of our code. It runs compute kernels of a known amount of work, natively in Metal and through the engine's Vulkan device on MoltenVK. It records the GPU's clock during our timed runs. It times Apple's own temporal upscaler beside FSR 3.1. Then it restates the epic's measured pass times against what the machine predicts.

## Terms

- The peak: the highest rate that the GPU's units can reach at the highest clock, from published unit counts.
- A probe: a compute kernel of a known amount of work, whose result the CPU computes too.
- A share of the peak: a probe's measured rate divided by the peak.
- The clock: the GPU's frequency. The GPU switches between six performance states, from 388.8 MHz to 1296 MHz.
- The sustained clock: the clock after several seconds of full load, 1293 MHz.
- The run's clock: the mean clock over the GPU's active time during a timed run.
- A lane-cycle: one cycle of one of the GPU's 3,072 FP32 lanes.
- A rolled loop: a loop with one step of its work in each pass. An unrolled loop holds several steps in each pass.

## Result

1. This Mac has the 24-core M1 Max GPU, not the 32-core one. Its peaks are 7.96 TFLOPS, 248.8 Gtexel/s and 400 GB/s.
2. The machine reaches its peaks. The FMA probe reaches 96.7 % of the FP32 peak natively and 95.6 % through MoltenVK. Buffer reads reach 88 % and 87 % of 400 GB/s. MoltenVK adds no work per access, except about 7 % on cached texture samples.
3. A rolled loop of dependent FMAs reaches only 41 % of the peak, natively and through MoltenVK alike. Eight steps per pass reach 97 %. Apple's compiler causes this, not MoltenVK.
4. Our timed runs measure at a low clock. A headless run of the deck at 1280 × 720 runs at a mean of 800 MHz, with or without a trace. That is 0.62 of the sustained clock. At 1920 × 1080 it runs at about 1050 MHz, 0.81. A pass whose time follows the clock takes 0.62 or 0.81 of its measured time at the sustained clock.
5. FSR 3.1's 2.8 ms comes mostly from the clock. FSR's timing test waits for each frame, and its GPU runs at about 820 MHz. With eight frames per submission, FSR takes 1.98 ms at about 1190 MHz. MetalFX takes 2.55 ms. At an equal duty cycle, FSR takes 0.77 to 1.02 times MetalFX's time.
6. Two passes are slow beyond the machine. The lean walk takes 2.3 to 2.7 times what the machine predicts. Light candidates with direct light on the deck take 1.7 to 3.6 times their budget, even at the sustained clock. Accumulate direct is near what its reads cost, so its budget was too tight.

## The machine and its peaks

`system_profiler` and the I/O registry (`ioreg`, `AGXAcceleratorG13X`, `gpu-core-count = 24`) show a Mac Studio (Mac13,1) with the M1 Max and a 24-core GPU. The ticket assumed 32 cores. Every peak below is the 24-core GPU's.

| Peak | Value | How it follows | Source |
|---|---:|---|---|
| FP32 | 7.963 TFLOPS | 3,072 lanes × 2 FLOP per FMA × 1.296 GHz | Apple's keynote of 2021-10-18: the 32-core GPU has 4,096 execution units and 10.4 TFLOPS, so 128 lanes per core. The highest clock, 1,296 MHz, is the last entry of `voltage-states9` of this Mac's power manager (`ioreg`, node `pmgr`). Apple's 10.4 TFLOPS scaled to 24 cores is 7.8 TFLOPS. |
| Texel rate | 248.8 Gtexel/s | 192 texture units × 1.296 GHz, one bilinear sample of a 32-bit texel per unit per cycle | Apple publishes no texture unit count. hmc-tech.com lists 192 for the 24-core M1 Max. This peak has the weakest source. |
| Memory bandwidth | 400 GB/s | LPDDR5 on a 512-bit bus | Apple's tech specs of the Mac Studio (2022), support.apple.com/en-us/111900: "24-core GPU", "400GB/s memory bandwidth". |

The GPU has six performance states: 388.8, 486.0, 648.0, 777.6, 972.0 and 1296.0 MHz. They come from `voltage-states9`, which holds pairs of a frequency in Hz and a voltage.

## The probes

Each probe runs natively in Metal (`tools/gpu-calibration/probes.swift`) and in Vulkan through the engine's device (`GpuCalibrationTests`, shaders in `tests/Tertius.Rendering.Tests/Shaders`). The two forms hold the same work, the same constants and the same result.

| Probe | Work of one dispatch | The check against the CPU |
|---|---|---|
| FMA | 2²⁰ invocations × 1,024 steps × 16 independent chains of `x = fma(x, a, b)`, eight steps per pass of the loop: 34.4 GFLOP | Each sampled invocation's sum of its chains equals the CPU's fused result within 10⁻⁵. One dropped pass of 8 steps moves every result by more than that. |
| FMA, rolled | The same, one step per pass | The same |
| Texture, cached | 2²⁰ invocations × 512 bilinear samples of a 32 × 32 RGBA8 image (4 KiB), at texel corners | The sum of the samples equals the CPU's within 0.25 + 10⁻³ per sample. One dropped sample removes at least 1. |
| Texture, streamed | 2²⁰ invocations × 64 bilinear samples of a 16384 × 16384 RGBA8 image (1 GiB). Each texel feeds exactly one sample. | The same. A CPU test makes sure that the walk reads each texel once. |
| Bandwidth | 2²⁰ invocations × 64 reads of 16 bytes from a 1 GiB buffer, each element once | Each sampled invocation's integer sum equals the CPU's exactly. |

`a` and `b` and the counts come from push constants, so no compiler can fold the work. The streamed resources hold 1 GiB, 21 times the 48 MB system level cache. A first form of the bandwidth probe read a 256 MiB buffer four times. It reported 688 GB/s, above the peak, because each pass hit what the pass before had just cached. The streamed probes now read each byte once.

### The counts of the work

`tools/gpu-calibration/count.sh` counts each probe's work at three levels. Xcode's Metal toolchain gives AIR, Apple's intermediate code. It gives no listing of the native instructions: `metal-objdump` has "no instruction printer for target agx2", and `applegpu-nt` "cannot emit assembly". So the third level compares the size of the native code of each kernel, from a binary archive.

| Probe | SPIR-V | AIR of the native kernel | AIR of MoltenVK's MSL | Native code, native and MoltenVK |
|---|---|---|---|---|
| FMA | 32 vec4 `Fma` in one loop | 32 float4 `air.fma` | 32 float4 `air.fma` | 3,088 B and 3,040 B |
| FMA, rolled | 4 vec4 `Fma` in one loop | 4 float4 `air.fma` | 4 float4 `air.fma` | 1,984 B and 1,952 B |
| Texture | 1 `OpImageSampleExplicitLod` in one loop | 1 sample | 1 sample, and 2 loads of the argument buffer before the loop | 2,320 B and 2,480 B |
| Bandwidth | 1 load of a uvec4 in one loop | 1 device load | 1 device load, and 2 loads of the argument buffer | 1,936 B and 2,016 B |

The unrolled FMA kernel holds 112 more scalar FMAs than the rolled one. Its native code is 1,104 B larger, about 10 B per FMA. So the FMAs reach the native code. The MSL comes from MoltenVK's own dump (`MVK_CONFIG_SHADER_DUMP_DIR`).

### The shares of the peak

7 rounds, 30 timed dispatches per run after 5 of warm-up, 210 dispatches per row. Each time is a median with its 10th and 90th percentiles. Metal times are the command buffer's GPU start and end. Vulkan times are timestamps around the dispatch.

| Probe | Metal, ms | Share | Vulkan, ms | Share |
|---|---|---:|---|---:|
| FMA | 4.464 (4.463 to 4.465) | 0.967 of 7.96 TFLOPS | 4.513 (4.505 to 6.276) | 0.956 |
| FMA, rolled | 10.476 (10.475 to 10.580) | 0.412 | 10.693 (10.211 to 10.776) | 0.404 |
| Texture, cached | 3.277 (3.277 to 3.283) | 0.658 of 248.8 Gtexel/s | 3.514 (3.345 to 3.525) | 0.614 |
| Texture, streamed | 3.753 (3.629 to 4.009) | 0.072 of the texel rate, 0.715 of 400 GB/s | 3.706 (3.597 to 3.931) | 0.073, 0.724 |
| Bandwidth | 3.042 (3.001 to 3.164) | 0.882 of 400 GB/s | 3.101 (2.998 to 3.321) | 0.866 |

What the table shows:

- The FMA probe runs at the peak. So the FP32 peak of this GPU is real, and MoltenVK's translation costs about 1 %.
- The rolled FMA probe runs at 41 % of the peak in both APIs. More chains do not help: 4, 8 and 16 vec4 chains per invocation all ran at 1.64 × 10¹² FMA/s. Two, four and eight steps per pass ran at 58 %, about 80 % and 84 %. Without a native listing, the cause stays a guess. One move of each value that the loop carries, per pass, fits the numbers.
- Cached samples reach 61 to 66 % of the published texel rate. Either the peak's source is wrong for this GPU, or a texture unit takes more than one cycle per bilinear sample of a corner. MoltenVK's form is 7 % slower than the native form.
- Streamed samples and buffer reads run at 72 % and 87 to 88 % of 400 GB/s. These are the machine's real bandwidths.

Fast math folds the plain forms. In a scratch kernel, `x = x * a` and `x = x + b` in four unrolled steps ran at 1.6 times the FMA peak, because the compiler turned four multiplies into one. Only `fma()` chains measure the ALU.

On MoltenVK, a timestamp around one dispatch can latch late. This happened in 5 of the 21 runs of the Vulkan table. The FMA probe read 0.23 ms (5 %) short, and the rolled probe 0.49 ms short. The wall time from submission to the fence stayed the same. The Metal times have no such error, so this report takes them as the reference.

## The clock under our runs

`tools/gpu-calibration/gpu-clock.c` reads the GPU's performance states through IOReport, group "GPU Stats", channel `GPUPH`. It needs no root. `powermetrics` needs root, and this spike did not try sudo. The tool prints the share of the active time in each state, and the mean clock over the active time. The active time is the time that the GPU is not off, so it holds short idle times between command buffers.

7 rounds, the order of the runs rotated by one run per round:

| Run | Mean clock over the active time, MHz | Share of the active time at 648 / 778 / 972 / 1296 MHz |
|---|---|---|
| (a) FMA probe after 10 s of idle, Metal | 1205 (1056 to 1236) | 0.11 / 0.00 / 0.18 / 0.71 |
| (a) The same, Vulkan | 1064 (1052 to 1158) | 0.27 / 0.00 / 0.10 / 0.63 |
| (b) Demo, deck, 120 frames, 1280 × 720 | 800 (748 to 990) | 0.37 / 0.23 / 0.34 / 0.06 |
| (b) The same with the pulse probe beside it | 791 (737 to 1022) | 0.38 / 0.23 / 0.31 / 0.07 |
| (c) FMA probe after 8 s of full load, Metal | 1293 (1292 to 1295) | 0.00 / 0.00 / 0.00 / 0.99 |
| (c) The same, Vulkan | 1287 (1279 to 1294) | 0.01 / 0.00 / 0.00 / 0.98 |

Three more runs, outside the rounds: the deck at 1920 × 1080 ran at 1022 and 1076 MHz in two runs. The deck at 1280 × 720 under `tools/gpu-trace.sh --no-counters` ran at 807 MHz, so a Metal System Trace does not change the clock.

The demo's frame span in the 7 runs of (b) was 11.18 ms (9.61 to 11.80).

### (a) Cold

The first FMA dispatch after 10 s of idle takes 8.93 ms (6.25 to 9.33) in Metal, against 4.46 ms warm. The median times of the first dispatches are 8.93, 6.93, 5.95, 5.95, 4.54 and 4.46 ms. So the GPU reaches its top clock after about 28 ms of full load. Through Vulkan it takes six dispatches, about 50 ms.

### (b) Between frames

The headless demo draws a frame, waits for it, and reads it back, so the GPU idles between frames. The GPU's governor then keeps it at 648 to 972 MHz most of the time. A short FMA probe ran beside the demo in its own process, 2²⁰ × 64 × 16 FMAs every 20 ms. Over 655 probes it took 0.602 ms (0.402 to 0.603). At the sustained clock it takes 0.279 ms. Its usual time fits the 648 MHz state. The pulse shares the GPU with the demo, so it gives a lower bound of the clock, not the clock.

### The ratio

At 1280 × 720, the clock-limited throughput between frames is 800 / 1293 = 0.62 of the sustained throughput (0.58 to 0.77 over the runs). At 1920 × 1080 it is about 0.81. A pass whose time follows the clock, such as an ALU-bound walk, takes 0.62 of its measured 720p time at the sustained clock. A pass bound by DRAM bandwidth gains less, because the memory does not run on the GPU's clock.

Two more consequences:

- The clock moves with the duty cycle of the GPU. A change that shortens the frame lowers the share of active time, and the governor can then lower the clock. The fix stage of the direct light (ter-9nrc's note) saw unchanged passes run 10 to 25 % faster in one build's traces. Comparisons between builds hold only at the same clock, so a timed run records its clock.
- A real game that keeps frames in flight keeps the GPU busier than the headless runs. Its clock is likely higher. This spike did not measure a window run.

## MetalFX against FSR

`tools/gpu-calibration/metalfx.swift` runs `MTLFXTemporalScaler` from 1280 × 720 to 1920 × 1080 on the inputs of FSR's timing test. These are the same pattern of colored squares, the same two depths and zero motion. A reactive mask of zeros, an exposure of 1, reversed depth and RGBA16F output match FSR too. Each frame runs in its own command buffer, timed by its GPU start and end. It keeps 300 frames after 60 of warm-up, as FSR's test does.

7 rounds, the order rotated, each run beside `gpu-clock`:

| Upscaler | Frames | Median (p10 to p90), ms | Run medians, ms | Clock, MHz |
|---|---:|---|---|---|
| FSR 3.1, each frame waited for (FSR's timing test) | 2,100 | 2.825 (2.571 to 3.414) | 2.675 to 3.030 | 820 (736 to 861) |
| FSR 3.1, 8 frames per submission | 259 batches | 1.982 (1.965 to 2.059) | 1.967 to 2.004 | 1192 (1037 to 1237) |
| MetalFX, each frame in its own command buffer | 2,100 | 2.549 (2.455 to 2.756) | 2.482 to 2.776 | 1015 (803 to 1227) |
| MetalFX with a gap of 0.6 ms after each frame | 2,100 | 2.745 (2.682 to 2.806) | 2.729 to 2.780 | 648 (647 to 682) |

Per round, FSR in batches took 0.773 (0.722 to 0.795) of MetalFX's run median. FSR per frame took 1.021 (0.979 to 1.108) of MetalFX's with the gap. FSR per frame took 1.076 (1.009 to 1.221) of MetalFX's without the gap.

What makes the terms equal: the inputs, the sizes, the warm-up and the count of frames match. The duty cycle did not match at first. FSR's test records each frame in C# after the last one ends, and the GPU idles in between, so it ran at about 820 MHz. MetalFX's frames follow each other closely, and ran at up to 1296 MHz. The two added rows match the duty cycles from both sides. FSR's batch time is the span of 8 frames over 8, which holds whenever MoltenVK latches a stamp.

What the two upscalers do differently:

- FSR runs AMD's eight passes plus an exposure step. The passes make the reactive mask from the opaque-only image, then prepare the inputs and the luma pyramid. Then they find the shading change and its pyramid, the reactivity, the luma instability and the accumulation. It runs no sharpening (`src/Tertius.Rendering/ThirdParty/Fsr3/README.md`).
- MetalFX is closed. Apple does not document its passes. It gets the reactive mask as an input, with no pass to generate one, and auto exposure is off.
- MetalFX's time drops little with its clock: 2.75 ms at 648 MHz against 2.55 ms at about 1000 MHz. Its time follows memory more than the clock. FSR's time follows the clock.
- This spike compares cost only. It does not compare the images.

So FSR is not slow for this machine. At an equal duty cycle it costs 0.77 to 1.02 times Apple's own upscaler.

## MoltenVK's settings

The source of MoltenVK 1.4.2 (tag v1.4.2, SHA-256 of the archive `6864db53...bb1cb`) and of its pinned SPIRV-Cross (`6c09849f`) lay under `/private/tmp` as data. Nothing from them ran.

Fast math:

- `MVK_CONFIG_FAST_MATH_ENABLED` defaults to 2, "on demand" (`MVKEnvironment.h`, lines 183 and 184).
- On demand, `MVKDevice::getMTLCompileOptions` takes the flags from SPIRV-Cross's `get_fp_fast_math_flags`. Those flags hold every fast-math flag, unless the entry point has `SignedZeroInfNanPreserve`, `ContractionOff`, an `FPFastMathDefault` or an `FPFastMathMode` decoration.
- With every flag, MoltenVK sets `MTLMathModeFast` and `MTLMathFloatingPointFunctionsFast`.
- `spirv-dis` finds none of these modes and no `NoContraction` in any of the engine's 67 modules, FSR's eight passes among them. So every engine shader compiles with Metal's fast math.
- Fast math off (`MVK_CONFIG_FAST_MATH_ENABLED=0`) made the rolled FMA probe 1.9 % slower and the cached texture probe 1.0 % slower. The other probes did not change. Nothing is left to gain.
- Fast math lets Metal assume that no value is NaN or infinite. Code that works on MoltenVK can then differ on a Linux driver, which keeps NaN and infinity. CLAUDE.md already names one such case, a `clamp` that turns NaN into 0.

Other defaults that can add work per access:

- Argument buffers. MoltenVK uses Metal 3 argument buffers by default (`MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS=1`, logged as "Descriptor sets binding resources using Metal3 argument buffers"). The loads of the image and sampler handles sit before the loop in AIR. With argument buffers off, the cached texture probe ran 4.9 % faster: 3.342 ms (3.174 to 3.353) against 3.514 ms. The other probes did not change. But the engine cannot run that way. The demo stops at "vkCreateGraphicsPipelines(shadow cascades cutout) failed: ErrorInitializationFailed" in all 5 runs. So the faster setting is not open to the engine.
- Buffer bounds. MoltenVK adds no bounds checks on an Apple GPU, and the engine does not enable `robustBufferAccess` (`MVKDevice.mm`, line 5150). The bandwidth probe runs within 2 % of the native one.
- Image reads. Only `MVK_CONFIG_TEXTURE_1D_AS_2D` changes image access, and only for 1D images. The streamed texture probe runs as fast as the native one.

## The epic's passes against the machine

Each prediction is a lower bound of the work at the run's clock: the probes' measured rates, scaled by the run's clock over 1296 MHz. A multiple near 1 means the pass runs as fast as the machine lets it. The texture rate is the cached probe's 152.8 Gtexel/s through Vulkan. A 64-bit texel, such as RGBA16F, can take two cycles per fetch, so a prediction from fetches can be up to two times too low.

| Pass | Measured, ms | Where and how | Budget of section 11, ms | At the sustained clock, ms | Prediction at the run's clock, ms | Measured over the prediction |
|---|---|---|---|---|---|---|
| Lean walk V1, with miss reads, both kinds | 5.93 to 5.96 | Deck, 720p, trace, 800 MHz (lean-trace-spike.md) | About 1.1 for both dispatches | 3.67 to 3.69 | 2.59 | 2.3 |
| Lean walk V5c, with miss reads | 2.67 to 2.81 | The same | About 1.1 | 1.65 to 1.74 | 1.05 | 2.5 to 2.7 |
| FSR 3.1 | 2.8 | FSR's timing test, 820 MHz (ter-7m38) | 1.0 to 2.0 | 1.98, measured in batches | MetalFX at the same duty cycle: 2.75 | 1.02 |
| Light candidates + direct light | 1.63 + 1.90 = 3.53 | Deck, 720p, trace, 800 MHz (ter-9nrc) | 0.6 to 1.3 | 2.19 | No work model | |
| Light candidates + direct light | 0.83 + 0.82 = 1.66 | lamp-hall-64, 720p, trace (ter-nmbm) | 0.6 to 1.3 | 1.03 | No work model | |
| Accumulate direct | 1.18 | Deck, 720p, trace (ter-9nrc) | 0.15 to 0.30 | 0.73 | 0.74 | 1.6 |
| Accumulate direct | 0.67 | lamp-hall-64, 720p, trace (ter-nmbm) | 0.15 to 0.30 | 0.41 | No count of its lit pixels | |
| Depth pyramid | 0.36 to 0.39 (the timer gave 1.19) | 1080p, trace (ARCHITECTURE.md, "Screen-space reflections"), about 1050 MHz | 0.53 at 720p, from the timer's 1.19 | 0.29 to 0.32 | 0.17 | 2.2 to 2.3 |
| Bloom | 0.31 (pass-map.md), 0.46 (ARCHITECTURE.md) | 1080p, timer, about 1050 MHz | 0.47 with the tone map | 0.25 and 0.37 | 0.22 | 1.4 and 2.1 |

How each prediction follows:

- The walk. The machine's clock and lane count give 3,072 × 0.8 GHz = 2.46 × 10¹² lane-cycles per second at the run's clock. A walk's lanes step together, so a subgroup takes as many steps as its longest walk. On the deck there are about 3.0 × 10⁵ rays of each kind, from the measured times and nanoseconds per ray. V5c's subgroups take 53.1 and 35.0 steps, so it does 26.4 million lane-steps. V1's take 157.8 and 60.0, 65.3 million. The profiling spike estimates one read and about 40 operations per step. At the rate of a rolled loop, such a step takes 40 / 0.41 = 98 lane-cycles.
- In those terms, the walk spends 255 lane-cycles per lane-step in V5c and 224 in V1. That is about 105 operations at the rolled loop's rate. The ticket's 27,000 lane-cycles per ray assumed 4,096 lanes at 1,296 MHz. On this GPU at the run's clock, a reflection ray of V5c costs 5.5 ns × 3,072 × 0.8 GHz = 13,500 lane-cycles.
- Accumulate direct, from its shaders on the branch `ter-nmbm-direct-light`. The temporal dispatch reads at most 12 values per pixel. The spatial dispatch reads at most 69: its own value, its distance and its surface key, then for each of 8 neighbors a surface key of 3 reads and one value, twice. On the deck, 94 % of the 921,600 pixels see a surface. That is 70 million reads.
- The depth pyramid's first level reads the distance and the normal of 5 pixels for each of the 2.07 million pixels, 20.7 million reads. Its writes and its other levels add about 36 MB, 0.10 ms at 346 GB/s.
- Bloom, from its shaders: the prefilter reads 36 texels for each of the 960 × 540 texels of the first level. Five downsamples take 13 bilinear taps, and five upsamples take 9. That is 27 million reads.

## Recommendation

For section 11 of the design:

1. State the clock of every budget. The budget's times came from runs at 0.62 (720p) to 0.81 (1080p) of the top clock. At the top clock, a pass that follows the clock takes that share of its measured time.
2. FSR 3.1's estimate of 1.0 to 2.0 ms holds at the top clock: 1.98 ms. At the clock of a headless run, plan 2.8 ms. FSR needs no work in our code. It runs as fast as Apple's own upscaler.
3. Accumulate direct's estimate of 0.15 to 0.30 ms was too tight for this machine. Its reads alone take 0.46 ms at the top clock and 0.74 ms at the run's clock. Fewer reads lower it: one surface key image, as ter-9nrc proposes, removes most of the 48 reads of surface keys per pixel.
4. The depth pyramid's estimate of 0.53 ms at 720p came from a timer that holds part of the direct light's work. Its trace time scales to about 0.17 ms at 720p.
5. Bloom's estimate holds. Bloom runs within 1.4 to 2.1 times its read count.
6. The lean walk is slow beyond the machine: 2.3 to 2.7 times what a rolled loop of 40 operations per step predicts. The profiling spike (ter-sftw) owns its instruction count. The rolled loop's 41 % is a cause that this spike can name: the walk's step loop, unrolled two to four times, gains on this GPU and costs nothing elsewhere.
7. Light candidates and direct light on the deck take 2.19 ms at the top clock, against 0.6 to 1.3 ms. That gap is in our code. ter-9nrc lists the candidates to measure.

For the method of every ticket's timing:

- Run `gpu-clock 100` beside each timed run, and compare builds only at the same clock.
- A probe of the machine's clock must not wait for each frame, or it measures its own low duty cycle. FSR's timing test is such a test, so a batch of frames is the better measure of FSR's cost.

One decision is open, because this spike did not measure it. The budget can hold at the clock of headless runs or at the top clock, which a GPU-bound windowed game likely reaches. A window run with its clock will settle it.

## Method

- Machine: Mac Studio (Mac13,1), M1 Max with a 24-core GPU, 64 GB, MoltenVK 1.4.2 from the dev shell, Xcode's Metal toolchain 27A266a.
- Builds: Release. The Vulkan probes and FSR's tests run from the Release test project, the demo from the Release demo. The native tools build with `xcrun swiftc -O` and `xcrun clang -O2` outside the dev shell, because the dev shell hides Xcode's Metal toolchain from `xcrun`.
- The lock: every timed run took `/tmp/tertius-gpu-measure.lock` through `tools/gpu-calibration/locked.sh`. It waits while another run holds the lock, records uptime at the start and the end, and removes the lock on any exit.
- The load: other agents built and tested on the machine. The 1-minute load at the starts and ends of the runs of the main rounds was 4.86 to 26.48, median 11.13.
- The rounds: 7 rounds of 12 runs, from 18:38 to 18:47 on 2026-11-03. Each round rotated the order by one run. The upscaler rounds ran 7 rounds of 4 runs, from 19:14 to 19:20. The argument buffer runs and the clocks at 1080p and under a trace ran after them.
- Raw data: `captures/3ug8/` (git ignores it): `rounds/`, `upscalers/`, `extra/`, `clock-extra/`, `counts/`, `mvk-dump/` and `analysis.md`.

To repeat the runs, follow tools/README.md, "GPU calibration".

## Limits

- The texel peak's source is a third-party page, and the probe reaches 66 % of it. The texel shares can be off by that source's error.
- Apple ships no listing of the native instructions. The rolled loop's cause stays a guess.
- The predictions count reads and operations from the shaders' source. They assume full caches for the cached reads and one cycle per fetch. They are lower bounds.
- The clock at 1080p rests on two runs, and the clock under a trace on one.
- The light candidates and the direct light have no work model here. They draw full-screen triangles with shadow lookups, and this spike did not count those.
- No window run was measured, so the clock of a real game stays open.
