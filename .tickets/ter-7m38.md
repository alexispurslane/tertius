---
id: ter-7m38
status: in_progress
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

**2026-11-03T18:41:58Z**

2026-11-03: launched as workflow run b27b717b-e7ea-4822-8692-6e628af080cc, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-7m38-fsr-16bit from 3344c722c. The brief adds the port of AMD's 3.1.6 fix of the FFX_HALF branch of ReprojectHistoryColor, which the 3.1.4 pin lacks.

**2026-11-03T19:45:58Z**

2026-11-03, the user's decision after the review's finding 1: the paired per-round comparison is the statistic (the orchestrator's reading, which the user accepted), so the build with both changes (FFX_HALF=1 in AMD's four 16-bit passes, the SPD define left out) is faster by about 0.07 to 0.085 ms and becomes the one build. The user accepts its new requirements (shaderFloat16, shaderInt16, quad subgroup operations in compute) on one condition: evidence that the code can run, not that it is fast, on NVIDIA, AMD and Metal GPUs from about 2018 on. The project's standing rule (CLAUDE.md) also keeps ANV and lavapipe. A public lead for NVIDIA: GPU Caps Viewer's report of an RTX 2070 on driver 417.23 (Geeks3D forum, December 2018) shows shaderInt16; NVIDIA's Linux driver 415.18.04 beta added VK_KHR_shader_float16_int8 (GamingOnLinux, December 2018). vulkan.gpuinfo.org refuses automated reads (HTTP 403).
