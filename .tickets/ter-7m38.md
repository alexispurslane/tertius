---
id: ter-7m38
status: open
deps: [ter-k63u]
links: []
created: 2026-11-03T16:31:49Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, fsr, performance]
---
# FSR 3.1's 16-bit build: measure it, and keep it if it is faster

The user's request of 2026-11-03, at high priority. Compile AMD's pinned FSR 3.1.4 GLSL (ter-k63u) with FFX_HALF=1, which needs shaderFloat16 and 16-bit storage access (GL_EXT_shader_16bit_storage in ffx_core_glsl.h). The user accepts new device requirements that every GPU from 2019 on offers: NVIDIA Turing and later, AMD Vega and RDNA, Intel Gen9 and later, Apple GPUs through MoltenVK, and lavapipe. Run the same tests as ter-k63u, and time the 16-bit build against the 32-bit build in alternating runs in one session under the GPU lock. In the same runs, also time AMD's subgroup variant of the pyramid pass (FFX_SPD_NO_WAVE_OPERATIONS=0), whose 16-bit form also needs shaderSubgroupExtendedTypes. Rule: if a variant is measurably faster on the M1 Max, it becomes the only build, and the engine requires its features. Otherwise FSR stays as ter-k63u built it. Either way the engine keeps one build of FSR. Update decision 12 of docs/plans/temporal-renderer.md, the device feature list in ARCHITECTURE.md, and the pin's README with the measured times.


## Notes

**2026-11-03T18:40:17Z**

2026-11-03, the orchestrator's addition, a small choice inside the chosen design: before the timing, add FSR's first snapshot test. Render the settled pattern of colored squares of Fsr3UpscalerTests (and its scene-change frames) with the 32-bit build, review the image as CLAUDE.md asks for a first approval, and store it as the reference. The 16-bit build and the subgroup variant must match that reference within the snapshot tolerance before their time counts. So the rule becomes: a variant that is measurably faster and matches the reference becomes the only build. The test also kills ter-k63u's survivors C50, C57, C58 and C66 (the table in .tickets/ter-k63u.md): run them again with tools/mutate.py --only and record the result.

**2026-11-03T19:18:00Z**

2026-11-03, the build (branch ter-7m38-fsr-16bit). Result: no variant is measurably faster, so FSR stays as ter-k63u built it, with one build and no switch at run time.

1. The snapshot reference. Fsr3UpscalerTests.TheSettledPatternAndTheFramesAfterAChangeOfTheSceneMatchTheirReferenceImage renders the still pattern of colored squares for 10 frames, inverts it, and renders three more frames. The four 96 x 72 outputs stand side by side in ReferenceImages/fsr3-pattern.png, two stops down and sRGB-encoded. The first approval was a review of the image against the scene: panel 1 is the 8 x 6 checkerboard of the 64 x 48 pattern at 12 output pixels a square, with the red squares (3.0) at the top left, green rising to the right and blue rising downward. Panels 2 to 4 show the inverted pattern, with a dark square at the top left. In panel 2 the old pattern's colors stay at the edges of each square, and they fade in panels 3 and 4. The test was red first, with no reference.

Survivors of ter-k63u, again with tools/mutate.py --only and /private/tmp/ter-k63u-mutants.json:

| id | change | result | killed by | FLIP of the mutant |
|---|---|---|---|---|
| C50 | exposure slot binds frame info | killed | TheSettledPatternAndTheFramesAfterAChangeOfTheSceneMatchTheirReferenceImage | 5,301 pixels above 0.1, largest 0.343 |
| C57 | default exposure cleared to 2 | killed | the same | 138 pixels above 0.1, largest 0.159 |
| C58 | default composition mask cleared to 1 | killed | the same | 10,253 pixels above 0.1, largest 0.678 |
| C66 | SPD levels use the full view | killed | the same | 4,446 pixels above 0.1, largest 0.969 |
4 rows: killed 4, survived 0 (wall time 41 s).

2. The variants, built per AMD's host. AMD's host compiles FFX_HALF=1 only for autogen reactive, prepare reactivity, shading change and accumulate (ffx_fsr3upscaler_shaderblobs.cpp of v1.1.4). Its 16-bit luminance pyramid is a stub, and a build with FFX_HALF=1 in every pass failed three FSR tests. ffx_spd.h tests whether FFX_SPD_NO_WAVE_OPERATIONS is defined, so the subgroup variant leaves the define out, as AMD's Vulkan build does.

| Build | Added SPIR-V capabilities | Vulkan requirement |
|---|---|---|
| (a) 16-bit | Float16, Int16 | shaderFloat16, shaderInt16 |
| (b) subgroup | GroupNonUniform, GroupNonUniformQuad | quad operations in the compute stage |
| (c) both | all four | all three |

No module uses 16-bit storage, and no build needs shaderSubgroupExtendedTypes, because the pyramids stay 32-bit. NVK, RADV (Vega on), ANV, MoltenVK and lavapipe offer each requirement by their source. NVIDIA's own driver has no public source, and gpuinfo.org refused the reads, so it is not checked. The pin's README has the table and the sources.

3. AMD's 3.1.6 fix: ffx_fsr3upscaler_reproject.h line 67 passes PreviousFrameUpscaleSize() to HistorySample16, as v2.4.0 does. pin.diff and check-pin.sh record it. The 32-bit modules did not change.

4. Correctness: every FSR test passed with each variant under validation with synchronization validation, with no message. Largest FLIP against the reference: 16-bit 0.0134, subgroup 0.0000, both 0.0134. The measured builds required shaderFloat16 and shaderInt16 in VulkanDevice, so two DeviceFeatureTests fixtures failed in every build, today's included. That edit is reverted.

5. The timing: 60 rounds, each of the five Release builds once per round in a rotating order, each run under the GPU lock with uptime at its start. 300 frames per run (ter-k63u's test), 18,000 frames per build. 1-minute load at the run starts: least 3.4, median 6.0, most 30.5.

| Build | Median, ms | p10, ms | p90, ms | Resampled median minus today's: middle and 90 % interval, ms |
|---|---|---|---|---|
| today's | 2.826 | 2.581 | 3.446 | |
| control (byte copy) | 2.769 | 2.580 | 3.440 | -0.047 (-0.178 to +0.066) |
| (a) 16-bit | 2.797 | 2.547 | 3.416 | -0.030 (-0.163 to +0.099) |
| (b) subgroup | 2.788 | 2.546 | 3.375 | -0.038 (-0.159 to +0.075) |
| (c) both | 2.776 | 2.521 | 3.348 | -0.052 (-0.168 to +0.061) |

The control alone came out 0.057 ms faster than today's build. Every variant gains less, so none is measurably faster. The method (60 rounds and the resampled interval) was fixed after round 19, before rounds 20 to 59 ran. After 20 rounds the subgroup build led by 0.14 ms, and the lead did not hold. At the 10th percentile, (c) is 0.060 ms faster (interval -0.070 to -0.022), outside the control's (-0.043 to +0.039). The rule judges the median.

6. The rule: the engine keeps ter-k63u's build. The csproj, VulkanDevice and DeviceFeatureTests did not change.

Raw data: /private/tmp/ter-7m38-timing (rounds.log, and the frame times of each run), scripts in /private/tmp/ter-7m38-variants/scripts.
