---
id: ter-l113
status: closed
deps: []
links: []
created: 2026-11-04T15:51:35Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
---
# Headless timing summary: on stamps that latch late, print neighbouring sums, not single-pass times

On MoltenVK (VulkanRenderer.StampsLatchLate), a pass's GPU time can hold part of the next pass's work. Today WriteGpuTimings in src/Tertius.Host/ScriptCommand.cs prints every pass's own mean, median and p95, then LateStampsNote. A reader takes the per-pass lines as true and has to sum neighbours by hand: ter-9nrc's measurements needed a script (fix/neighbours.py) for this. Suggested by ter-9nrc's review on 2026-11-04. Make the summary print only what holds on such a renderer: the span, and the sum of each run of neighbouring passes that the user names or that the tool picks, or mark each single-pass line as not valid on this driver. Portable: on a driver whose stamps do not latch late, the per-pass lines stay as they are. Design the rule for which sums to print before the build (two designs, with the user).

## Integration, 2026-11-05

Merged into main as `3b1f23c30865ac65a7b3947aa0318cbadd5aea30`, with the entire committed tree identical to validated commit `af2a772b98ab7626f2ba415404496a3496277f16`. Final Checked and Release suites passed 10,267 and 10,254 tests respectively, with zero failures and 19 existing skips each. All 38 scripted faults were caught; the changed Host coverage and six branch-outcome dispositions remain recorded below. The hardening workflow completed successfully and its worker stopped before ticket closure. Parent coordination notes are preserved. This closure changes ticket prose only, so it does not require another complete suite. Main integration did not wait for independent tracer, identity, light-budget or denoising work.

## Settled choices, 2026-11-04

The user delegated both choices to the orchestrator. Repeatable `--gpu-run "FIRST..LAST"` selects caller-named inclusive ranges of consecutive actual passes. No frame-graph group tags are added. On `StampsLatchLate` drivers, the console hides individual pass lines and prints the frame span, requested range sums and an explanation. Without requested ranges, it prints only the frame span and the explanation. Other drivers preserve individual pass lines and add requested sums.

## Implementation

- Each copied pass sample retains its recorded frame position. Every requested range sums its actual pass times within each frame before statistics are computed. Frame spans and ranges use the same statistics model and calculation. Existing frame selection, even-count medians and nearest-rank p95 stay unchanged.
- Ranges preserve request order and can overlap. Equal endpoints select one recorded pass. Names are case-sensitive and can contain spaces. Outer endpoint whitespace is trimmed. Malformed syntax, unknown names, reversed ranges and endpoints missing from any frame produce actionable command-line errors. Empty recorded passes count; skipped interior passes have no sample. Missing endpoints never become zeros or silently remove frames.
- Existing JSON fields and raw pass values remain. Added `runs` entries have `first`, `last` and `timing` statistics. Added `stampsLatchLate` and the derived `warning` identify raw values that are not valid alone. Frame spans remain measured spans, including inter-pass gaps.
- The implementation changes host reporting only. It adds no GPU work, timestamp rules, Vulkan features or rendered visuals. Coverage, mutation testing and integration remain with the parent orchestrator. This ticket stays in progress until integration.

## Validation

The first CLI behavior run failed 10 intended assertions because `--gpu-run` was unknown. The first range behavior run failed 14 intended assertions because range summaries were unavailable. A real MoltenVK capture failed because it still printed 29 individual pass lines. The implemented range, report and CLI tests then passed; the real capture class passed all 37 tests under `tools/gpu-lock.sh`. Later red assertions caught overlapping separators (`bloom...capture`) and preservation of the existing negative-frame filter. Both now pass.

All builds and tests used the Nix dev shell. GPU-bearing suites used `tools/gpu-lock.sh`. Both solution builds included `benchmarks/Tertius.Benchmarks`, covering the gap documented by ter-uxud.

| Configuration | Solution build | Full solution tests | Skips |
|---|---|---|---|
| Checked | Passed, zero warnings and errors | 10,262 passed, zero failures | 19 |
| Release | Passed, zero warnings and errors | 10,249 passed, zero failures | 19 |

The 19 skips are 18 opt-in measurement tests and one individual-pass timestamp test that does not apply to MoltenVK. The full Host suite passed 2,019 tests in both configurations, with five opt-in measurements skipped. Synthetic report tests cover ordinary-driver output as well as late-driver output. CSharpier checked all seven touched C# files. `git diff --check` passed. One early focused invocation hit a test-host handshake failure before running any tests; subsequent focused and full runs passed. Logs are in `/tmp/ter-l113-logs` on the build machine. Coverage and mutations were not run, as assigned to the parent.

## Self-review

- Correctness: per-frame range sums precede statistics; missing endpoints and reversed frame order fail explicitly. Raw pass data and measured frame spans remain separate. Tests protect copied renderer storage and duplicate-frame filtering.
- Fulfillment: repeatable caller-selected ranges, late-driver filtering, ordinary-driver compatibility, JSON metadata, CLI help and public architecture documentation are covered. No renderer or Vulkan behavior changed.
- Design: endpoint validation has one implementation for both endpoints. Frame and range statistics use `SpanSummary` and the same calculation. `GpuTimingReport` and `RunSummary` are report payloads, not new state owners; the warning is derived from the driver flag. Frame spans and pass samples are independent measurements, not mirrored stores. The existing playback operation remains the lifecycle choke point rather than spreading its resource and error ordering across extra wrappers. Its length and the range query's guard branches remain advisory review signals, not reasons to add artificial interfaces.

## Review fixes

Both findings are accepted. The class of mistake is a report consumer discarding the report's timing-validity information. The existing `stampsLatchLate` flag and derived `warning` remain authoritative; neither tool identifies a driver by name or calculates another set of timing statistics.

| Finding | Resolution | Behavior evidence |
|---|---|---|
| The documented timing wrapper hid the explanation | `demo-timing.sh` prints the existing JSON `warning` before forwarding the timing lines. It keeps no second warning message. The stale documentation said, "The full run log explains why individual pass times are omitted." The documentation now states that the terminal also shows the explanation. | The real wrapper runs against controlled external build and renderer commands, through the real clock recorder and lock helper with isolated test lock paths. Its warning-forwarding assertions first failed with and without requested ranges. Both now pass; ordinary and legacy output also stays unchanged. |
| The comparison reporter printed invalid individual-pass differences and ignored ranges | `gpu_clock.py compare` suppresses all individual-pass rows when either input has a late-stamp flag or a warning. It explains the omission and retains the reports' warnings. It reads the existing per-frame range means, matches both endpoints and leaves missing cells empty rather than inventing zeros. Legacy reports keep their previous behavior. The old documentation promised "the mean of each pass of each run" without a validity condition; it now documents the shared validity rule and range rows. | Red assertions reproduced late/late and both mixed-driver orders, warning-only and flag-only reports, absent range rows and a range compared with a legacy report. Ordinary-driver and legacy pass comparisons remain covered. A subprocess test also runs the real comparison CLI. |

The focused red run produced ten intended failing assertions, including subtests. After the fixes, all 43 tool tests pass. The clock-recording regression test also checks preservation of raw pass values, frame fields, range summaries and validity metadata. The fix changes only tool reporting, tests and documentation; the Host calculation, renderer, shaders and Vulkan behavior are unchanged. No finding is rejected, and no new user-visible design choice is open.

### Independent fix-stage validation

Every command finished in the foreground, one build or test command at a time. Logs are in `/tmp/ter-l113-logs/fix`. Each build/test log starts with its actual Nix command invocation. The full suites ran through `tools/gpu-lock.sh`; the full solution builds compiled `Tertius.Benchmarks` in both configurations before the suites used `--no-build`.

| Check | Result | Evidence file in that folder |
|---|---|---|
| `nix develop --command python3 -I tools/test_gpu_clock.py -v` | 43 passed | `tool-final.log` |
| `nix develop --command dotnet test --project tests/Tertius.Host.Tests -c Checked --filter-class '*GpuTiming*'` | 35 passed | `host-focused.log` |
| `nix develop --command dotnet build Tertius.slnx -c Checked --no-restore -v minimal -nologo` | Passed, zero warnings and errors, including benchmarks | `build-checked.log` |
| `nix develop --command bash tools/gpu-lock.sh /tmp/ter-l113-logs/fix/checked-gpu.log dotnet test --solution Tertius.slnx -c Checked --no-build` | 10,262 passed, zero failed, 19 skipped | `test-checked.log`, `checked-gpu.log` |
| `nix develop --command dotnet build Tertius.slnx -c Release --no-restore -v minimal -nologo` | Passed, zero warnings and errors, including benchmarks | `build-release.log` |
| `nix develop --command bash tools/gpu-lock.sh /tmp/ter-l113-logs/fix/release-gpu.log dotnet test --solution Tertius.slnx -c Release --no-build` | 10,249 passed, zero failed, 19 skipped | `test-release.log`, `release-gpu.log` |
| `nix develop --command bash -n tools/demo-timing.sh` | Passed | `shell-syntax.log` |

The 19 skips are the existing opt-in measurements and the individual-pass timestamp test that does not apply to MoltenVK. These are fresh full-suite runs, not a re-reading of the original builder's logs. No coverage or mutation testing was run; the orchestrator still owns those checks.

### Review assessment gaps

| Review gap | New evidence | Limit |
|---|---|---|
| No independent build/test execution in review | The commands and fresh results above, including direct execution of the comparison CLI and wrapper tests | None of the required fix-stage checks is unavailable or failing |
| No test of wrapper explanation forwarding | The red wrapper assertions and the passing `DemoTiming` tests, plus a real MoltenVK wrapper run | The unit tests replace only external build/renderer commands; the smoke run uses the real renderer |
| No late/mixed-driver or requested-range comparison tests | `TimingsComparison` tests and the real mixed-driver comparison in `mixed-compare.log` | Clock validity remains a separate check; the report does not claim that unknown clocks compare |
| No ordinary-driver Vulkan execution | Real wrapper runs on MoltenVK and KosmicKrisp using `scripts/pbr-spheres.json`, `--size 160x90`, `--video none` and `--gpu-run bloom..capture`, both through the GPU lock. Each reports two frames and preserves 29 raw passes. MoltenVK prints zero individual-pass lines and the original warning once; KosmicKrisp, whose late-stamp flag is false, prints all 29 individual-pass lines. Both print the frame span and requested range. `smoke-checks.log` checks these results. | KosmicKrisp has the already documented timestamp defect ter-dgen, so that run verifies reporting compatibility, not valid performance measurements. No Linux GPU is available here. The optional `gpu-clock` binary is not built, and both smoke reports correctly mark the clock unknown. No images were written. |
| Exact Nix invocations absent from earlier saved logs | Every new build/test log records its invocation, and the real wrapper logs record the selected ICD environment and options | No earlier command is inferred from a log that lacks its invocation |
| No complete changed-type cohesion/depth evidence | `type-metrics-v3` ran with the built Demo's 15 explicit external metadata references. `metric-references.json`, `type-metrics.json` and `changed-types.json` retain provenance, source locations, counts and applicability for all 18 types in the three ticket-touched C# files. Every selected type is complete or not applicable, with zero unresolved state accesses. | The synthetic source compilation still has 773 binding errors. A complete state-access measurement does not claim that this synthetic compilation builds. No complete repository-wide binding claim or cross-reference-set delta is made. Python and shell code are outside this C# counter's population. |

The changed-type measurements below are design evidence, not a reason to reshape a payload or add artificial state access. TCC is the shared-instance-storage method-pair count divided by possible eligible pairs. Depth is handwritten cyclomatic complexity divided by declared API entries.

| Type | TCC | Depth | Design assessment |
|---|---|---|---|
| `ScriptCommand` | Not applicable, static | `156/17 = 9.1765` | Keeps playback and report orchestration at the existing boundary; this fix changes none of it |
| `GpuTimingSummary` | `5/6 = 0.8333`, complete | `41/5 = 8.2` | Frame-span and pass queries read independent measurements. Keeping both under `Add` preserves their frame association; splitting solely to remove their non-sharing pair would weaken that ownership |
| `GivenOptions` | `3/6 = 0.5`, complete | `15/5 = 3` | A parsed command-line payload with value and validation queries. The counter records no storage for the LINQ-based `RunChoiceProblem`; the source still consults the parsed inputs through its `Gives` predicate. No separate option store or validation owner is warranted |
| `GpuTimingReport` | Not applicable | `2/4 = 0.5` | Compatible wire payload with a derived warning, not a behavior owner that needs a deeper interface |
| `Headless` | Not applicable | `2/3 = 0.6667` | Run-choice payload whose existing capture-target query does not justify hiding its required input fields |
| `RendererOverrides` | Not applicable | `10/10 = 1` | Configuration payload with the existing application operation; unrelated to timing calculation and unchanged by this fix |
| The other 12 selected types | Not applicable | Not applicable | Declaration-only payloads or generated union declarations; their raw counts remain in `changed-types.json` |

Correctness: per-frame range statistics, frame spans and raw JSON remain unchanged; both tool consumers use the existing validity metadata. Fulfillment: both reported output defects have red-to-green behavior evidence, fresh full-suite checks and real-renderer smoke evidence with stated limits. Design: driver validity stays in the report instead of being guessed by each consumer, range means come from the existing summary, and the per-type evidence has explicit applicability. No review finding remains unresolved.

## Parent coordination notes preserved during hardening

The root snapshot and its base are preserved as `parent-notes.latest.md` and `parent-notes.base.md` under `/private/tmp/tertius-hardening-20261105/ter-l113`. These historical notes supplement the branch's implementation and fix evidence. They do not replace it.

### 2026-11-04T23:32:15Z

2026-11-04: user delegated both timing-summary choices. Settled hybrid: caller names inclusive ranges of consecutive render passes with repeatable --gpu-run FIRST..LAST; on StampsLatchLate drivers hide individual pass lines from the console and show frame span plus explicitly named sums. Keep raw per-pass JSON with its driver-validity warning, add per-frame sums before computing mean, median and p95, and keep ordinary console per-pass reporting on other drivers. All workflow agents now use Codex gpt-6.1-sol, reasoningEffort=xhigh. No implementation exists to replay.

### 2026-11-04T23:36:53Z

2026-11-04: implementation workflow 01c1ee3f-5f19-4f3d-96f7-560560a4e5e2, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-l113-headless-timing-summary from f703caf174af14d5350e410ce2b26acfcc42e5fe. Builder, reviewer and any fixer are Codex gpt-6.1-sol reasoningEffort=xhigh. The caller-selected ranges and omission of invalid single-pass console values are settled, not open user choices.

### 2026-11-05T12:13:08Z

2026-11-05: parent-owned hardening continuation started as workflow e823b93f-b08c-4843-af64-364692a31f57, version 077df081-6e60-4160-b9a4-b89ea970421a, preserving branch head 79fac63e9. Missing coverage, scripted fault checks, and final suites only; completed build/review/fix stages are not repeated. Completion delivery to the takeover thread was rejected (FORBIDDEN); the admitted run has no delivery destination. Parent retains merge/closure ownership.

### Immutable scope correction

The parent later authorized other integrations to advance `main`. Every coverage and Stryker scope command in this hardening uses immutable base `f703caf174af14d5350e410ce2b26acfcc42e5fe`, not a moving `main`. The checkout remains on `ter-l113-headless-timing-summary` and preserves implementation head `79fac63e9a2a5aeaca257d5a1d61e9113515ab5a` as its starting point.

## Parent-commissioned validation and test hardening, 2026-11-05

The hardening source commit is `03f0f6132a738ff0e89104142e153b3e6c96dde0`. It adds five Host test cases and one assertion in the existing Python legacy-report test. It also preserves the parent coordination notes. It changes no production file, shader, Vulkan operation or reporting decision. `ARCHITECTURE.md` still describes the unchanged implementation.

All artifacts below are under `/private/tmp/tertius-hardening-20261105/ter-l113`. The original review base remains `f703caf174af14d5350e410ce2b26acfcc42e5fe`. The original implementation/fix head remains `79fac63e9a2a5aeaca257d5a1d61e9113515ab5a`. No reset, stash, cleanup, branch switch, checkout creation, merge, push or ticket closure occurred.

### Changed Host coverage

The full Host test project ran with Cobertura coverage in Checked before hardening, then in Checked and Release after hardening. The before run passed 2,019 tests with five skips. Each after run passed 2,024 tests with five skips and zero failures. Every build and test ran in Nix, with GPU-bearing tests under `tools/gpu-lock.sh`. `coverage-before/command.txt` and each `coverage-after/<configuration>/Host/command.txt` record the commands.

`tools/coverage/changed-lines.py --base f703caf174af14d5350e410ce2b26acfcc42e5fe` measures the fixed ticket scope. Both the before report and the union of the after reports cover 152/152 changed executable lines and 98/104 changed branch outcomes. Coverage did not increase. The mutation results below show why the additional assertions matter despite the unchanged coverage counts.

| Affected production file | Changed lines covered, before and after | Changed branch outcomes covered, before and after | Whole-file lines covered after | Whole-file branch outcomes covered after |
|---|---:|---:|---:|---:|
| `src/Tertius.Host/ScriptCommand.Options.cs` | 1/1 | 0/0, not applicable | 26/26 | 20/20 |
| `src/Tertius.Host/ScriptCommand.cs` | 74/74 | 36/38 | 564/593 | 256/266 |
| `src/Tertius.Host/Scripting/GpuTimingSummary.cs` | 77/77 | 62/66 | 93/93 | 70/74 |

There are no uncovered changed executable lines. The six uncovered changed branch outcomes are these defensive fallbacks:

| Exact source line | Uncovered outcome | Disposition |
|---|---|---|
| `src/Tertius.Host/ScriptCommand.cs:663` | Missing successful value after a successful range summary | Judgment call: `Runs` returns a list or a problem, never a default result. Public Host behavior cannot produce this fallback. |
| `src/Tertius.Host/ScriptCommand.cs:808` | Missing option value in the non-range arm, whose expression continues at line 811 | Judgment call: parsing stores a nonempty value list before `OptionProblem` visits a present value-bearing option. No accepted command line can produce this fallback. |
| `src/Tertius.Host/Scripting/GpuTimingSummary.cs:123` | Missing parsed endpoints after no syntax problem | Judgment call: `ReadRun` returns endpoints or a problem, never a default result. |
| `src/Tertius.Host/Scripting/GpuTimingSummary.cs:127` | Missing range statistics after no range problem | Judgment call: `SumRun` returns statistics or a problem, never a default result. |
| `src/Tertius.Host/Scripting/GpuTimingSummary.cs:140` | Missing first-endpoint samples after no endpoint problem | Judgment call: `EndpointSamples` returns samples or a problem, never a default result. |
| `src/Tertius.Host/Scripting/GpuTimingSummary.cs:141` | Missing last-endpoint samples after no endpoint problem | Judgment call: the same return contract applies to the last endpoint. |

The remaining uncovered whole-file lines are unchanged from the before report. In `ScriptCommand.cs`, lines 243 and 299 are impossible empty parsed-union arms. Lines 279-288 and 603-612 enter a real window, outside this headless assignment. Line 588 is the unchanged unexpected-engine-failure catch-all. Lines 636-638 reject an unknown capture-target case, which the closed target union cannot supply. Lines 725-727 reject an exposure/capture count mismatch, outside the timing-report behavior. These 29 unchanged lines remain outside this ticket's measured changes. Their exact line lists and all whole-file partial branches remain in `coverage-before/file-details.json` and `coverage-after/file-details.json`.

This coverage measures actual Host C# execution, including parsing, headless playback, report calculation, JSON serialization and console output. It does not measure Python, shell or shader execution. Python and shell behavior has separate executed fault checks below.

### Deliberate mutation ledger

`ledger.json` is the single deliberate fault ledger. `prepare-ledger.py` derives `host-mutants.json` for the required runner. The final Host run uses `tools/mutate.py` inside the Nix shell and GPU lock, with 24 faults. The separate `tool-faults.py` runs the existing tool tests against isolated copies of the real Python and shell files, with 14 faults. It compiles changed Python copies, requires a passing baseline, bounds each run to 120 seconds, retains failure logs and makes sure that checkout source hashes stay unchanged. It never edits a production file in this checkout.

The exact inner commands, executed sequentially under Nix, are:

```sh
python3 -I tools/mutate.py /private/tmp/tertius-hardening-20261105/ter-l113/host-mutants.json /private/tmp/tertius-hardening-20261105/ter-l113/host-mutation-final
python3 -I /private/tmp/tertius-hardening-20261105/ter-l113/tool-faults.py /private/tmp/tertius-hardening-20261105/ter-l113/tool-mutation-final
```

`mutation-final.command.txt` records the outer Nix/GPU-lock context and the other commands in that sequence. `host-mutation-final/results.json`, `tool-mutation-final/results.json` and `mutation-summary.json` retain detailed outcomes. The final score is 38/38 killed: 24/24 Host and 14/14 Python/shell. There are zero surviving, no-coverage, timeout, build-failed or no-tests results in the final ledger. This is a deliberate fault population, not a claim that an exhaustive automatic mutator generated no other faults.

The first pass killed 33/37 compiling faults. H07, H11, H16 and S02 survived. Each survivor now has behavioral evidence, not an equivalence waiver:

| Survivor | Missing behavior protected | Red evidence and final disposition |
|---|---|---|
| H07 | A stale older frame cannot change range statistics | `AnOlderFinishedFrameCannotChangeTheRecordedRangeStatistics` fails on H07 with three frames and a 105 ms mean instead of two frames and 7.5 ms. Killed in `host-mutation-red` and `host-mutation-final`. |
| H11 | An endpoint first seen later names the earlier missing frame | `AnEndpointFirstRecordedInALaterFrameReportsTheEarlierMissingFrame` fails on H11 for each endpoint because the error identifies frame 9 instead of frame 7. Killed in both Host reruns. |
| H16 | Both endpoint names match recorded case exactly | `AGpuRunEndpointMustMatchTheRecordedPassNameCase` fails on H16 for `Bloom` and `Capture`. Killed in both Host reruns. |
| S02 | A legacy report without `warning` produces no traceback | The existing legacy-wrapper test now asserts empty standard error. It fails on S02 even though the shell continues and exits zero. Killed in `tool-mutation-red` and `tool-mutation-final`. |

The initial H22 attempt did not compile because discarding `runs` triggered IDE0060 for an unused parameter. It was not counted as killed. H22 was rewritten to pass `runs.Take(0).ToArray()`, which keeps the parameter used while removing all requested ranges. That valid fault is killed by report-output and error tests. Both attempts and the original ledger are preserved in `host-mutation-before`, `host-mutation-red`, `ledger.initial.json` and `host-mutants.initial.json`.

The official scope command was `nix develop --command python3 -I tools/stryker/changed.py f703caf174af14d5350e410ce2b26acfcc42e5fe /private/tmp/tertius-hardening-20261105/ter-l113/stryker-scope`. It reports no changed C# lines in Core, Simulation or Physics. Therefore no official Stryker host or assembly mutation ran. No NOT VALID, Dubious test or infrastructure timeout is counted as evidence. The Host runner's complete restores and the isolated tool copies leave all five owned production files byte-for-byte unchanged from `79fac63e9`.

The following table gives exactly one final result per deliberate fault. The historical run tables remain in the artifact logs.
| id | place | represented fault | final result | first failing behavioral test | seconds |
|---|---|---|---|---|---:|
| H01 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:149` | Exclude the first endpoint | killed | `GpuTimingSummaryTests.AGpuRunCountsEachFinishedFrameOnceAfterTheRendererRewritesItsStorage` | 4.009 |
| H02 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:150` | Exclude the last endpoint | killed | `GpuTimingReportTests.JsonPreservesRawPassAndFrameFieldsAlongsideRunStatisticsAndDriverValidity(stampsLatchLate: True)` | 3.920 |
| H03 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:152` | Collapse all frame sums into the first frame | killed | `GpuTimingReportTests.JsonPreservesRawPassAndFrameFieldsAlongsideRunStatisticsAndDriverValidity(stampsLatchLate: True)` | 3.975 |
| H04 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:153` | Add endpoint medians rather than summarizing frame sums | killed | `GpuTimingSummaryTests.GpuRunMediansSummarizePerFrameSumsRatherThanSumPassMedians` | 6.211 |
| H05 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:153` | Add endpoint p95 values rather than summarizing frame sums | killed | `GpuTimingSummaryTests.AGpuRunUsesEachFramesActualPassOrderEvenWhenAnInteriorPassIsSkipped` | 3.826 |
| H06 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:50` | Count duplicate frames | killed | `GpuTimingSummaryTests.AFrameThatSeveralRendersReportCountsOnce` | 5.983 |
| H07 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:50` | Accept a stale frame older than the last finished frame | killed | `GpuTimingSummaryTests.AnOlderFinishedFrameCannotChangeTheRecordedRangeStatistics` | 3.682 |
| H08 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:150` | Use the first frame pass positions when an interior pass is skipped | killed | `GpuTimingSummaryTests.AGpuRunUsesEachFramesActualPassOrderEvenWhenAnInteriorPassIsSkipped` | 3.874 |
| H09 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:57` | Discard empty recorded endpoint passes | killed | `GpuTimingSummaryTests.AnEmptyRecordedPassIsAnEndpointNotAMissingPass` | 3.805 |
| H10 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:116` | Lose the no-timings explanation for requested ranges | killed | `GpuTimingSummaryTests.RequestedGpuRunsNeedRecordedFrames` | 3.669 |
| H11 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:161` | Misidentify the missing frame when an endpoint first appears later | killed | `GpuTimingSummaryTests.AnEndpointFirstRecordedInALaterFrameReportsTheEarlierMissingFrame(missing: "bloom")` | 3.800 |
| H12 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:103` | Accept overlapping range separators | killed | `ScriptCommandOptionsTests.AMalformedGpuRunExplainsTheInclusiveRangeSyntax(run: "bloom...capture")` | 3.820 |
| H13 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:103` | Accept an all-whitespace endpoint | killed | `ScriptCommandOptionsTests.AMalformedGpuRunExplainsTheInclusiveRangeSyntax(run: " ..capture")` | 3.637 |
| H14 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:106` | Stop trimming outer endpoint whitespace | killed | `GpuTimingSummaryTests.AGpuRunTrimsOnlyOuterEndpointWhitespace` | 3.594 |
| H15 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:143` | Reject a single-pass range | killed | `GpuTimingSummaryTests.SeveralGpuRunsKeepTheirRequestedOrderAndMayShareEndpoints` | 3.741 |
| H16 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:158` | Accept a range endpoint with the wrong case | killed | `GpuTimingSummaryTests.AGpuRunEndpointMustMatchTheRecordedPassNameCase(run: "bloom..Capture", missing: "Capture")` | 3.658 |
| H17 | `src/Tertius.Host/Scripting/GpuTimingSummary.cs:118` | Reverse caller-selected range order | killed | `GpuTimingSummaryTests.SeveralGpuRunsKeepTheirRequestedOrderAndMayShareEndpoints` | 3.748 |
| H18 | `src/Tertius.Host/ScriptCommand.cs:809` | Validate only the last repeated range option | killed | `ScriptCommandOptionsTests.AMalformedGpuRunIsNotHiddenByALaterValidGpuRun` | 3.796 |
| H19 | `src/Tertius.Host/ScriptCommand.cs:910` | Discard requested ranges after CLI validation | killed | `ScriptCommandOptionsTests.AHeadlessRunRetainsEveryRequestedGpuRunInCommandLineOrder` | 3.833 |
| H20 | `src/Tertius.Host/ScriptCommand.cs:684` | Print misleading individual passes on a late-stamp driver | killed | `GpuTimingReportTests.TheConsoleShowsRequestedRunsAndShowsIndividualPassesOnlyWhenTheirTimesAreValid(stampsLatchLate: True)` | 3.677 |
| H21 | `src/Tertius.Host/ScriptCommand.cs:762` | Lose late-stamp validity metadata in the JSON report | killed | `GpuTimingReportTests.JsonPreservesRawPassAndFrameFieldsAlongsideRunStatisticsAndDriverValidity(stampsLatchLate: True)` | 3.799 |
| H22 | `src/Tertius.Host/ScriptCommand.cs:657` | Ignore requested range errors and sums when writing the report | killed | `GpuTimingReportTests.JsonPreservesRawPassAndFrameFieldsAlongsideRunStatisticsAndDriverValidity(stampsLatchLate: True)` | 3.692 |
| H23 | `src/Tertius.Host/ScriptCommand.cs:402` | Succeed silently when requested ranges have no rendered frames | killed | `ScriptCommandCaptureTests.GpuRunsOnAScriptWithoutRenderedFramesExplainWhyNoTimingsExist` | 10.031 |
| H24 | `src/Tertius.Host/ScriptCommand.cs:454` | Lose the range request between headless playback and report writing | killed | `ScriptCommandCaptureTests.AnInvalidGpuRunIsACommandLineErrorRatherThanAnInternalFailure(run: "capture..bloom", problem: "is reversed")` | 9.664 |
| P01 | `tools/gpu_clock.py:232` | Print misleading individual rows for mixed drivers | killed | `test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons (__main__.TimingsComparison.test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons)` | 1.861 |
| P02 | `tools/gpu_clock.py:233` | Ignore a warning when the driver flag is absent | killed | `test_a_report_warning_without_a_driver_flag_still_prevents_individual_pass_comparisons (__main__.TimingsComparison.test_a_report_warning_without_a_driver_flag_still_prevents_individual_pass_comparisons)` | 1.843 |
| P03 | `tools/gpu_clock.py:233` | Ignore a late-stamp flag when the warning is absent | killed | `test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons (__main__.TimingsComparison.test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons)` | 1.874 |
| P04 | `tools/gpu_clock.py:252` | Suppress ordinary and legacy pass comparisons instead of invalid ones | killed | `test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons (__main__.TimingsComparison.test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons)` | 1.881 |
| P05 | `tools/gpu_clock.py:239` | Discard original warning explanations from the comparison | killed | `test_a_late_driver_in_either_report_hides_all_individual_pass_comparisons (__main__.TimingsComparison.test_a_late_driver_in_either_report_hides_all_individual_pass_comparisons) (reference_late=True, other_late=True)` | 1.867 |
| P06 | `tools/gpu_clock.py:257` | Require new range metadata in a legacy reference report | killed | `test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons (__main__.TimingsComparison.test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons)` | 1.865 |
| P07 | `tools/gpu_clock.py:258` | Require new range metadata in a legacy comparison report | killed | `test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons (__main__.TimingsComparison.test_a_late_driver_flag_without_a_warning_still_explains_omitted_pass_comparisons)` | 1.888 |
| P08 | `tools/gpu_clock.py:257` | Ignore the reference per-frame range means | killed | `test_requested_ranges_use_the_reported_per_frame_aggregate_means (__main__.TimingsComparison.test_requested_ranges_use_the_reported_per_frame_aggregate_means) (stamps_latch_late=False)` | 1.861 |
| P09 | `tools/gpu_clock.py:258` | Ignore the comparison per-frame range means | killed | `test_a_requested_range_can_be_compared_with_a_legacy_report_without_ranges (__main__.TimingsComparison.test_a_requested_range_can_be_compared_with_a_legacy_report_without_ranges)` | 1.846 |
| P10 | `tools/gpu_clock.py:261` | Match ranges by only their first endpoint | killed | `test_a_requested_range_can_be_compared_with_a_legacy_report_without_ranges (__main__.TimingsComparison.test_a_requested_range_can_be_compared_with_a_legacy_report_without_ranges)` | 1.878 |
| P11 | `tools/gpu_clock.py:261` | Invent a zero for a range missing from the reference report | killed | `test_a_requested_range_can_be_compared_with_a_legacy_report_without_ranges (__main__.TimingsComparison.test_a_requested_range_can_be_compared_with_a_legacy_report_without_ranges)` | 1.826 |
| S01 | `tools/demo-timing.sh:45` | Drop the MoltenVK warning forwarded to the terminal | killed | `test_a_late_driver_explanation_reaches_the_terminal_with_or_without_requested_ranges (__main__.DemoTiming.test_a_late_driver_explanation_reaches_the_terminal_with_or_without_requested_ranges) (requested_ranges=False)` | 1.854 |
| S02 | `tools/demo-timing.sh:43` | Reject legacy timing reports without a warning field | killed | `test_a_legacy_report_without_validity_fields_keeps_its_individual_pass_output (__main__.DemoTiming.test_a_legacy_report_without_validity_fields_keeps_its_individual_pass_output)` | 1.865 |
| S03 | `tools/demo-timing.sh:47` | Lose requested range and ordinary pass output from the wrapper | killed | `test_a_late_driver_explanation_reaches_the_terminal_with_or_without_requested_ranges (__main__.DemoTiming.test_a_late_driver_explanation_reaches_the_terminal_with_or_without_requested_ranges) (requested_ranges=False)` | 1.851 |
38 rows: killed 38, timeout 0, survived 0, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0.

### Complete validation of the hardening source commit

`source-validation/result.txt` records `passed` for `03f0f6132a738ff0e89104142e153b3e6c96dde0`. Both complete solution builds include `Tertius.Benchmarks` and finish with zero warnings and errors. The suites use `dotnet test --solution Tertius.slnx -c <configuration> --no-build` after their corresponding builds, through the GPU lock.

| Configuration | Complete solution tests passed | Skipped | Failed | Focused timing/CLI/capture tests passed |
|---|---:|---:|---:|---:|
| Checked | 10,267 | 19 | 0 | 155, zero skips |
| Release | 10,254 | 19 | 0 | 155, zero skips |

The skips remain the same 18 opt-in measurements and one individual-pass timing test that does not apply to MoltenVK. All eight test projects run in each complete solution suite. The Host project passes 2,024 tests with five existing measurement skips in both configurations.

All seven existing Python tool suites pass, with 344 tests and zero failures or skips:

| Python test file | Passed |
|---|---:|
| `tools/test_fsr3_timing.py` | 10 |
| `tools/test_gpu_calibration.py` | 20 |
| `tools/test_gpu_clock.py` | 43 |
| `tools/test_gpu_lock.py` | 39 |
| `tools/test_gpu_trace.py` | 97 |
| `tools/test_mutate.py` | 50 |
| `tools/test_stryker.py` | 85 |

The mutation-runner tests deliberately terminate a child process. Their saved `Stopped: 15` traceback is expected fault-injection output, not a failed Python test. CSharpier passes all seven ticket-touched C# files. `bash -n tools/demo-timing.sh`, `git diff --check` and the staged whitespace check pass. No formatter is added or applied to unrelated files.

### Final commit identity and reproducible commands

`validate.sh` runs complete solution builds and suites, all seven existing Python tool test files, focused timing tests in both configurations, CSharpier, shell syntax and whitespace checks. Each command gets its own log and command record. It also makes sure that all five owned production files match `79fac63e9a2a5aeaca257d5a1d61e9113515ab5a` exactly.

`source-validation/tested-head.txt` records the full source-validation commit, `03f0f6132a738ff0e89104142e153b3e6c96dde0`. The final evidence-only commit repeats this validation after this note is committed. Its exact full SHA is recorded from `git rev-parse HEAD` in `final-validation/tested-head.txt`, and its starting worktree status is in `final-validation/working-status.txt`. This records the final commit tested without trying to embed a commit's own hash inside its contents. `final-validation/result.txt` must read `passed` before local hardening is ready.

The final command is:

```sh
nix develop --command bash /private/tmp/tertius-hardening-20261105/ter-l113/validate.sh final-validation
```

### Limits and preserved evidence

- The real MoltenVK and KosmicKrisp wrapper smoke evidence in `/tmp/ter-l113-logs/fix` is reused because production code is unchanged. Both reports preserve two frames and 29 raw passes. MoltenVK shows no individual console rows and forwards the original warning. KosmicKrisp shows 29 individual rows. Both show the requested range. The saved mixed-driver comparison retains only the frame and range rows.
- KosmicKrisp has the known timestamp defect `ter-dgen`. Its smoke run proves reporting compatibility, not timing accuracy. No Linux GPU timing or portability measurement is claimed. The optional `gpu-clock` binary remains absent, and the smoke reports correctly identify unknown clocks. No new rendered visual or screenshot reference was approved.
- The first coverage launch omitted the GPU-lock helper's log argument and exited 2 before any test ran. `coverage-launch-error.log` preserves that failed invocation. It is not coverage evidence. The corrected before run and both after runs passed completely. There was no unresolved infrastructure timeout or flaky test.
- The new tests use fixed inputs and no wall-clock assertions. A soak run and an MTBF estimate do not describe this finite report calculation. No new background service, lifecycle or concurrency mechanism was added.
- Artifacts stay outside the checkout. `source-validation/disk.txt` and `final-validation/disk.txt` retain free-space evidence. No artifact belonging to other work was removed. Parent notes, original commits, renderer probes and measurements remain preserved. Integration, ticket closure and user decisions remain with the parent.
