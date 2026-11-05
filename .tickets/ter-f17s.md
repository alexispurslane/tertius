---
id: ter-f17s
status: closed
deps: []
links: []
created: 2026-11-04T21:59:59Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# The renderer follows an entity from frame to frame by its whole handle: the shadow tiles' holders and layers, and the impostor key

Found in the review of ter-nt65 on 2026-11-04. ShadowTiles follows a light from frame to frame by its entity slot alone: Keep and Held give a light the layers that its slot held, RanksAsHolder gives it the holder margin (it ranks at twice its rank irradiance), and IsStale matches a tile record by the slot. So a shadow-casting lamp that takes the slot of a destroyed holder before the next submitted frame inherits the old lamp's layers and margin, a share that it did not earn. The cached tile content stays correct, because a record also holds the light's place, aim, projection and origin frame. ImpostorKey (EntityIndex, Part, Mesh, Material) also keys state that lasts from frame to frame by the slot alone. STYLE.md: no member between modules takes or returns a bare slot. RenderInstance and the render lights already carry EntityGeneration beside EntityIndex, and Core has the handle Entity(uint Index, uint Generation). The user's decision of 2026-11-04 (design A): keep the light's generation on the snapshot and make the renderer's frame-to-frame state key by the whole entity, so that the class of bug is impossible; ter-nt65 (the removal of the light's generation) closes without a merge.


## Integration, 2026-11-05

Merged exact delivered commit `332b3d6c691eb3b2b5112b4d8ccb1fd55adacc15` into main as `6ce9b55c92bfbf981a9869c6a2670041ef7d326f`. Only ticket history conflicted; every committed main paragraph and both parent coordination notes were preserved. All 19 incoming C# files retained their validated contents, and existing automatic cleanup ran before the final checks without changing them. The combined source, tests and build inputs have code-tree fingerprint `91328f295de6603589fc0eacbd897fba0160936d`.

Both complete solution builds passed with zero warnings/errors, including benchmarks. The combined Checked suite passed 10,282 tests; Release passed 10,269. Each had zero failures and 19 existing skips. Logs, exact commands and code/tree identities are in `/private/tmp/tertius-integration-20261105/merge-f17s/`. Normal merge hooks reused the passing Checked records for the exact staged code, rather than repeating builds or tests. Component coverage and fault dispositions remain the valid evidence recorded below; no official mutation run was repeated for this integration. The closure commit changes ticket prose only and needs no repeated suite.

The advisory metrics still identify existing long GPU scenario tests and `ShadowTiles.IsStale`. This identity fix changes comparisons, not that method's control flow. No unrelated split was added merely to lower a metric after its design review and validation. Follow-up ter-6gf8 remains separate, with its original content and documented EOF whitespace warning preserved at this merge. Tracer/History Rule A, the light budget and denoising were not made integration prerequisites.

## Notes

**2026-11-04T22:01:02Z**

2026-11-04: started as a workflow, run d8dc1d3b-6a17-4f9c-85a0-92ec9720c346 (version 869b0b07), branch ter-f17s-entity-handle from 808e89668, Opus builder at high and Opus reviewer at xhigh. Settled by the orchestrator: PreparedLight and PreparedInstance carry one Core Entity value in place of the bare slot, so every renderer store that lasts from frame to frame (ShadowTiles' records, holders and layers; ImpostorKey) keys by slot and generation and no comparison can use the slot alone; other slot-only state outside the prepared types is reported, not fixed.

**2026-11-04T22:26:52Z**

2026-11-04: built. Commit 123a61b3b changes the types alone: PreparedLight.Entity and PreparedInstance.Entity, one Entity value each, with every comparison still by slot. On that commit the five new tests fail for the defect (a lamp in a destroyed holder's slot ranks as a holder, keeps its layer, and keeps the cached tile; a caster in a reused slot keeps the tile; a ball in a reused slot inherits the impostor hysteresis). The next commit compares whole handles in ShadowTiles (Keep, Held, RanksAsHolder, IsStale, the caster records, the invariants) and keys ImpostorKey by Entity; the tests pass. Two guard tests, a lamp of generation 7 that keeps its layers and its margin, pass on both commits. No GPU layout copies the slot. Full Checked and Release suites pass.

**2026-11-04T22:37:55Z**

2026-11-04: review fixes. RenderInstance, PointRenderLight and SpotRenderLight gain a computed Entity member (slot with generation, no new field, RenderLight stays 88 bytes), and the six batcher sites read it. New tests pass a live generation through the batcher: a kept point and spot light carry their whole entity, a placed instance carries its whole entity, and the light-holder mark test now uses generation 3 on both sides. Core tests pin the three Entity members. These tests are green on arrival, because the batcher already passed the generation; they lock it in. Mutants for the orchestrator's list: drop the generation at SceneBatcher's light preparation (point, spot), at PreparedInstance.Entity, and on one side of the light-holder mark. ARCHITECTURE 'The render snapshot' and 'Lights' now say that the batcher marks and names by the whole Entity, and limit the claim to stores that read the prepared types. ter-6gf8 follows up PlacementKey.

**2026-11-04T23:36:53Z**

2026-11-04 Sol handoff: workflow d8dc1d3b-6a17-4f9c-85a0-92ec9720c346 already completed build, review and fix successfully. Preserve the clean final branch ter-f17s-entity-handle at aff67ddcf2d04b0744c0744647cf6a3dfdd4c281 and its reported Checked/Release evidence. Do not restart or redo those stages. Orchestrator hardening and integration remain separate. The branch also retains follow-up ticket ter-6gf8.

**2026-11-05T12:13:08Z**

2026-11-05: parent-owned hardening continuation started as workflow 7a3aa2cf-f86c-43ed-8fd1-d766e4ec63e6, version 077df081-6e60-4160-b9a4-b89ea970421a, preserving aff67ddcf. Exact original base verified as 808e8966873f659cdf81ad4ca85b1e9b87c5543a; an erroneous expanded base in launch arguments was corrected immediately by steering the existing worker, without restart or history changes. Coverage, official Core Stryker, listed Rendering mutants, and final suites remain the scope. Parent retains merge/closure ownership.


### 2026-11-05: parent-commissioned hardening complete

The preserved checkout was clean on `ter-f17s-entity-handle` at `aff67ddcf2d04b0744c0744647cf6a3dfdd4c281` before writing. The verified original scope base is `808e8966873f659cdf81ad4ca85b1e9b87c5543a`. Coverage and official mutation comparisons are pinned to initial-main `f703caf174af14d5350e410ce2b26acfcc42e5fe`, whose merge base is that original scope base. Later main advances did not reduce this scope.

This worker did not repeat implementation, review or fix stages. It used no agents or workflows. All builds, tests, cleanup and mutation runs used the Nix dev shell, with one heavy command at a time. Every process finished in the foreground. The official run's Checked outputs were left alone until its wrapper finished. No production behavior changed, and `src/` remains byte-identical to the starting HEAD. The follow-up `ter-6gf8` remains unchanged. Parent root notes, including the original workflow launch, handoff and admitted hardening workflow, were merged with the branch's implementation and review notes. The root snapshots remain at `/private/tmp/tertius-integration-20261105/root-notes/ter-f17s.md` and `ter-f17s.base.md`; this worker did not edit the parent checkout.

### Coverage

Before hardening, complete Core and Rendering projects ran with managed coverage in Checked and Release. Core passed 1,207 / 1,175 tests; Rendering passed 2,623 / 2,622 with six existing skips in each configuration. After the final cleanup, focused coverage ran `RenderEntityTests` and the four affected Rendering classes: `BatcherLightTests`, `BatcherTests`, `ShadowTilesTests` and `ImpostorManagerTests`. Those runs passed 3 Core tests and 294 Rendering tests in each configuration, with no failures or skips.

Both measurements cover 49/49 changed executable lines and 16/16 changed branches. The table gives each affected file, not a pooled substitute. Whole-file columns describe the initial complete Core/Rendering coverage runs. A changed value of 0/0 means no executable changed line or branch, not missing execution of a field declaration.

| File | Changed lines before | Changed lines after | Changed branches before | Changed branches after | Whole-file lines before | Whole-file branches before |
|---|---:|---:|---:|---:|---:|---:|
| `src/Tertius.Core/Seams/RenderLight.cs` | 2/2 | 2/2 | 0/0 | 0/0 | 2/2 | 0/0 |
| `src/Tertius.Core/Seams/RenderSeam.cs` | 1/1 | 1/1 | 0/0 | 0/0 | 109/111 | 17/22 |
| `src/Tertius.Rendering/Scene/ImpostorManager.cs` | 1/1 | 1/1 | 0/0 | 0/0 | 384/396 | 154/162 |
| `src/Tertius.Rendering/Scene/PreparedLight.cs` | 0/0 | 0/0 | 0/0 | 0/0 | 7/7 | 0/0 |
| `src/Tertius.Rendering/Scene/PreparedScene.cs` | 0/0 | 0/0 | 0/0 | 0/0 | 224/226 | 64/66 |
| `src/Tertius.Rendering/Scene/SceneBatcher.cs` | 33/33 | 33/33 | 0/0 | 0/0 | 830/837 | 279/282 |
| `src/Tertius.Rendering/Scene/ShadowTiles.cs` | 12/12 | 12/12 | 16/16 | 16/16 | 339/342 | 164/170 |

No changed line is uncovered and no changed branch is partial. Uncovered whole-file behavior was inspected in `coverage-before/file-summary.json`:

- `RenderSeam.cs`: unchanged diagnostic `ToString` cases.
- `ImpostorManager.cs`: unchanged capacity growth, free-state reuse, geometry fallbacks, an unused property and invalid/unreachable input cases.
- `PreparedScene.cs`: unchanged invalid-index guards.
- `SceneBatcher.cs`: unchanged command growth and unreachable union/ambient cases.
- `ShadowTiles.cs`: unchanged malformed internal pose ranges and the invalid cone aim for a point light.

These untouched paths did not expand this identity ticket into another campaign. CPU coverage measures managed C# execution only, never shader execution. Branch counts select the best report per source line, as `tools/coverage/changed-lines.py` does; they do not union individual branch identities.

### Mutation outcomes

The official command was `nix develop --command python3 -I tools/stryker/changed.py f703caf174af14d5350e410ce2b26acfcc42e5fe /private/tmp/tertius-hardening-20261105/ter-f17s/stryker`. It ran through the locking wrapper, concurrency 1, nice 20 and four xunit threads. The result is official and valid: no validity problems, no Dubious tests and no mutation timeouts. Its initial CPU run passed 4,619 tests. Total wrapper time was 2,568 seconds, including 2,436 seconds of per-test coverage capture; recorded 1-minute load mean was 11.0 and maximum was 34.7.

Stryker generated no automatic mutants on the three changed `Entity` getter expressions, so it supplies no getter mutation score. Its unmutated lines are `RenderLight.cs:38,78` and `RenderSeam.cs:114`. Listed faults C01-C06 cover losing the index and losing the generation at each getter. The official removed row is XML documentation, with no executable behavior to restore as a mutant.

The existing deliberate list contains 27 faults, run by `nix develop --command python3 -I tools/mutate.py /private/tmp/tertius-hardening-20261105/ter-f17s/mutants.json /private/tmp/tertius-hardening-20261105/ter-f17s/listed-before`. It first killed 25 and left R06 and R08 alive, with zero timeouts, build failures, no-coverage results or zero-test runs. R06 showed that dropping only the spot light holder's generation escaped 97 existing batcher tests. `OnlyTheGpuInstancesOfASpotLightsEntityCarryItsLightHolderMark` now uses live generations 1, 7 and 65,537, with an unrelated entity alongside the holder. The same runner with `--only R06` produced three assertion failures on the fault: the GPU holder mark was 0 instead of 1. Its baseline passed all 100 batcher tests, and the runner restored the source before cleanup and final validation. This is a test gap, not a production defect.

R08 is equivalent for valid snapshots: a snapshot has only one generation per entity slot, so normalizing generations on both the collected holder keys and the instance lookup preserves every holder mark. This lookup retains no state between snapshots. No structural assertion or invalid multi-generation snapshot was added to force a kill.

The final table uses the R06 recheck as its one final outcome, while keeping its initial survivor evidence outside the checkout. Raw listed score: 26/27. Excluding the justified R08 equivalent, the score is 26/26 non-equivalent faults killed. No new fault campaign was added after scope freeze.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| Core-Seams/RenderLight.cs-R9 | src/Tertius.Core/Seams/RenderLight.cs:9 | the branch removed 1 lines here | removed |  | - |
| C01 | src/Tertius.Core/Seams/RenderLight.cs:38 | point seam loses generation | killed | RenderEntityTests.APointLightNamesItsEntityBySlotAndGeneration | 11.4 |
| C02 | src/Tertius.Core/Seams/RenderLight.cs:38 | point seam loses slot | killed | RenderEntityTests.APointLightNamesItsEntityBySlotAndGeneration | 6.9 |
| C03 | src/Tertius.Core/Seams/RenderLight.cs:78 | spot seam loses generation | killed | RenderEntityTests.ASpotLightNamesItsEntityBySlotAndGeneration | 4.8 |
| C04 | src/Tertius.Core/Seams/RenderLight.cs:78 | spot seam loses slot | killed | RenderEntityTests.ASpotLightNamesItsEntityBySlotAndGeneration | 5.2 |
| C05 | src/Tertius.Core/Seams/RenderSeam.cs:114 | instance seam loses generation | killed | RenderEntityTests.AnInstanceNamesItsEntityBySlotAndGeneration | 4.7 |
| C06 | src/Tertius.Core/Seams/RenderSeam.cs:114 | instance seam loses slot | killed | RenderEntityTests.AnInstanceNamesItsEntityBySlotAndGeneration | 6.5 |
| R01 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1083 | point preparation loses generation | killed | BatcherLightTests.AKeptLightCarriesTheSlotAndTheGenerationOfItsEntity | 5.0 |
| R02 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1093 | spot preparation loses generation | killed | BatcherLightTests.AKeptLightCarriesTheSlotAndTheGenerationOfItsEntity | 5.3 |
| R03 | src/Tertius.Rendering/Scene/SceneBatcher.cs:1111 | prepared light loses generation after case selection | killed | BatcherLightTests.AKeptLightCarriesTheSlotAndTheGenerationOfItsEntity | 5.5 |
| R04 | src/Tertius.Rendering/Scene/SceneBatcher.cs:556 | prepared instance loses generation | killed | BatcherTests.APlacedInstanceCarriesTheSlotAndTheGenerationOfItsEntity | 5.2 |
| R05 | src/Tertius.Rendering/Scene/SceneBatcher.cs:575 | point holder collection loses generation | killed | BatcherTests.TheGpuInstanceOfAnEntityThatHoldsALightSaysSoAndAnyOtherDoesNot | 4.1 |
| R06 | src/Tertius.Rendering/Scene/SceneBatcher.cs:576 | spot holder collection loses generation | killed | BatcherTests.OnlyTheGpuInstancesOfASpotLightsEntityCarryItsLightHolderMark(generation: 65537), BatcherTests.OnlyTheGpuInstancesOfASpotLightsEntityCarryItsLightHolderMark(generation: 1), BatcherTests.OnlyTheGpuInstancesOfASpotLightsEntityCarryItsLightHolderMark(generation: 7) | 4.5 |
| R07 | src/Tertius.Rendering/Scene/SceneBatcher.cs:457 | holder lookup loses generation | killed | BatcherTests.TheGpuInstanceOfAnEntityThatHoldsALightSaysSoAndAnyOtherDoesNot | 5.3 |
| R08 | src/Tertius.Rendering/Scene/SceneBatcher.cs:457 | both sides of single-snapshot holder lookup lose generation; equivalent for valid snapshots | survived |  | 4.1 |
| R09 | src/Tertius.Rendering/Scene/ShadowTiles.cs:398 | replacement light inherits holder margin | killed | ShadowTilesTests.ALampInTheSlotOfADestroyedHolderRanksAsAChallenger | 4.2 |
| R10 | src/Tertius.Rendering/Scene/ShadowTiles.cs:378 | replacement light inherits layers through slot-only Keep and Held | killed | ShadowTilesTests.ALampInTheSlotOfADestroyedOneKeepsNoneOfItsLayers | 5.0 |
| R11 | src/Tertius.Rendering/Scene/ShadowTiles.cs:521 | replaced light reuses stale tile | killed | ShadowTilesTests.ALampInTheSlotOfADestroyedOneDrawsItsTileAgain | 7.1 |
| R12 | src/Tertius.Rendering/Scene/ShadowTiles.cs:540 | replaced caster reuses stale tile | killed | ShadowTilesTests.ACasterOfAnotherEntityPartMeshOrMaterialDrawsItsTileAgain(change: "entity in the slot of a destroyed one") | 4.7 |
| R13 | src/Tertius.Rendering/Scene/ShadowTiles.cs:584 | recorded light loses generation and live cache identity | killed | ShadowTilesTests.TwoLightsInOnePlaceKeepTheirOwnCachedTiles, ShadowTilesTests.TileWithAStillPosedCasterStaysCached, ShadowTilesTests.ARankingThatSwingsByLessThanTheMarginAtTheBudgetsEdgeKeepsOneOwner | 6.2 |
| R14 | src/Tertius.Rendering/Scene/ShadowTiles.cs:595 | recorded caster loses generation and invalidates stable cache | killed | ShadowTilesTests.TwoLightsInOnePlaceKeepTheirOwnCachedTiles, ShadowTilesTests.TileWithAStillPosedCasterStaysCached, ShadowTilesTests.TileWithTwoStillPosedCastersOfDifferentPosesStaysCached | 7.1 |
| R15 | src/Tertius.Rendering/Scene/ShadowTiles.cs:369 | live holder cannot keep its own layers | killed | ShadowTilesTests.ALightThatStaysInTheBudgetKeepsItsLayers, ShadowTilesTests.ALightNewToTheBudgetTakesTheLowestFreeLayers, ShadowTilesTests.ALampInTheSlotOfADestroyedOneKeepsNoneOfItsLayers | 4.9 |
| R16 | src/Tertius.Rendering/Scene/ShadowTiles.cs:398 | live holder loses valid margin | killed | ShadowTilesTests.ARankingThatSwingsByLessThanTheMarginAtTheBudgetsEdgeKeepsOneOwner, ShadowTilesTests.AFrameThatWasNeverSubmittedMakesNoHolder, ShadowTilesTests.AHolderKeepsItsTilesWhileAChallengerOfItsTierIsLessThanTwiceAsBright | 4.9 |
| R17 | src/Tertius.Rendering/Scene/ShadowTiles.cs:521 | live light loses valid tile cache | killed | ShadowTilesTests.TwoLightsInOnePlaceKeepTheirOwnCachedTiles, ShadowTilesTests.TileWithAStillPosedCasterStaysCached, ShadowTilesTests.ALampInTheSlotOfADestroyedOneDrawsItsTileAgain | 6.9 |
| R18 | src/Tertius.Rendering/Scene/ShadowTiles.cs:540 | live caster loses valid tile cache | killed | ShadowTilesTests.TwoLightsInOnePlaceKeepTheirOwnCachedTiles, ShadowTilesTests.TileWithAStillPosedCasterStaysCached, ShadowTilesTests.ACasterOfAnotherEntityPartMeshOrMaterialDrawsItsTileAgain(change: "entity in the slot of a destroyed one") | 7.7 |
| R19 | src/Tertius.Rendering/Scene/ShadowTiles.cs:463 | submitted tile bookkeeping loses generation | killed | ShadowTilesTests.TwoLightsInOnePlaceKeepTheirOwnCachedTiles, ShadowTilesTests.TileWithAStillPosedCasterStaysCached, ShadowTilesTests.ARankingThatSwingsByLessThanTheMarginAtTheBudgetsEdgeKeepsOneOwner | 4.6 |
| R20 | src/Tertius.Rendering/Scene/ImpostorManager.cs:16 | reused slot inherits impostor hysteresis | killed | ImpostorManagerTests.AnInstanceInTheSlotOfADestroyedOneWithTheSamePartMeshAndMaterialGetsItsOwnImpostorState | 4.7 |
| R21 | src/Tertius.Rendering/Scene/ImpostorManager.cs:16 | impostor key substitutes first live generation for actual generation | killed | ImpostorManagerTests.KeysWithoutATileAreForgottenAfterTheirLifetime, ImpostorManagerTests.TilesAreEvictedLeastRecentlyUsedFirstAndOnlyFromKeysThatAreGoneOrGeometry, ImpostorManagerTests.AFringedMaterialNeverBecomesAnImpostor | 4.2 |
28 rows: killed 26, timeout 0, survived 1, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 1

R08's survivor is triaged as equivalent for the reason above. `Core-Seams/RenderLight.cs-R9` removes only the old XML description of `EntityIndex`, not behavior. The six explicit Core faults compensate for the official tool's missing getter mutator. The listed cache faults also verify that stable live lights and casters retain valid cache state, while replacement lights/casters do not inherit tile content, layers or holder margin. The impostor fault is killed by the reused-slot hysteresis test. Existing fixtures include live generations 1, 2, 3, 4, 5 and 7; the new spot-holder test adds 65,537.

### Final validation and tested identity

The final tested code-and-test commit is `c56491e4cce7ebed5665c0cfe42fad413ab2bfb7`. It contains exactly the production/test tree that passed the final suites; the code tree hash is `57cc857b464ef50c46216194069adcc3cbe536c3`. The commit hook confirmed full Checked test records on that staged code and skipped duplicate builds/tests. Later evidence changes modify ticket prose only. Their code tree is checked against this hash instead of repeating the full suites.

Before that freeze, `nix develop --command bash tools/cleanup.sh <19 explicitly named ticket-touched C# files>` applied the existing Roslyn and CSharpier cleanup. Only two test files changed: the new spot-holder cases, formatting, and the existing Core test's local declaration. No production file changed. CSharpier checked all 19 files again after final validation. Whitespace checks on the hardening delta passed. The original-base whitespace check reports one pre-existing warning: `.tickets/ter-6gf8.md:15: new blank line at EOF.` The same check against the starting HEAD reports exactly that warning. The follow-up file remains unchanged; `pre-existing-whitespace-check.log` and `original-scope-whitespace-check.log` preserve the comparison.

Final complete commands, each run once after the last actual test change, were `nix develop --command dotnet test --solution Tertius.slnx -c Checked` and the same command with `-c Release`, under the existing GPU lock. Checked: total 10,255, passed 10,236, failed 0, skipped 19, duration 4m 38s 764ms. Release: total 10,242, passed 10,223, failed 0, skipped 19, duration 4m 27s 482ms. The skips are the existing optional measurements and driver-specific timing skip; no new skip was added.

| Test project | Checked passed | Release passed | Checked / Release skipped | Checked / Release failed |
|---|---:|---:|---:|---:|
| Core | 1207 | 1175 | 0 / 0 | 0 / 0 |
| Host | 1978 | 1978 | 5 / 5 | 0 / 0 |
| Physics | 1492 | 1493 | 8 / 8 | 0 / 0 |
| Rendering | 2626 | 2625 | 6 / 6 | 0 / 0 |
| Simulation | 2001 | 2020 | 0 / 0 | 0 / 0 |
| Slop | 597 | 597 | 0 / 0 | 0 / 0 |
| Style | 276 | 276 | 0 / 0 | 0 / 0 |
| Unions | 59 | 59 | 0 / 0 | 0 / 0 |

The Release benchmark project also built explicitly: `nix develop --command dotnet build benchmarks/Tertius.Benchmarks/Tertius.Benchmarks.csproj -c Release`, with zero warnings and zero errors. This was a build check, not a new performance benchmark. There are no remaining validation blockers. No merge, push, PR, ticket closure, worktree removal, Jolt change, unrelated refactor or new soak campaign was performed. Integration remains parent-owned.

Bounded logs, the exact deliberate list, original and final outcomes, coverage XML, source-tree identity and commit logs are preserved at `/private/tmp/tertius-hardening-20261105/ter-f17s`. Key files are `validation-evidence.json`, `mutation-outcomes.json`, `mutation-outcomes.md`, `stryker/results.json`, `listed-before/results.json`, `listed-red-r06/results.json`, `coverage-before/file-summary.json`, `coverage-after/file-summary.json`, `final-checked.log`, `final-release.log`, `benchmark-build.log`, `cleanup.log` and `final-format-check.log`. No other work was deleted.
