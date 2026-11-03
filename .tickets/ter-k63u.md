---
id: ter-k63u
status: open
deps: []
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, fsr]
---
# FSR 3.1: the pinned source, the build and the host

Build ticket 1 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, section 5 and decisions 12, 30 and 31. Acceptance: section 14, item 1, except the lavapipe run: the user decided on 2026-10-25 to skip Linux runs for now (ter-kzj3), so this ticket runs its GPU test under the validation layer on MoltenVK, and ticket 7 runs it on lavapipe. Nothing in the frame changes: FSR runs only in its tests.


## Notes

**2026-11-03T15:41:10Z**

Built (branch ter-k63u-fsr31-host):

- The pin: AMD's FidelityFX SDK v1.1.4 (commit c6efa6bf7f2027b3ec94f28578bb5965eabb9e55, FSR 3.1.4) in src/Tertius.Rendering/ThirdParty/Fsr3, with AMD's MIT notice in LICENSE.txt, in every file and in THIRD-PARTY-NOTICES.md. The dev shell fetches the same commit as TERTIUS_FFX_SDK (native/fidelityfx-sdk.nix), and tools/fsr3-golden/check-pin.sh compares the folder with it.
- The reason for the pin: 3.1.4 is the last release with GLSL passes. 3.1.5 (SDK v2.0.0) and 3.1.6 (SDK v2.4.0) ship HLSL only. The 3.1.5 fix is in RCAS, which the engine does not run. The 3.1.6 fix is in the FFX_HALF branch of the reprojection, and its 32-bit branch already had it in 3.1.4. So neither affects the portable build. The README of the pin has the comparison.
- The build: Tertius.Rendering.csproj compiles the eight dispatched passes with glslangValidator, FFX_HALF=0 and FFX_SPD_NO_WAVE_OPERATIONS=1, and runs spirv-val on each module. Capabilities: Shader, ImageQuery, StorageImageExtendedFormats, and StorageImageWriteWithoutFormat in the accumulate pass. The device now requires shaderStorageImageWriteWithoutFormat (DeviceFeatureTests). The most storage images in one pass is 7 (the shading change pyramid), which Fsr3Upscaler.StorageImageProblem checks.
- The format defect, resolved by section 5's order: the validation layer reported both rw_luma_history (rgba8 against AMD's RGBA16F image) and rw_output_autoreactive (r32f against the R8 reactive mask). 3.1.5 and 3.1.6 declare both with no format in HLSL, so rule 1 gave nothing, and rule 2 changed the declarations to rgba16f and r8. tools/fsr3-golden/pin.diff is the whole difference from AMD's tag. After the change the GPU test reports no validation message, with synchronization validation on, on MoltenVK.
- The host: Fsr3Upscaler, Fsr3Constants, Fsr3Bindings and fsr_exposure.comp. Fsr3HostTests compare the jitter, the phase counts, the constants of every frame, the pyramid constants, the dispatch groups and order, the clears and the images with the golden values that tools/fsr3-golden/ prints from AMD's own host code. Fsr3UpscalerTests upscale a known input on the GPU: a constant color, a reset, the exposure step's ratio against AMD's, two runs with the same bits, and no validation message.
- The measured time: from 1280 x 720 to 1920 x 1080, a median of 2.79 ms on the Apple M1 Max in Release (10th percentile 2.59 ms, 90th 3.11 ms, 300 frames, timestamps around Record), at a 1-minute load of 5.5 on 10 cores, under the GPU lock. The design expected 1.0 to 2.0 ms.

Corrections to the design (section 5 has a dated note): AMD's shaders multiply the color by the exposure image and never divide by the pre-exposure, so the host binds AMD's default exposure (read as 1) and the exposure step writes only deltaPreExposure. Record takes the command buffer and the barrier batch instead of a FrameContext, and the constructor has no depth parameter.

Not done here: the lavapipe run (ticket ter-cgif), and coverage and mutation testing (the orchestrator's pass).
