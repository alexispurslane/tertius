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

**2026-11-03T17:00:00Z**

The review's fix stage (one pass, all findings):

1. FSR's device-limit check is the engine's one check now. Fsr3Upscaler.CheckArguments folds the bindings of every pass and of the exposure step into a LimitedDescriptors and throws LimitedDescriptors.Problem before any layout exists. StorageImageProblem and Fsr3Bindings.MostStorageImages are gone. A new test refuses a device whose compute stage sees 8 sampled images (prepare reactivity samples 9), which the old check missed. The ARCHITECTURE rows of the storage-image limit and of the per-stage check say so.
2. Fsr3HostTests.EveryPassBindsWhatItsCompiledModuleDeclaresUnderAmdsNames reads the OpName and Binding words of each embedded module and maps the names to Fsr3Slot by AMD's three name tables. It failed when two rows of the table were swapped. The SPIR-V reader (tests/Tertius.Rendering.Tests/Spirv.cs) is shared with ShaderConstantsTests.
3. Every member of Fsr3Frame and Fsr3Inputs.Frame has a doc comment with its units and sign. The class summary of Fsr3Upscaler is within STYLE.md's 50 words, and the design's interface comment is the built one.
4. WriteConstants uploads the constants around the word of DeltaPreExposure, so the exposure step is that word's one writer, and the order of the two writes no longer matters. The doc comments are true now.
5. WriteSet sizes its three stackalloc buffers by the pass's bindings.
6. Record refuses a reactive mask that is not R8_UNORM (a GPU test, red first: without the check it recorded and returned).
7. ARCHITECTURE no longer says that golden values check each binding's image of a pair. It names what they check, and the binding test. The formats row lists R32G32_SFLOAT.
8. Decision 31, the usage code, the interface comment, the exposure step and the inputs table of section 5 say what the host does. The dated note records the change.
9. A GPU test upscales a constant color at 48 x 32 and then 56 x 40 within a largest render size of 64 x 48, with no validation message. CPU tests refuse a largest render size with a longer side below 64 or a side of 1 pixel (the second was red first).
10. golden.cpp points at tools/README.md, "FSR 3.1 golden values".

Also: build.sh compiles with -ffp-contract=off. The golden file stayed byte-equal. The tolerance comment of the constant-color tests names the steps that 1e-3 allows.

The 3.1.5 and 3.1.6 comparison is reproducible now. The README of the pin lists the fetch commands, the NAR hashes and the result for each header. Two more differences exist, and neither has an effect: three lines of ffx_core_gpu_common.h end with ";;", and ffx_fsr3upscaler_rcas.h includes ffx_core.h by a longer path. AMD's HLSL bindings changed only their register macro.
