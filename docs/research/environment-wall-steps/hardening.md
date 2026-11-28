# Regression and coverage evidence

The display regression moves the eye forward and backward past an offscreen emissive panel. The receiving wall is 14 m from the scene origin and stays outside all light grids. The test requires the bright and dark ends to differ by more than 10 display-luma levels. The ends differ by 90 levels. The test bounds the largest single-frame change to one fifth of that difference, 18 levels. The original renderer jumps by 59 and 47 levels. The new renderer's largest change is 12 levels toward the panel and 9 levels away from it. A blend over half a turn fails the bound (mutant R04 below).

The focused renderer run passed 75 tests. A later run passed all seven tests in `EnvironmentMotionTests`, including the new test for a second capture during an unfinished blend. After the review, the six environment test classes passed 100 tests.

| Behavior | Check |
|---|---|
| A capture starts changing the light of two directions in the same draw | Sample two irradiance directions on different cube faces. |
| A fresh capture replaces an active blend after a cut | Move behind an opaque panel and verify that both directions receive zero light from the hidden source on the first draw. |
| A whole-map capture can still finish its blend on another map's turn | Run two maps with six faces per turn. |
| A wrapped capture mask leaves unrelated blends alone | Run four faces per turn while the top face is still blending. |
| A second capture starts from the light already shown | Black out the source one draw into its blend and check the remaining light. |
| The settled light keeps its directional weights and occlusion | Compare the captured light with the weighted convolution and run the environment and reflection tests. |
| A blend lasts one turn of the capture schedule | Check that `EnvironmentFaces.TurnDraws` is the fewest draws in which `InTurn` draws every slot, from any frame. |
| The buffer size matches the shaders' layout | Read the header length and the record stride from the compiled SPIR-V of both irradiance shaders. |

## Changed-line coverage

After the review fixes, the Checked Cobertura report of the full Rendering project covers all 118 instrumented changed C# lines and all 26 changed branches. That run passed 3,815 tests, with 12 skipped. Before the review, the full Rendering run passed 3,805 tests, with 12 skipped. The seven CPU test projects passed 5,731 tests, with eight skipped. The Host project passed 2,198 tests, failed three and skipped 28. The same three tests fail on clean `main` at `d3123fe71`, with the same assertions. They expect the exposure timing and pass order from before #4, and #41 tracks them.

| Source file | Lines covered | Branches covered |
|---|---:|---:|
| `DescriptorLayouts.cs` | 3/3 | 0/0 |
| `EnvironmentMap.cs` | 27/27 | 6/6 |
| `EnvironmentPrefilterPass.cs` | 86/86 | 20/20 |
| `EnvironmentFaces.cs` | 1/1 | 0/0 |
| `IrradianceCube.cs` | 1/1 | 0/0 |

Cobertura covers the managed renderer. The shader checks use the rendered regression, Vulkan validation, SPIR-V validation and scripted mutations. `IrradianceContributionLayoutTests` checks that both compiled shaders use the 1,008-byte header and the 48-byte record stride that size the buffer. The [Vulkan resource check](README.md#vulkan-resources) records the largest allocations and dispatch sizes.

## Mutation testing

The change touches no code of Core, Simulation or Physics, so it has no official Stryker run. `tools/mutate.py` ran the first 18 of the [listed mutants](mutants.json) of the renderer's C# code and GLSL. Each mutant ran the test classes of its row in Checked. The first run killed 17 mutants. C02 did not compile, because the analyzer rejects an unused parameter. Its rerun computes the same wrong count with `Math.Min(servedMaps, 1)`, and the tests killed it. [The results](mutation-results.json) give 18 killed of these 18, with no survivors, timeouts or uncovered mutants. After each run the script restored the files, and the index tree was unchanged.

| Mutant | Line | Change | Result | First killing test |
|---|---|---|---|---|
| C01 | `EnvironmentMap.cs:269` | Blend through a camera cut | Killed | `EnvironmentMotionTests.CuttingBehindAnOpaquePanelUsesTheDestinationLightOnTheFirstDraw` |
| C02 | `EnvironmentMap.cs:271` | Use one map's turn for every chain length | Killed | `EnvironmentMotionTests.CapturingAWholeMapOnItsOrdinaryTurnStillEasesTheLight` |
| C03 | `EnvironmentMap.cs:273` | Round the draw count down | Killed | `EnvironmentMotionTests.ASecondCaptureStartsFromWhatTheWallAlreadyReceived` |
| C04 | `EnvironmentPrefilterPass.cs:361` | Combine only map zero | Killed | `EnvironmentMotionTests.CapturingAWholeMapOnItsOrdinaryTurnStillEasesTheLight` |
| C05 | `EnvironmentPrefilterPass.cs:311` | Treat a wrap as a capture of all six faces | Killed | `EnvironmentMotionTests.AWrapInTheCaptureOrderLetsAnUntouchedFaceFinishItsBlend` |
| C06 | `EnvironmentPrefilterPass.cs:379` | Build coarser irradiance levels only for map zero | Killed | `EnvironmentMapsPerFrameTests.ALitPuffOutsideTakesTheMeanRadianceOfTheOutsidesMap` |
| C07 | `EnvironmentPrefilterPass.cs:350` | Omit the dependency between convolution and combine | Killed | `EnvironmentMotionTests.ASecondCaptureStartsFromWhatTheWallAlreadyReceived` |
| C08 | `EnvironmentPrefilterPass.cs:326` | Convolve only the first captured face | Killed | `EnvironmentMapsPerFrameTests.AfterTwoTurnsOfTheFacesEveryFacesFiltersAreTheBitsOfARefresh` |
| G01 | `environment_irradiance_combine.comp:38` | Normalize by face count | Killed | `EnvironmentPrefilterTests.AUniformMapGivesACubeOfItsOwnRadiance(faceSize: 32)` |
| G02 | `environment_irradiance.comp:74` | Omit the cosine weight | Killed | `EnvironmentPrefilterTests.EachFramesIrradianceCubeIsTheConvolutionOfThatFramesMap` |
| G03 | `environment_irradiance.comp:101` | Finish one draw late | Killed | `EnvironmentMotionTests.ASecondCaptureStartsFromWhatTheWallAlreadyReceived` |
| G04 | `environment_irradiance_combine.comp:25` | Publish every target immediately | Killed | `EnvironmentMotionTests.ASecondCaptureStartsFromWhatTheWallAlreadyReceived` |
| G05 | `environment_irradiance_combine.comp:28` | Discard the light published partway through a blend | Killed | `EnvironmentMotionTests.ASecondCaptureStartsFromWhatTheWallAlreadyReceived` |
| G06 | `environment_irradiance_combine.comp:26` | Spread the first partial update over an extra draw | Killed | `EnvironmentMotionTests.ASecondCaptureStartsFromWhatTheWallAlreadyReceived` |
| G07 | `environment_irradiance.comp:60` | Read the next capture face | Killed | `EnvironmentMotionTests.ASecondCaptureStartsFromWhatTheWallAlreadyReceived` |
| G08 | `environment_irradiance.comp:39` | Use the wrong receiving direction | Killed | `EnvironmentPrefilterTests.EachFramesIrradianceCubeIsTheConvolutionOfThatFramesMap` |
| G09 | `environment_irradiance.comp:98` | Discard the captured open-sky share | Killed | `EnvironmentPrefilterTests.EachFramesIrradianceCubeIsTheConvolutionOfThatFramesMap` |
| G10 | `environment_irradiance_combine.comp:30` | Keep only the last face's radiance | Killed | `EnvironmentMotionTests.ASecondCaptureStartsFromWhatTheWallAlreadyReceived` |

The review moved the blend length into the capture schedule, so the code of C01 to C03 no longer exists. Rows R01 to R07 cover the lines that the review changed. The tests killed all seven. The walk regression also killed R01 and R04, a blend through a cut and a blend over half a turn.

| Mutant | Line | Change | Result | First killing test |
|---|---|---|---|---|
| R01 | `EnvironmentMap.cs:268` | Blend through a camera cut | Killed | `EnvironmentMotionTests.CuttingBehindAnOpaquePanelUsesTheDestinationLightOnTheFirstDraw` |
| R02 | `EnvironmentFaces.cs:31` | Use one map's turn for every chain length | Killed | `EnvironmentMotionTests.CapturingAWholeMapOnItsOrdinaryTurnStillEasesTheLight` |
| R03 | `EnvironmentFaces.cs:31` | Round the turn down | Killed | `EnvironmentFacesTests.ATurnIsTheFewestDrawsThatDrawEverySlotFromAnyFrame(maps: 1, facesPerFrame: 4)` |
| R04 | `EnvironmentMap.cs:268` | Blend over half a turn | Killed | `EnvironmentMotionTests.ASecondCaptureStartsFromWhatTheWallAlreadyReceived` |
| R05 | `EnvironmentMap.cs:266` | Capture a new map's faces in turn | Killed | `EnvironmentMapsPerFrameTests.WhenTheCameraEntersAChildItsNewMapAndTheOldCameraFramesMapRefresh` |
| R06 | `IrradianceCube.cs:25` | Size the records by a stride that the shaders do not use | Killed | `IrradianceContributionLayoutTests.AShaderStepsThroughTheRecordsByTheStrideThatTheBufferSizeCounts(file: "environment_irradiance_combine.comp")` |
| R07 | `IrradianceCube.cs:22` | Count a header shorter than the shaders' header | Killed | `IrradianceContributionLayoutTests.AShaderFindsTheFirstRecordAfterTheHeaderThatTheBufferSizeCounts(file: "environment_irradiance_combine.comp")` |
