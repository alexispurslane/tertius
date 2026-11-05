---
id: ter-l113
status: in_progress
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
