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
