---
id: ter-rgg9
status: in_progress
deps: [ter-gayt]
links: []
created: 2026-11-04T20:36:02Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, trace]
---
# The screen tracer's walk takes the hill-climb's portable changes

From ter-dzxf (docs/research/temporal-renderer/walk-hill-climb.md, 'Reproduction and handoff'): after ter-gayt merges, change walkScreen in screen_walk.glsl to the two-image combination bounds-predicate+loop-predicate+facing: the vector Boolean bounds test, the vector Boolean loop condition without short-circuit control flow, and the sign-only facing test (no normalize of the decoded normal, local to the facing test). Keep the original arithmetic order, the two-image depth layout (plan decision 46), reflection cap 256 and diffuse cap 62, and the words form of the per-ray inputs (tools/sol-walk/loop-reads.py counts 0). The harness cut the extracted walk by 21.8 to 23.6 % at cap 62 with every hit unchanged on 3,038,431 saved rays; the projected whole trace on the deck is about 3.14 ms against 3.90. Acceptance: the C# copy (ScreenRay) and the GPU tests that pin the shader to it pass unchanged; the real busy frame on demo-exit's deck at the top clock is measured and reported against the 12 ms budget, before and after. The user chose portable only on 2026-11-04: no Mac-only Metal path.


## Notes

**2026-11-05T15:42:54Z**

Preserved launch-ter-rgg9.md from /private/tmp/tertius-integration-20261105/ before the final commit. SHA-256: 6ce6024479ea5e9cc33a4033b1273f2663433e5578285383f3e060b5c58b0d1b

```markdown
---
id: ter-rgg9
status: in_progress
deps: [ter-gayt]
links: []
created: 2026-11-04T20:36:02Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, trace]
---
# The screen tracer's walk takes the hill-climb's portable changes

From ter-dzxf (docs/research/temporal-renderer/walk-hill-climb.md, 'Reproduction and handoff'): after ter-gayt merges, change walkScreen in screen_walk.glsl to the two-image combination bounds-predicate+loop-predicate+facing: the vector Boolean bounds test, the vector Boolean loop condition without short-circuit control flow, and the sign-only facing test (no normalize of the decoded normal, local to the facing test). Keep the original arithmetic order, the two-image depth layout (plan decision 46), reflection cap 256 and diffuse cap 62, and the words form of the per-ray inputs (tools/sol-walk/loop-reads.py counts 0). The harness cut the extracted walk by 21.8 to 23.6 % at cap 62 with every hit unchanged on 3,038,431 saved rays; the projected whole trace on the deck is about 3.14 ms against 3.90. Acceptance: the C# copy (ScreenRay) and the GPU tests that pin the shader to it pass unchanged; the real busy frame on demo-exit's deck at the top clock is measured and reported against the 12 ms budget, before and after. The user chose portable only on 2026-11-04: no Mac-only Metal path.


## Notes

**2026-11-05T15:17:51Z**

2026-11-05: tracer/Rule A merged, closed and pushed at 34c28466e641a864fd6a915c029e08f07b3b9ccc. Started workflow 680e3ea3-0eba-481b-88ba-41f1e578fa95, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-rgg9-portable-screen-walk, exact local base 34c28466e with startFromOrigin=false. Codex gpt-6.1-sol reasoningEffort=xhigh for build/review/fix. One of four heavy allocations. Only previously selected two-image portable bounds/loop/facing changes; retain childAt/solved correctness and ray caps 256/62. No approximation or Mac-only path. Parent owns hardening, integration and closure.

```

**2026-11-05T16:26:21Z**

Implementation stage complete on the supplied integrated tracer/History Rule A base 34c28466e641a864fd6a915c029e08f07b3b9ccc, in /Users/alexispurslane/.t3/worktrees/tertius/ter-rgg9-portable-screen-walk on branch ter-rgg9-portable-screen-walk. This is not ticket closure or parent hardening completion.

Ported only the settled vector Boolean bounds and loop predicates and local sign-only facing normal. Bounds remain in their existing screen_ray.glsl owner. The loop keeps the original negated ray-end comparison. Arithmetic order, exact childAt/solved tracking, slab checks, two depth images, integer-words inputs, reflection cap 256 and diffuse cap 62 are unchanged. No nudge, approximation, one-image pyramid, scheduler, private Metal API, or history/denoising change.

Three real compiled-probe tests failed for the intended missing execution-cost behavior before implementation and passed afterward. GPU-locked replays preserve all 3,038,431 active saved-ray hit identities against the current integrated baseline. The historical cap-62 V6 baseline differs on 195,044 identities, so its 21.8–23.6% saving and 3.14-ms trace projection are not transferred. Captured input file hashes match retained provenance. Both extracted current walks have five record accesses outside the loop and zero inside. Both extracted modules and the real block/own-ray shaders pass Vulkan 1.3 SPIR-V validation.

Three 120-frame real demo-exit deck runs per build, after tick 399, 1280x720, two captures in flight, all under the project GPU lock, all at mean/p10/p90 1296 MHz: mean GPU frame span 11.316 -> 11.182 ms, saving 0.134 ms (1.18%); mean margin below the existing 12-ms budget is 0.818 ms. Each run's frame p95 is below 12 ms. The neighboring reflection-rays-through-screen-trace-resolve interval is 3.602 -> 3.474 ms (3.56%); it is not isolated pass-own GPU cost on MoltenVK.

The real deck observation film records 152 draws: 32 initial warm-up draws and 120 shown frames with distinct snapshot times. Shown draw index advances 32 -> 151 without another reset, and history-image parity alternates. Three inspected captures are /private/tmp/ter-rgg9-20261105/screenshots/deck-000000.png (lit cabin framing and floor reflections), deck-000059.png (person in doorway) and deck-000119.png (person left on deck). Fine floor speckle remains. The PNG observation run averages 812.6 MHz and is excluded from the top-clock performance comparison.

Required checks completed in the Nix dev shell, one build/test command at a time: tools/cleanup.sh on the changed C# file; full Checked solution 9,948 passed/20 skipped/0 failed; full Release solution 9,935 passed/20 skipped/0 failed; Release benchmark build 0 warnings/0 errors. The unchanged skips include ter-186q and the late-latching driver's isolated-pass assertion. Temporary measurement drivers are absent from both complete suite builds. Code/test/build-input tree d22c4fcb51fae5ecf942d661aecdee2341d7b3b8 is unchanged after those checks. Full report, source/input hashes, timings, saved-ray gate and history/validation records: docs/research/temporal-renderer/portable-screen-walk.md and its sibling records directory. All raw logs and replay scripts remain in /private/tmp/ter-rgg9-20261105.

No unsettled fork. Linux runtime/performance is unavailable here and remains user-deferred under ter-cgif. Coverage, mutation testing, integration, push and ticket closure belong to the parent and were not run here. The launch brief is preserved in the earlier note before this commit. No other checkout, preserved tracer, root source, or worker artifact was changed.

### Review fix, 2026-11-05

Completed the single review-fix stage in the same bound checkout and branch, starting from reviewed head `1d037b55c7309a5eeed65a630f05f82d5991f661`. Ran `tk start ter-rgg9`; the ticket remains in progress for parent hardening and integration. No agents, workflows, checkout changes, merges, pushes or ticket closure were performed.

| Finding or assessment gap | Resolution or retained limit |
|---|---|
| Duplicated production decoder ownership | Confirmed the copied reconstruction and folding in `hitsPixel`. Moved those operations into `octDecodeUnnormalized`, owned by `octahedral.glsl`. `octDecode` normalizes that shared result for shading; facing consumes it without normalization. Original reconstruction and folding arithmetic order is unchanged. |
| Refactor instruction shape and arithmetic were unverified | All 63 production shader modules are byte-identical to the reviewed head after debug stripping and identifier compaction. All 82 production/test modules plus both extracted replay modules pass Vulkan 1.3 SPIR-V validation. The three unchanged compiled-operation tests pass before and after extraction. This static ownership defect claimed no runtime failure; no new behavior or artificial failing runtime test was introduced. |
| Review had not executed builds, tests or GPU dispatches | This fix stage ran fresh focused tests, complete Checked and Release suites, shader validation, saved-ray GPU dispatches, real-renderer history observations, six GPU-locked timing runs and Release benchmark compilation. Exact logs, hashes and outcomes are retained in `docs/research/temporal-renderer/portable-screen-walk/review-fix.json`. |
| Saved-ray evidence needed to apply to the final port | Independently rehashed captured inputs and replayed the full current walk against the integrated baseline. All 3,038,431 active ray identities agree; all 32 outcome files are byte-identical to retained pre-extraction outputs, including hit-entry values. Historical V6 still differs on 195,044 identities, so its timing projection remains rejected. |
| Universal floating-point equivalence was not established | Retained as an evidence limit. The saved-ray gate is finite, not a claim about every grazing direction. The extraction itself retains the production shader instructions; this does not prove universal equivalence of the earlier normalized-to-unnormalized optimization. |
| ter-186q far-vanishing-point regression remains skipped | Unchanged and outside this ticket's selected bounds/loop/facing port. Both complete suites retain their existing skips; this stage does not claim to fix ter-186q. |
| Fresh actual-deck timing after refactor was missing | Three alternating 120-frame runs per build at matched mean/p10/p90 clocks of 1296 MHz give 11.317 ms for the integrated base and 11.196 ms for the final port. The 0.121-ms reduction is 1.07%; final mean margin to 12 ms is 0.804 ms, and all final p95 values are below 12 ms. This compares the complete port against the integrated base, not the extraction against its predecessor. |
| Isolated trace-pass GPU cost and broader performance were unavailable | No isolated pass-own cost is claimed from MoltenVK timestamps. Retained neighboring intervals are labeled as intervals. Performance evidence concerns this 1280-by-720 deck stretch on this Mac, not every frame or driver. Linux runtime/performance remain explicitly user-deferred under ter-cgif. |
| Capture/history provenance | A fresh sampled observation proves 120 advancing shown frames, draw indices 32 through 151, distinct snapshot times, alternating image parity and no reset after the first shown frame. Three current captures were opened and inspected. Capture clocks average 820.1 MHz, p10 644.0 and p90 942.6; its 16.292-ms frame mean is excluded from the top-clock budget comparison. The first attempt omitted the clock sampler; its raw captures/history/logs are preserved separately rather than discarded. |
| Historical report source identity could be mistaken for current validation | The stale text was “The code and build-input tree is `d22c4fcb51fae5ecf942d661aecdee2341d7b3b8`.” Its section now says “Initial implementation validation.” A separate correction section records current tree `49b4137202ce507ffa92c86f45a662ac0788f508`, current source/assembly/log hashes and fresh results. ARCHITECTURE.md now names the shared decoder owner. |
| Parent-owned hardening and integration | Coverage, mutation testing, integration, push and closure were intentionally not run. These remain parent-owned, not unfinished work within this fix stage. |

Before final validation, `tools/cleanup.sh` ran on the ticket's changed C# test file and made no further source change. Complete Checked: 9,948 passed, 20 skipped, zero failures, 3m 52s 884ms. Complete Release: 9,935 passed, 20 skipped, zero failures, 3m 53s 217ms. Release benchmark build: zero warnings and errors. Temporary replay and observation classes are absent from all final Checked and Release test assemblies. The existing C# ScreenRay reference and real-shader correctness tests are unchanged from the integrated base. No bounds/loop predicate, slab/cell tracking, image layout, ray-word input, cap, nudge, scheduler, private Metal API, history or denoising policy changed in this correction.

The earlier note still preserves the complete `/private/tmp/tertius-integration-20261105/launch-ter-rgg9.md` before this final commit. Its independently rechecked SHA-256 remains `6ce6024479ea5e9cc33a4033b1273f2663433e5578285383f3e060b5c58b0d1b`. No unresolved design fork remains. This stage completes only the scoped review fix, not ticket closure or parent integration readiness.

## Parent hardening and main reconciliation, 2026-11-05

The completed workflow `680e3ea3-0eba-481b-88ba-41f1e578fa95` delivered clean commit `a3d5cf545f32503bbc26666c122ec55003bcf8c1`. The parent verified terminal success, settled members and complete cleanup, then hardened the delivered change without repeating implementation, review or fix stages.

The official changed-code classifier ran through Nix against main. It found no changed C# implementation in Core, Simulation or Physics. The production delta consists only of `octahedral.glsl`, `screen_ray.glsl` and `screen_walk.glsl`. Changed production C# executable coverage is not applicable, 0/0 lines and 0/0 branches, not 100%. CPU coverage does not measure GLSL; no shader line-coverage percentage is claimed. Evidence is `/private/tmp/tertius-hardening-20261105/ter-rgg9/official-classifier.log`.

The scripted listed campaign initially killed eight of eleven faults. B01, B02 and L02 survived. Two real-GPU theories, twelve cases in `ScreenWalkBoundaryTests`, now check viewport lower/upper boundaries and actual wall hits across ray-segment lengths and step limits. The original restored shader passes, each of the three faults fails a behavioral assertion, and the restored shader passes again. Production shader source was not changed to kill a mutant. Scoped C# cleanup passed.

This is the single final disposition table. The first campaign and targeted follow-up remain separate before/after evidence, not additional final mutants.

| ID | Deliberate fault | Final result | Detecting evidence |
| --- | --- | --- | --- |
| P01 | Restore scalar short-circuit bounds branching | Killed | `ScreenWalkExecutionTests.CheckingTheScreenBoundsNeedsNoConditionalBranch` |
| P02 | Restore separate step-limit and ray-end branches | Killed | `ScreenWalkExecutionTests.TheWalkBranchesOnTheCombinedStepCapAndRayEndPredicates` |
| P03 | Normalize the facing-only decoded normal | Killed | `ScreenWalkExecutionTests.TestingSurfaceFacingNeedsNoNormalNormalization` |
| B01 | Reject a viewport cell only when both boundary failures hold | Killed after new tests | `ScreenWalkBoundaryTests.TheShaderKeepsOnlyCellsInsideBothExclusiveViewportBounds` |
| B02 | Accept the viewport's exclusive upper edge | Killed after new tests | `ScreenWalkBoundaryTests.TheShaderKeepsOnlyCellsInsideBothExclusiveViewportBounds` |
| L01 | Stop every walk after one step | Killed | `ScreenTraceRenderTests.AHitFartherThanTheLargestHalfFloatStoresTheLargestHalfFloatAsItsDistance` |
| L02 | Ignore the ray segment's end | Killed after new tests | `ScreenWalkBoundaryTests.TheShaderHitsTheNextWallPixelOnlyWithinItsSegmentAndStepCap` |
| L03 | Continue when either loop guard holds | Killed | `ScreenWalkExecutionTests.TheWalkBranchesOnTheCombinedStepCapAndRayEndPredicates` |
| N01 | Reverse the decoded surface normal | Killed | `ScreenTraceRenderTests.ASurfaceAboveTheReflectionLimitReceivesSpecularLightFromItsDiffuseRays` |
| N02 | Remove required unit-length shading normalization | Killed | `ScreenTraceRenderTests.UnderAUniformEnvironmentWithNoBlockerBothIndirectSignalsConvergeToItsLight` |
| N03 | Fold the wrong octahedral hemisphere | Killed | `ScreenTraceRenderTests.AHitFartherThanTheLargestHalfFloatStoresTheLargestHalfFloatAsItsDistance` |

Final outcomes: 11/11 killed; zero survivors, equivalents, judgment calls, timeouts, no-coverage, build-failed or no-tests outcomes. Lists, exact assertions and restoration results are in `mutants.json`, `mutations/results.json`, `survivors.json`, `survivors-after/results.json`, `mutation-lock.log`, `survivors-lock.log` and `cleanup.log` under `/private/tmp/tertius-hardening-20261105/ter-rgg9/`. The three compiled-operation faults protect the selected instruction-shape decisions; the other faults test visible or ray-hit behavior. No reliability soak or universal floating-point equivalence is claimed.

The parent combined main `6119ffdaaddc0bf28e6f92dcd94bdc809c463822`, including the already integrated Entity placement-key change, with the delivered branch and the twelve new cases. ARCHITECTURE.md retains both the shared octahedral decoder owner and the Entity placement-key model. A failed indexed stash application interrupted only the merge bookkeeping; all six incoming paths and both parent test files were saved, restored selectively and verified by SHA-256 before validation. No root ticket overlay or unrelated stash was discarded. Recovery receipts are in `/private/tmp/tertius-integration-20261105/merge-rgg9/`.

Combined code/test/build-input identity is `a60dc7a1e0dc37d804f799cce0e89a4d53ce5281`. Fresh complete Checked and Release validation is running through Nix and `tools/gpu-lock.sh`, with one test process at a time. This paragraph records preparation, not a passing result or integration claim; final results follow separately.

The root launch note was extracted from the delivered ticket and compared byte for byte before integration. Its SHA-256 remains `6ce6024479ea5e9cc33a4033b1273f2663433e5578285383f3e060b5c58b0d1b`. Current validation does not change the recorded performance scope, the existing ter-186q skip, deferred Linux runtime under ter-cgif, or the lack of isolated trace-pass GPU timing. No denoising, corrected environment-read search, FSR policy, ray count or history policy is imported by this reconciliation.

### Combined validation passed

The fresh combined run completed successfully through the normal Nix dev shell and GPU lock:

`nix develop --command bash -c 'bash tools/gpu-lock.sh /private/tmp/tertius-integration-20261105/merge-rgg9/combined-lock.log bash tools/test-all.sh -c Checked,Release -j 1 -o /private/tmp/tertius-integration-20261105/merge-rgg9/combined'`

| Configuration | Passed | Existing skips | Failed | Full solution build, including benchmarks |
| --- | ---: | ---: | ---: | --- |
| Checked | 9,970 | 20 | 0 | Passed, zero warnings/errors |
| Release | 9,957 | 20 | 0 | Passed, zero warnings/errors |

All sixteen full-suite test records match the combined code/test/build-input identity `a60dc7a1e0dc37d804f799cce0e89a4d53ce5281`. The twelve added boundary/segment cases pass in both configurations. Current production shaders remain byte-identical to delivered commit `a3d5cf545f32503bbc26666c122ec55003bcf8c1`; only Entity reconciliation and parent tests changed code inputs. Whitespace checks pass. The complete log and per-module outputs are retained under `/private/tmp/tertius-integration-20261105/merge-rgg9/combined-lock.log` and `combined/`. Ticket prose written after the run does not change the tested inputs. Normal commit hooks, main integration, closure and push follow; none is claimed by this validation receipt alone.
