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
