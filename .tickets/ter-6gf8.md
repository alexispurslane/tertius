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
