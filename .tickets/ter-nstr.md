---
id: ter-nstr
status: closed
deps: []
links: []
created: 2026-11-12T02:59:35Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Fewer GPU dispatches: one dispatch for FSR's 13 clears, and the depth and light pyramids in fewer dispatches

Sweep rows 2 and 4 (/private/tmp/tertius-sweep-20261111/report.md, section 7), approved by the user on 2026-11-11. Exact image. Expected gain on the deck: 0.15 to 0.30 ms for the clears and 0.10 to 0.15 ms for the pyramids. Each dispatch behind a barrier is its own Metal encoder under MoltenVK 1.4.2, and each encoder boundary idles the GPU about 17 us (2.32 ms over 139 boundaries on the deck), so fewer dispatches also cut that idle time on the Mac. Images must stay bit-identical; measure the deck before and after.


## Notes

**2026-11-12T07:03:50Z**

2026-11-12 orchestrator: the pyramid half waits. With the patched MoltenVK (ter-2zga) it saves about 0 ms (the patch keeps all 13 pyramid barriers in one encoder); build it only if the user rejects ter-2zga. The FSR clears half goes ahead and merges on its own.

**2026-11-12T07:34:16Z**

Scope change from the orchestrator on 2026-11-12: ter-nstr builds only the FSR clears (one compute dispatch, sweep addendum A6 proposal 2). The pyramid half waits for the user's decision on the patched MoltenVK, because the patch already keeps the pyramids' 13 barriers in one encoder, so the pyramid half saves about 0 ms with the patch and 0.10 to 0.15 ms only on stock MoltenVK. It is worth building only if the user rejects the patch. Ticket te-r7nt holds it, with the design notes.

**2026-11-12T09:49:28Z**

Mutation testing (tools/mutate.py, 21 listed mutants of commit a0d717094; Rendering only, so no Stryker run):

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| C01 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:345 | Record declares no barrier for the clear step | killed | Fsr3UpscalerTests.TwoTimedBatchesInFlightOfHalfTheUpscalersFramesInFlightReportNothingToValidation, Fsr3UpscalerTests.TheLargestWindowAllocationHandlesTheControllersActiveSizesUnderValidation, Fsr3UpscalerTests.AConstantColorUpscalesToTheSameColorBelowTheLargestRenderSizeAndAfterTheRenderSizeChanges | 11.1 |
| C02 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:347 | Record dispatches no clear step | killed | Fsr3UpscalerTests.AnUpscaleZeroesTheLevelsOfTheLuminancePyramidThatNoPassOfAmdsWrites, Fsr3UpscalerTests.TheSettledPatternAndTheFramesAfterAChangeOfTheSceneMatchTheirReferenceImage | 10.1 |
| C03 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:616 | level guard > to >= | killed | Fsr3HostTests.ALargestRenderSizeIsRefusedWhenItsLuminancePyramidHasMoreLevelsThanTheClearStepBinds(width: 16383, accepted: True) | 4.6 |
| C04 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:616 | level guard admits 14 levels | killed | Fsr3HostTests.ALargestRenderSizeIsRefusedWhenItsLuminancePyramidHasMoreLevelsThanTheClearStepBinds(width: 16384, accepted: False) | 4.6 |
| C05 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:300 | push constant one level short | killed | Fsr3UpscalerTests.TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid, Fsr3UpscalerTests.AnUpscaleZeroesTheLevelsOfTheLuminancePyramidThatNoPassOfAmdsWrites | 8.3 |
| C06 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:310 | dispatch covers only the pyramid's level 0 | killed | Fsr3UpscalerTests.TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid, Fsr3UpscalerTests.TheSettledPatternAndTheFramesAfterAChangeOfTheSceneMatchTheirReferenceImage | 86.7 |
| C07 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:967 | last level's binding holds the level below | killed | Fsr3UpscalerTests.TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid, Fsr3UpscalerTests.AnUpscaleZeroesTheLevelsOfTheLuminancePyramidThatNoPassOfAmdsWrites | 7.5 |
| C08 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:276 | the counter leaves the zeroed images | killed | Fsr3HostTests.EveryClearOfAmdsOnAnImageThatTheClearStepDoesNotZeroIsATransferClearWithAmdsValueInAmdsOrder, Fsr3UpscalerTests.TwoTimedBatchesInFlightOfHalfTheUpscalersFramesInFlightReportNothingToValidation, Fsr3UpscalerTests.TheLargestWindowAllocationHandlesTheControllersActiveSizesUnderValidation | 10.3 |
| C09 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:288 | barrier keeps the contents | survived |  | 10.3 |
| C10 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:147 | limit check and pool skip the clear step | killed | Fsr3HostTests.ADeviceWhoseComputeStageSeesFewerStorageImagesThanTheClearStepBindsIsRefused | 4.0 |
| C11 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:665 | images without the copy-source usage | survived |  | 10.0 |
| C12 | src/Tertius.Rendering/Upscaling/Fsr3Bindings.cs:268 | SpdLevel stops at level 11 | killed | Fsr3HostTests.EachStorageSlotOfTheLuminancePyramidWritesItsOwnLevelAndTheSampledSlotAllOfThem, Fsr3UpscalerTests.AnOutputOfAnotherSizeThanTheOutputSizeIsRefused(width: 97, height: 72), Fsr3UpscalerTests.AnOutputOfAnotherSizeThanTheOutputSizeIsRefused(width: 96, height: 73) | 10.0 |
| C13 | src/Tertius.Rendering/Upscaling/Fsr3Bindings.cs:239 | level bindings shifted by one | killed | Fsr3HostTests.TheClearStepBindsAStorageImageAtEachBindingThatItsCompiledModuleDeclares | 6.3 |
| C14 | src/Tertius.Rendering/Upscaling/Fsr3Upscaler.cs:998 | counter binding holds the reconstructed depth | killed | Fsr3UpscalerTests.TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid | 9.4 |
| S01 | src/Tertius.Rendering/Shaders/fsr_clear.comp:67 | counter written at texel (1, 1) | killed | Fsr3UpscalerTests.TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid | 8.9 |
| S02 | src/Tertius.Rendering/Shaders/fsr_clear.comp:70 | loop skips the last level | killed | Fsr3UpscalerTests.TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid, Fsr3UpscalerTests.AnUpscaleZeroesTheLevelsOfTheLuminancePyramidThatNoPassOfAmdsWrites | 8.1 |
| S03 | src/Tertius.Rendering/Shaders/fsr_clear.comp:72 | no early end of the level loop | survived |  | 8.3 |
| S04 | src/Tertius.Rendering/Shaders/fsr_clear.comp:66 | no store into the reconstructed depth | killed | Fsr3UpscalerTests.TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid, Fsr3UpscalerTests.TwoTimedBatchesInFlightOfHalfTheUpscalersFramesInFlightReportNothingToValidation, Fsr3UpscalerTests.TheLargestWindowAllocationHandlesTheControllersActiveSizesUnderValidation | 113.9 |
| S05 | src/Tertius.Rendering/Shaders/fsr_clear.comp:37 | pyramid levels get ones | killed | Fsr3UpscalerTests.TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid, Fsr3UpscalerTests.AnUpscaleZeroesTheLevelsOfTheLuminancePyramidThatNoPassOfAmdsWrites | 34.2 |
| S06 | src/Tertius.Rendering/Shaders/fsr_clear.comp:53 | level 9 stores into level 8 | killed | Fsr3UpscalerTests.TheClearStepZeroesEveryTexelOfTheReconstructedDepthTheCounterAndEachLevelOfTheLuminancePyramid | 8.0 |
| S07 | src/Tertius.Rendering/Shaders/fsr_clear.comp:64 | bounds test >= to > | survived |  | 8.1 |
21 rows: killed 17, timeout 0, survived 4, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

Answers to the survivors:
- C09, equivalent: the clear step writes every texel of every level of its three images, so a barrier that keeps their old contents changes no texel that any reader sees.
- S03, equivalent: each level of the pyramid is no larger than the one below, so after the first level that a texel lies outside, the loop stores nothing more.
- C11, judgment call: the copy-source usage serves only the tests' readback of FSR's images. Without it the read still works on MoltenVK and shows only as a validation message, which these tests do not assert.
- S07, judgment call: the mutant stores one texel past the reconstructed depth's edge. The Vulkan spec leaves that store undefined without the robustness features, Metal drops it, and no test on this driver can see it.

**2026-11-12T10:53:08Z**

Coverage of the changed lines (FSR tests, Debug): 99/101 lines, 15/16 branches. The two uncovered lines each throw UnreachableException, which only a bug reaches.

Timing on the deck (scripts/temporal-upscale-timing.json, Release, 1920 x 1080, 1,296 MHz, exclusive GPU lock, /private/tmp/tertius-nstr/time). Window-like runs (--in-flight 2, --pass-timestamps off), five alternating rounds on stock MoltenVK: medians main 23.141, 23.207, 23.184, 23.213, 23.422 ms; branch 23.101, 23.201, 23.154, 23.206, 23.238 ms. Median of the round medians 23.207 against 23.201 ms, median difference -0.03 ms, within the noise. Median p95 23.708 against 23.734 ms. Patched MoltenVK (ter-2zga), two rounds: -0.18 and -0.02 ms. With a time stamp after each pass (the Metal trace runs): GPU timer median 24.431 against 24.201 ms. Metal System Trace: FSR 22 -> 10 encoders, idle before FSR 0.42 -> 0.26 ms; frame 140 -> 128 encoders, idle per frame 2.41 -> 2.23 ms. So the clears' encoders are gone, but in a window-like run most of their cost overlapped other GPU work: the frame gains about 0.03 ms on stock, not the estimated 0.23 to 0.30 ms.

Images: the 67 frames of scripts/temporal-upscale-validation.json (deck, glass and exterior stills and films) and the 16-spot lamp hall are byte-identical to main d418c984d.

Final tests on the merged code: Checked 10,770 passed, Release 10,757 passed, 0 failed.
