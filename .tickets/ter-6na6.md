---
id: ter-6na6
status: in_progress
deps: []
links: []
created: 2026-10-30T21:51:41Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
---
# Count cohesion through properties and helpers and report metric applicability

Implement the selected property and same-instance helper traversal for cohesion. Distinguish applicable zero, not-applicable and incomplete measurements, including implementation-depth applicability. Version counting semantics, retain absolute evidence and historical compatibility, test by red-green TDD, and refresh the historical C# comparison with the new version.

## Implementation notes, 2026-10-30

The implementation sub-agent completed the tool changes on `ter-6na6-state-cohesion`. The ticket remains in progress. The parent owns reference recalibration, overall coverage, integration, review and merge. No engine code, `CLAUDE.md` or `docs/research/**` was changed. No branch switch, merge, push or real-history backfill was performed.

### Analysis and schema

- `InstanceStateAccess` supplies each method's known storage and unresolved source evidence. Automatic, field-backed and positional-record properties resolve to actual backing fields. Explicit properties use only executed accessors, including compound assignments, deconstruction targets and reference-returning getters.
- Source-available helpers, chains, recursive cycles, generic/partial methods, inherited implementations and invoked nonstatic local functions share this analysis. Private helpers and accessors do not widen the eligible pair population. Exact base/private/sealed dispatch is preserved. A sealed receiver resolves its real override even inside inherited helpers.
- Other receivers, initializer targets, `nameof`, unused local functions and unexecuted lambda bodies do not supply this instance's fields. Dependency bodies are not traversed. Evaluated arguments and dependency-field reads remain visible. User-defined conversions do not preserve receiver identity.
- Same-instance binding failures, unavailable source bodies, ambiguous virtual dispatch, dynamic member/indexer access, unavailable base storage and primary-constructor captures produce incomplete evidence. Private metadata fields are imported so that an external base's private storage cannot become an incorrect no-storage result.
- `ClassRatio.Applicability` carries optional `status` and `reason`. `ClassMeasure.TccApplicability` derives from that authority. Raw numerator and denominator remain in incomplete/not-applicable type rows, while their ratio is null. `DepthApplicability` is explicit, nullable legacy metadata. `UnresolvedStateAccess` retains file, one-based start/end lines, one-based start column, exclusive end column and reason. Distinct calls on one line and repeated unavailable-helper call sites remain distinct.
- Cohesion pools only complete applicable types. `CohesionPopulation` and `DepthPopulation` each expose `included`, `notApplicable` and `incomplete`. `UnresolvedStateAccessCount` counts distinct source spans/reasons. An all-incomplete cohesion pool displays `incomplete`, not zero or ordinary no-data.
- Depth excludes declaration-only surface. Constructor bodies and default interface implementation retain the existing WMC/API rules. Private-only implementation keeps its raw WMC in the pooled numerator despite per-type depth being not applicable.
- Provenance is `type-metrics-v3`. Notes, detail JSON, CSV and hook summaries carry the new metadata. Six CSV columns append population and unresolved-location counts. The pre-commit numeric addendum remains ratio-only, followed by limitations. Version/reference mismatches suppress deltas and prevent backfill replacement. V2 notes and full detail JSON remain readable with unavailable, not fabricated, applicability metadata. Same-version backfill fills only missing metadata and is idempotent.

### Red-green evidence

Tests were written against existing APIs before implementation. The initial state-access batch failed nine of ten assertions, showing missing property, helper and conditional-call access. The initial applicability batch failed all seven tests, including declaration-only depth dilution and incomplete types entering the pool. Subsequent red tests caught wrong sealed dispatch, setter/getter confusion, reference-returning properties, lost call locations, user-defined receiver conversion, explicit-null legacy metadata, missing v2 detail compatibility, private metadata storage, primary-constructor captures and positional-record storage. Each was rerun green after its implementation change.

There are 59 added .NET test cases and five added Python report cases. Red logs are retained outside the checkout under `/tmp/ter-6na6-red-*.log`.

### Final validation

All commands ran inside `nix develop`; builds ran sequentially. Only Slop test projects were built/tested.

| Command inside the dev shell | Passed | Failed | Skipped |
|---|---:|---:|---:|
| `dotnet test --project tests/Tertius.Slop.Tests -c Checked` | 567 | 0 | 0 |
| `dotnet test --project tests/Tertius.Slop.Tests -c Release` | 567 | 0 | 0 |
| `python3 tests/Tertius.Slop.Tests/commit_report_cases.py` | 19 | 0 | 0 |

CSharpier formatted only the assigned C# files. Full-run logs are `/tmp/ter-6na6-full-checked.log`, `/tmp/ter-6na6-full-release.log` and `/tmp/ter-6na6-python.log`. Overall coverage and engine integration remain parent-owned; neither coverage nor Stryker was launched here.

### Hand mutations

Each mutation changed real production code, built only the Slop test project and ran its named test class under `timeout 120`. Every mutation was restored through a patch, checked byte-for-byte and touched after restoration. All 17 mutations were caught by assertion failures. There were no survivors, build-error kills or timeouts. This is a scoped hand-mutation result, not an exhaustive mutation score.

| Deliberate fault | Test class | Failed cases |
|---|---|---:|
| Count other-instance fields as this instance's storage | `StateCohesionTests` | 1 |
| Stop following instance helpers | `StateCohesionTests` | 5 |
| Skip the getter during compound writes | `StateExecutionTests` | 2 |
| Drop automatic-property storage | `StateCohesionTests` | 1 |
| Walk unused local-function bodies | `StateExecutionTests` | 1 |
| Walk unexecuted lambda bodies | `StateCohesionTests` | 1 |
| Count `nameof` references as execution | `StateCohesionTests` | 1 |
| Suppress ambiguous-dispatch evidence | `IncompleteStateTests` | 2 |
| Keep unavailable helpers visited and lose later call locations | `IncompleteStateTests` | 1 |
| Include incomplete types in the cohesion pool | `MetricApplicabilityTests` | 4 |
| Include declaration-only surface in depth | `MetricApplicabilityTests` | 4 |
| Ignore provenance during backfill | `ApplicabilityHistoryTests` | 1 |
| Present an all-incomplete pool as ordinary n/a | `ApplicabilityHistoryTests` | 1 |
| Revert current counting provenance to v2 | `ApplicabilityHistoryTests` | 2 |
| Hide private metadata storage | `ClassMetricOptionsTests` | 1 |
| Add private helpers to eligible method pairs | `StateCohesionTests` | 5 |
| Exclude synthesized positional-record accessors | `StateCohesionTests` | 1 |

Mutation logs and results are `/tmp/ter-6na6-mutants/`, `/tmp/ter-6na6-mutations.log` and `/tmp/ter-6na6-record-mutation.log`.

### Review boundaries

- This remains a combined source compilation, not a project build. Project defines, source generation and assembly boundaries are not reconstructed. Global binding diagnostics remain visible alongside per-type incomplete evidence.
- The receiver analysis follows implicit/explicit `this`, `base`, identity-preserving reference conversions and conditional access. Local aliases of `this` and arbitrary delegate targets remain outside this analysis; there is no general dependency or alias traversal.
- Consumers must check nullable applicability metadata in legacy records. They must not pool raw type counts from not-applicable or incomplete rows. Private-only implementation still supplies pooled WMC even though its per-type depth is not applicable.
- No subjective role classifier, numerical threshold, retired score, engine behavior change or actual-note migration was introduced.

## Parent hardening

The initial coverage run passed all 567 tests. ClassAnalysis, ClassMetrics and the changed history code had 100% line coverage. InstanceStateAccess had 94.08284023668639% line coverage and 92.76315789473685% branch coverage.

Parent review added 12 edge cases. Four initial assertions failed for the intended reasons: interfaces with multiple contract methods appeared incomplete, and unresolved indexing or conditional calls on this could appear complete. A follow-up red assertion showed interface contract bodies still adding false unresolved-location warnings. Fixed both through the existing receiver/applicability paths. Added passing coverage for sealed explicit-interface dispatch, indexer getters/setters, missing required accessors and unresolved conditional calls on another receiver.

The resulting full Checked coverage run passed 579 tests. Before formatting, InstanceStateAccess line coverage was 95.40229885057471% and branch coverage 91.875%; ClassAnalysis was 100% lines and 98.83720930232558% branches. The remaining gaps are compiler recovery syntax and defensive checks for missing accessor associations/conditional parents. Standard Roslyn operation trees preserve those associations; malformed binding and missing accessor behavior have public regression tests. The associated-symbol and conditional-parent fallback comments explain their retention. Reproduction logs and XML reports are under /tmp/ter-6na6-coverage*. A final formatted-source report follows below.

Four additional valid hand mutations were caught by assertion failures: analyze interface contract bodies as executable state access; ignore unresolved self indexing; ignore unresolved conditional method binding; and discard a resolved explicit interface implementation. No valid mutant survived or timed out. One initial indexer mutation was rejected by IDE0059 for an unused pattern variable, so it was corrected and rerun; that build rejection is not counted as a kill. All source bytes were restored after each run. Logs: /tmp/ter-6na6-parent-mutation-*.log.


## Parent verification and reference refresh

Final formatted-source coverage passed all 579 Slop tests. ClassAnalysis has 100% line coverage and 98.83720930232558% branch coverage. InstanceStateAccess has 94.44444444444444% line coverage and 91.875% branch coverage. ClassMetricSummary has 100% line coverage and 95.65217391304348% branch coverage. The formatted report is `/tmp/ter-6na6-coverage-formatted.cobertura.xml`. Remaining uncovered InstanceStateAccess lines concern Roslyn syntax recovery or defensive missing-association/conditional-parent paths described above. Total valid hand mutations: 21, all caught by assertions.

Full integration ran through `nix develop --command bash tools/test-all.sh -j 3 -o /tmp/ter-6na6-integration` and exited zero. All eight modules passed in both configurations:

| Configuration | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Checked | 9440 | 0 | 14 |
| Release | 9427 | 0 | 14 |

The skips are existing platform/explicit filters: Physics 8, Rendering 1 and Host 5 per configuration. Slop passed all 579 cases in each configuration. The Python report suite passed all 19 cases during implementation.

Remeasured all six pinned external source populations and unchanged Tertius engine source with the Release counter at `b39a94c226906ddc7919f4ef12801e23236d52b9`. All 1,204 external included source hashes match their v2 manifests. Counting version is v3; the metadata fingerprint is unchanged. Independent arithmetic verified pooled counts, applicability populations, all type ratios and quantiles. Local document links, dataset links, source revisions, compiler-error totals and source-line totals also passed checks.

The durable results are `docs/research/type-metric-reference-v3/README.md` and seven JSON datasets. `CLAUDE.md` now explains v3, including zero versus not applicable versus incomplete, updated local examples and retained pair populations. Tertius measures TCC `7494/8441`, depth `10122/3894`, and cohesion population 176 included / 752 not applicable / 46 incomplete. Its pair coverage is 82.75%; Newtonsoft.Json retains only 3.70%, so its ratio is explicitly not a project-wide quality estimate. The higher ratios arise from changed counting rules, not engine refactoring. V2 datasets and existing notes remain intact.


## Independent review and fixes

The independent reviewer found three P2 receiver defects by inspection: property patterns lost same-instance storage; parentheses/conversions hid unresolved self access; and unresolved initializer targets contaminated the containing instance's evidence. Design quality passed. The parent reproduced all three findings with 12 intended assertion failures in an 18-case regression batch, then fixed them within the existing receiver analysis. Controls cover other receivers, nested property patterns, initializer values, field patterns and both switch forms. All 18 cases now pass; the full Slop suite has 597 cases.

Four additional valid hand mutations were caught by assertions: use the pattern instead of its matched input; treat a nested property's receiver as this; reject converted unresolved self receivers; and count unresolved initializer targets as this. One initial pattern mutation was rejected by IDE0059 for an unused variable and was corrected before rerunning; it is not counted as a kill. All production bytes were restored and touched. Total valid hand mutations: 25, no survivors or timeouts. Logs: `/tmp/ter-6na6-review-{red,green,mutation-*}.log`.

Coverage after the review fixes passed 597 cases: InstanceStateAccess has 95.38461538461539% line coverage and 94.88636363636364% branch coverage; ClassAnalysis remains 100% lines / 98.83720930232558% branches; ClassMetricSummary remains 100% lines / 95.65217391304348% branches. Uncovered receiver paths are the documented recovery/association fallbacks, whole conditional-access recovery and unsupported invalid syntax. The resolved and malformed public behaviors are covered. Report: `/tmp/ter-6na6-review-coverage.cobertura.xml`.
