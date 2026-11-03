---
id: ter-ohqr
status: in_progress
deps: []
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# Motion vectors, the frame history and the surface test

Build ticket 2 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, section 3, section 8 ("The surface test") and decisions 14 and 19 to 24. Acceptance: section 14, item 2. Nothing on screen changes. Its result is the measured time of the G-buffer and the surface test.


## Notes

**2026-11-03T14:56:37Z**

2026-11-03: launched as workflow run 6a1e1603-9008-429e-af16-654b92848535, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-ohqr-motion-vectors from 70fe35650.

**2026-11-03T16:17:30Z**

Builder, 2026-11-03: built and measured.

What was built:
- The snapshot: `RenderInstance.EntityGeneration` and `PreviousScale`, and `RenderSnapshot.CameraCuts` (a count of builds that did not interpolate the camera). The builder keeps the last scale per entity in a `ScaleMemory` column. `RenderLight` gets no generation yet: ReSTIR's ticket adds it.
- `FrameHistory` (src/Tertius.Rendering/Frame/FrameHistory.cs): `Begin` is the one choke point of the resets (first frame, a new cut count, a new output size), the draw index and the last frame's camera relative to this eye. `Drew` commits after the submission, beside `EnvironmentMap.Drew`. Contracts on `Drew`, and `AssertInvariants`.
- The placement history: `SceneBatcher.Prepare` joins the snapshot's keys (slot, generation, part) with the last frame's in one sorted merge (`MotionJoin`) and writes one `GpuMotion` per placed instance (Moved, Still, New). The batcher now interpolates the scale.
- The kept deformed positions (`KeptPositions`): two device buffers by parity, written by `deform.comp` (deform set binding 5), read by `mesh.vert` (frame set binding 18).
- The motion target, RGBA16F, the G-buffer's seventh attachment, written by `mesh.vert` and `gbuffer.frag` under `ShaderConstants.WritesMotion`, and by the impostor billboards. A device below 8 color attachments gets a `NotSupportedException`.
- The surface test pass (`SurfaceTestPass`, `surface_test.comp`, `signals.glsl`'s `surfaceTaps`) and `SurfaceHistory`: two surface histories, the R8_UINT surface test image, the R32F device depth, and the sky's motion, in one dispatch after the G-buffer.
- Read hooks for the tests: `ReadMotion`, `ReadSurfaceTest`, `ReadDeviceDepth`, `ReadSurfaceHistory`.

Choices inside the design:
- The motion rows are indexed by placed index, not like the instance array, because the billboards, faces and shadow views append instances after the main view's. A billboard reads its object's row.
- `GpuMotion` adds `FirstVertex` in its padding, so a vertex finds its place in its kept copy.
- The frame set's vertex stage now sees 10 storage buffers (was 8): the motion rows and the kept positions.

Measured (Release, M1 Max, demo-exit's deck window, `--start 399`, 120 frames; Metal traces without counters, 1-minute loads 13.5 to 31 at the starts, because other agents built at the same time):

| Pass | 1280 × 720 base | 1280 × 720 branch | 1920 × 1080 base | 1920 × 1080 branch | Budget at 1280 × 720 |
|---|---:|---:|---:|---:|---:|
| G-buffer, busy mean, ms | 0.284, 0.333 | 0.341 | 0.398 | 0.425 | 0.22 |
| Surface test, busy mean, ms | | 0.187 | | 0.325 | 0.10 |

The renderer's timer, 7 interleaved rounds of base, a byte copy of base as the control, and the branch, on the deck and floor windows (loads 4.2 to 20.6 at 1280 × 720, 6.5 to 14.0 at 1920 × 1080): the surface test's median 0.173 and 0.198 ms at 1280 × 720, 0.304 and 0.362 ms at 1920 × 1080. The G-buffer's median paired change from base: +0.014 and +0.009 ms at 1280 × 720, +0.018 and −0.008 ms at 1920 × 1080, against the control's +0.036, +0.005, +0.008 and −0.003 ms. So the motion target's cost is within the noise, and the surface test takes about twice its budget of 0.10 ms. The base G-buffer already takes more than the budget's 0.22 ms.

**2026-11-03T17:37:21Z**

Fix stage, 2026-11-03: every finding of the review is resolved.

| Finding | Resolution |
|---|---|
| `RenderLight` has no generation | Both cases gain `EntityGeneration` after `EntityIndex`. `RenderSnapshotBuilder.AddLight` takes the entity and writes its generation. Two tests in `RenderSnapshotKeyTests` (a point and a spot lamp in a reused slot) failed with 0 before the fix. A light takes 88 bytes now, 84 before. |
| Two counts of drawn frames | `VulkanRenderer.frameNumber` is gone. The renderer reads `FrameHistory.DrawnFrames` through a derived `FrameNumber`. `CommitDrawnFrame` calls `history.Drew` last, because it advances the number that the other commits name. |
| A growth of the kept positions commits at record time | `KeptPositions` keeps the replaced buffers as the copy's source until `KeptPositions.Drew`, after the submission. Every frame before it records the copy again. Three tests in `GeometryStoreRegionTests` failed before the fix. `ter-nua7` holds the same class in `GeometryStore` (uploads, vertex buffer growth) and in the tracked barrier state. |
| The interpolated scale can be flat | `InterpolatedScale` takes the current scale when an axis changes its sign. `BatcherMotionTests.AnInstanceThatMirrorsBetweenTwoTicksDrawsAtItsScaleNotAFlatOne` found (0, 1, 1) before the fix. |
| No check of `maxFragmentOutputAttachments` | `ColorAttachmentProblem` also refuses a device with fewer than 8 fragment outputs. A test in `MotionTargetTests` failed before the fix. ARCHITECTURE.md has the row. |
| No test turns the camera, and no test of the billboard's motion | New tests: `TheMotionProjectionPlacesAPointWhereTheTurnedCameraSeesIt` (CPU, 3 rotations), `AStillBoxAndTheSkySeenFromATurningCameraGiveTheMotionOfTheTurn`, `ABillboardMovesWithItsObjectsCenter` and `ANewBillboardGivesAMotionOutsideTheLastFramesImage` (GPU). They test code that already existed, so each passed on its first run. |
| Nobody saw the 13 GPU tests fail on a wrong result | Not done in this stage. The brief of the fix stage forbids mutation testing, and CLAUDE.md forbids hand-made mutants. The hardening pass runs `tools/mutate.py` over `motion.glsl`, `mesh.vert`, `surface_test.comp`, `signals.glsl` and `impostor.frag`, which shows whether each GPU test can fail. |
| The kept positions follow the region only by the caller's order | `GeometryStore.ReserveDeformed` takes the kept positions and grows them in the same step. `DeformPass.Record` requires that they hold the region. A comment gives the reason for the order of `UpdateKeptPositions` after `ReserveRegion`. |
| `PendingFrame` exposes `Arm`, `TakePlacements` and a mutable list | `PendingFrame` is nested in `FrameHistory` and holds no state. Only `Begin` arms it and only `Drew` commits it. Its `Write` and `Place` keep their contracts. |
| `KeptCopy.VertexCount` is dead | Removed. A comment in `MotionJoin.Place` says why: the join compares the static mesh first, and a mesh never changes its vertices. |
| A doc comment on the wrong method | It sits on `CreateDeformStore` again. |

Other changes: ARCHITECTURE.md now says that the interpolated scale changes the image for a growing object, and lists the case where the last frame drew a static mesh and this frame deforms it.

**2026-11-03T21:56:48Z**

Hardening, 2026-11-03.

The far-depth fix (commit 182c34ddd): the surface history kept the view depth in a half float, at most 65,504 m, so a farther surface failed the surface test in every frame. The R32F device depth is now the one store of the view depth. `SurfaceHistory` keeps two device depths by the parity that alternates its two surface histories. The surface test reads the last one through `signals.glsl` (`DeviceDepthNear / lastDevice`), and FSR reads this frame's. The history's x channel stays unused. `SurfaceTestRenderTests.AStillSurfaceFarBeyondAHalfFloatsRangeFindsItsOwnSurfaceInTheLastFrame` (100 km and 10,000 km) failed on the old code with bit 0 clear at pixel 0, and passes now.

Coverage of the changed src lines (Debug and Release, `tools/coverage`): 704 of 710 lines (99.2 %), 159 of 172 branches (92.4 %). The uncovered lines and their answers:

| Place | Answer |
|---|---|
| KeptPositions.cs:92, a second growth before a drawn frame | Test: `GeometryStoreRegionTests.ASecondGrowthBeforeADrawnFrameRetiresTheEmptyBuffersOfTheFirstAndKeepsTheDrawnOnesAsTheSource` |
| FrameHistory.cs:168-169, `Drew` without a camera | Test: `FrameHistoryTests.ACommitOfADrawWhoseBatchWroteNoCameraThrowsAndCommitsNothing` |
| KeptPositions.cs:120, a parity without a source | No test: a growth sets both sources or neither, so the `continue` is defensive, as its comment says |
| FrameHistory.cs:200 and the partial branches 191, 199, 280, 288 | No test: these are the failing sides of contracts and of `AssertInvariants`, which only a bug reaches |
| RenderTargets.cs:451, 454 and SurfaceHistory.cs:122, the device refusals | No test: no test device lacks 8 attachments or a required storage format. `ColorAttachmentProblem` has its own tests |
| DeformPass.cs:73, the `UnreachableException` | No test: a frame with a job has a region, which the kept positions hold |
| RenderSnapshotBuilder.cs:940-941, partial branches | No test: the failing sides of `AssertInvariants` |

The official Stryker run (`tools/stryker/changed.py main`, 10,035 s, valid): Core had 12 spans and no mutant. Simulation had 17 mutants: 13 killed and 4 timeouts, 23.5 % of the run, below the 25 % share, at a 1-minute load of at most 50.5, 5.05 per core. The 4 timeouts remove or weaken a `Contract.Invariant` of `RenderSnapshotBuilder.AssertInvariants`. They count as kills under the rule, but no behavior can tell them from the original code. The two no-mutant lines (235-236, the cut count) and the lines without a Stryker mutant went to the list as K01 to K11.

The listed mutants (`tools/mutate.py`): 118 rows over the Rendering C# and GLSL and the Core and Simulation lines that Stryker could not mutate. The first run gave 79 killed, 28 survived and 11 build-failed. The 11 build failures were analyzer errors on an unused value, and each was rewritten so that it compiles. Each survivor that a test can reach got a new test, which the second and third runs showed red on its mutant: S17, S20, S21, S26, S33, S36, S37, S38, S42, S43, S47, C01, C03, C05, C27, C35, C41, C42, C43, C45, C46, C48, C54, C57 and C58. The table below gives each row's last run.

The answers to the 6 survivors:

| id | Answer |
|---|---|
| S29 | Judgment call: the projection clips every surface nearer than the 5 cm near plane, so 0.05 over the view depth exceeds 1 by at most a rounding step, and no test can draw a nearer surface. |
| S34 | Equivalent: no pass reads a sky texel's surface history, because its device depth of 0 skips the tap before the history read. |
| S39 | Judgment call: a texel fetch outside the image is undefined in Vulkan without robustImageAccess, so no portable test can tell the bound from a fetch that returns 0, as this device does. |
| S40 | Judgment call: without the skip, the tap divides 0.05 by 0, whose result Vulkan leaves undefined. On this device it gives infinity, and the plane test then fails the tap anyway. |
| C30 | Judgment call: a replaced kept buffer that retires one frame early is a use after free on the GPU, which only a submission in flight can show. The tests wait for the device, and `DeferredReleases` has its own tests. |
| K02 | Equivalent: a previous scale of zero has no volume, and `SceneBatcher.InterpolatedScale` takes the current scale for it, which `Create` writes as the previous scale. |

The 13 GPU tests that the review named, and one mutant that turned each red:

| Test | Mutants that failed it (first run) |
|---|---|
| MotionVectorRenderTests.ABoxThatMovesByAKnownStepGivesTheAnalyticMotion | S02 (depth ratio inverted), S01, S05, S10 |
| MotionVectorRenderTests.AStillBoxSeenFromACameraThatStepsGivesTheMotionOfTheEyesStep | S01 (last and now swapped), S05, S10 |
| MotionVectorRenderTests.ATurningBoxGivesTheAnalyticMotion | S10 (last rows 1 and 2 swapped), S01, S05 |
| MotionVectorRenderTests.AGrowingBoxGivesTheAnalyticMotion | S02, S01, S10 |
| MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions | S08 (never the kept copy), S09, S16, C09, C23, C31 |
| MotionVectorRenderTests.ANewInstanceGivesAMotionOutsideTheLastFramesImage | S04 (no-history motion 0), S03, S07, S18 |
| MotionVectorRenderTests.TheSkysMotionFollowsATurningRoot | S23 (root turn ignored), S22, S24, S35 |
| SurfaceTestRenderTests.EveryPixelOfAStillSceneFindsItsOwnSurfaceInTheLastFrame | S48 (last depth read as the device depth), C17, C52 |
| SurfaceTestRenderTests.AFrameThatResetsFindsNoSurfaceInTheLastFrame | S19 (reset ignored), C12, C36 |
| SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame | S41 (plane bound × 100), S25, S27 |
| SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest | S45 (roughness step 1) |
| SurfaceTestRenderTests.TheSurfaceHistoryHoldsEachPixelsNormalAndRoughness | S30 (roughness 0) |
| SurfaceTestRenderTests.TheDeviceDepthIsReversedAndInfiniteAndZeroInTheSky | S32 (sky device depth 1), S28 |

C24, C25, C37 and C39 failed every GPU test, so they do not count as evidence for a single test.

New tests, 22 in all, beside the far-depth test:
- Coverage: the two tests named above.
- Lock-in of the decisions: `MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions` and `SurfaceTestRenderTests.TheThirdFrameFindsTheSurfacesOfTheSecondFrameNotOfTheFirst` (the parity of the kept positions and of the surface histories follows the one count of drawn frames), `FrameHistoryTests.ADrawAtAnotherOutputSizeResets` for each axis alone, the mirror test for each of the three axes, and `AStillSurfaceOfTheSameRoughnessPassesTheSpecularTest`. A frame that throws changes no record and consumes no cut: `FrameHistoryTests` already held it, and a test at the renderer's level needs a hook that makes a frame throw after `Begin`, which the renderer does not have. A new generation in a reused slot is New: `BatcherMotionTests.AnInstanceInAReusedSlotWithAnotherGenerationIsNew` already held it.
- For the mutants: the turned wall, the half texel at the image's edge, the place just outside the image, the box that comes nearer, the sky's pass bits, the box's last column, the fold of 30°, the grazing walls, the cutout box, the instance past the first 1,024, two deformed copies, a copy that moved in the region, the frame after a region growth, two billboards, the slot of 0, the eye's offset, a previous scale too small to draw, and the validation check of two growths.

No other defect turned up.

The mutation table of both tools:

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| Simulation-3737 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:595 | renderInterpolate && last... -> (true?last.Scale :scale) | killed | RenderSnapshotKeyTests.WhenAnotherEntityBecomesTheCameraThePreviousScaleIsTheCurrentScale, RenderSnapshotKeyTests.AnObjectThatAppearsInTheWorldHasItsOwnScaleAsItsPreviousScale | - |
| Simulation-3738 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:595 | renderInterpolate && last... -> (false?last.Scale :scale) | killed | RenderSnapshotKeyTests.AFrameOwnerThatGrowsCarriesItsScaleAtThePreviousTick, RenderSnapshotKeyTests.ThePreviousScaleOfAGrowingObjectIsItsScaleAtThePreviousTick | - |
| Simulation-3739 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:595 | renderInterpolate && last... -> renderInterpolate \|\| last... | killed | RenderSnapshotKeyTests.WhenAnotherEntityBecomesTheCameraThePreviousScaleIsTheCurrentScale, RenderSnapshotKeyTests.AnObjectThatAppearsInTheWorldHasItsOwnScaleAsItsPreviousScale | - |
| Simulation-3740 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:595 | last.Stamp == buildStamp - 1 -> last.Stamp != buildStamp - 1 | killed | RenderSnapshotKeyTests.AFrameOwnerThatGrowsCarriesItsScaleAtThePreviousTick, RenderSnapshotKeyTests.AnObjectThatAppearsInTheWorldHasItsOwnScaleAsItsPreviousScale, RenderSnapshotKeyTests.ThePreviousScaleOfAGrowingObjectIsItsScaleAtThePreviousTick | - |
| Simulation-3741 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:595 | buildStamp - 1 -> buildStamp + 1 | killed | RenderSnapshotKeyTests.AFrameOwnerThatGrowsCarriesItsScaleAtThePreviousTick, RenderSnapshotKeyTests.ThePreviousScaleOfAGrowingObjectIsItsScaleAtThePreviousTick | - |
| Simulation-3876 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:940 | Contract.Invariant(last.S... -> ; | timeout |  | - |
| Simulation-3877 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:940 | last.Stamp >= 0 && last.S... -> last.Stamp >= 0 \|\| last.S... | timeout |  | - |
| Simulation-3878 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:940 | last.Stamp >= 0 -> last.Stamp < 0 | killed | UnitRotationWorldTests.AShipFrameThatPhysicsMovesKeepsAUnitRotationOnEveryTick, UnitRotationWorldTests.AStaticFrameWhoseOwnerWritesARotationThatIsNotUnitLengthGetsTheUnitRotationOfTheSameTurn(System.String,Tertius.DQuaternion), TransformTests.AnOwnerWithoutABodyWritesItsFramesTransform | - |
| Simulation-3879 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:940 | last.Stamp >= 0 -> last.Stamp > 0 | killed | UnitRotationWorldTests.AShipFrameThatPhysicsMovesKeepsAUnitRotationOnEveryTick, UnitRotationWorldTests.AStaticFrameWhoseOwnerWritesARotationThatIsNotUnitLengthGetsTheUnitRotationOfTheSameTurn(System.String,Tertius.DQuaternion), TransformTests.AnOwnerWithoutABodyWritesItsFramesTransform | - |
| Simulation-3880 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:940 | last.Stamp <= buildStamp -> last.Stamp > buildStamp | killed | UnitRotationWorldTests.AShipFrameThatPhysicsMovesKeepsAUnitRotationOnEveryTick, UnitRotationWorldTests.AStaticFrameWhoseOwnerWritesARotationThatIsNotUnitLengthGetsTheUnitRotationOfTheSameTurn(System.String,Tertius.DQuaternion), TransformTests.AnOwnerWithoutABodyWritesItsFramesTransform | - |
| Simulation-3881 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:940 | last.Stamp <= buildStamp -> last.Stamp < buildStamp | killed | StaleHandleWorldTests.AMemberWhoseGoalAlreadyHoldsAnswersFalseForAHandleThatNamesNoLiveEntity(System.String,System.String), StaleHandleWorldTests.AMemberThatReadsOrWritesAnEntityRefusesAHandleThatNamesNoLiveEntity(System.String,System.String), StaleHandleWorldTests.DestroyingADeadEntityReturnsFalseAndLeavesTheEntityThatReusedItsSlotAlive | - |
| Simulation-3882 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:941 | Contract.Invariant(camera... -> ; | timeout |  | - |
| Simulation-3883 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:941 | cameraCuts >= 0 && camera... -> cameraCuts >= 0 \|\| camera... | timeout |  | - |
| Simulation-3884 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:941 | cameraCuts >= 0 -> cameraCuts < 0 | killed | UnitRotationWorldTests.AShipFrameThatPhysicsMovesKeepsAUnitRotationOnEveryTick, UnitRotationWorldTests.AStaticFrameWhoseOwnerWritesARotationThatIsNotUnitLengthGetsTheUnitRotationOfTheSameTurn(System.String,Tertius.DQuaternion), TransformTests.AnOwnerWithoutABodyWritesItsFramesTransform | - |
| Simulation-3885 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:941 | cameraCuts >= 0 -> cameraCuts > 0 | killed | UnitRotationWorldTests.AShipFrameThatPhysicsMovesKeepsAUnitRotationOnEveryTick, UnitRotationWorldTests.AStaticFrameWhoseOwnerWritesARotationThatIsNotUnitLengthGetsTheUnitRotationOfTheSameTurn(System.String,Tertius.DQuaternion), TransformTests.AnOwnerWithoutABodyWritesItsFramesTransform | - |
| Simulation-3886 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:941 | cameraCuts <= buildStamp -> cameraCuts > buildStamp | killed | UnitRotationWorldTests.AShipFrameThatPhysicsMovesKeepsAUnitRotationOnEveryTick, UnitRotationWorldTests.AStaticFrameWhoseOwnerWritesARotationThatIsNotUnitLengthGetsTheUnitRotationOfTheSameTurn(System.String,Tertius.DQuaternion), TransformTests.AnOwnerWithoutABodyWritesItsFramesTransform | - |
| Simulation-3887 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:941 | cameraCuts <= buildStamp -> cameraCuts < buildStamp | killed | StaleHandleWorldTests.AMemberWhoseGoalAlreadyHoldsAnswersFalseForAHandleThatNamesNoLiveEntity(System.String,System.String), StaleHandleWorldTests.AMemberThatReadsOrWritesAnEntityRefusesAHandleThatNamesNoLiveEntity(System.String,System.String), StaleHandleWorldTests.DestroyingADeadEntityReturnsFalseAndLeavesTheEntityThatReusedItsSlotAlive | - |
| Simulation-Internal/Rendering/RenderSnapshotBuilder.cs-L235 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:235 | every Stryker mutant failed to compile | no-mutant |  | - |
| Simulation-Internal/Rendering/RenderSnapshotBuilder.cs-L236 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:236 | every Stryker mutant failed to compile | no-mutant |  | - |
| S01 | src/Tertius.Rendering/Shaders/motion.glsl:18 | motionOf: last and now swapped | killed | SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame, MotionVectorRenderTests.ATurningBoxGivesTheAnalyticMotion, MotionVectorRenderTests.TheSkysMotionFollowsATurningRoot | 19.8 |
| S02 | src/Tertius.Rendering/Shaders/motion.glsl:18 | motionOf: depth ratio inverted | killed | MotionVectorRenderTests.AGrowingBoxGivesTheAnalyticMotion, MotionVectorRenderTests.ABoxThatMovesByAKnownStepGivesTheAnalyticMotion | 20.4 |
| S03 | src/Tertius.Rendering/Shaders/motion.glsl:16 | motionOf: no check of a last w of 0 | killed | MotionVectorRenderTests.ANewInstanceGivesAMotionOutsideTheLastFramesImage, MotionVectorRenderTests.ANewBillboardGivesAMotionOutsideTheLastFramesImage | 20.5 |
| S04 | src/Tertius.Rendering/Shaders/motion.glsl:9 | no-history motion (2, 2) -> (0, 0) | killed | MotionVectorRenderTests.ANewInstanceGivesAMotionOutsideTheLastFramesImage, MotionVectorRenderTests.ANewBillboardGivesAMotionOutsideTheLastFramesImage | 19.9 |
| S05 | src/Tertius.Rendering/Shaders/motion.glsl:18 | motionOf: NDC to image units 0.5 -> 1.0 | killed | SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame, MotionVectorRenderTests.ATurningBoxGivesTheAnalyticMotion, MotionVectorRenderTests.TheSkysMotionFollowsATurningRoot | 20.4 |
| S06 | src/Tertius.Rendering/Shaders/motion.glsl:25 | lastClipOf: this frame's projection | killed | MotionVectorRenderTests.AStillBoxAndTheSkySeenFromATurningCameraGiveTheMotionOfTheTurn | 18.8 |
| S07 | src/Tertius.Rendering/Shaders/mesh.vert:66 | mesh.vert: New instance gets a last place | killed | MotionVectorRenderTests.ANewInstanceGivesAMotionOutsideTheLastFramesImage | 19.1 |
| S08 | src/Tertius.Rendering/Shaders/mesh.vert:36 | lastPositionOf: never the kept copy | killed | MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions, MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions | 9.5 |
| S09 | src/Tertius.Rendering/Shaders/mesh.vert:39 | lastPositionOf: kept x and y swapped | killed | MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions, MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions | 11.5 |
| S10 | src/Tertius.Rendering/Shaders/mesh.vert:64 | mesh.vert: last rows 1 and 2 swapped | killed | SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest, SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame, SurfaceTestRenderTests.TheThirdFrameFindsTheSurfacesOfTheSecondFrameNotOfTheFirst | 9.9 |
| S11 | src/Tertius.Rendering/Shaders/mesh.vert:60 | mesh.vert: writesMotion never | killed | SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest, SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame, SurfaceTestRenderTests.TheThirdFrameFindsTheSurfacesOfTheSecondFrameNotOfTheFirst | 9.4 |
| S12 | src/Tertius.Rendering/Shaders/gbuffer.frag:60 | gbuffer.frag: motion 0 | killed | SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest, SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame, SurfaceTestRenderTests.TheThirdFrameFindsTheSurfacesOfTheSecondFrameNotOfTheFirst | 9.7 |
| S13 | src/Tertius.Rendering/Shaders/impostor.frag:72 | billboard: does not move with its object | killed | MotionVectorRenderTests.ABillboardMovesWithItsObjectsCenter | 10.1 |
| S14 | src/Tertius.Rendering/Shaders/impostor.frag:72 | billboard: New gets a last place | killed | MotionVectorRenderTests.ANewBillboardGivesAMotionOutsideTheLastFramesImage | 11.1 |
| S15 | src/Tertius.Rendering/Shaders/impostor.frag:72 | billboard: object step reversed | killed | MotionVectorRenderTests.ABillboardMovesWithItsObjectsCenter | 9.7 |
| S16 | src/Tertius.Rendering/Shaders/deform.comp:147 | deform.comp: kept x from y | killed | MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions, MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions | 9.2 |
| S17 | src/Tertius.Rendering/Shaders/deform.comp:146 | deform.comp: kept index without the job target | killed | MotionVectorRenderTests.ADeformedMeshInTheFrameAfterTheRegionGrewMovesFromTheGrowingFramesPositions, MotionVectorRenderTests.EachOfTwoDeformedMeshesMovesFromItsOwnLastPositions | 12.9 |
| S18 | src/Tertius.Rendering/Shaders/frame.glsl:217 | frame.glsl: MotionNew constant | killed | MotionVectorRenderTests.ANewInstanceGivesAMotionOutsideTheLastFramesImage, MotionVectorRenderTests.ANewBillboardGivesAMotionOutsideTheLastFramesImage | 18.6 |
| S19 | src/Tertius.Rendering/Shaders/surface_test.comp:62 | surface test: reset ignored | killed | SurfaceTestRenderTests.AFrameThatResetsFindsNoSurfaceInTheLastFrame | 33.7 |
| S20 | src/Tertius.Rendering/Shaders/surface_test.comp:62 | surface test: no lower bound of the place | killed | SurfaceTestRenderTests.APixelWhoseLastPlaceLiesJustOutsideTheLastImageFindsNoSurface(column: 0, lastPlace: -0.25) | 12.9 |
| S21 | src/Tertius.Rendering/Shaders/surface_test.comp:62 | surface test: no upper bound of the place | killed | SurfaceTestRenderTests.APixelWhoseLastPlaceLiesJustOutsideTheLastImageFindsNoSurface(column: 319, lastPlace: 320.25) | 12.8 |
| S22 | src/Tertius.Rendering/Shaders/surface_test.comp:48 | sky: no motion written | killed | MotionVectorRenderTests.TheSkysMotionFollowsATurningRoot, MotionVectorRenderTests.AStillBoxAndTheSkySeenFromATurningCameraGiveTheMotionOfTheTurn | 10.7 |
| S23 | src/Tertius.Rendering/Shaders/surface_test.comp:47 | sky: root turn ignored | killed | MotionVectorRenderTests.TheSkysMotionFollowsATurningRoot | 13.1 |
| S24 | src/Tertius.Rendering/Shaders/surface_test.comp:45 | sky: ray not in root axes | killed | MotionVectorRenderTests.TheSkysMotionFollowsATurningRoot | 10.8 |
| S25 | src/Tertius.Rendering/Shaders/surface_test.comp:56 | view depth = distance | killed | SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame | 12.1 |
| S26 | src/Tertius.Rendering/Shaders/surface_test.comp:63 | last point ignores the depth ratio | killed | SurfaceTestRenderTests.ABoxThatComesNearerFindsItsOwnSurfaceInTheLastFrame | 12.5 |
| S27 | src/Tertius.Rendering/Shaders/surface_test.comp:60 | last place: motion subtracted | killed | SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame | 10.8 |
| S28 | src/Tertius.Rendering/Shaders/surface_test.comp:41 | sky test < 0 | killed | SurfaceTestRenderTests.TheDeviceDepthIsReversedAndInfiniteAndZeroInTheSky, MotionVectorRenderTests.TheSkysMotionFollowsATurningRoot, MotionVectorRenderTests.AStillBoxAndTheSkySeenFromATurningCameraGiveTheMotionOfTheTurn | 10.6 |
| S29 | src/Tertius.Rendering/Shaders/surface_test.comp:67 | device depth: no clamp to 1 | survived |  | 12.9 |
| S30 | src/Tertius.Rendering/Shaders/surface_test.comp:66 | history: roughness 0 | killed | SurfaceTestRenderTests.TheSurfaceHistoryHoldsEachPixelsNormalAndRoughness, SurfaceTestRenderTests.AStillSurfaceOfTheSameRoughnessPassesTheSpecularTest | 23.2 |
| S31 | src/Tertius.Rendering/Shaders/surface_test.comp:66 | history: normal code 0 | killed | SurfaceTestRenderTests.EveryPixelOfAStillWallTurnedAwayFromTheCameraFindsItsOwnSurfaceInTheLastFrame | 11.6 |
| S32 | src/Tertius.Rendering/Shaders/surface_test.comp:51 | sky device depth 1 | killed | SurfaceTestRenderTests.TheDeviceDepthIsReversedAndInfiniteAndZeroInTheSky | 10.5 |
| S33 | src/Tertius.Rendering/Shaders/surface_test.comp:50 | sky surface test bits 1 | killed | SurfaceTestRenderTests.EverySkyPixelFindsNoSurfaceInTheLastFrame | 13.1 |
| S34 | src/Tertius.Rendering/Shaders/surface_test.comp:49 | sky surface history 1 | survived |  | 12.8 |
| S35 | src/Tertius.Rendering/Shaders/surface_test.comp:46 | sky: last root turn inverted | killed | MotionVectorRenderTests.TheSkysMotionFollowsATurningRoot | 10.2 |
| S36 | src/Tertius.Rendering/Shaders/signals.glsl:39 | 2x2 footprint: no half-texel shift | killed | SurfaceTestRenderTests.APixelWhoseLastPlaceLiesInTheFirstHalfTexelOfTheLastImageTestsOnlyTheTexelsInsideIt | 20.8 |
| S37 | src/Tertius.Rendering/Shaders/signals.glsl:44 | 2x2 footprint: every tap the first texel | killed | SurfaceTestRenderTests.ATapAcrossAFoldOfThirtyDegreesFailsTheNormalTest, SurfaceTestRenderTests.TheRightmostColumnOfAStillBoxFindsNoSurfaceInTheFartherWallBesideIt, SurfaceTestRenderTests.APixelWhoseLastPlaceLiesInTheFirstHalfTexelOfTheLastImageTestsOnlyTheTexelsInsideIt | 21.4 |
| S38 | src/Tertius.Rendering/Shaders/signals.glsl:44 | 2x2 footprint: taps 1 and 2 swapped | killed | SurfaceTestRenderTests.ATapAcrossAFoldOfThirtyDegreesFailsTheNormalTest, SurfaceTestRenderTests.TheRightmostColumnOfAStillBoxFindsNoSurfaceInTheFartherWallBesideIt, SurfaceTestRenderTests.APixelWhoseLastPlaceLiesInTheFirstHalfTexelOfTheLastImageTestsOnlyTheTexelsInsideIt | 21.5 |
| S39 | src/Tertius.Rendering/Shaders/signals.glsl:45 | tap bound: no upper bound | survived |  | 21.7 |
| S40 | src/Tertius.Rendering/Shaders/signals.glsl:48 | tap: sky texel not skipped | survived |  | 21.7 |
| S41 | src/Tertius.Rendering/Shaders/signals.glsl:53 | plane bound x100 | killed | SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame | 18.6 |
| S42 | src/Tertius.Rendering/Shaders/signals.glsl:53 | plane bound: no abs | killed | SurfaceTestRenderTests.TheRightmostColumnOfAStillBoxFindsNoSurfaceInTheFartherWallBesideIt | 22.0 |
| S43 | src/Tertius.Rendering/Shaders/signals.glsl:53 | normal bound: any normal | killed | SurfaceTestRenderTests.ATapAcrossAFoldOfThirtyDegreesFailsTheNormalTest | 21.5 |
| S44 | src/Tertius.Rendering/Shaders/signals.glsl:53 | normal read from xy | killed | SurfaceTestRenderTests.EveryPixelOfAStillWallTurnedAwayFromTheCameraFindsItsOwnSurfaceInTheLastFrame | 19.6 |
| S45 | src/Tertius.Rendering/Shaders/signals.glsl:57 | roughness step any | killed | SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest | 19.1 |
| S46 | src/Tertius.Rendering/Shaders/signals.glsl:58 | specular bit never set | killed | SurfaceTestRenderTests.AStillSurfaceOfTheSameRoughnessPassesTheSpecularTest | 19.4 |
| S47 | src/Tertius.Rendering/Shaders/signals.glsl:28 | last view ray: y up | killed | SurfaceTestRenderTests.EveryTapOfAStillWallSeenAtAGrazingAngleThatLiesOnTheWallPasses(axisX: 1, axisY: 0) | 21.7 |
| S48 | src/Tertius.Rendering/Shaders/signals.glsl:50 | last depth = device depth | killed | SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest, SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame, SurfaceTestRenderTests.TheThirdFrameFindsTheSurfacesOfTheSecondFrameNotOfTheFirst | 20.6 |
| S49 | src/Tertius.Rendering/Shaders/signals.glsl:40 | plane bound not relative to depth | killed | SurfaceTestRenderTests.AStillSurfaceFarBeyondAHalfFloatsRangeFindsItsOwnSurfaceInTheLastFrame(distance: 10000000) | 18.4 |
| C01 | src/Tertius.Rendering/Scene/MotionJoin.cs:93 | Still: previousEye.X dropped | killed | BatcherMotionTests.AnInstanceWhoseKeyIsNotAboveTheOneBeforeItIsStillInTheCamerasFrame | 23.2 |
| C02 | src/Tertius.Rendering/Scene/MotionJoin.cs:97 | Still: previousEye.Y dropped | killed | BatcherMotionTests.AnInstanceWhoseKeyIsNotAboveTheOneBeforeItIsStillInTheCamerasFrame | 9.9 |
| C03 | src/Tertius.Rendering/Scene/MotionJoin.cs:101 | Still: previousEye.Z dropped | killed | BatcherMotionTests.AnInstanceWhoseKeyIsNotAboveTheOneBeforeItIsStillInTheCamerasFrame | 12.6 |
| C04 | src/Tertius.Rendering/Scene/MotionJoin.cs:33 | Identify: equal key keeps its identity | killed | BatcherMotionTests.AnInstanceWhoseKeyIsNotAboveTheOneBeforeItIsStillInTheCamerasFrame, BatcherMotionTests.TheHistoryKeepsTheInvariantsOfItsPlacementsAfterABatch | 12.7 |
| C05 | src/Tertius.Rendering/Scene/MotionJoin.cs:33 | Identify: first key 0 has no identity | killed | BatcherMotionTests.AnInstanceOfSlotZeroThatTheLastFrameDrewMovesFromTheRowsThatItDrew | 12.9 |
| C06 | src/Tertius.Rendering/Scene/MotionJoin.cs:56 | Place: a changed mesh is not New | killed | BatcherMotionTests.AnInstanceWhoseMeshChangedIsNew | 9.8 |
| C07 | src/Tertius.Rendering/Scene/MotionJoin.cs:78 | Find: cursor passes the equal key | killed | BatcherMotionTests.ThePreviousRowsAreTheRowsThatTheLastFrameDrewAtItsInterpolationFraction, BatcherMotionTests.AnInstanceThatTheLastFrameDrewMovesFromTheRowsThatItDrew, BatcherMotionTests.AnInstanceAfterAKeyThatTheLastFrameDidNotDrawStillFindsItsOwnKey | 10.4 |
| C08 | src/Tertius.Rendering/Scene/MotionJoin.cs:81 | Find: generation ignored | killed | BatcherMotionTests.AnInstanceInAReusedSlotWithAnotherGenerationIsNew | 11.1 |
| C09 | src/Tertius.Rendering/Scene/MotionJoin.cs:69 | Place: kept copy dropped | killed | MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions, MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions | 11.6 |
| C10 | src/Tertius.Rendering/Scene/MotionJoin.cs:65 | Place: Moved -> Still | killed | BatcherMotionTests.AnInstanceThatTheLastFrameDrewMovesFromTheRowsThatItDrew, BatcherMotionTests.AnInstanceAfterAKeyThatTheLastFrameDidNotDrawStillFindsItsOwnKey | 8.9 |
| C11 | src/Tertius.Rendering/Scene/MotionJoin.cs:104 | Still: kind Moved | killed | BatcherMotionTests.AnInstanceWhoseKeyIsNotAboveTheOneBeforeItIsStillInTheCamerasFrame | 9.4 |
| C12 | src/Tertius.Rendering/Frame/FrameHistory.cs:143 | Begin: cut count ignored | killed | FrameHistoryTests.ADrawWhoseSnapshotCountsAnotherCutResets, FrameHistoryTests.ADrawAfterASkippedSnapshotThatHeldACutResets, FrameHistoryTests.ADrawThatThrowsBeforeDrewDoesNotConsumeItsCut | 9.5 |
| C13 | src/Tertius.Rendering/Frame/FrameHistory.cs:144 | Begin: output width ignored | killed | FrameHistoryTests.ADrawAtAnotherOutputSizeResets(width: 1920, height: 720) | 9.8 |
| C14 | src/Tertius.Rendering/Frame/FrameHistory.cs:145 | Begin: output height ignored | killed | FrameHistoryTests.ADrawAtAnotherOutputSizeResets(width: 1280, height: 1080) | 11.7 |
| C15 | src/Tertius.Rendering/Frame/FrameHistory.cs:148 | Begin: draw index stays | killed | FrameHistoryTests.ADrawOfTheSameCutAfterADrawnFrameContinuesIt, FrameHistoryTests.ADrawThatThrowsBeforeDrewLeavesTheDrawIndexAndTheParityAsTheyWere, FrameHistoryTests.TheDrawIndexCountsTheDrawnFramesSinceTheLastReset | 9.7 |
| C16 | src/Tertius.Rendering/Frame/FrameHistory.cs:154 | Begin: another camera frame continues | killed | FrameHistoryTests.ADrawInAnotherFrameOfTheCameraContinuesNoPreviousCameraOrPlacement | 9.6 |
| C17 | src/Tertius.Rendering/Frame/FrameHistory.cs:244 | Parity always 0 | killed | FrameHistoryTests.TheParityAlternatesWithEachDrawnFrame, FrameHistoryTests.ACommittedDrawKeepsTheParityOfTheImagesThatItWrote, SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest | 11.2 |
| C18 | src/Tertius.Rendering/Frame/FrameHistory.cs:268 | PreviousViewOf: eye step reversed | killed | BatcherMotionTests.AnInstanceWhoseKeyIsNotAboveTheOneBeforeItIsStillInTheCamerasFrame, FrameHistoryTests.AResetByTheOutputSizeKeepsThePreviousCamera, FrameHistoryTests.ThePreviousEyeAfterAStepOfTenKilometersFarFromTheFramesOriginIsExactToAMillimeter | 10.4 |
| C19 | src/Tertius.Rendering/Frame/FrameHistory.cs:250 | PreviousRenderSize: this draw's | killed | FrameHistoryTests.ADrawAtAnotherRenderSizeDoesNotReset, FrameHistoryTests.ThePreviousCameraIsTheDrawnFramesCamera | 10.5 |
| C20 | src/Tertius.Rendering/Frame/FrameHistory.cs:181 | Drew: draw index not kept | killed | FrameHistoryTests.TheDrawIndexCountsTheDrawnFramesSinceTheLastReset | 12.0 |
| C21 | src/Tertius.Rendering/Frame/FrameHistory.cs:257 | PreviousPlacements: also across frames | killed | FrameHistoryTests.ADrawInAnotherFrameOfTheCameraContinuesNoPreviousCameraOrPlacement | 10.0 |
| C22 | src/Tertius.Rendering/Frame/FrameHistory.cs:157 | Begin: pending placements not cleared | killed | FrameHistoryTests.TheRecordKeepsItsInvariantsThroughDrawsResetsAndDrawsThatThrow | 9.9 |
| C23 | src/Tertius.Rendering/Resources/KeptPositions.cs:48 | kept positions: read = written | killed | MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions, MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions | 9.3 |
| C24 | src/Tertius.Rendering/Frame/SurfaceHistory.cs:63 | surface history: last = written | killed | SurfaceTestRenderTests.TheSurfaceHistoryHoldsEachPixelsNormalAndRoughness, SurfaceTestRenderTests.AFrameThatResetsFindsNoSurfaceInTheLastFrame, SurfaceTestRenderTests.TheDeviceDepthIsReversedAndInfiniteAndZeroInTheSky | 10.2 |
| C25 | src/Tertius.Rendering/Frame/SurfaceHistory.cs:69 | device depth: last = written | killed | SurfaceTestRenderTests.TheSurfaceHistoryHoldsEachPixelsNormalAndRoughness, SurfaceTestRenderTests.AFrameThatResetsFindsNoSurfaceInTheLastFrame, SurfaceTestRenderTests.TheDeviceDepthIsReversedAndInfiniteAndZeroInTheSky | 9.7 |
| C26 | src/Tertius.Rendering/Resources/KeptPositions.cs:91 | Grow: old always retired | killed | GeometryStoreRegionTests.TheBuffersThatAGrowthReplacedRetireWithTheFrameThatDrewTheirCopy, GeometryStoreRegionTests.AGrowthThatNoDrawnFrameFollowedIsCopiedAgainByTheNextFrame, GeometryStoreRegionTests.ASecondGrowthBeforeADrawnFrameRetiresTheEmptyBuffersOfTheFirstAndKeepsTheDrawnOnesAsTheSource | 11.9 |
| C27 | src/Tertius.Rendering/Resources/KeptPositions.cs:109 | Grow: source count overwritten | killed | GeometryStoreRegionTests.ASecondGrowthBeforeADrawnFrameRetiresTheEmptyBuffersOfTheFirstAndKeepsTheDrawnOnesAsTheSource | 12.1 |
| C28 | src/Tertius.Rendering/Resources/KeptPositions.cs:65 | Reserve: copies recorded only without sources | killed | GeometryStoreRegionTests.AGrowthThatNoDrawnFrameFollowedIsCopiedAgainByTheNextFrame, GeometryStoreRegionTests.ASecondGrowthBeforeADrawnFrameRetiresTheEmptyBuffersOfTheFirstAndKeepsTheDrawnOnesAsTheSource, GeometryStoreRegionTests.AGrowthThatAFrameDrewIsNotCopiedAgain | 9.2 |
| C29 | src/Tertius.Rendering/Resources/KeptPositions.cs:79 | Drew: sources kept | killed | GeometryStoreRegionTests.AGrowthThatAFrameDrewIsNotCopiedAgain | 9.6 |
| C30 | src/Tertius.Rendering/Resources/KeptPositions.cs:78 | Drew: source retires a frame early | survived |  | 11.8 |
| C31 | src/Tertius.Rendering/Frame/FrameResources.cs:192 | frame set binds the written kept buffer | killed | MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions, MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions | 10.0 |
| C32 | src/Tertius.Rendering/Frame/FrameResources.cs:203 | deform set binds the read kept buffer | killed | MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions, MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions | 9.3 |
| C33 | src/Tertius.Rendering/ShaderConstants.cs:82 | specialization: writesMotion 0 | killed | SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest, SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame, SurfaceTestRenderTests.TheThirdFrameFindsTheSurfacesOfTheSecondFrameNotOfTheFirst | 9.1 |
| C34 | src/Tertius.Rendering/Passes/GeometryPass.cs:54 | opaque pipeline writes no motion | killed | SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest, SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame, SurfaceTestRenderTests.TheThirdFrameFindsTheSurfacesOfTheSecondFrameNotOfTheFirst | 9.3 |
| C35 | src/Tertius.Rendering/Passes/GeometryPass.cs:65 | cutout pipeline writes no motion | killed | MotionVectorRenderTests.ACutoutBoxThatMovesGivesTheAnalyticMotion | 11.9 |
| C36 | src/Tertius.Rendering/Passes/SurfaceTestPass.cs:66 | reset push constant 0 | killed | SurfaceTestRenderTests.AFrameThatResetsFindsNoSurfaceInTheLastFrame | 9.3 |
| C37 | src/Tertius.Rendering/Frame/RenderTargets.cs:431 | attachments: 8 refused | killed | MotionTargetTests.ADeviceWhoseFragmentStageWritesFewerThanEightOutputsIsRefused, MotionTargetTests.ADeviceWithEightColorAttachmentsDrawsTheGBuffer, SurfaceTestRenderTests.TheSurfaceHistoryHoldsEachPixelsNormalAndRoughness | 4.3 |
| C38 | src/Tertius.Rendering/Frame/RenderTargets.cs:433 | fragment outputs: bound 4 | killed | MotionTargetTests.ADeviceWhoseFragmentStageWritesFewerThanEightOutputsIsRefused | 9.4 |
| C39 | src/Tertius.Rendering/Frame/RenderTargets.cs:326 | G-buffer set: distance not bound | killed | SurfaceTestRenderTests.TheSurfaceHistoryHoldsEachPixelsNormalAndRoughness, SurfaceTestRenderTests.AFrameThatResetsFindsNoSurfaceInTheLastFrame, SurfaceTestRenderTests.TheDeviceDepthIsReversedAndInfiniteAndZeroInTheSky | 9.5 |
| C40 | src/Tertius.Rendering/Scene/PreparedScene.cs:671 | AddPlaced: every motion row New | killed | BatcherMotionTests.AnInstanceWhoseKeyIsNotAboveTheOneBeforeItIsStillInTheCamerasFrame, BatcherMotionTests.AnInstanceThatTheLastFrameDrewMovesFromTheRowsThatItDrew, BatcherMotionTests.AnInstanceAfterAKeyThatTheLastFrameDidNotDrawStillFindsItsOwnKey | 11.8 |
| C41 | src/Tertius.Rendering/Scene/ImpostorManager.cs:419 | billboard placed index 0 | killed | MotionVectorRenderTests.ABillboardMovesWithItsOwnObjectsCenterNotWithAnotherObjects | 11.8 |
| C42 | src/Tertius.Rendering/Passes/ImpostorPass.cs:194 | billboard GpuInstance placed index 0 | killed | MotionVectorRenderTests.ABillboardMovesWithItsOwnObjectsCenterNotWithAnotherObjects | 12.1 |
| C43 | src/Tertius.Rendering/Scene/SceneBatcher.cs:262 | InterpolatedScale: no volume check | killed | BatcherMotionTests.AnInstanceWhosePreviousScaleIsTooSmallToDrawDrawsAtItsScale | 12.6 |
| C44 | src/Tertius.Rendering/Scene/SceneBatcher.cs:262 | InterpolatedScale: mirror lerps | killed | BatcherMotionTests.AnInstanceThatMirrorsBetweenTwoTicksDrawsAtItsScaleNotAFlatOne(axis: 1), BatcherMotionTests.AnInstanceThatMirrorsBetweenTwoTicksDrawsAtItsScaleNotAFlatOne(axis: 2), BatcherMotionTests.AnInstanceThatMirrorsBetweenTwoTicksDrawsAtItsScaleNotAFlatOne(axis: 0) | 12.3 |
| C45 | src/Tertius.Rendering/Scene/SceneBatcher.cs:261 | InterpolatedScale: z sign ignored | killed | BatcherMotionTests.AnInstanceThatMirrorsBetweenTwoTicksDrawsAtItsScaleNotAFlatOne(axis: 2) | 12.4 |
| C46 | src/Tertius.Rendering/Scene/SceneBatcher.cs:243 | DrawnCamera: eye offset dropped | killed | BatcherMotionTests.ThePreviousEyeFollowsTheEyesOffsetFromTheRenderOrigin | 12.6 |
| C47 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1388 | MotionProjection: y up | killed | BatcherMotionTests.TheMotionProjectionPlacesAPointWhereTheTurnedCameraSeesIt(yaw: 0.300000012, pitch: 0, roll: 0), BatcherMotionTests.TheMotionProjectionPlacesAPointWhereTheTurnedCameraSeesIt(yaw: 0, pitch: 0, roll: 0), BatcherMotionTests.TheMotionProjectionPlacesAPointWhereTheTurnedCameraSeesIt(yaw: 0.200000003, pitch: -0.400000006, roll: 0.100000001) | 9.0 |
| C48 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1486 | PreviousCameraRight: tan of the vertical half fov | killed | SurfaceTestRenderTests.EveryTapOfAStillWallSeenAtAGrazingAngleThatLiesOnTheWallPasses(axisX: 0, axisY: 1) | 12.4 |
| C49 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1490 | PreviousCameraForward: not turned | killed | BatcherMotionTests.TheFrameConstantsHoldTheLastFramesCameraAndRenderSize | 9.5 |
| C50 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1496 | PreviousRootRotation: identity | killed | MotionVectorRenderTests.TheSkysMotionFollowsATurningRoot | 12.3 |
| C51 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1484 | PreviousEye: 0 | killed | BatcherMotionTests.ThePreviousRowsOfAStillObjectAreRelativeToTheLastFramesEye | 8.8 |
| C52 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1492 | PreviousViewport: width from height | killed | BatcherMotionTests.TheFrameConstantsHoldTheLastFramesCameraAndRenderSize, SurfaceTestRenderTests.ASurfaceWhoseRoughnessChangedPassesTheSurfaceTestAndFailsTheSpecularTest, SurfaceTestRenderTests.ABoxThatMovesUncoversPixelsThatFindNoSurfaceInTheLastFrame | 10.1 |
| C53 | src/Tertius.Rendering/Scene/SceneBatcher.cs:516 | Place: scale not interpolated | killed | BatcherMotionTests.AGrowingInstancesBoundFollowsItsInterpolatedSize, BatcherMotionTests.AGrowingInstanceDrawsAtItsInterpolatedScale | 9.4 |
| C54 | src/Tertius.Rendering/Scene/SceneBatcher.cs:482 | MotionOf: frame mesh, not static mesh | killed | MotionVectorRenderTests.ADeformedMeshWhoseCopyMovedInTheRegionMovesFromItsLastPositions | 13.1 |
| C55 | src/Tertius.Rendering/Scene/SceneBatcher.cs:497 | KeptMeshOf: no kept copy | killed | MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions, MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions | 9.4 |
| C56 | src/Tertius.Rendering/Scene/SceneBatcher.cs:484 | MotionOf: first vertex 0 | killed | MotionVectorRenderTests.ADeformedMeshMovesFromItsLastFramesDeformedPositions, MotionVectorRenderTests.AThirdFrameOfADeformedMeshMovesFromTheSecondFramesPositions | 9.0 |
| C57 | src/Tertius.Rendering/Frame/FrameResources.cs:167 | motion buffer growth not rebound | killed | MotionVectorRenderTests.AnInstancePlacedAfterTheFirstThousandGivesItsMotion | 12.0 |
| C58 | src/Tertius.Rendering/VulkanRenderer.cs:578 | commit: kept positions never drawn | killed | MotionVectorRenderTests.ADeformedMeshInTheFrameAfterTheRegionGrewMovesFromTheGrowingFramesPositions | 12.3 |
| K01 | src/Tertius.Core/Seams/RenderSeam.cs:161 | RenderInstance.Create: generation dropped | killed | RenderSnapshotKeyTests.AnInstanceCarriesTheGenerationOfItsEntity, RenderSnapshotKeyTests.ANewEntityInTheSlotOfADestroyedOneCarriesItsOwnGeneration | 4.2 |
| K02 | src/Tertius.Core/Seams/RenderSeam.cs:172 | RenderInstance.Create: previous scale zero | survived |  | 4.3 |
| K03 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:220 | build: cut count not copied | killed | RenderSnapshotInterpolationTests.WhenTheCameraChangesFrameTheSnapshotCountsOneMoreCut, RenderSnapshotInterpolationTests.ASnapshotWithoutACameraKeepsTheCountAndTheSnapshotAfterItCountsOneMoreCut, RenderSnapshotInterpolationTests.WhenAnotherEntityBecomesTheCameraTheSnapshotCountsOneMoreCut | 4.5 |
| K04 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:235 | build: cut counted when interpolating | killed | RenderSnapshotInterpolationTests.WhenTheCameraChangesFrameTheSnapshotCountsOneMoreCut, RenderSnapshotInterpolationTests.ASnapshotWithoutACameraKeepsTheCountAndTheSnapshotAfterItCountsOneMoreCut, RenderSnapshotInterpolationTests.WhenAnotherEntityBecomesTheCameraTheSnapshotCountsOneMoreCut | 4.1 |
| K05 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:236 | build: cut count stuck at 1 | killed | RenderSnapshotInterpolationTests.WhenTheCameraChangesFrameTheSnapshotCountsOneMoreCut, RenderSnapshotInterpolationTests.ASnapshotWithoutACameraKeepsTheCountAndTheSnapshotAfterItCountsOneMoreCut, RenderSnapshotInterpolationTests.WhenAnotherEntityBecomesTheCameraTheSnapshotCountsOneMoreCut | 5.0 |
| K06 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:566 | instance generation 0 | killed | RenderSnapshotKeyTests.AnInstanceCarriesTheGenerationOfItsEntity, RenderSnapshotKeyTests.ANewEntityInTheSlotOfADestroyedOneCarriesItsOwnGeneration | 4.0 |
| K07 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:596 | scale memory not written | killed | RenderSnapshotKeyTests.ThePreviousScaleOfAGrowingObjectIsItsScaleAtThePreviousTick, RenderSnapshotKeyTests.AFrameOwnerThatGrowsCarriesItsScaleAtThePreviousTick | 4.0 |
| K08 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:596 | scale memory stamped a build early | killed | RenderSnapshotKeyTests.ThePreviousScaleOfAGrowingObjectIsItsScaleAtThePreviousTick, RenderSnapshotKeyTests.AFrameOwnerThatGrowsCarriesItsScaleAtThePreviousTick | 4.1 |
| K09 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:628 | point light generation 0 | killed | RenderSnapshotKeyTests.APointLampInTheSlotOfADestroyedOneCarriesItsOwnGeneration | 4.1 |
| K10 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:653 | spot light generation 0 | killed | RenderSnapshotKeyTests.ASpotLampInTheSlotOfADestroyedOneCarriesItsOwnGeneration | 4.0 |
| K11 | src/Tertius.Simulation/Internal/Rendering/RenderSnapshotBuilder.cs:570 | previous scale = current | killed | RenderSnapshotKeyTests.ThePreviousScaleOfAGrowingObjectIsItsScaleAtThePreviousTick, RenderSnapshotKeyTests.AFrameOwnerThatGrowsCarriesItsScaleAtThePreviousTick | 4.1 |
137 rows: killed 125, timeout 4, survived 6, no-coverage 0, build-failed 0, no-tests 0, no-mutant 2, removed 0
