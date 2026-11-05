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
