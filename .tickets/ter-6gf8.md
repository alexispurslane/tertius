---
id: ter-6gf8
status: in_progress
deps: [ter-f17s]
links: []
created: 2026-11-04T22:37:00Z
type: chore
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# The placement key holds the instance's entity as one Entity

Found in the review of ter-f17s on 2026-11-04. PlacementKey(uint Slot, uint Generation, ushort Part) in src/Tertius.Rendering/Frame/FrameHistory.cs is a second encoding of the identity that PreparedInstance.Entity with Part holds, and that ImpostorKey and the shadow tiles' caster records now use. MotionJoin.Identify builds it from RenderInstance's two fields. It is correct, because it compares the generation too, but the renderer has two models of instance identity. Make it PlacementKey(Entity Entity, ushort Part), built from RenderInstance.Entity, with Order => ((ulong)Entity.Index << 16) | Part, so that Order still ignores the generation (one slot holds one entity at a time).

## Workflow launch, November 5, 2026, 09:06 EST

Prerequisite ter-f17s is merged, closed and pushed. This existing follow-up started from main 64ab730a0e0300942212d9b2d96e74f0bfcd3ec5 on its own T3-created branch ter-6gf8-placement-entity. Workflow run e9068609-558c-48ee-9769-c6c272602fff uses saved version 869b0b07-3413-4b06-8707-a88a91bf6e91, source/schema SHA-256 ef99649844214c80f35d90851840fa568fce615016714356c3fd31b470f6fade. Its stages are build, read-only review, and one fix stage if needed. Builder and reviewer use Codex gpt-6.1-sol with reasoningEffort xhigh. This is workflow launch, not implementation acceptance or ticket closure.

Settled scope and checks are in /private/tmp/tertius-integration-20261105/ter-6gf8-brief.md. Preserve generation-sensitive equality, generation-independent slot-and-part order, and existing motion/history behavior. Do not import unmerged tracer, light-budget or denoising work. The parent owns coverage, mutations, integration and closure. This workflow takes the freed fourth heavy-agent allocation; each worker runs only one heavy command at a time and checks load, memory and disk before starting it. Automatic workflow delivery is unavailable to this parent; stages may send one ordinary queued completion message as specified in the brief.

## Build stage, November 5, 2026

The builder worked only in `/Users/alexispurslane/.t3/worktrees/tertius/ter-6gf8-placement-entity`, on branch `ter-6gf8-placement-entity`. The starting HEAD was `64ab730a0e0300942212d9b2d96e74f0bfcd3ec5`, and the checkout was clean. The builder ran `tk start ter-6gf8` and preserved the parent's launch note above.

`PlacementKey` now holds the existing Core `Entity` and the render part. `MotionJoin.Identify` takes the entity from `RenderInstance.Entity`. Equality still includes the slot, generation and part. Order still includes only the slot and part. No Core code, rendering policy, shader or unmerged branch was changed. `FrameHistoryTests` uses the consolidated identity, and `ARCHITECTURE.md` records the distinction between equality and order.

Five added behavior tests supply seven cases. They cover separate histories for render parts, a newly drawn part, generation-independent ordering, duplicate slot-and-part rejection, and continued motion after slot reuse. The cases include zero and maximum generations, the maximum slot and the maximum render part. These characterization tests passed before the production refactor, as required for unchanged behavior. No behavioral correction or behavioral red is claimed. A constructor compile failure was not used as evidence.

### Tested inputs

The final code-tree hash from `bash tools/code-tree.sh` is `186f2638ec8ceb2070cc645c8658db1f6e4a589e`. This hash identifies the source, tests and build inputs, rather than Markdown notes. Cleanup preceded both full builds and both full test runs. Only this ticket note changed after validation. The commit containing this note therefore carries those tested code inputs.

All logs are in `/private/tmp/ter-6gf8-build-20261105/`. Every build and test command ran in the Nix dev shell, in the foreground, one heavy command at a time. Capacity checks found about 13 GB of free disk and 52–53% free memory. No other work's artifacts were deleted.

| Check | Exact command | Result | Log |
|---|---|---|---|
| Motion behavior before refactor | `nix develop --command dotnet test --project tests/Tertius.Rendering.Tests -c Checked --filter-class 'Tertius.Rendering.Tests.BatcherMotionTests'` | 30 passed, 0 failed, 0 skipped | `focused-before.log` |
| Motion and history after refactor | `nix develop --command dotnet test --project tests/Tertius.Rendering.Tests -c Checked --filter-class 'Tertius.Rendering.Tests.BatcherMotionTests' --filter-class 'Tertius.Rendering.Tests.FrameHistoryTests'` | 52 passed, 0 failed, 0 skipped | `focused-after.log` |
| Cleanup | `nix develop --command bash tools/cleanup.sh src/Tertius.Rendering/Frame/FrameHistory.cs src/Tertius.Rendering/Scene/MotionJoin.cs tests/Tertius.Rendering.Tests/BatcherMotionTests.cs tests/Tertius.Rendering.Tests/FrameHistoryTests.cs` | Passed on all four touched C# files | `cleanup.log` |
| Checked solution build | `nix develop --command dotnet build Tertius.slnx -c Checked -v minimal` | Passed, including Tertius.Benchmarks, with 0 warnings and 0 errors | `checked-build.log` |
| Checked full suite | `nix develop --command bash tools/gpu-lock.sh /private/tmp/ter-6gf8-build-20261105/checked-tests.log dotnet test --solution Tertius.slnx -c Checked --no-build` | 10,289 passed, 0 failed, 19 skipped, 10,308 total | `checked-tests.log`, `checked-lock.log` |
| Release solution build | `nix develop --command dotnet build Tertius.slnx -c Release -v minimal` | Passed, including Tertius.Benchmarks, with 0 warnings and 0 errors | `release-build.log` |
| Release full suite | `nix develop --command bash tools/gpu-lock.sh /private/tmp/ter-6gf8-build-20261105/release-tests.log dotnet test --solution Tertius.slnx -c Release --no-build` | 10,276 passed, 0 failed, 19 skipped, 10,295 total | `release-tests.log`, `release-lock.log` |

Each full suite ran all eight test modules. The 19 skips in each configuration are the project's explicit measurements and driver-dependent timing tests: 8 Physics, 5 Host and 6 Rendering. Neither run had a snapshot failure. The GPU helper acquired and released the project lock for each full suite.

No coverage or mutation testing ran in this stage. The parent owns coverage, mutation testing, integration and closure. Read-only review remains a later stage. This build stage introduces no unresolved design fork and leaves the ticket in progress. Nothing was merged, pushed or published as a PR.

## Single fix stage, November 5, 2026

The read-only review passed correctness, design and fulfillment at `9f04addffd964c020cc98d7d3acd492838f1fe71`. Its findings list was empty. This stage strengthens the evidence for its assessment gaps. No production code, test or architecture change was needed. The launch note above stays unchanged, and the ticket remains in progress.

### Review gaps and resolutions

| Review gap | Resolution and limit |
|---|---|
| No fresh execution during read-only review | This fix stage executed the motion and history tests in Checked and Release. Each run passed 52 cases, with no failures or skips. |
| No source stamp for the original pre-refactor run | A fresh baseline run passed 52 cases against the exact original production source. The earlier unstamped log remains historical evidence only. |
| Incomplete capacity and GPU-lock evidence | New logs retain load, free memory and disk before every heavy command. Every fresh test log includes the owned lock's process, start time, token and command label. |
| Coverage and mutation adequacy | These checks remain parent-owned under the brief. This stage ran neither and claims no result for them. |
| Linux execution | No Linux execution is claimed. The host is macOS arm64. The unchanged refactor adds no Vulkan call, device requirement or shader change. |
| Independent performance and per-type metrics | No performance or metric improvement is claimed. The accepted design evidence concerns one identity representation and reduced caller knowledge, not measured speed or ratios. |
| Integration and closure | These steps remain parent-owned. Stage completion establishes implementation and review readiness, not integration or ticket closure. Nothing was merged, pushed or published. |

### Baseline identity and fresh execution

The baseline run used this same bound checkout without resetting HEAD or changing the branch. The two production files and their direct history-test caller temporarily used their original representation. Each file's blob hash matched `64ab730a0e0300942212d9b2d96e74f0bfcd3ec5`. A comparison of every file under `src` against that base was empty. Only `BatcherMotionTests.cs` differed under `src` and `tests`, with the reviewed regression cases intact.

The baseline code-tree hash was `5ca48273cc88ae478adb8b21efb5bb09156ec6c5`. The regression test file's blob hash was `53f96a4ba8888818e01b70c690c0ac02f3d6a653`. `baseline-inputs.log` records the original and current blob hashes, the empty production comparison and the capacity snapshot. `baseline-tests.log` records the code-tree hash before and after execution and the test assembly's matching build stamp. This establishes a fresh behavioral baseline, not a behavioral red or a constructor failure.

The reviewed representation was then restored exactly. Cleanup ran on all four ticket-touched C# files before the final tests and made no changes. The final code-tree hash remains `186f2638ec8ceb2070cc645c8658db1f6e4a589e`.

New evidence is in `/private/tmp/ter-6gf8-fix-20261105/`. Every command below ran in the foreground, one heavy command at a time. The test commands ran through `nix develop --command bash tools/gpu-lock.sh <log> bash -c '<audit and test>'`. Each test log retains the full audit-and-test shell body in its lock holder's command label.

| Check | Command inside the Nix GPU-lock wrapper | Result | Log |
|---|---|---|---|
| Original representation with reviewed regression tests | `dotnet test --project tests/Tertius.Rendering.Tests -c Checked --filter-class Tertius.Rendering.Tests.BatcherMotionTests --filter-class Tertius.Rendering.Tests.FrameHistoryTests` | 52 passed, 0 failed, 0 skipped | `baseline-tests.log` |
| Final Checked motion and history | `dotnet test --project tests/Tertius.Rendering.Tests -c Checked --filter-class Tertius.Rendering.Tests.BatcherMotionTests --filter-class Tertius.Rendering.Tests.FrameHistoryTests` | 52 passed, 0 failed, 0 skipped | `checked-tests.log` |
| Final Release motion and history | `dotnet test --project tests/Tertius.Rendering.Tests -c Release --filter-class Tertius.Rendering.Tests.BatcherMotionTests --filter-class Tertius.Rendering.Tests.FrameHistoryTests` | 52 passed, 0 failed, 0 skipped | `release-tests.log` |

The cleanup command was `nix develop --command bash tools/cleanup.sh src/Tertius.Rendering/Frame/FrameHistory.cs src/Tertius.Rendering/Scene/MotionJoin.cs tests/Tertius.Rendering.Tests/BatcherMotionTests.cs tests/Tertius.Rendering.Tests/FrameHistoryTests.cs`. Its output is `cleanup.log`.

The capacity snapshots show 13 GB of free disk and 54% free memory. The one-minute load before baseline testing, cleanup, final Checked testing and final Release testing was 1.48, 3.88, 4.97 and 10.14, respectively. The snapshots are in `baseline-inputs.log` and `final-inputs.log`. The three test runs each waited zero seconds for the project GPU lock. All three helpers finished successfully without a lock-ownership warning. No other work's artifacts were deleted.

### Reused full-suite evidence

The full Checked and Release solution builds and suites above remain valid for the identical final code inputs. Both solution builds include `Tertius.Benchmarks` and report zero warnings and errors. The full suites report Checked 10,289 passed and Release 10,276 passed, with zero failures and 19 skips each. All 16 full-suite records still match the final code-tree hash. `input-evidence.log` retains those record values and SHA-256 digests of the earlier validation logs.

The prior full-suite logs themselves contain GPU-lock start time, wait duration and end time. The separate empty `*-lock.log` files were redirected launcher output, not the helper's evidence log. `tools/gpu-lock.sh` writes its evidence to its first log argument. The fresh test logs add explicit holder records. The historical capacity check cannot be reconstructed, so this stage supplies complete current capacity snapshots instead.

The new test logs contain matching before-test, after-test and build-stamp hashes for their respective inputs. No full suite was repeated for this ticket-only evidence change. All commands finished before stage completion. There is no unresolved in-scope finding or design fork. Parent-owned coverage, mutations, integration and closure remain outside this stage.

## Parent hardening commission, November 5, 2026, 10:30 EST

The implementation/review/fix workflow is terminal with cleanup complete, final clean HEAD b752684eec209afb7d7aaf9a89763d1b8aeb9b16. The build-stage notice at 9f04addf is historical, not a reason to restart review. The parent verified identical source/test/build inputs between those commits and inspected the final evidence audit. The original immutable scope remains 64ab730a0e0300942212d9b2d96e74f0bfcd3ec5 through b752684eec209afb7d7aaf9a89763d1b8aeb9b16; current main's unrelated tracer changes are not this ticket's hardening scope.

The narrow ter-69s8 checkpoint finished cleanly at 4fe431f72c762ec9dd5b7b6cb5bf9759ed7daa47 and explicitly released its heavy allocation. Parent-owned coverage, listed mutations, decision tests and final validation now receive that fourth slot. The FSR, portable-walk and corrected-environment-read workflows hold the other three. Check current capacity before commands: latest observation was load 8.59, 53% free memory and about 12 GB free disk. No production redesign, main import, screenshot campaign, integration or ticket closure is authorized to this worker.

Hardening request ter-6gf8-hardening-20261105-b752684e-v1 uses saved version 077df081-6e60-4160-b9a4-b89ea970421a, source/schema SHA-256 7e9e66bef16b0ba38ba74d49785c05a3c823a720f20bfd1ab8d7af7ae0c9e669. The exact brief is /private/tmp/tertius-integration-20261105/ter-6gf8-hardening-brief.md; it explicitly overrides the reusable template's obsolete main/capacity text, fixes the comparison to the original scope, and permits exact-input evidence reuse. New evidence belongs under /private/tmp/tertius-hardening-20261105/ter-6gf8/. The confirmed workflow run is 1cdc9479-765f-4891-a14b-2072ea2f92eb, using Codex gpt-6.1-sol with reasoningEffort xhigh. Keep this ticket in progress until the parent integrates and closes it.

## Parent-commissioned hardening, November 5, 2026

The worker inspected the existing path, branch, HEAD and working changes before writing. The starting checkout was clean at `b752684eec209afb7d7aaf9a89763d1b8aeb9b16` on `ter-6gf8-placement-entity`. No branch, checkout, production file or architecture decision changed during hardening. The worker started no agent or workflow.

The original scope is exactly `64ab730a0e0300942212d9b2d96e74f0bfcd3ec5..b752684eec209afb7d7aaf9a89763d1b8aeb9b16`, not a comparison with moving main. Its only production changes are `src/Tertius.Rendering/Frame/FrameHistory.cs` and `src/Tertius.Rendering/Scene/MotionJoin.cs`. The new hardening tests are in commit `48edae8710d6abab3cdffea2b7c287767e3da161`. Its canonical source/test/build-input tree is `d9be0d29914d1608c80a65dc449925cb1e686d42`. Markdown changes do not enter that tree.

Evidence is under `/private/tmp/tertius-hardening-20261105/ter-6gf8/`. The earlier build and fix folders remain intact. The parent ticket snapshot and its base remain untouched at their original paths. Copies and SHA-256 digests are in the new evidence folder. This ticket retains the whole prior branch note and appends the parent's unique hardening commission. Neither side's notes were dropped.

### Official scope classification

The exact classifier command was `nix develop --command python3 -I tools/stryker/changed.py 64ab730a0e0300942212d9b2d96e74f0bfcd3ec5 /private/tmp/tertius-hardening-20261105/ter-6gf8/classifier --head b752684eec209afb7d7aaf9a89763d1b8aeb9b16`.

`classifier.log` lists FrameHistory lines 8, 11 and 17, MotionJoin line 28, and the removed constructor lines at MotionJoin line 28. It reports no changed C# lines in Core, Simulation or Physics. Therefore no official Stryker host ran. There is no unrelated CPU-module campaign, Stryker score or Stryker timeout claim. The deliberate list covers the changed Rendering behavior instead.

### Coverage by affected file

Changed-line coverage counts instrumented C# lines in the original scope. Branch coverage counts executed outcomes of instrumented C# decisions. Both changed executable lines are covered in Checked and Release. Neither changed line has an instrumented branch, so `0/0` is not applicable, not a branch-coverage percentage.

| Affected file | Changed lines, Checked and Release | Changed branches | Whole file, final Checked | Whole file, final Release |
|---|---|---|---|---|
| `src/Tertius.Rendering/Frame/FrameHistory.cs` | 1/1, line 17 | 0/0 | Lines 99/100, branches 50/56 | Lines 91/92, branches 44/46 |
| `src/Tertius.Rendering/Scene/MotionJoin.cs` | 1/1, line 28 | 0/0 | Lines 59/59, branches 20/20 | Lines 59/59, branches 20/20 |

Before hardening, the focused 52-case coverage runs passed in both configurations. FrameHistory measured Checked 90/100 lines and 35/56 branches, Release 81/92 lines and 28/46 branches. MotionJoin measured 59/59 lines and 19/20 branches in each. Final coverage ran every Rendering test, not only those two classes. The wider test selection accounts for the additional headless and deformed-copy coverage. It is not a measured coverage gain attributable only to the three new cases.

The final unfiltered coverage runs passed Checked 2,636 cases with 6 existing skips and Release 2,635 cases with 6 existing skips. Both had zero failures. `coverage-before/` preserves the initial reports. `coverage/`, `coverage-detail-before.json`, `coverage-detail.json`, `coverage-summary.log` and the two changed-coverage logs preserve the final reports and their comparison.

Every remaining uncovered outcome was inspected:

- FrameHistory line 270 and the other outcome at line 269 detect a placement list whose keys do not rise. MotionJoin rejects such keys before insertion. Checked also guards direct internal insertion. This unchanged invariant-failure path does not justify a new identity-behavior test or private-state corruption.
- FrameHistory line 261 leaves the empty-record shortcut of the diagnostic invariant check uncovered. This unchanged diagnostic route does not alter placement identity. No test of that check's branch structure was added.
- Checked-only partial branches at lines 180, 236, 363 and 371 are unchanged preconditions for draw bounds, the armed lifecycle and ordered writes. Their remaining outcomes violate the internal protocol. Release omits these contracts. The suite covers the ordinary no-camera error at line 237 in both configurations.

FrameHistory line 8 changes documentation only. The record declaration at line 11 is not exposed as an executable line by this coverage provider. Generated equality coverage is not claimed. The compiling K01–K03 mutations below exercise equality through observable motion, rather than through compiler-generated implementation details. Synthesized formatting, deconstruction and hash values have no consumer in this motion merge. No framework-only tests or hash-value assertions were added. CPU coverage does not measure shader execution.

### Deliberate mutations and behavioral tests

A mutant is a deliberate source fault. One list, `mutants.json`, holds all twelve cases. Both runs used `nix develop --command bash tools/gpu-lock.sh <log> python3 -I tools/mutate.py /private/tmp/tertius-hardening-20261105/ter-6gf8/mutants.json <out>` with separate before/after logs and output folders. The project runner alone applied, built, tested and restored every mutant. No mutant was edited, built, tested or restored by hand.

The initial baseline passed 52 cases. Eight mutants were killed. K02, O03 and O05 survived, as did the equivalent R01. Before adding a missing test, the runner first reproduced its represented fault. Two test methods add three cases. The runner then passed the 55-case baseline and demonstrated each new case failing on its represented fault:

- K02 makes a newly drawn lower-numbered part borrow the previous rows of a different, higher-numbered part. `ANewLowerRenderPartDoesNotBorrowAHigherPartsPreviousRows` fails with Moved instead of New.
- O03 loses the upper slot bits before forming the order. `TheLastRenderPartOfASlotDoesNotHideTheNextSlotsMotion(65535, 65536)` fails with Still instead of Moved.
- O05 overlaps the highest part bit with the next slot. `TheLastRenderPartOfASlotDoesNotHideTheNextSlotsMotion(0, 1)` fails with Still instead of Moved.

These are literal, deterministic frame histories with no random source or wall-clock dependency. They test motion kind, previous model rows and valid history, not a packing constant or private implementation text. No actual production defect was found or changed.

The following is the one final outcome table. Each row compiled, ran the intended classes and has one result. The killed-by names identify real assertion failures. `mutations-before/` and `mutations-after/` preserve every build log, test log, replacement and exact result.

| ID | Place | Deliberate change | Final result | Killed by or triage | Seconds |
|---|---|---|---|---|---:|
| K01 | `src/Tertius.Rendering/Frame/FrameHistory.cs:11` | Equality omits generation | Killed | `AnInstanceInAReusedSlotWithAnotherGenerationIsNew` and both continued-history generation cases | 5.8 |
| K02 | `src/Tertius.Rendering/Frame/FrameHistory.cs:11` | Equality omits part | Killed | `ANewLowerRenderPartDoesNotBorrowAHigherPartsPreviousRows` | 3.6 |
| K03 | `src/Tertius.Rendering/Frame/FrameHistory.cs:11` | Equality omits slot | Killed | `AnInstanceAfterAKeyThatTheLastFrameDidNotDrawStillFindsItsOwnKey` | 3.6 |
| O01 | `src/Tertius.Rendering/Frame/FrameHistory.cs:17` | Order omits slot | Killed | History invariant behavior and both adjacent-slot cases | 5.7 |
| O02 | `src/Tertius.Rendering/Frame/FrameHistory.cs:17` | Order omits part | Killed | `RenderPartsOfTheSameEntityKeepTheirOwnPreviousRows` | 3.6 |
| O03 | `src/Tertius.Rendering/Frame/FrameHistory.cs:17` | Slot shifts as 32-bit before widening | Killed | Adjacent-slot case `(65535, 65536)` | 3.5 |
| O04 | `src/Tertius.Rendering/Frame/FrameHistory.cs:17` | Order uses generation instead of slot | Killed | History invariant behavior and both adjacent-slot cases | 3.5 |
| O05 | `src/Tertius.Rendering/Frame/FrameHistory.cs:17` | Slot shifts by 15, overlapping part bits | Killed | Adjacent-slot case `(0, 1)` | 3.6 |
| I01 | `src/Tertius.Rendering/Scene/MotionJoin.cs:28` | Construction discards generation | Killed | Reused-slot and continued-history generation cases | 3.7 |
| I02 | `src/Tertius.Rendering/Scene/MotionJoin.cs:28` | Construction discards slot | Killed | Both adjacent-slot cases and missing-key behavior | 3.4 |
| I03 | `src/Tertius.Rendering/Scene/MotionJoin.cs:28` | Construction discards part | Killed | Independent parts, a new higher part and a new lower part | 3.8 |
| R01 | `src/Tertius.Rendering/Scene/MotionJoin.cs:28` | Restore removed slot/generation reads through explicit Entity construction | Survived, equivalent | `RenderInstance.Entity` constructs exactly the same Entity from EntityIndex and EntityGeneration, so the restored reads cannot change motion or history. | 3.5 |

Final totals are 12 rows: killed 11, survived 1 equivalent, timeout 0, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0. The raw kill share is 11/12. Excluding the justified equivalent, all 11/11 deliberate behavioral faults are killed. The three initial non-equivalent survivors now have behavior tests. There are no untriaged survivors or invalid mutants. R01 accounts for the relevant removed constructor reads without treating a constructor compile error as a kill.

### Final validation and input identity

Scoped cleanup ran on the two production files and both ticket-touched test files before the final coverage and suites. It left the canonical input tree unchanged at `d9be0d29914d1608c80a65dc449925cb1e686d42`. The exact commit tested by both complete solution suites is `48edae8710d6abab3cdffea2b7c287767e3da161`. The suite log records that commit and tree before and after execution. All sixteen full-suite test records match that tree. `final-test-summary.json` retains every module's exact counts and checks each module's zero exit code.

Both full suites were fresh because the new tests changed the original input tree `186f2638ec8ceb2070cc645c8658db1f6e4a589e`. The earlier full-suite and build logs remain historical evidence, not the final new-test validation. This evidence-only ticket commit changes no source, test or build input. The fresh full-suite results therefore also apply to its identical canonical inputs. `final-head-audit.log` records the delivered commit's exact SHA, clean state, input identity and post-commit focused execution. This avoids claiming that a note can contain its own commit hash.

The final suite command inside the Nix GPU-lock wrapper was `bash tools/test-all.sh -c Checked,Release -j 1 -o /private/tmp/tertius-hardening-20261105/ter-6gf8/final-suites`. It built both complete solutions, then ran one module at a time. `suite-locked-inputs.log` retains the wrapper's owned lock record and capacity snapshot. The wrapper waited 166 seconds, finished normally, and released its own lock.

| Final check | Result | Evidence under the hardening folder |
|---|---|---|
| Complete Checked solution suite, all 8 modules | 10,292 passed, 0 failed, 19 skipped, 10,311 total | `final-suites/Checked/`, `final-suites-lock.log` |
| Complete Release solution suite, all 8 modules | 10,279 passed, 0 failed, 19 skipped, 10,298 total | `final-suites/Release/`, `final-suites-lock.log` |
| Checked solution build, including Tertius.Benchmarks | Passed, 0 warnings, 0 errors | `final-suites/Checked/build.log` |
| Release solution build, including Tertius.Benchmarks | Passed, 0 warnings, 0 errors | `final-suites/Release/build.log` |
| Scoped Roslyn and CSharpier cleanup before freezing inputs | Passed on all 4 ticket-touched C# files, no input change | `cleanup.log`, `frozen-inputs.log` |
| `nix develop --command csharpier check` on the same 4 files | Passed, 4 files checked, no edits | `format-check.log`, `format-correct-launch.log` |
| Whitespace in pending changes and the original-base committed range | Passed | `whitespace-and-source.log` |
| Production and architecture compared with original scope head | No difference | `whitespace-and-source.log`, `note-preservation.log` |

The 19 skips in each suite are the existing opt-in measurements and driver-dependent timing cases: 8 Physics, 6 Rendering and 5 Host. There were no snapshot failures or reference-image approvals. No extra benchmark build was needed because both complete solution builds compiled it.

An initial `dotnet csharpier` check could not locate a .NET tool. The dev shell provides the standalone `csharpier` command. That invocation never ran the formatter. Its retained log is `format-check-wrong-launcher.log`; the correct standalone check passed. No formatting or test failure remains.

### Limits and parent handoff

Every build, test, coverage and mutation command used Nix. Heavy commands ran one at a time in the foreground. Test and mutation commands used the project GPU lock. Capacity logs include load, memory and free disk before the runs. The first measurement showed 53% free memory and 12 GB free disk. The final-check measurement showed 52% free memory and 13 GB free disk, with one-minute load 16.87. These are capacity observations, not performance measurements. The new evidence occupies about 17 MB. No other ticket's work or artifacts were deleted.

This is macOS arm64 validation, not Linux runtime validation. CPU coverage measures no shader lines, and the original scope changes no shader. There is no performance, long-soak or MTBF claim. The new behavior tests use literal deterministic inputs, and no suspect flakiness was observed. No visible feature changed, so the authoritative hardening brief requires no new screenshot campaign.

The ticket remains in progress. Hardening readiness is not integration or closure. The parent must integrate with current tracer main and run the affected combined validation. There is no additional routine implementation/review/fix stage. The worker merges, pushes, closes, publishes and removes nothing. The worker releases its fourth heavy allocation only after the final foreground commands end; the final audit and completion message record that release.
