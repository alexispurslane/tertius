# Spike: what the development Mac's GPU can do, and at which clock our runs measure it (ter-3ug8)

This spike separates the speed of the machine from the speed of our code. It runs compute kernels of a known amount of work, natively in Metal and through the engine's Vulkan device on MoltenVK. It records the GPU's clock during our timed runs and during runs of the demo in a window. It counts the work of each pass and holds the pass's time against a roofline of that work. It times Apple's own temporal upscaler beside FSR 3.1 as a reference of scale. Then it restates the epic's pass times and section 11's budget at the clock of a window run.

## Terms

- The peak: the highest rate that the GPU's units can reach at the highest clock, from published unit counts.
- A probe: a compute kernel of a known amount of work, whose result the CPU computes too.
- A share of the peak: a probe's measured rate divided by the peak.
- The clock: the GPU's frequency. The GPU switches between six performance states, from 388.8 MHz to 1296 MHz.
- The top clock: the highest state, 1296 MHz.
- The sustained clock: the clock after several seconds of full load, 1293 MHz.
- The run's clock: the mean clock over the GPU's active time during a run's timed span. The timed span is the part of the run that the measurement times, without its setup.
- The windowed clock: the mean clock of a run of the demo in a window, over its steady span. The steady span runs from 5 s after the start to the last presented frame. When the GPU limits the frame rate, the windowed clock is the top clock.
- A lane-cycle: one cycle of one of the GPU's 3,072 FP32 lanes.
- A lane operation: one arithmetic operation of one lane, such as one component of a float4 add.
- A rolled loop: a loop with one step of its work in each pass. An unrolled loop holds several steps in each pass.
- The roofline of a pass: the largest of three quotients of the pass's counted work. The first is its lane operations over the measured ALU rate. The second is its texture reads over the measured texel rate. The third is its image bytes over the measured bandwidth. The largest quotient is the time that this GPU predicts for the pass.

## Result

1. This Mac has the 24-core M1 Max GPU, not the 32-core one. Its peaks are 7.96 TFLOPS, 248.8 Gtexel/s and 400 GB/s.
2. The machine reaches its peaks. The FMA probe reaches 96.7 % of the FP32 peak natively and 95.6 % through MoltenVK. Buffer reads reach 88 % and 87 % of 400 GB/s. MoltenVK adds no work per access, except about 7 % on cached texture samples.
3. A rolled loop of dependent FMAs reaches only 41 % of the peak, natively and through MoltenVK alike. Two steps per pass reach 77 %, four reach 93 % and eight reach 97 %. More chains in a rolled loop do not help. Apple's compiler causes this, not MoltenVK.
4. Our timed runs measure at a low clock. A headless run of the deck at 1280 × 720 runs at a mean of 800 MHz, with or without a trace. That is 0.62 of the top clock. At 1920 × 1080 it runs at about 1050 MHz, 0.81.
5. A game in a window runs at the top clock. The demo in a window keeps two frames in flight. When the GPU limits its frame rate, the windowed clock is 1296 MHz on the deck and on lamp-hall-16. So every budget below holds at the windowed clock. An estimate converts a headless time to it: the measured time × the run's clock / 1296 MHz.
6. FSR 3.1 runs near its roofline. Five of its eight passes take 0.9 to 1.2 times what their own counted work predicts. The two pyramid passes take 2.0 and 3.0 times, and the shading change, 0.01 ms, 2.3 times. At the windowed clock FSR takes 1.93 to 1.99 ms a frame, at the top of its budget of 1.0 to 2.0 ms. About 0.2 to 0.45 ms of that frame is GPU idle time between FSR's encoders.
7. Against their own counted work, the depth pyramid runs at 1.0 to 1.1 times its roofline and bloom at 1.4 to 2.1. Accumulate direct runs at 1.6 times its roofline, which alone is above its budget. Light candidates with direct light count 1.64 ms of work with one pass of each loop. That is above their budget of 0.6 to 1.3 ms. The lean walk takes 2.1 to 3.3 times an estimate of its work at the rolled loop's rate.

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
| FMA | 2²⁰ invocations × 1,024 steps × 16 independent chains of `x = fma(x, a, b)`, eight steps per pass of the loop: 34.4 GFLOP | Each sampled invocation's sum of its chains equals the CPU's fused result within 10⁻⁴ of it. One dropped pass of 8 steps moves every result by at least 3.7 × 10⁻⁴ of it. A driver that rounds each product before its add moves it by at most 3.4 × 10⁻⁵. GLSL lets a driver do that for an `fma()` that no precise variable consumes. |
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
- The rolled FMA probe runs at 41 % of the peak in both APIs. The next section measures why.
- Cached samples reach 61 to 66 % of the published texel rate. Either the peak's source is wrong for this GPU, or a texture unit takes more than one cycle per bilinear sample of a corner. MoltenVK's form is 7 % slower than the native form.
- Streamed samples and buffer reads run at 72 % and 87 to 88 % of 400 GB/s. These are the machine's real bandwidths.

On MoltenVK, a timestamp around one dispatch can latch late. This happened in 5 of the 21 runs of the Vulkan table. The FMA probe read 0.23 ms (5 %) short, and the rolled probe 0.49 ms short. The wall time from submission to the fence stayed the same. The Metal times have no such error, so this report takes them as the reference.

### The variants of the FMA probe

`probes fma-variants` runs seven variants of the native FMA probe and the plain form. A variant has V independent float4 chains and S steps of every chain in each pass of its loop. The tool writes each variant out statement by statement, because the compiler kept a loop over an array of chains rolled and in memory. In AIR, each variant holds V × S float4 `air.fma` in its loop. The variant of 4 chains and 8 steps is the FMA probe, and the variant of 4 chains and 1 step is the rolled probe.

7 rounds at 1287 to 1296 MHz, 30 timed dispatches per variant and round (`captures/3ug8/variants/`, `analyze.py probes`). Other sessions used the GPU during these rounds, so the medians hold their slowdowns. The 10th percentile shows the variant without them.

| Variant | Median (p10 to p90), ms | Share of the peak at the median | At the 10th percentile |
|---|---|---:|---:|
| 4 chains, 1 step (rolled) | 11.886 (10.476 to 17.387) | 0.363 | 0.412 |
| 4 chains, 2 steps | 5.618 (5.617 to 8.594) | 0.768 | 0.768 |
| 4 chains, 4 steps | 4.639 (4.638 to 7.064) | 0.930 | 0.930 |
| 4 chains, 8 steps (the FMA probe) | 4.464 (4.463 to 5.169) | 0.967 | 0.967 |
| 2 chains, 1 step | 5.884 (5.872 to 9.018) | 0.367 | 0.367 |
| 8 chains, 1 step | 20.249 (19.805 to 26.636) | 0.426 | 0.436 |
| 16 chains, 1 step | 39.916 (38.671 to 52.279) | 0.432 | 0.446 |
| Plain form, 4 chains, 4 steps | 4.641 (4.639 to 7.004) | 0.930 | 0.930 |

What the table shows:

- The steps per pass decide the rate. One step reaches 41 %, two 77 %, four 93 % and eight 97 %.
- More chains do not help a rolled loop. With 2 to 16 float4 chains, it stays at 37 to 45 %.
- A cost per pass of the loop fits the numbers, such as one move of each value that the loop carries. Without a native listing, the cause stays a guess.
- The plain form, `x = x * a` then `x = x + b`, runs as fast as the variant of 4 steps. Fast math contracts each pair into one FMA. The first run of this spike reported that a scratch kernel of this form ran at 1.6 times the FMA peak. That kernel left no source and no log, and this form does not repeat it. This report withdraws the claim.

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

Three more runs, outside the rounds: the deck at 1920 × 1080 ran at 1022 and 1076 MHz in two runs. The deck at 1280 × 720 under `tools/gpu-trace.sh --no-counters` ran at 807 MHz, so a Metal System Trace does not change the clock. A scratch driver outside the tools ran these three runs, and their logs are in `captures/3ug8/clock-extra/`.

The demo's frame span in the 7 runs of (b) was 11.18 ms (9.61 to 11.80).

### (a) Cold

The first FMA dispatch after 10 s of idle takes 8.93 ms (6.25 to 9.33) in Metal, against 4.46 ms warm. The median times of the first dispatches are 8.93, 6.93, 5.95, 5.95, 4.54 and 4.46 ms. So the GPU reaches its top clock after about 28 ms of full load. Through Vulkan it takes six dispatches, about 50 ms.

### (b) Between frames

The headless demo draws a frame, waits for it, and reads it back, so the GPU idles between frames. The GPU's governor then keeps it at 648 to 972 MHz most of the time. A short FMA probe ran beside the demo in its own process, 2²⁰ × 64 × 16 FMAs every 20 ms. Over 655 probes it took 0.602 ms (0.402 to 0.603). At the sustained clock it takes 0.279 ms. Its usual time fits the 648 MHz state. The pulse shares the GPU with the demo, so it gives a lower bound of the clock, not the clock.

### The ratio

At 1280 × 720, the clock between frames is 800 / 1296 = 0.62 of the top clock (0.58 to 0.76 over the runs). At 1920 × 1080 it is about 0.81 (0.79 to 0.83). A pass whose time follows the clock, such as an ALU-bound walk, takes 0.62 of its measured 720p time at the top clock. A pass bound by DRAM bandwidth gains less, because the memory does not run on the GPU's clock.

Two more consequences:

- The clock moves with the duty cycle of the GPU. A change that shortens the frame lowers the share of active time, and the governor can then lower the clock. The fix stage of the direct light (ter-9nrc's note) saw unchanged passes run 10 to 25 % faster in one build's traces. Comparisons between builds hold only at the same clock, so a timed run records its clock.
- A real game keeps frames in flight, so it keeps the GPU busier than the headless runs. The next section measures its clock.

## The clock of a window run

The headless demo waits for each frame, so its clock is not the clock of a game. This section runs the demo in a window, as a player sees it, beside `gpu-clock`.

### How the window path presents

The code of the window path sets these terms:

- `WindowHost` plays a script on the wall clock, 60 ticks per second. Its render thread draws the latest state again and again. It does not wait for a frame, and it reads no frame back, except for a capture.
- `RendererOptions.FramesInFlight` is 2: the CPU records a frame while the GPU runs the frame before it.
- `RendererOptions.VSync` is true, so `SwapchainTarget.ChoosePresentMode` takes FIFO: each image waits for its turn on the display.

  ```csharp
  private PresentModeKHR ChoosePresentMode()
  {
      if (vsync)
          return PresentModeKHR.FifoKhr;
      ...
  ```

- The swapchain has `capabilities.MinImageCount + 1` images. MoltenVK logs "Created 3 swapchain images". With FIFO, MoltenVK keeps the layer's display sync on (`MVKSwapchain.mm`, line 506).
- A script in the window uses the renderer's default options, with no impostors, as a headless run does.

### The display

The Mac has one display, 1080 × 1920 pixels in portrait, at a scale of 1. macOS reports 60 Hz for it. CoreGraphics gives it the vendor "unkn" and the model "virt", so it is a virtual display.

A window of 1920 × 1080 does not fit on it. With `--size 1920x1080`, macOS made the window 1080 × 1080, 0.56 of the pixels of 1920 × 1080. So each view ran in two sizes. The second size, `--size 1080x1920`, became 1080 × 1858, the largest window below the menu bar. It holds 0.97 of the pixels of 1920 × 1080.

The presents never passed 120 per second: MoltenVK logged each 60 frames 0.500 s apart. So the display path caps the frame rate at 120 Hz, although macOS reports 60 Hz. This spike did not find why. The virtual display's compositor is the likely cause.

### The measurement

The runs used `tools/gpu-calibration/window.sh` with the scripts `window-deck.json` and `window-lamp-hall-16.json`. The deck script shows the deck camera of demo-exit for 1,119 ticks, the ticks of the headless deck runs and 600 more. The hall script shows lamp-hall-16 for 1,080 ticks. Each run ends with one capture, which shows the intended view.

Beside each run, `gpu-clock` sampled the clock every 100 ms. A second sampler read the GPU's busy share every 0.5 s, from the property `"Device Utilization %"` of the I/O registry's `AGXAccelerator`. The busy share differs from gpu-clock's active share. The active share is the time that the GPU is not off, and in every window run it was 0.998 to 1.000. MoltenVK's performance log (`MVK_CONFIG_PERFORMANCE_TRACKING=1`) gave the frame rate and the wait for a drawable. Every figure is over the steady span, 126 to 135 samples per run. The steady span runs from 5 s after the start to MoltenVK's last log of presented frames. The wait for a drawable is MoltenVK's mean over the whole run. `analyze.py windows captures/3ug8/window` prints the table.

| View | Window | Runs | Windowed clock, MHz | Share of the top clock | Busy share, % | Frames per second | Wait for a drawable, ms |
|---|---|---:|---|---|---|---|---|
| Deck | 1080 × 1858 | 2 | 1296.0, 1296.0 | 1.00 | 100.0, 100.0 | 93.9, 93.2 | 0.10, 0.10 |
| lamp-hall-16 | 1080 × 1858 | 2 | 1296.0, 1296.0 | 1.00 | 95.2, 100.0 | 83.7, 112.0 | 0.12, 0.07 |
| Deck | 1080 × 1080 | 3 | 1252.5, 1254.9, 1258.9 | 0.97 | not sampled, 95.7, 95.9 | 119.8, 119.2, 118.4 | 5.61, 5.20, 5.19 |
| lamp-hall-16 | 1080 × 1080 | 2 | 1045.2, 1070.3 | 0.81 to 0.83 | 90.5, 89.4 | 119.9, 119.9 | 7.36, 7.38 |

The busy share of 95.2 % holds one sample of 0 % near the end of the steady span, between samples of 100 %. The first deck run at 1080 × 1080 ran before the busy sampler existed. The rate of lamp-hall-16 at 1080 × 1858 varied between its two runs, but its clock did not.

Before this review, window.sh's summary compared the time of a sample as text, so it kept only the samples from 5 s to 10 s. The report then gave the deck at 1080 × 1080 as 1171 to 1190 MHz, 0.91 to 0.92. The summary now lives in `analyze.py` and compares numbers.

What the table shows:

- At 1080 × 1858, the GPU limits the frame rate. The CPU waits about 0.1 ms for a drawable, and the GPU is busy 95 to 100 % of the time. The windowed clock is the top state, 1296 MHz, in all four runs.
- At 1080 × 1080, the cap of 120 frames per second limits the frame rate. The CPU waits 5 to 7 ms for each drawable, and the GPU has slack in each frame. The governor then lowers the clock, to 0.97 of the top clock on the deck and to 0.81 to 0.83 on lamp-hall-16.
- A frame that the display cap limits already fits in its time. Its clock is lower, but no budget binds it then. When the GPU limits the frame rate, a budget binds the frame, and the GPU runs at 1296 MHz.

So the budgets of section 11 of the design hold at the windowed clock, 1296 MHz. It is the sustained clock of 1293 MHz within 0.3 %.

### The rule that converts a headless time to the windowed clock

For a pass whose time follows the clock, an estimate of the time in a window is:

```text
windowed time = measured time × the run's clock / 1296 MHz
```

The run's clock is the clock that `gpu-clock` recorded over the measured run's timed span. A measurement that prints the markers `timed-start` and `timed-end` around its timed work gets that clock from `span_clock` of `tools/gpu_clock.py` (tools/README.md, "The GPU clock of a timed run"). A run's clock over the whole process holds its setup, which runs at 648 MHz. FSR's batched runs show the difference: 1208 MHz over the whole run, 1257 MHz over the timed span.

No run of the epic before this spike recorded its clock. For such a run, this report assumes the clock of this spike's runs of the same kind. It gives the range that follows from their spread:

| Kind of the historic run | Assumed clock, MHz | Factor to the windowed clock |
|---|---|---|
| Headless deck at 1280 × 720, as in lean-trace-spike.md, ter-9nrc and ter-nmbm | 800 (748 to 990) | 0.62 (0.58 to 0.76) |
| Headless deck at 1920 × 1080, as in pass-map.md and ARCHITECTURE.md | 1050 (1022 to 1076) | 0.81 (0.79 to 0.83) |

The view changes the duty cycle, and so the clock. The runs of lamp-hall-64 had another frame time than the deck, so their assumed clock is less sure than the deck's.

The rule holds best for a pass bound by its ALU work or by its cached reads, because those run on the GPU's clock. A pass bound by DRAM bandwidth gains less, because the memory does not run on the GPU's clock. Its windowed time lies between the converted time and the measured time.

Two checks bound the rule's error:

- FSR's passes at two known clocks. In the traces of FSR (see "FSR 3.1 against its own work"), the frames run at 972 MHz or at 1296 MHz. The rule converts each pass's time at 972 MHz to 0.75 of it. The measured time at 1296 MHz is 6 to 14 % more than the converted time: 6 % for Accumulate and PrepareReactivity, 8 % for LumaPyramid, 11 % for ShadingChangePyramid and 14 % for PrepareInputs.
- The deck's frame. The headless deck at 1920 × 1080 has a frame span of 16.0 ms at 1022 MHz. The rule converts that to 12.6 ms, and 12.2 ms for the 0.97 of its pixels that the window holds. The deck at 1080 × 1858 takes 10.7 ms per frame in the window. So here the rule gives 14 % more. The frame interval of a window and the frame span of a headless run measure different spans, and this spike did not split the difference.

So the rule is an estimate, within about 15 % either way. A budget check near its limit needs a measurement at the windowed clock, such as FSR's batched measurement.

## FSR 3.1 against its own work

The fair test of FSR holds each of its passes against its own counted work, the roofline of the pass.

### The time of each pass

`tools/gpu-calibration/fsr-trace.sh` records a Metal System Trace of FSR's batched measurement: 45 submissions of 8 frames from 1280 × 720 to 1920 × 1080, of which the last 37 are timed. MoltenVK ends its compute encoder at each pipeline barrier (`MVKCmdPipeline.mm`, lines 160 and 161), and FSR's `Record` puts a barrier before each dispatch. So each pass of a frame shows as one encoder of its own. `fsr-passes.py` takes each pass's busy time, the union of its encoder's GPU intervals. It keeps only the frames of the timed submissions in which no other process ran on the GPU: 279, 270 and 280 of the 296 frames of three traces. Other sessions' test runs ran on the GPU during the traces, and they take no GPU lock. On 2026-11-04, ticket `ter-q58r` changed the batched measurement to 60 warm-up and 150 timed submissions, with two submissions on the queue at once and 16 frames in flight. The figures in this section come from the old lengths, so a new trace gives other counts.

Under a trace, the clock moves between 972 and 1296 MHz from frame to frame. Each pass's times fall into two clusters, 1.25 to 1.33 times apart. The frames at the top clock are those whose summed pass time lies within 10 % of the 10th percentile. They were 25 %, 70 % and 19 % of the frames of the three traces. These shares imply a mean clock of 1053, 1198 and 1035 MHz, and gpu-clock measured 1047, 1174 and 1020 MHz over the timed spans. So the fast cluster is the top clock.

The medians at the top clock agree across the three traces within 2 %. The summed pass time at the top clock is 1.71 ms. The frame span at the top clock, from one frame's stamp to the next, is 2.14 to 2.16 ms under the trace.

### The count of each pass's work

`tools/gpu-calibration/work.py` counts the work of each pass from MoltenVK's MSL of FSR's shaders. It compiles each kernel into AIR, unrolls every loop of a constant trip count with the toolchain's `air-opt`, and counts per invocation:

- The lane operations: each arithmetic, compare, select or conversion instruction and each math call, weighted by its vector width.
- The texture reads: each sample, gather or read call.
- The loops that remain, those whose trip count is not a constant.

The count is static, so both sides of a branch count. The dispatch sizes come from `Fsr3Upscaler.Groups` and each shader's workgroup size. The bytes are the footprint of the images that the pass binds, each read or written once, a lower bound of its memory traffic. The rates are this spike's Vulkan probes at the top clock. The FMA probe gives 3.81 × 10¹² lane operations per second, 0.956 of the peak. The cached texture probe gives 153.1 × 10⁹ texture reads per second. The bandwidth probe gives 346 GB/s.

| Pass | Invocations | Lane operations per invocation | Texture reads per invocation | Image bytes, MB | ALU, ms | Texture, ms | Bytes, ms | Prediction, ms | Bound | Measured at the top clock, ms | Measured over the prediction |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|
| Exposure step | 1 | 4 | 0 | 0.0 | 0.000 | 0.000 | 0.000 | 0.000 | | 0.005 | A fixed cost per dispatch |
| GenerateReactive | 921,600 | 54 | 2 | 15.7 | 0.013 | 0.012 | 0.045 | 0.045 | Bytes | 0.055 | 1.2 |
| PrepareInputs | 921,600 | 168 | 11 | 33.2 | 0.041 | 0.066 | 0.096 | 0.096 | Bytes | 0.106 | 1.1 |
| LumaPyramid | 61,440 | 1,030 | 69 | 4.1 | 0.017 | 0.028 | 0.012 | 0.028 | Texture | 0.056 | 2.0 |
| ShadingChangePyramid | 61,440 | 3,660 | 225 | 8.6 | 0.059 | 0.090 | 0.025 | 0.090 | Texture | 0.268 | 3.0 |
| ShadingChange | 230,400 | 37 | 3 | 1.5 | 0.002 | 0.005 | 0.004 | 0.005 | Texture | 0.010 | 2.3 |
| PrepareReactivity | 921,600 | 388 | 27 | 20.7 | 0.094 | 0.162 | 0.060 | 0.162 | Texture | 0.140 | 0.9 |
| LumaInstability | 921,600 | 109 | 5 | 18.4 | 0.026 | 0.030 | 0.053 | 0.053 | Bytes | 0.065 | 1.2 |
| Accumulate | 2,073,600 | 1,729 | 41 | 52.3 | 0.942 | 0.555 | 0.151 | 0.942 | ALU | 0.918 | 1.0 |
| The passes | | | | | | | | 1.421 | | 1.624 | 1.1 |

What the table shows:

- Five passes run at 0.9 to 1.2 times their roofline. Accumulate, 57 % of FSR's pass time, is ALU-bound and runs at its roofline. The shading change runs at 2.3 times, but it takes 0.01 ms.
- The two pyramids run at 2.0 and 3.0 times their roofline. They are AMD's single-pass downsampler. Each group writes its levels through threadgroup memory with barriers, and the last group builds the small levels alone. The count holds neither the threadgroup memory nor the barriers, so their gap is not a measured cause. Together they take 0.32 ms, 20 % of the pass time.
- PrepareReactivity runs below its roofline, at 0.9. It keeps 3 loops after the unroll, and its count holds both sides of each branch. So its count is not a strict bound.
- Between the passes, the GPU idles. Under the trace, the frame span at the top clock is 2.14 to 2.16 ms. The passes, AMD's clears and the update of the constants take 1.71 ms of it. Without the trace, FSR's batches take 1.99 ms at 1257 MHz, about 1.93 ms at the top clock by the rule. So 0.2 to 0.45 ms of each frame is the gap between FSR's ten encoders and its clears. MoltenVK starts a new encoder at each barrier, and the GPU waits for each encoder to drain before the next starts.

### FSR at the windowed clock

The batched measurement now prints the markers of its timed span. 5 new rounds of `tools/gpu-calibration/upscalers.sh` ran from 22:11 to 22:12 on 2026-11-03 (`captures/3ug8/upscalers-2/`, `analyze.py upscalers`):

| Upscaler | Frames or batches | Median (p10 to p90), ms | Run medians, ms | Clock of the whole run, MHz | Clock over the timed span, MHz |
|---|---:|---|---|---|---|
| FSR 3.1, each frame waited for (FSR's timing test) | 1,500 | 2.668 (2.567 to 3.070) | 2.634 to 2.740 | 880 (809 to 889) | |
| FSR 3.1, 8 frames per submission | 185 batches | 1.991 (1.970 to 2.185) | 1.975 to 2.085 | 1208 (1203 to 1226) | 1257 (1241 to 1272) |
| MetalFX, each frame in its own command buffer | 1,500 | 2.506 (2.430 to 2.663) | 2.469 to 2.643 | 1225 (846 to 1258) | |
| MetalFX with a gap of 0.6 ms after each frame | 1,500 | 2.734 (2.678 to 2.800) | 2.717 to 2.761 | 648 (648 to 659) | |

So FSR takes 1.99 ms at 1257 MHz, and 1.93 ms at the windowed clock by the rule. The budget of 1.0 to 2.0 ms holds at its top, with almost no margin.

The first run of this spike gave FSR's batches 1192 MHz, the mean over the whole run. That mean holds about 0.4 s of setup at 648 MHz. Over the rounds, the run clock went from 1037 to 1237 MHz, but the batch medians stayed at 1.967 to 2.004 ms. The batches themselves ran near 1296 MHz. The first run's figure of 1.82 ms at the windowed clock came from that mean, and this report withdraws it.

### MetalFX as a reference of scale

`tools/gpu-calibration/metalfx.swift` runs `MTLFXTemporalScaler` from 1280 × 720 to 1920 × 1080 on the inputs of FSR's timing test. These are the same pattern of colored squares, the same two depths and zero motion. A reactive mask of zeros, an exposure of 1, reversed depth and RGBA16F output match FSR too. Each frame runs in its own command buffer, timed by its GPU start and end. It keeps 300 frames after 60 of warm-up, as FSR's test does.

MetalFX's time is only a reference of what a vendor-tuned temporal upscaler costs on this GPU. MetalFX does the same job as FSR, with the same inputs and one upscaled output. But it runs another algorithm, and Apple does not document its internals. So its time says nothing about whether FSR runs as fast as this GPU allows. The rooflines above answer that.

The first run's 7 rounds, the order rotated, each run beside `gpu-clock` (`captures/3ug8/upscalers/`). The clocks are means over the whole run:

| Upscaler | Frames | Median (p10 to p90), ms | Run medians, ms | Clock of the whole run, MHz |
|---|---:|---|---|---|
| FSR 3.1, each frame waited for (FSR's timing test) | 2,100 | 2.825 (2.571 to 3.414) | 2.677 to 3.030 | 820 (736 to 861) |
| FSR 3.1, 8 frames per submission | 259 batches | 1.982 (1.965 to 2.059) | 1.967 to 2.004 | 1192 (1037 to 1237) |
| MetalFX, each frame in its own command buffer | 2,100 | 2.549 (2.455 to 2.756) | 2.483 to 2.776 | 1015 (803 to 1227) |
| MetalFX with a gap of 0.6 ms after each frame | 2,100 | 2.745 (2.682 to 2.806) | 2.729 to 2.780 | 648 (647 to 682) |

`captures/3ug8/analysis.md` gives FSR's timing test as 2.715 ms. That figure pools the 7 runs of the timing test inside the main calibration rounds (`rounds/`, 18:38 to 18:47). The table above pools the 7 runs of the upscaler rounds (`upscalers/`, 19:14 to 19:20). They are different runs under another load and clock.

The duty cycle sets the clock. FSR's timing test records each frame in C# after the last one ends. The GPU idles in between, so it ran at about 820 MHz. MetalFX's frames follow each other closely, and ran at up to 1296 MHz. MetalFX's time drops little with its clock: 2.75 ms at 648 MHz against 2.55 ms at about 1000 MHz. So its time follows memory more than the clock.

What the two upscalers do differently:

- FSR runs AMD's eight passes plus an exposure step. The passes make the reactive mask from the opaque-only image, then prepare the inputs and the luma pyramid. Then they find the shading change and its pyramid, the reactivity, the luma instability and the accumulation. It runs no sharpening (`src/Tertius.Rendering/ThirdParty/Fsr3/README.md`).
- MetalFX is closed. Apple does not document its passes. It gets the reactive mask as an input, with no pass to generate one, and auto exposure is off.
- This spike compares cost only. It does not compare the images.

## MoltenVK's settings

The source of MoltenVK 1.4.2 lay under `/private/tmp` as data: tag v1.4.2, SHA-256 of the archive `6864db53...bb1cb`. So did its pinned SPIRV-Cross (`6c09849f`). Nothing from them ran.

Fast math:

- `MVK_CONFIG_FAST_MATH_ENABLED` defaults to 2, "on demand" (`MVKEnvironment.h`, lines 183 and 184).
- On demand, `MVKDevice::getMTLCompileOptions` takes the flags from SPIRV-Cross's `get_fp_fast_math_flags`. Those flags hold every fast-math flag, unless the entry point has `SignedZeroInfNanPreserve`, `ContractionOff`, an `FPFastMathDefault` or an `FPFastMathMode` decoration.
- With every flag, MoltenVK sets `MTLMathModeFast` and `MTLMathFloatingPointFunctionsFast`.
- `spirv-dis` finds none of these modes and no `NoContraction` in any of the engine's 67 modules, FSR's eight passes among them. So every engine shader compiles with Metal's fast math.
- Fast math off (`MVK_CONFIG_FAST_MATH_ENABLED=0`) made the rolled FMA probe 1.9 % slower and the cached texture probe 1.0 % slower. The other probes did not change. Nothing is left to gain.
- Fast math lets Metal assume that no value is NaN or infinite. Code that works on MoltenVK can then differ on a Linux driver, which keeps NaN and infinity. CLAUDE.md already names one such case, a `clamp` that turns NaN into 0.

Other defaults that can add work per access:

- Argument buffers. MoltenVK uses Metal 3 argument buffers by default (`MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS=1`, logged as "Descriptor sets binding resources using Metal3 argument buffers"). The loads of the image and sampler handles sit before the loop in AIR. With argument buffers off, the cached texture probe ran 4.9 % faster: 3.342 ms (3.174 to 3.353) against 3.514 ms. The other probes did not change. But the engine cannot run that way. The demo stops at "vkCreateGraphicsPipelines(shadow cascades cutout) failed: ErrorInitializationFailed" in all 5 runs. So the faster setting is not open to the engine. A scratch driver, `captures/3ug8/extra-rounds.sh`, ran these 5 runs (`extra/`).
- If the device has residency sets, argument buffers also end the compute encoder at each pipeline barrier (`MVKCmdPipeline.mm`, lines 160 and 161). So each dispatch after a barrier gets an encoder of its own, and the GPU idles between them (see "FSR 3.1 against its own work").
- Buffer bounds. MoltenVK adds no bounds checks on an Apple GPU, and the engine does not enable `robustBufferAccess` (`MVKDevice.mm`, line 5150). The bandwidth probe runs within 2 % of the native one.
- Image reads. Only `MVK_CONFIG_TEXTURE_1D_AS_2D` changes image access, and only for 1D images. The streamed texture probe runs as fast as the native one.

## The epic's passes against the machine

Each row holds the pass's time against the roofline of its own counted work, at the top clock. The column "At the windowed clock" converts each measured time by the rule. It uses the assumed clock of its kind of run, and the range of that clock. A multiple near 1 means the pass runs as fast as its counted work lets it. A multiple holds the rule's error too, about 15 % either way.

The counts come from MoltenVK's MSL of each pass, by `work.py count` (`captures/3ug8/pass-counts/counts.txt`). The light passes and accumulate direct come from a headless run of the deck on the branch `ter-nmbm-direct-light`. The depth pyramid and bloom come from this branch, which has main's engine. The rates are those of FSR's table. A 64-bit texel, such as RGBA16F, can take two cycles per fetch, so a texture term can be up to two times too low.

| Pass | Measured, ms | Where and how | Budget of section 11, ms | At the windowed clock, ms | ALU, ms | Texture, ms | Bytes, ms | Prediction, ms | Measured over the prediction |
|---|---|---|---|---|---:|---:|---:|---:|---|
| Lean walk V1, with miss reads, both kinds | 5.93 to 5.96 | Deck, 720p, trace (lean-trace-spike.md) | About 1.1 for both dispatches | 3.66 to 3.68 (3.42 to 4.55) | 0.69, or 1.61 at the rolled loop's rate | 0.43 | 0.03 | 1.61 | 2.3 (2.1 to 2.8) |
| Lean walk V5c, with miss reads | 2.67 to 2.81 | The same | About 1.1 | 1.65 to 1.74 (1.54 to 2.15) | 0.28, or 0.65 at the rolled loop's rate | 0.17 | 0.03 | 0.65 | 2.5 to 2.7 (2.4 to 3.3) |
| FSR 3.1 | 1.99 | Batched, 1257 MHz over the timed span (this spike) | 1.0 to 2.0 | 1.93 | 1.19 | 0.95 | 0.45 | 1.42, the sum of its passes' rooflines | 1.1 for the passes, 1.4 for the frame |
| Light candidates + direct light | 1.63 + 1.90 = 3.53 | Deck, 720p, trace (ter-9nrc) | 0.6 to 1.3 | 2.18 (2.04 to 2.70) | 1.64 | 0.23 | 0.34 | 1.64, with one pass of each loop | 1.3 (1.2 to 1.6) |
| Light candidates + direct light | 0.83 + 0.82 = 1.66 | lamp-hall-64, 720p, trace (ter-nmbm) | 0.6 to 1.3 | 1.02 (0.96 to 1.27) | 1.64 | 0.23 | 0.34 | 1.64, with one pass of each loop | 0.6 (0.6 to 0.8) |
| Accumulate direct | 1.18 | Deck, 720p, trace (ter-9nrc) | 0.15 to 0.30 | 0.73 (0.68 to 0.90) | 0.34 | 0.46 | 0.30 | 0.46 | 1.6 (1.5 to 2.0) |
| Accumulate direct | 0.67 | lamp-hall-64, 720p, trace (ter-nmbm) | 0.15 to 0.30 | 0.41 (0.39 to 0.51) | | | | No count of its lit pixels | |
| Depth pyramid | 0.36 to 0.39 (the timer gave 1.19) | 1080p, trace (ARCHITECTURE.md, "Screen-space reflections") | 0.53 at 720p, from the timer's 1.19 | 0.29 to 0.32 (0.28 to 0.32) | 0.30 | 0.14 | 0.06 | 0.30 | 1.0 to 1.1 |
| Bloom | 0.31 (pass-map.md), 0.46 (ARCHITECTURE.md) | 1080p, timer | 0.47 with the tone map | 0.25 and 0.37 (0.24 to 0.38) | 0.15 | 0.18 | 0.08 | 0.18 | 1.4 and 2.1 |

How each prediction follows:

- The walk. Its loop of steps runs a count of steps that depends on the scene, so the static count does not hold it. This row takes its work from earlier estimates, not from a count. A walk's lanes step together, so a subgroup takes as many steps as its longest walk. On the deck there are about 3.0 × 10⁵ rays of each kind, from the measured times and nanoseconds per ray. V5c's subgroups take 53.1 and 35.0 steps, so it does 26.4 million lane-steps. V1's take 157.8 and 60.0, 65.3 million. The profiling spike (ter-sftw) estimates one read and about 40 operations per step. At the measured ALU rate, 40 operations per lane-step take 0.28 ms for V5c. A rolled loop of dependent operations runs at 0.41 of the peak. So one such step takes 40 / 0.41 = 98 lane-cycles, 0.65 ms for V5c. The prediction takes the rolled loop's rate, because the walk's loop is rolled.
- In those terms, the walk spends 255 lane-cycles per lane-step in V5c and 224 in V1 at the run's clock. That is about 105 operations at the rolled loop's rate. The ticket's 27,000 lane-cycles per ray assumed 4,096 lanes at 1,296 MHz. On this GPU at the run's clock, a reflection ray of V5c costs 5.5 ns × 3,072 × 0.8 GHz = 13,500 lane-cycles.
- FSR. The sum of its passes' rooflines from the table above. Its passes take 1.62 ms at the top clock. Its frame takes 1.93 ms, and the gaps between its encoders hold the rest.
- Light candidates and direct light. Both draw a full-screen triangle at 1280 × 720, 921,600 fragments each. Light candidates count 2,006 lane operations and 13 texture reads per fragment. Direct light counts 4,766 and 26. Each keeps loops over the lights and the candidates, whose counts depend on the scene, and the count holds one pass of each. It also holds both sides of every branch. The bytes term reads 16 images of the render size once each, at 8 bytes per texel, an estimate. The deck's time is 1.3 times the count. lamp-hall-64's time is 0.6 times it, so there the fragments skip much of the code that the count holds.
- Accumulate direct. The spatial dispatch counts 69 reads and 1,323 lane operations per pixel, the temporal dispatch 12 and 172. On the deck, 94 % of the 921,600 pixels see a surface: 70 million reads. The bytes term reads 14 images of the render size once each, at 8 bytes per texel, an estimate.
- The depth pyramid. Its first level runs one invocation per cell of level 1, 960 × 540 = 518,400 at 1080p. Each counts 40 reads, the distance and the normal of 5 pixels for each of the cell's 4 pixels, and 2,156 lane operations. So it is ALU-bound, not bound by its reads. The other levels add 0.003 ms of ALU work. The first run of this spike counted only the reads, 0.17 ms at the run's clock, and gave a multiple of 2.2 to 2.3. This report withdraws it.
- Bloom. The prefilter reads 36 texels for each of the 960 × 540 texels of the first level and counts 872 lane operations. Five downsamples take 13 bilinear taps and 112 lane operations, and five upsamples take 9 and 125. That is 27 million reads.

## Section 11 at the windowed clock

Section 11's target of 15.0 ms per frame holds at the windowed clock, because the design sets it, not a measurement. Nearly every time of section 11 comes from headless runs at about 1050 MHz: pass-map.md and metal-trace-spike.md at 1920 × 1080. No such run recorded its clock, so this table assumes 1050 MHz (1022 to 1076) and the factor 0.81 (0.79 to 0.83).

The column "Estimate at the windowed clock" converts an estimate that came from a headless time, such as "the share". Where this spike measured a pass at the windowed clock, the measurement replaces the estimate. A guess or a count of reads stays as it is, because neither came from a clock.

| Pass | Today at 1080p, ms | Today at the windowed clock, ms | Estimate of section 11, ms | Basis of the estimate | Estimate at the windowed clock, ms |
|---|---:|---:|---:|---|---:|
| Deformation | 0.02 | 0.02 | 0.03 | Headless | 0.02 |
| Shadow maps and local shadows | 0.91 | 0.74 | 0.91 | Headless | 0.74 |
| Environment faces and prefilter | 0.97 | 0.79 | 0.97 | Headless | 0.79 |
| G-buffer | 0.42 | 0.34 | 0.22 | Headless share | 0.18 |
| Surface test | New | | 0.10 | A count of 12 reads per pixel | 0.10. Its texture term is 0.07. |
| Depth pyramid | 1.19 by the timer, 0.36 to 0.39 in a trace | 0.29 to 0.32 in a trace | 0.53 | The timer's headless share | 0.13 to 0.14, the trace's share, which its roofline of 0.13 at 720p matches |
| Light candidates and direct light | 4.21 | 3.41 | 0.6 to 1.3 | Section 7 | 2.04 to 2.70 on the deck, measured. Their counted work alone takes 1.64. |
| Accumulate direct | New | | 0.15 to 0.30 | Two dispatches of two parts | 0.68 to 0.90 on the deck, measured. Its reads alone take 0.46. |
| Screen trace | 4.40, the occlusion and reflection passes | 3.56 | 0.8 to 1.7 | A guess | 0.8 to 1.7, a guess. The lean walk V5c alone took 1.65 to 1.74. |
| Accumulate diffuse and specular | New | | 0.20 to 0.40 | A guess | 0.20 to 0.40, a guess |
| The corrected read | New | | 0.2 to 0.6 | ter-s6zr's headless 2.7 ms | 0.16 to 0.49 |
| Composite | 0.43 | 0.35 | 0.20 | Headless share | 0.16 |
| Scatter, haze | 0.55 | 0.45 | 0.24 | Headless share | 0.19 |
| Transparent and the copy | 1.26 | 1.02 | 0.61 | Headless share | 0.49 |
| Exposure | 0.19 | 0.15 | 0.09 | Headless share | 0.07 |
| FSR 3.1 | New | | 1.0 to 2.0 | Unmeasured then | 1.93 to 1.99, measured |
| Bloom, tone map | 0.47 | 0.38 | 0.47 | Headless | 0.38 |
| SMAA | 0.16 | 0.13 | 0 | Deleted | 0 |
| Outlines, capture | 0.04 | 0.03 | 0.04 | Headless | 0.03 |
| The frame | 15.2 | 12.3 (12.0 to 12.6) | 7.4 to 10.7 | The sum | 9.1 to 11.5 |

What the table shows:

- Today's frame of 15.2 ms takes about 12.3 ms at the windowed clock, so it already fits the target of 15.0 ms there.
- Three estimates were too tight for this machine and this code: light candidates with direct light, accumulate direct, and FSR at the top of its range. At a render size of 1280 × 720, the measurements raise the frame from 7.4 to 10.7 ms. It becomes 9.1 to 11.5 ms at the windowed clock.
- At a render scale of 1, the render-size passes cost 2.25 times as much: 11.7 to 16.9 ms. The frame then takes 15.6 to 20.9 ms. So on demo-exit, the controller settles at a scale between about 0.81 and 0.97, not 0.86 and 1.
- The screen trace and the accumulation of diffuse and specular light are still guesses. The lean walk V5c alone, at 1.65 to 1.74 ms, sits at the top of the screen trace's guess of 0.8 to 1.7 ms.

## Recommendation

For section 11 of the design:

1. State every budget at the windowed clock, 1296 MHz, the clock of a game whose GPU limits its frame rate. Replace its table by the table of "Section 11 at the windowed clock" above. Its estimate of the frame at 1280 × 720 becomes 9.1 to 11.5 ms, and the controller's scale 0.81 to 0.97.
2. FSR 3.1's estimate of 1.0 to 2.0 ms holds at the windowed clock, at its top: 1.93 to 1.99 ms. Its passes run at 1.1 times their rooflines as a whole. Two changes in our code can lower the frame's FSR time. The first is fewer encoders, because 0.2 to 0.45 ms of each frame is GPU idle time between them. The second is a look at the two pyramid passes, which run at 2.0 and 3.0 times their rooflines. Neither is measured yet.
3. Accumulate direct's estimate of 0.15 to 0.30 ms was too tight for this machine. Its reads alone take 0.46 ms at the windowed clock. Fewer reads lower it: one surface key image, as ter-9nrc proposes, removes most of the 48 reads of surface keys per pixel.
4. The depth pyramid's estimate of 0.53 ms at 720p came from a timer that holds part of the direct light's work. By its count of pixels, its trace time scales to 0.13 to 0.14 ms at 720p at the windowed clock. It runs at its roofline.
5. Bloom's estimate of 0.47 ms with the tone map holds. Bloom takes 0.24 to 0.38 ms at the windowed clock, 1.4 to 2.1 times its roofline.
6. The lean walk takes 2.1 to 3.3 times its estimated work at the rolled loop's rate. The profiling spike (ter-sftw) owns its instruction count. The rolled loop's 41 % is a cause that this spike can name: the walk's step loop, unrolled two to four times, reaches 77 to 93 % of the peak in the probe, and costs nothing elsewhere. ter-dzxf owns that change.
7. Light candidates and direct light on the deck take 2.04 to 2.70 ms at the windowed clock, against 0.6 to 1.3 ms. Their counted work alone, with one pass of each loop, takes 1.64 ms. So their budget was too tight for the code as it is, and the counted work, not a slow machine, explains most of the time. Which branches run and how many passes each loop makes on the deck stays unmeasured. ter-9nrc lists the candidates to measure.

For the method of every ticket's timing:

- Run `gpu-clock 100` beside each timed run. Print the markers `timed-start` and `timed-end` around the timed work. Compare builds only at the same clock over the timed span. ter-qk78 builds this into the timing tools.
- Convert a headless time to the windowed clock by the rule above before you hold it against a budget. The rule is an estimate, within about 15 %. Near a limit, measure at the windowed clock.
- Hold a pass against the roofline of its own counted work (`work.py`), not against another vendor's pass.
- A probe of the machine's clock must not wait for each frame, or it measures its own low duty cycle. FSR's timing test is such a test, so a batch of frames is the better measure of FSR's cost.

The first run of this spike left one decision open: the budget was to hold either at the clock of headless runs or at the top clock. The window runs settle it. When the GPU limits the frame rate, a window run reaches 1296 MHz on both views. So section 11's budgets hold at the windowed clock, and the headless figures above are context.

## Method

- Machine: Mac Studio (Mac13,1), M1 Max with a 24-core GPU, 64 GB, MoltenVK 1.4.2 from the dev shell, Xcode's Metal toolchain 27A266a.
- Builds: Release. The Vulkan probes and FSR's tests run from the Release test project, the demo from the Release demo. The native tools build with `xcrun swiftc -O` and `xcrun clang -O2` outside the dev shell, because the dev shell hides Xcode's Metal toolchain from `xcrun`.
- The lock: every timed run took `/tmp/tertius-gpu-measure.lock`. The first run took it through `tools/gpu-calibration/locked.sh`, and the review's runs through `tools/gpu-lock.sh`, which replaced it. The helper waits while another run holds the lock, records uptime at the start and the end, and removes the lock on any exit. Since the review, the waiters take turns in the order in which they came.
- The load: other agents built and tested on the machine. The 1-minute load at the starts and ends of the runs of the main rounds was 4.86 to 26.48, median 11.13.
- The rounds: 7 rounds of 12 runs, from 18:38 to 18:47 on 2026-11-03. Each round rotated the order by one run. The upscaler rounds ran 7 rounds of 4 runs, from 19:14 to 19:20. The argument buffer runs and the clocks at 1080p and under a trace ran after them.
- The window runs: 2 rounds of 4 runs, each round in a rotated order, from 21:12 to 21:25 on 2026-11-03, after one first deck run at 20:47. A window opened on the display for each run. The 1-minute load at the starts and ends of the 8 runs of the rounds was 3.50 to 20.52, median 6.12.
- The review's runs on 2026-11-03, with the 1-minute load at their starts and ends:
  - 7 rounds of the FMA variants from 22:04 to 22:06, at 15.6 to 19.0.
  - Three traces of FSR's batched measurement from 22:07 to 22:10, at about 19.7.
  - 5 upscaler rounds from 22:11 to 22:12, at 5.4 to 6.9.
  - The counts of the passes come from MSL dumps of short headless runs, which time nothing.
- Raw data: `captures/3ug8/`, which git ignores. The runs of the first stage are in `rounds/`, `upscalers/`, `extra/`, `clock-extra/`, `counts/`, `mvk-dump/`, `window/` and `analysis.md`. The review's runs are in `upscalers-2/`, `variants/`, `fsr-trace/`, `fsr-dump/`, `fsr-counts/`, `pass-dump/` and `pass-counts/`.
- Scratch drivers outside the tools ran two sets of runs: `captures/3ug8/extra-rounds.sh` for the argument buffer runs in `extra/`, and the three runs of `clock-extra/`. Every other figure comes from the tools of tools/README.md, "GPU calibration".

To repeat the runs, follow tools/README.md, "GPU calibration".

## Limits

- The texel peak's source is a third-party page, and the probe reaches 66 % of it. The texel shares can be off by that source's error.
- Apple ships no listing of the native instructions. The rolled loop's cause stays a guess, and the counts are of AIR, not of the native code.
- The counts are static: both sides of each branch count, and a loop whose trip count depends on the data counts once. So a roofline is not a strict bound for a pass with such loops or with large branches, such as the light passes and PrepareReactivity.
- The bytes terms are footprints of the bound images, each read once. Cache misses can move more bytes. For the light passes and accumulate direct, the bytes terms assume 8 bytes per texel.
- The clocks of the epic's historic runs are assumed, not recorded. The rule that converts them is an estimate within about 15 %.
- The clock at 1080p rests on two runs, and the clock under a trace on one.
- The window runs ran on a virtual display whose present caps at 120 frames per second. A window of 1920 × 1080 did not fit on it, so the GPU-limited runs used 1080 × 1858, 0.97 of the pixels.
- On a display of 60 Hz, a frame that the GPU draws in less than 16.7 ms leaves the GPU slack. Its clock can then drop as in the runs at 1080 × 1080. Such a frame fits its time already. This spike did not measure a physical display.
- Each row of the window table rests on two or three runs.
- Other sessions ran GPU work during the review's runs, without the lock. The FSR traces leave out every frame that another process overlapped, and the variants' 10th percentiles show the variants without them.
