# C# reference measurements with properties, helpers and applicability

This study remeasures the [same pinned source populations](../type-metric-reference-2026-10-30/README.md)
with `type-metrics-v3` for [ter-6na6](../../../.tickets/ter-6na6.md).
No external source bytes changed. The Tertius engine source also matches the
earlier snapshot. These are changes to measurement, not improvements to the
measured projects. The v2 dataset remains intact.

## New counting rules

TCC (tight class cohesion) now includes fields reached through properties and
resolvable helpers on the same instance. It remains a fraction of eligible
method pairs sharing storage. Only complete, applicable types enter the
project ratio. Private helpers and accessors do not add denominator methods.
See the [counting rules](../../../tools/README.md#type-coherence-and-source-evidence-ter-dyss).

Depth is weighted method count (WMC), the sum of handwritten function
cyclomatic complexity, divided by declared API entries. A declared API entry
is a member accessible outside its type, including internal/protected members.
Types without handwritten functions have no implementation depth. They retain
raw API counts but add no denominator entries to the project ratio. An
implementation-bearing type with no API entries still contributes WMC.
The WMC and API-entry definitions themselves are unchanged.

## Cohesion and the measured population

Pair coverage below is included possible pairs divided by included plus
incomplete possible pairs. It excludes structurally not-applicable types.
It measures the retained pair population, not confidence or code quality.
Incomplete possible pairs describe declarations whose state access remains
unresolved; their eligibility can itself depend on unavailable storage.

| Project | TCC: shared/possible pairs | TCC | Types: included / n/a / incomplete | Pair coverage |
|---|---:|---:|---:|---:|
| [Dapper](dapper.json) | 769/790 | 0.9734 | 9 / 45 / 7 | 50.54% |
| [Serilog](serilog.json) | 3,244/3,365 | 0.9640 | 25 / 71 / 5 | 95.38% |
| [Noda Time](nodatime.json) | 1,393/3,428 | 0.4064 | 80 / 169 / 10 | 93.15% |
| [Newtonsoft.Json](newtonsoft.json) | 259/333 | 0.7778 | 34 / 183 / 27 | 3.70% |
| [NUnit](nunit.json) | 1,801/1,951 | 0.9231 | 85 / 360 / 36 | 54.18% |
| [Autofac](autofac.json) | 573/869 | 0.6594 | 45 / 175 / 12 | 77.73% |
| [Tertius](tertius.json) | 7,525/8,487 | 0.8867 | 186 / 752 / 36 | 83.21% |

Newtonsoft.Json's value represents only 3.70% of the included-plus-incomplete
possible pairs. It is a measurement of a small subset, not a project-wide
quality estimate. Dapper and NUnit also exclude substantial pair populations.
The earlier missing-build-symbol limitations remain, especially for NUnit and
Newtonsoft.Json. Even zero compiler errors do not resolve unavailable external
method bodies or runtime-selected implementations.

## Implementation depth

The excluded API column records declaration-only surface removed from pooling.
Those entries remain in the detailed type evidence. No extra implementation
weight was introduced to make data-only types look deeper.

| Project | Depth: WMC/API entries | Depth | Types: applicable / n/a | Excluded declaration API entries |
|---|---:|---:|---:|---:|
| Dapper | 1,526/469 | 3.2537 | 47 / 14 | 37 |
| Serilog | 1,217/584 | 2.0839 | 84 / 17 | 106 |
| Noda Time | 3,332/1,976 | 1.6862 | 202 / 57 | 285 |
| Newtonsoft.Json | 6,074/1,941 | 3.1293 | 165 / 79 | 347 |
| NUnit | 4,553/2,438 | 1.8675 | 398 / 83 | 276 |
| Autofac | 2,006/994 | 2.0181 | 175 / 57 | 236 |
| Tertius | 10,122/3,894 | 2.5994 | 481 / 493 | 877 |

Per-type n/a includes private-only implementation with no API denominator.
Its WMC can still contribute to the project numerator. Therefore the number of
applicable depth ratios is not the number of types supplying numerator weight.

## Numerical context

The six selected external TCC values range from `0.4064` to `0.9734`, with a median of `0.8504`. The six selected external depth values range from `1.6862` to `3.2537` complexity units per API entry, with a median of `2.0510`. Each project has equal weight in these medians. This is descriptive
context only: the complete subsets differ, and the projects were selected
deliberately. Do not use these ranges as good/bad thresholds. Keep incomplete
populations beside the ratios when comparing projects or commits.

## Reviewed examples

| Type | v2 direct TCC | v3 TCC | Interpretation |
|---|---:|---:|---|
| Tertius `DPose` | 0/21 = 0 | 21/21 = 1 | Following properties exposes the shared transform storage. |
| Tertius `TopicSets` | 33/55 = 0.6000 | 51/55 = 0.9273 | Helper traversal connects more subscription/membership operations. |
| Tertius `EventList` | 28/36 = 0.7778 | 36/36 = 1 | Shared storage does not repair the ownership defect recorded in ter-c8lk. |
| Noda Time `LocalDate` | 0/300 = 0 | 36/300 = 0.1200 | Property traversal adds sharing, but same-instance traversal still does not describe every semantic relation in a value type. |
| Serilog `Logger` | 12/3160 = 0.0038 | 3079/3160 = 0.9744 | Helper calls expose the state used by convenience overloads. |
| Dapper `DbWrappedReader` | 741/741 = 1 | 741/741 = 1 | Repeated forwarding still produces maximum cohesion without establishing depth. |
| Dapper `GridReader` | 0/703 = 0 | Incomplete; 703 known shared pairs retained | Runtime dispatch or unavailable bodies keep the type outside the complete pool. |
| Newtonsoft.Json `TraceJsonWriter` | 1326/1378 = 0.9623 | Incomplete; 1326 known shared pairs retained | Its known forwarding evidence remains visible despite unresolved paths. |

Tertius `RendererOptions` has raw depth counts `0/20`. V3 reports n/a with reason
`no-handwritten-functions`, rather than a zero implementation-depth score.
Its 20 API entries are retained as evidence. Interface contracts similarly have
no own instance storage to measure; absent contract bodies do not create false
unresolved-storage warnings.

## Per-type distributions

Each applicable, complete type has one vote below. Undefined/incomplete ratios
are omitted. Defined zeros remain. Percentiles use linear interpolation at
zero-based position `(n - 1) * p`, where `n` is the applicable type count and `p`
is the percentile as a fraction. These distributions use the new population
and cannot be treated as a quality change from the v2 distributions.

| Project | Metric | Applicable types | P10 | P25 | P50 | P75 | P90 |
|---|---|---:|---:|---:|---:|---:|---:|
| Dapper | tcc | 9 | 0.1333 | 0.1667 | 0.7143 | 1.0000 | 1.0000 |
| Dapper | depth | 47 | 1.0000 | 1.0000 | 1.5000 | 3.4167 | 6.4000 |
| Serilog | tcc | 25 | 0.0800 | 0.3333 | 1.0000 | 1.0000 | 1.0000 |
| Serilog | depth | 84 | 1.0000 | 1.3333 | 2.0000 | 3.0278 | 8.4750 |
| Noda Time | tcc | 80 | 0.2272 | 0.4000 | 0.6594 | 1.0000 | 1.0000 |
| Noda Time | depth | 202 | 0.7500 | 1.0000 | 1.3560 | 2.2181 | 3.9917 |
| Newtonsoft.Json | tcc | 34 | 0.0000 | 0.7045 | 1.0000 | 1.0000 | 1.0000 |
| Newtonsoft.Json | depth | 165 | 0.4000 | 1.0000 | 1.6667 | 3.3333 | 7.4903 |
| NUnit | tcc | 85 | 0.0381 | 0.3333 | 1.0000 | 1.0000 | 1.0000 |
| NUnit | depth | 398 | 1.0000 | 1.0000 | 1.3750 | 2.5000 | 4.0000 |
| Autofac | tcc | 45 | 0.0000 | 0.3333 | 1.0000 | 1.0000 | 1.0000 |
| Autofac | depth | 175 | 0.5000 | 1.0000 | 1.7500 | 2.6667 | 3.9000 |
| Tertius | tcc | 186 | 0.0857 | 0.6910 | 1.0000 | 1.0000 | 1.0000 |
| Tertius | depth | 481 | 0.5000 | 0.9231 | 1.4444 | 2.5000 | 4.7143 |

## Reproduction and limitations

The counter implementation is `010e74714a89c7d8477e46d5a4cee5c2fa755afa`.
The original Release binary SHA256 is `ff61423eb08e62951ec11b56fd2a2276510e86d768a121daffae968958c00359`.
The metadata reference fingerprint remains
`6756909B5E79A0B8E806995EF6661640C81D1DB2BA5FCD39E4AA254CE606436B`.
The tool source had no working changes during measurement.

Each JSON file links its v2 `sourceDataset`, which contains the immutable
revision, archive URL/checksum and inclusion manifest for external projects.
Reconstruct that source input using the old report's instructions, build the
counter at the implementation commit above, and run `all` on the input inside
the Nix dev shell with `TERTIUS_SLOP_REFERENCE_LIST` unset. No historical build,
project symbols, generated sources or dependency assemblies are added.

The new files retain all per-type raw counts, applicability reasons, unresolved
source spans, quantiles, excluded populations, diagnostic counts and raw-report
hashes. Full raw reports remain in `/tmp/ter-6na6-reference-v3/` on the research
machine. All 1,204 external included source hashes were rechecked against the
v2 manifests. All pooled sums, population counts and type ratios were
independently recomputed from the raw reports.

The original compilation-error counts are unchanged. Unresolved state access
is a separate quantity: a valid virtual call can have unknown runtime dispatch,
and a compiler error elsewhere does not necessarily prevent measurement of a
type. Primary-constructor captures without available lowered storage symbols
remain incomplete. Local aliases of `this`, arbitrary delegate targets and
state passed as an argument to static helpers remain outside the traversal.
The counters do not establish whole-program semantic cohesion or code quality.
