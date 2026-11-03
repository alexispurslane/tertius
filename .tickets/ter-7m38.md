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

