---
id: ter-k63u
status: in_progress
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

**2026-11-03T14:56:37Z**

2026-11-03: launched as workflow run ab327160-9e42-468f-9b3e-3478fde59677, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-k63u-fsr31-host from 70fe35650. The lavapipe run moved to ter-cgif, because the user decided on 2026-10-25 to skip Linux runs for now.

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

**2026-11-03T18:23:08Z**

2026-11-03, test hardening (steps 4 to 6 of the test process).

Coverage on changed lines (tools/README.md, "Coverage on changed lines", Debug and Release):

- Before: 848/855 changed lines (99.2%), 184/205 branches (89.8%). Uncovered: Fsr3Upscaler.cs 559-562 and 564, the refusals of a render size and an output size, and 694 and 706.
- After: 853/855 lines (99.8%), 190/205 branches (92.7%). The new refusal tests cover 559-564. This measurement reran the Rendering tests in Debug and reused the Release report of the first run.
- 694 and 706 stay uncovered: they are the `UnreachableException` arms of ImageOf and BufferOf for slots that hold no image or no buffer. No binding table reaches them, and EveryPassBindsWhatItsCompiledModuleDeclaresUnderAmdsNames holds the tables.
- The partial branches at Fsr3Upscaler.cs 323, 352, 406, 641 and 699 and Fsr3Bindings.cs 210 are the throw arms that the compiler adds to a switch expression over an enum, for a value outside the enum. Line 505 is the empty-array branch that the compiler adds to `fixed`. No test can reach them.
- AMD's pinned files in ThirdParty/Fsr3 are third-party code. They are not tested line by line. The build checks each module with spirv-val, the binding test reads each module, and the GPU tests run every pass.

Mutation testing: the branch changes no line of Core, Simulation or Physics, so the official Stryker run does not apply. Every mutant is in the list for tools/mutate.py (/private/tmp/ter-k63u-mutants.json): one or more for each decision and branch of the new C#, fsr_exposure.comp, the two declarations of pin.diff, the device feature and one define of the csproj. The first run gave 58 killed, 36 survived and 3 that did not compile. C20, C60 and C64 then got mutants that compile. The table is the last full run against the final tests. B06 was run again with `--only B06` after its test.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| C01 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:46 | SPD mips cap 12 -> 11 | survived |  | 8.2 |
| C02 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:59 | SPD groups x without +1 | killed | EveryPassOfEveryFrameDispatchesAmdsGroups, TheLuminancePyramidsConstantsOfEveryFrameAreAmds, TheFirstFrameAfterAChangeOfTheSceneFollowsTheNewScene | 7.4 |
| C03 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:68 | generator flags lose UseComponentsMax | killed | TheReactiveMaskGeneratorTakesTheValuesOfAmdsSample | 6.9 |
| C04 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:129 | delta word index + 1 | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, TheExposureStepWritesAmdsRatioOfThisFramesPreExposureToTheLastFrames | 6.4 |
| C05 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:140 | layouts lose the exposure step's | killed | ADeviceWhoseComputeStageSeesFewerStorageBuffersThanTheExposureStepBindsIsRefused, WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard | 6.5 |
| C06 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:211 | five SPD level views | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages | 7.6 |
| C07 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:248 | Clears index swaps reset and odd | killed | EveryFrameClearsAmdsImagesWithAmdsValuesInAmdsOrder, AResetFrameUpscalesAsTheFirstFrameOfANewUpscalerDoes | 8.8 |
| C08 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:260 | first execution never | killed | ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages | 14.9 |
| C09 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:264 | first frame does not reset accumulation | survived |  | 9.2 |
| C10 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:270 | parity inverted | survived |  | 7.9 |
| C11 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:270 | every frame even | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AChangeOfThePreExposureScalesTheHistoryByTheRatioAlone | 6.9 |
| C12 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:271 | one set for every frame | killed | TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation | 6.1 |
| C13 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:274 | defaults never cleared | survived |  | 5.8 |
| C14 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:276 | SPD constants of the largest render size | survived |  | 6.8 |
| C15 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:278 | no exposure step | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, TheExposureStepWritesAmdsRatioOfThisFramesPreExposureToTheLastFrames | 6.4 |
| C16 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:279 | no reactive generator | killed | TheGeneratorMarksEveryPixelWhoseColorDiffersFromTheOpaqueOnlyColorWithAmdsBinaryValue | 7.0 |
| C17 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:280 | Record's clears ignore the reset | killed | AResetFrameUpscalesAsTheFirstFrameOfANewUpscalerDoes | 6.8 |
| C18 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:280 | Record's clears of the other parity | killed | AResetFrameUpscalesAsTheFirstFrameOfANewUpscalerDoes | 6.6 |
| C19 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:281 | last clear skipped | survived |  | 5.8 |
| C20 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:285 | executions never counted | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation, AChangeOfThePreExposureScalesTheHistoryByTheRatioAlone | 6.0 |
| C21 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:309 | phase count factor 4 | killed | TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites, ThePhaseCountOfEachRenderAndOutputWidthIsAmds | 6.9 |
| C22 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:317 | Halton x from the first point | killed | TheJitterOffsetOfEachIndexAndPhaseCountIsAmds | 6.2 |
| C23 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:331 | shading change groups at a quarter | killed | EveryPassOfEveryFrameDispatchesAmdsGroups, TheFirstFrameAfterAChangeOfTheSceneFollowsTheNewScene | 6.0 |
| C24 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:335 | accumulate groups over the render size | killed | EveryPassOfEveryFrameDispatchesAmdsGroups, AConstantColorUpscalesToTheSameColorAtEveryPixelOfTheOutput, AConstantColorUpscalesToTheSameColorBelowTheLargestRenderSizeAndAfterTheRenderSizeChanges | 12.3 |
| C25 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:354 | accumulation R16F | killed | TheImagesHaveAmdsNamesFormatsAndSizesWithoutTheLanczosTable, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation | 5.8 |
| C26 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:370 | SPD chain one level short | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, TheImagesHaveAmdsNamesFormatsAndSizesWithoutTheLanczosTable, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard | 4.2 |
| C27 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:397 | Halton digit + 1 | killed | TheJitterOffsetOfEachIndexAndPhaseCountIsAmds | 7.6 |
| C28 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:436 | first clears lose Luma2 | killed | EveryFrameClearsAmdsImagesWithAmdsValuesInAmdsOrder | 6.1 |
| C29 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:439 | reset clears not on the first execution | survived |  | 5.8 |
| C30 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:441 | reset clears the written accumulation | killed | EveryFrameClearsAmdsImagesWithAmdsValuesInAmdsOrder, AResetFrameUpscalesAsTheFirstFrameOfANewUpscalerDoes | 5.9 |
| C31 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:444 | frame info clear x 0 | killed | EveryFrameClearsAmdsImagesWithAmdsValuesInAmdsOrder | 5.7 |
| C32 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:448 | no clear of the reconstructed depth | killed | EveryFrameClearsAmdsImagesWithAmdsValuesInAmdsOrder | 6.0 |
| C33 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:466 | no immutable sampler | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation, TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation | 5.9 |
| C34 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:503 | pool sizes for one frame | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages | 7.3 |
| C35 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:510 | pool sets for one frame | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages | 7.0 |
| C36 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:536 | 64-pixel minimum -> 63 | killed | ALargestRenderSizeWhoseLongerSideIsBelow64PixelsIsRefused(width: 48, height: 63), ALargestRenderSizeWhoseLongerSideIsBelow64PixelsIsRefused(width: 63, height: 48) | 5.9 |
| C37 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:539 | shorter side minimum 1 | killed | ALargestRenderSizeOneOfWhoseSidesIsOnePixelIsRefused | 5.9 |
| C38 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:540 | output zero check only for both sides | killed | AnOutputSizeWithASideOfNoPixelsIsRefused(width: 96, height: 0), AnOutputSizeWithASideOfNoPixelsIsRefused(width: 0, height: 72) | 5.8 |
| C39 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:541 | zero frames in flight taken | killed | AnUpscalerWithNoFrameInFlightIsRefused | 5.9 |
| C40 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:544 | limit check skips the exposure step | killed | ADeviceWhoseComputeStageSeesFewerStorageBuffersThanTheExposureStepBindsIsRefused | 5.8 |
| C41 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:548 | limit check counts nothing | killed | ADeviceWhoseComputeStageSeesFewerSampledImagesThanAPassBindsIsRefused, ADeviceWhoseComputeStageSeesFewerStorageBuffersThanTheExposureStepBindsIsRefused, ADeviceWhoseComputeStageSeesFewerStorageImagesThanAPassBindsIsRefused | 5.9 |
| C42 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:557 | render width bound < | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages | 5.7 |
| C43 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:557 | render height bound + 1 | killed | ARenderSizeOutsideTheLargestRenderSizeIsRefused(width: 64, height: 49) | 5.8 |
| C44 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:558 | zero render width taken | killed | ARenderSizeOutsideTheLargestRenderSizeIsRefused(width: 0, height: 48) | 6.8 |
| C45 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:558 | zero render height taken | killed | ARenderSizeOutsideTheLargestRenderSizeIsRefused(width: 64, height: 0) | 26.1 |
| C46 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:563 | output check \|\| -> && | killed | AnOutputOfAnotherSizeThanTheOutputSizeIsRefused(width: 97, height: 72), AnOutputOfAnotherSizeThanTheOutputSizeIsRefused(width: 96, height: 73) | 6.5 |
| C47 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:567 | reactive format check | killed | AReactiveMaskOfAFormatOtherThanR8UnormIsRefused | 6.4 |
| C48 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:581 | FSR images not storage | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation | 6.3 |
| C49 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:593 | constants not a storage buffer | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation, TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation | 6.6 |
| C50 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:648 | exposure slot binds frame info | survived |  | 5.9 |
| C51 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:650 | composition mask binds the reactive mask | survived |  | 5.9 |
| C52 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:656 | upscaled history read = write | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation | 5.8 |
| C53 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:662 | accumulation read = write | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages, UpscalingReportsNothingToValidation | 6.8 |
| C54 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:667 | luma history read = write | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation, TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation | 6.4 |
| C55 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:672 | current luma = previous luma image | killed | TheFirstFrameAfterAChangeOfTheSceneFollowsTheNewScene | 9.3 |
| C56 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:673 | previous luma = current luma image | killed | TheFirstFrameAfterAChangeOfTheSceneFollowsTheNewScene | 9.2 |
| C57 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:712 | default exposure cleared to 2 | survived |  | 7.9 |
| C58 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:713 | default composition mask cleared to 1 | survived |  | 8.6 |
| C59 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:732 | clear only level 0 | survived |  | 9.9 |
| C60 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:757 | SPD upload only the mips | survived |  | 7.2 |
| C61 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:762 | reactive upload only the scale | killed | TheGeneratorMarksEveryPixelWhoseColorDiffersFromTheOpaqueOnlyColorWithAmdsBinaryValue | 8.2 |
| C62 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:773 | second range includes the ratio word | survived |  | 6.9 |
| C63 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:774 | first range includes the ratio word | survived |  | 6.7 |
| C64 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:775 | upload after the ratio word stops at one word | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AChangeOfThePreExposureScalesTheHistoryByTheRatioAlone | 6.6 |
| C65 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:807 | sampler binding written | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages | 6.0 |
| C66 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:828 | SPD levels use the full view | survived |  | 6.2 |
| C67 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:831 | sampled images in General | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation, TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation | 6.4 |
| C68 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:853 | no barrier on the exposure buffer | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard | 6.4 |
| C69 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:854 | no barrier before the exposure write | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation, TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation | 6.2 |
| C70 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:878 | no uniform barriers | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages | 6.0 |
| C71 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:888 | every pass dispatches render-size groups | killed | AConstantColorUpscalesToTheSameColorAtEveryPixelOfTheOutput, AConstantColorUpscalesToTheSameColorBelowTheLargestRenderSizeAndAfterTheRenderSizeChanges | 12.0 |
| C72 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:895 | no dedup of image barriers | survived |  | 6.4 |
| C73 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:947 | a push range of 0 bytes | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation, TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation | 6.1 |
| K01 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:65 | Created max render x = output | killed | TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites | 8.0 |
| K02 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:72 | accumulation per frame 1/4 | killed | TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites | 6.3 |
| K03 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:104 | upscale size x = max render | killed | EveryPassOfEveryFrameDispatchesAmdsGroups, TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites, AConstantColorUpscalesToTheSameColorAtEveryPixelOfTheOutput | 9.9 |
| K04 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:115 | phase count without the zero rule | survived |  | 6.3 |
| K05 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:117 | phase step > -> >= | killed | TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites | 6.3 |
| K06 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:118 | phase step up 2 | killed | TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites | 6.0 |
| K07 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:120 | phase step down 2 | killed | TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites | 6.6 |
| K08 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:122 | delta time clamp 2 s | killed | TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites | 6.7 |
| K09 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:123 | frame index never reset | killed | TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites | 7.0 |
| K10 | src/Tertius.Rendering/Upscaling/Fsr3Constants.cs:135 | depth param 0 sign | killed | TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites | 6.2 |
| B01 | src/Tertius.Rendering/Upscaling/Fsr3Bindings.cs:101 | sampler binding 1000 | killed | EveryPassBindsWhatItsCompiledModuleDeclaresUnderAmdsNames, WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard | 7.2 |
| B02 | src/Tertius.Rendering/Upscaling/Fsr3Bindings.cs:107 | generator output binding 3 | killed | EveryPassBindsWhatItsCompiledModuleDeclaresUnderAmdsNames, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation | 7.4 |
| B03 | src/Tertius.Rendering/Upscaling/Fsr3Bindings.cs:166 | reactivity binds the composition mask at 3 | killed | EveryPassBindsWhatItsCompiledModuleDeclaresUnderAmdsNames | 6.5 |
| B04 | src/Tertius.Rendering/Upscaling/Fsr3Bindings.cs:226 | Read parity swapped | killed | EveryFrameClearsAmdsImagesWithAmdsValuesInAmdsOrder, WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard | 6.9 |
| B05 | src/Tertius.Rendering/Upscaling/Fsr3Bindings.cs:229 | Written parity swapped | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation | 6.3 |
| B06 | src/Tertius.Rendering/Upscaling/Fsr3Bindings.cs:233 | SpdMip5 not a level | killed | EachStorageSlotOfTheLuminancePyramidWritesItsOwnLevelAndTheSampledSlotAllOfThem | 8.9 |
| D01 | src/Tertius.Rendering/Vulkan/VulkanDevice.cs:430 | feature not required | killed | ADeviceWithoutStorageWritesWithoutAFormatIsRefusedByItsName, ADeviceWithoutAFeatureThatTheRendererEnablesIsRefusedByItsName | 7.8 |
| D02 | src/Tertius.Rendering/Vulkan/VulkanDevice.cs:507 | feature not enabled | killed | ADeviceWithoutAFeatureThatTheRendererEnablesIsRefusedByItsName, ADeviceThatReportsJustTheFeaturesThatTheRendererEnablesIsTaken | 6.4 |
| S01 | src/Tertius.Rendering/Shaders/fsr_exposure.comp:26 | ratio inverted | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, TheExposureStepWritesAmdsRatioOfThisFramesPreExposureToTheLastFrames, AChangeOfThePreExposureScalesTheHistoryByTheRatioAlone | 7.1 |
| S02 | src/Tertius.Rendering/Shaders/fsr_exposure.comp:26 | ratio without the last frame | killed | TheExposureStepWritesAmdsRatioOfThisFramesPreExposureToTheLastFrames | 6.7 |
| S03 | src/Tertius.Rendering/Shaders/fsr_exposure.comp:26 | writes the next word | killed | WithoutResetsTheUpscaleOfAStillPatternSettles, AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, TheExposureStepWritesAmdsRatioOfThisFramesPreExposureToTheLastFrames | 6.4 |
| S04 | src/Tertius.Rendering/ThirdParty/Fsr3/sdk/include/FidelityFX/gpu/fsr3upscaler/ffx_fsr3upscaler_callbacks_glsl.h:398 | pin: luma history back to rgba8 | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation, TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation | 6.8 |
| S05 | src/Tertius.Rendering/ThirdParty/Fsr3/sdk/include/FidelityFX/gpu/fsr3upscaler/ffx_fsr3upscaler_callbacks_glsl.h:785 | pin: autoreactive back to r32f | killed | AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard, UpscalingReportsNothingToValidation, TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation | 7.9 |
| M01 | src/Tertius.Rendering/Tertius.Rendering.csproj:11 | FSR built for forward depth | survived |  | 14.2 |
97 rows: killed 78, timeout 0, survived 19, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

The answers for the 19 survivors:

| id | answer | reason |
|---|---|---|
| C01 | judgment call | AMD caps the pyramid at 12 levels, which only a longer render side of 4,096 pixels reaches. The golden sequences stop at 1920, and the golden test pins `Fsr3SpdConstants.For` at their sizes. |
| C09 | equivalent | The first execution clears both accumulation images to 0, and AMD's passes give a history of no accumulated frames no weight. TheFirstFrameOfAnUpscalerReadsNoHistoryEvenWithoutAReset gives the same bits with the mutant, also on reused memory. |
| C10 | equivalent | Inverting every frame's parity swaps the two images of each ping-pong pair in the clears and in the bindings at once, and both images of a pair have the same description. |
| C13 | judgment call | The two default images are 1 × 1 images that only this clear writes. Fresh memory on this driver holds zeros, and the old upscaler's same images held zeros, so no test reaches memory that the clear changes. |
| C14 | judgment call | The pyramid's constants for the largest render size changed no bit of any measured output, also below the largest render size. The golden test pins `For` at each frame's render size, which AMD's host uses. |
| C19 | judgment call | The per-frame clear of the pyramid changed no measured bit, on fresh or reused memory: the shading change pyramid writes the texels that the next pass reads. AMD schedules the clear, and the golden test pins the list. |
| C29 | judgment call | On the first execution, the reset clears add only the frame info, which feeds AMD's auto exposure. The engine does not bind it, and no measured bit changed. |
| C50 | judgment call | AMD's passes multiply by the exposure image's value and the accumulate pass divides by it, so another value cancels except in AMD's tone-mapped weights. The measured outputs moved by 2% to 21%, but no reference image says which output is right. |
| C51 | judgment call | The test scenes draw no transparency, so the reactive mask that the mutant binds as the composition mask holds 0 wherever an output is read. |
| C57 | judgment call | An exposure of 2 cancels as in C50. The settled output moved by 1%, and no reference image says which output is right. |
| C58 | judgment call | A composition mask of 1 lowers the history's weight everywhere. The settled output moved by 3.5%, and the scene-change output followed faster. No reference image says which output is right. |
| C59 | judgment call | As C19: the clear of the pyramid's upper levels changed no measured bit. |
| C60 | judgment call | As C14: with the mip count uploaded, the rest of cbSPD changed no measured bit. The golden test pins the values. |
| C62 | equivalent | The exposure step writes the ratio word after the upload, behind a barrier from the transfer write to the compute write, so the uploaded value never reaches a pass. |
| C63 | equivalent | As C62, for the first range. |
| C66 | judgment call | A pyramid level bound through the whole chain writes level 0, which Vulkan allows, so validation reports nothing. The scene-change output moved by 19%, under any threshold that holds on another GPU. EachStorageSlotOfTheLuminancePyramidWritesItsOwnLevelAndTheSampledSlotAllOfThem pins the level of each slot. |
| C72 | equivalent | A second request for a state that an image already has in the pass adds one more barrier from storage to storage. It orders more and changes no result. |
| K04 | equivalent | The phase count is 0 only before the first frame, and Record always resets the first frame. |
| M01 | judgment call | The depth convention changes only the dilation of the motion vectors and the disocclusion test, and the test scenes have no motion. The frame's integration is the first place with moving depth edges. |

Tests added (steps 5 and 6):

- Fsr3HostTests: ADeviceWhoseComputeStageSeesFewerStorageBuffersThanTheExposureStepBindsIsRefused (C40), AnOutputSizeWithASideOfNoPixelsIsRefused (C38), AnUpscalerWithNoFrameInFlightIsRefused (C39), EachStorageSlotOfTheLuminancePyramidWritesItsOwnLevelAndTheSampledSlotAllOfThem (B06).
- Fsr3UpscalerTests, refusals: ARenderSizeOutsideTheLargestRenderSizeIsRefused (C43, C44, C45), AnOutputOfAnotherSizeThanTheOutputSizeIsRefused (C46).
- Fsr3UpscalerTests, lock-ins: TheFirstFrameOfAnUpscalerReadsNoHistoryEvenWithoutAReset (the decision `Reset || first`, which C09 shows AMD's shaders also hold), WithoutResetsTheUpscaleOfAStillPatternSettles (C11, C20, C64), AResetFrameUpscalesAsTheFirstFrameOfANewUpscalerDoes (C17, C18), AChangeOfThePreExposureScalesTheHistoryByTheRatioAlone (C11, C20, C64), ANewUpscalerOverTheMemoryOfAnOldOneReadsNoneOfItsImages (C08), TheFirstFrameAfterAChangeOfTheSceneFollowsTheNewScene (C55, C56), AnExposureBufferThatTheGpuWritesJustBeforeTheUpscaleGivesItsRatioWithoutAHazard (C68), TwoFramesRecordedIntoOneCommandBufferReportNothingToValidation (C12), TheGeneratorMarksEveryPixelWhoseColorDiffersFromTheOpaqueOnlyColorWithAmdsBinaryValue (C16, C61).

The constant-color tests cannot show the history, because AMD's rectification clamps the history to the current frame's neighborhood, which is one color. The new lock-ins use the pattern of colored squares. Their thresholds come from measurements on the M1 Max, and each test's comment gives them.

No defect found. Open question for a later ticket: C50, C57, C58 and C66 change the output, but only a reference image can tell a right output from a wrong one. A reference upscale, such as an approved FLIP snapshot of the settled pattern, would kill them.
