# The bloom as compute dispatches: measured, not merged (ter-lm61)

The bloom runs 11 steps at 6 levels: the first downsample, 5 steps down and 5 steps up. On main each step is a render pass, so MoltenVK opens 11 Metal render encoders. The proposal was 11 compute dispatches that follow each other in one compute encoder under the patched MoltenVK. The estimate was a gain of 0.13 to 0.20 ms on the deck.

I built it on 2026-11-12, on main at `c87eac88f`. The build gains nothing that a frame can show, so it stays out of main.

## The times

The Mac recorded take 9 under the exclusive GPU lock, on the deck at 1280 × 720, upscaled to 1920 × 1080. The GPU clock stayed between 1290.8 and 1295.6 MHz. All times are in ms.

| Quantity | Main | Compute bloom |
|---|---:|---:|
| Frame span as a window, median of 111 frames | 19.938 | 19.930 |
| Frame span as a window, second trace | 19.955 | disturbed, not counted |
| Frame span with the pass time stamps, median | 20.253 | 20.339 |
| Encoders in a window frame | 68 | 57 |
| Idle time inside a window frame | 1.282 | 1.229 |
| The bloom's GPU work | 0.301 | 0.350 |
| The idle time before the bloom's encoders | 0.133 | 0.020 |
| What the bloom pays (work plus idle time) | 0.435 | 0.370 |
| What the tone map pays | 0.187 | 0.229 |

The idle time is the time in a frame span in which the GPU runs no encoder. "Pays" is from the adding count of `tools/pays.py`, on the traces with the pass time stamps.

The 11 encoder breaks went away, and the idle time fell by 0.11 ms. The work rose by 0.05 ms, and the tone map's idle time rose by 0.04 ms. The frame span moved by 0.01 ms in the window trace and by the other sign in the trace with stamps. Both are inside the spread of two runs of one build, which is about 0.02 ms.

## Why the gain is so small

- The break before a render encoder of the bloom costs about 12 µs of idle time. The mean over all render encoders of the frame is 22 µs, and a break before a compute encoder costs 22 to 24 µs. So the bloom's breaks were the cheap ones.
- One step of the bloom costs about 32 µs as a dispatch and about 27 µs as a render pass. The levels are small: level 5 has 20 × 12 pixels. A dispatch has a floor of 10 to 18 µs on this GPU at any size.
- The fences of the first command buffer follow the bloom. The idle time that left the bloom's encoders came back in part before the tone map.

The lesson for the other encoder proposals: a removed compute or blit encoder gains its break. A small render pass that turns into a dispatch gains almost nothing. The price of a break differs by pass, so a timed proxy build comes before each real build.

## The image

The stills of 25 scripts are the same, byte for byte: 139 of 139 PNG files and 97 of 97 other files. [stills-compare.md](stills-compare.md) holds the table.

The up step adds in the shader, and main adds with the blender. The Apple blender rounds toward zero, and the shader's store rounds to nearest. No still shows a difference from it.

## What main keeps

Main keeps one test from this work, `EveryBloomLevelHoldsTheDesignsChainOverTheHdrImage`. It compares every level of the GPU with a model of the chain on the CPU, at an even and at an odd image size. It passed on the render-pass bloom and on the compute bloom. Before it, no test read a level other than level 0.

## The files

| Path | Content |
|---|---|
| `bloom-compute.patch` | The measured build of the engine, as a patch against `c87eac88f` |
| `take9/` | The job list and the lock log of the timed take, the adding count of both builds, and the idle time of each trace |
| `stills-wide-bl1.jobs`, `stills-wide-bl1.lock.log` | The untimed take that rendered the stills |
| `stills-compare.md` | The byte comparison of the stills |

The patch does not hold the test `EveryStepOfTheBloomIsAComputeShader`, which was red on main and green on the build. The commit `fad9f2b47` holds the whole build with its tests. It is under the local ref `refs/scratch/ter-lm61-bloom-compute` of the main repository.
