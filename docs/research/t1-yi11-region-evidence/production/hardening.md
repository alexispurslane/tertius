# The hardening of t1-yi11

This record holds the checks of the tests themselves for commit `8081c945d` and the test commits after it: the full
suites, the Linux driver lavapipe, the coverage of the changed C# lines and the listed mutants. The ticket file stays
as the orchestrator wrote it, so the mutant table and its triage live here.

## The full suites

`bash tools/test-all.sh -c Checked`, then `-c Release`, each under `tools/gpu-lock.sh --shared`, on commit `4ed27ec97`
(the code of `495df3e81` less its one test change below):

| Module | Checked: total, failed, skipped | Release: total, failed, skipped |
|---|---|---|
| Core | 1,207, 0, 0 | 1,175, 0, 0 |
| Simulation | 2,007, 0, 0 | 2,026, 0, 0 |
| Physics | 1,500, 0, 8 | 1,501, 0, 8 |
| Rendering | 3,595, 0, 11 | 3,594, 0, 11 |
| Host | 2,089, 0, 8 | 2,089, 0, 8 |
| Unions | 59, 0, 0 | 59, 0, 0 |
| Style | 276, 0, 0 | 276, 0, 0 |
| Slop | 597, 0, 0 | 597, 0, 0 |

## Lavapipe

Two cloud jobs on a CPU worker (`tools/runpod/gpu_job.py submit --kind cpu`, Mesa 26.2.4, llvmpipe with LLVM 21.1.8,
Vulkan 1.4.354), through `docs/research/back-face-depth-layer/acceptance-tools/lavapipe-tests.sh`:

1. Commit `4ed27ec97`, the classes of `AccumulatorRenderTests` (nested ones too), `DiffuseFilterRenderTests` and
   `RegionGateSourceTests`: 200 tests, 199 passed, 1 failed. All 26 new gate tests passed. The failure was
   `DiffuseFilterRenderTests.ASingleBrightRayBlockSampleNeverGrowsMoreConspicuousOverThe32DrawsAfterIt`, which this
   ticket had changed for the cap of 128. After its highest draw the peak held at 0.0015420914 and moved by 2^-20, one
   half float step, up and down (0.0015420914, 0.0015411377, 0.0015420914). The test asked for a strict descending
   order. Commit `495df3e81` lets each draw after the highest pass the draw before by at most one half float step.
2. Commit `495df3e81`, `DiffuseFilterRenderTests`: 30 tests, 30 passed.

The two jobs cost $0.0190 and $0.0059 by the job log's estimate, $0.0249 in all. The trial had $11.15 left before them.

## Coverage of the changed C# lines

The branch changes two executable C# lines: `Cap: 128` and `SmoothCap: 128` in `SignalParameters.Diffuse`. The new
`TemporalSignal.RegionCauseWindowDraws` is a `const`, which has no sequence point. A Debug coverage run of
`RegionGateSourceTests` and `DiffuseFilterRenderTests` (32 tests, all passed) and
`tools/coverage/changed-lines.py --base 3cfcfd2b3` give: changed lines covered 2/2, no changed branches.

## Listed mutants

`tools/mutate.py` ran 32 listed mutants: 29 of the shader `region_drift.glsl` and 3 of the C# constants. Their classes
are `*AccumulatorRenderTests+RegionDrift*` and `*RegionGateSourceTests`, and `AccumulatorRenderTests` with
`DiffuseFilterRenderTests` for the two cap mutants. There is no Stryker row: the branch changes no line of Core,
Simulation or Physics.

| id | place | change | result | killed by (first of up to 3) | s |
|---|---|---|---|---|---:|
| S01 | `region_drift.glsl:43` | gate window 127 | killed | RegionGateSourceTests.TheRegionShaderKeepsTheGateOpenForRegionCauseWindowDraws | 108 |
| S02 | `region_drift.glsl:54` | seed bar 4 | killed | RegionDrift+SignedNeighbors.ANeighborsAnswerFromTwoDrawsBeforeConfirmsNothing | 69 |
| S03 | `region_drift.glsl:55` | confirm bar 3 | killed | RegionDrift+SignedNeighbors.ANeighborsAnswerFromTwoDrawsBeforeConfirmsNothing | 69 |
| S04 | `region_drift.glsl:171` | only light codes are causes | killed | RegionDrift+ChangeGate.ASamplesMotionBitIsAnObservedCauseToo | 67 |
| S05 | `region_drift.glsl:208` | no cause opens the gate | killed | RegionDrift+ChangeGate.ACameraCutRestartsAnOpenGateUnestablished | 68 |
| S06 | `region_drift.glsl:211` | a reset opens the gate | killed | RegionDrift+ChangeGate.AColdStartLeavesTheGateUnestablishedForItsFirstValidDraws | 68 |
| S07 | `region_drift.glsl:212` | an open gate at 0 counts as unestablished | killed | RegionDrift+ChangeGate.ACameraCutRestartsAnOpenGateUnestablished | 72 |
| S08 | `region_drift.glsl:213` | establish after 9 draws | killed | RegionDrift+ChangeGate.AfterFullRateFramesValidDrawsInARowTheGateIsEstablishedAndClosed | 68 |
| S09 | `region_drift.glsl:214` | a move does not hold the gate at 0 | killed | RegionDrift+ChangeGate.AfterAMoveStopsTheGateStaysOpenForRegionCauseWindowDraws | 67 |
| S10 | `region_drift.glsl:216` | the gate counts 2 a draw | killed | RegionDrift+ChangeGate.ACameraCutRestartsAnOpenGateUnestablished | 69 |
| S11 | `region_drift.glsl:231` | this draw has no square in the newer moments | killed | RegionDrift+SignedNeighbors.ANeighborsAnswerFromTwoDrawsBeforeConfirmsNothing | 64 |
| S12 | `region_drift.glsl:249` | open answer has no sign | killed | RegionDrift+ChangeGate.WhileTheGateIsOpenAChangeBelowTheSeedBarGetsASignedAnswer | 67 |
| S13 | `region_drift.glsl:263` | Welch uses the older spread twice | killed | RegionDrift+SignedNeighbors.ANeighborsAnswerFromTwoDrawsBeforeConfirmsNothing | 66 |
| S14 | `region_drift.glsl:265` | no guard of a zero spread | survived |  | 99 |
| S15 | `region_drift.glsl:267` | a zero-spread difference gives t = 0 | killed | RegionDrift+ChangeGate.UnderTheClosedGateAStepOfLightWithoutNoiseAnswers | 71 |
| S16 | `region_drift.glsl:279` | seed strictly above the bar | survived |  | 67 |
| S17 | `region_drift.glsl:281` | confirm strictly above the bar | survived |  | 66 |
| S18 | `region_drift.glsl:285` | no neighbor row below | killed | RegionDrift+SignedNeighbors.TheCornerAnchorsTakeTheAnswerOfTheirDiagonalNeighbor | 66 |
| S19 | `region_drift.glsl:286` | no neighbor column to the right | killed | RegionDrift+SignedNeighbors.TheCornerAnchorsTakeTheAnswerOfTheirDiagonalNeighbor | 66 |
| S20 | `region_drift.glsl:288` | no upper bound of the neighbors | killed | RegionDrift.AfterTheRenderSizeFallsNoBlockTakesAnAnswerThatNoAnchorGaveAtThatSize | 66 |
| S21 | `region_drift.glsl:289` | any sign confirms | killed | RegionDrift+SignedNeighbors.AWeakChangeDoesNotTakeTheAnswerOfANeighborOfTheOtherSign | 67 |
| S22 | `region_drift.glsl:289` | neighbors of two draws before | killed | RegionDrift+SignedNeighbors.ANeighborsAnswerFromTwoDrawsBeforeConfirmsNothing | 66 |
| S23 | `region_drift.glsl:299` | test after 18 valid draws | killed | RegionDrift+SignedNeighbors.ANeighborsAnswerFromTwoDrawsBeforeConfirmsNothing | 69 |
| S24 | `region_drift.glsl:302` | gate 0 is closed | killed | RegionDrift+ChangeGate.WhileTheGateIsOpenAChangeBelowTheSeedBarGetsASignedAnswer | 68 |
| S25 | `region_drift.glsl:302` | gate 128 is open | killed | RegionDrift+SignedNeighbors.ANeighborsAnswerFromTwoDrawsBeforeConfirmsNothing | 67 |
| S26 | `region_drift.glsl:322` | the gate before reads the answer | killed | RegionDrift+ChangeGate.AColdStartLeavesTheGateUnestablishedForItsFirstValidDraws | 67 |
| S27 | `region_drift.glsl:351` | newer parts have no squares | killed | RegionDrift+SignedNeighbors.ANeighborsAnswerFromTwoDrawsBeforeConfirmsNothing | 66 |
| S28 | `region_drift.glsl:363` | a reset keeps the valid draws | killed | RegionDrift+ChangeGate.ACameraCutRestartsAnOpenGateUnestablished | 66 |
| S29 | `region_drift.glsl:364` | one tap with a cause is not enough | killed | RegionDrift+SignedNeighbors.AnOpenGatesSignedAnswerConfirmsTheWeakChangeOfAClosedNeighbor | 70 |
| C01 | `SignalParameters.cs:58` | diffuse cap 127 | killed | DiffuseFilterRenderTests.AQuietPixelKeepsTheLightThatTheFilterMovedIntoIt | 111 |
| C02 | `SignalParameters.cs:59` | smooth cap 32 | killed | DiffuseFilterRenderTests.TheExcessOfASingleBrightRayBlockSampleFadesAtTheSlowBlendsPace | 76 |
| C03 | `TemporalSignal.cs:85` | C# gate window 127 | killed | RegionGateSourceTests.TheRegionShaderKeepsTheGateOpenForRegionCauseWindowDraws | 36 |

32 rows: killed 29, survived 3. S01 timed out first while it waited for the GPU lock (tc-5azv), and was killed when run again alone.

### The three survivors

| id | Answer | Reason |
|---|---|---|
| S14 | Judgment call | Without the guard, a spread of 0 divides by 0. Metal gives the IEEE result: ±infinity, which passes the seed bar as the guard's ±1e9 does, or NaN for no difference, which fails both bars and confirms nothing, as the guard's 0 does. So no test on this Mac can see the guard. The Vulkan specification leaves a division by 0 undefined, so the guard stays for the Linux drivers. |
| S16 | Judgment call | `>=` against `>` differs only where \|t\| equals the seed bar 5.0 exactly. t is a quotient of float sums of region lights with a square root of the spreads, so no designed test light gives exactly 5.0 without a fragile construction. The bar is a calibration from a replay, and its uncertainty is far larger than the choice at equality. |
| S17 | Judgment call | The same as S16 at the confirm bar 2.6. |
