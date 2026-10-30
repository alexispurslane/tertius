---
id: ter-dyss
status: closed
deps: [ter-tf17]
links: []
created: 2026-10-27T17:20:51Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
---
# Type cohesion and depth

Design: `docs/plans/code-quality-tooling.md`, decision 22; exact counting rules in `tools/README.md`, "Type coherence and source evidence". The accepted user design supersedes the earlier four-score proposal and subsequent method-group proposal.

## Accepted scope

- TCC is the sole cohesion metric: pooled shared direct instance-field method pairs divided by pooled possible visible ordinary instance-method pairs. Constructors are excluded.
- Depth is pooled WMC divided by pooled API entries over the same measured type population. WMC sums existing Function.Complexity, including private/static functions, with nested types separate. API entries are declared public/internal/protected variants of methods, constructors, operators, properties, indexers, events and fields, once per member/overload, not per accessor. Inherited/synthesized members are excluded. Zero denominators retain raw counts and mean not applicable.
- Depth and erosion remain separate. More branching increases WMC and can increase depth without improving design; erosion independently reports excessive function complexity. No score is a validated quality failure or automatic refactoring verdict.
- Preserve named type/member/function evidence and locations, direct shared fields/pairs, raw WMC/API counts and deltas. Aggregate and detail share one implementation. No later class-specific hook finding lists.
- Integrate measurement, staged report, notes, CSV, push summary and resumable/idempotent backfill. Keep old metrics/metadata and unknown top-level JSON. Enrich every valid merged note line and prefer the most complete line. Preserve incompatible summaries rather than mixing metadata profiles.
- Remove dependency scoring, threshold-based interface burden, standalone exposure scores and method-group machinery. No arbitrary limits, dependency scores, calibration defaults or engine changes.

## Implementation

Implementation and parent review are complete on `ter-dyss-class-metrics`. No design choices remain open for the accepted TCC/depth scope. Type population, member visibility, field/property counting, body ownership and historical limitations are documented.

The source compilation binds handwritten snapshot source against platform metadata and optional explicit third-party metadata. It never builds historical projects or discovers current engine outputs. Partial declarations merge, nested types own their functions, extension blocks belong to their containing types, and enum/delegate evidence is retained without inventing generated functions. Counting version `type-metrics-v2` and metadata content fingerprints control comparisons. Binding errors remain visible in summaries/details.

## Delegate verification

- Full Slop Checked: 495 passed, zero failed/skipped.
- Full Slop Release: 495 passed, zero failed/skipped.
- Python commit report: 6 passed.
- Red-green tests first demonstrated missing depth/API JSON, unwanted method-group report output, missing partial-depth enrichment and a no-change backfill incorrectly reported as updated. Earlier tests demonstrated absent coherence evidence, wrong inherited-state eligibility, missing enum/delegate evidence and `nameof(field)` incorrectly counted as runtime state access. Every failure was corrected and the complete suites rerun.
- Final C# hand mutations: 15/15 killed. Cases: zero denominator as zero; maximum instead of pooled TCC denominator; dropping zero-API types from WMC; maximum instead of pooled API total; function count instead of existing CC; dropping fields; dropping internal members; including private API; dropping properties; counting `nameof`; including constructors in TCC; omitting depth enrichment; ignoring depth completeness; rewriting an unchanged backfill; comparing incompatible metadata.
- Final Python hand mutations: 4/4 killed. Cases: comparing incompatible metadata, showing zero for not-applicable ratios, erasing raw count changes, and showing missing depth as not applicable. All mutated files were restored and touched before the final full runs.
- Earlier retained coherence/history behavior also has successful hand mutations in `obj/revised-mutations`; the three obsolete method-group mutations and the abandoned four-score run are not acceptance evidence for this scope.
- Logs: `/tmp/ter-dyss-final-{checked,release,python,mutations}.log`, `/tmp/ter-dyss-depth-*.log`, `/tmp/ter-dyss-backfill-incompatible-red.log`; final mutation mappings in ignored `tools/Tertius.Slop/obj/final-mutations/`.

Initial delegate measurement over unchanged engine source from 0eb95afa, using the parent's explicit external metadata manifest: 974 types, 1,887/10,179 shared/possible pairs (TCC 0.1853816681), 10,122 WMC / 4,771 API entries (depth 2.1215678055), 3,994 function measures. The full `all` report took 10.071 seconds. Its 764 binding errors reflect intentionally absent generators and combined-source limitations, not a successful historical build. Erosion, verbosity and their existing raw counts exactly match the earlier report over this source. Artifact: ignored `tools/Tertius.Slop/obj/calibration/final-production.json`. No obsolete threshold calibration is used.

The delegate left wider-suite testing, changed-code coverage and integration to the parent. The delegate performed no production backfill, merge, push, ticket closure or engine change. Normal commit hooks remain enabled. Parent-authored ter-c8lk, ter-gnwg and ter-c4eb are included unchanged as independent future engine tasks. Preexisting untracked `.DS_Store` files and `nohup.out` are preserved.

## Commands

Read-only measurement (prints a note; does not attach it):

```sh
nix develop --command dotnet run --project tools/Tertius.Slop -- measure HEAD --branch ter-dyss-class-metrics --source seed --details /tmp/slop-details.json --parent HEAD
```

Backfill with the same default metadata configuration as the hooks:

```sh
nix develop --command dotnet run --project tools/Tertius.Slop -- backfill
```

## Parent review

- Correctness: fixed an initializer receiver defect. Assignments such as `new A { x = 1 }` and `this with { x = 1 }` affected another instance but counted as current-instance field use. Three regression cases failed with one false shared pair before the fix. Roslyn field-reference operations now identify the receiver. Version 2 prevents comparisons against the earlier counting rule.
- Ticket fulfillment: pooled ratios and absolute counts reach measurement, hooks, notes, CSV and push summaries. Named evidence uses the same type rows as the totals. Existing tests cover merged notes, unknown metadata, missing depth, incompatible references and repeated backfill. Production backfill and publication are recorded below when complete.
- Design: pass under the software-design skill. ClassAnalysis owns symbol binding and receiver identity. ClassMeasure derives weight and surface counts from the evidence. Existing SlopReport and history paths carry the new summary. No separate score, threshold or engine state was added.

The parent compared default platform references, explicit package references, and an experimental union-generator run. All three measured 974 types and depth 10,122/4,771. Both source configurations measured TCC 1,887/10,179 and identical direct field sets. They reported 3,344 and 764 binding errors respectively. The generator run reported seven errors and added ten possible pairs from generated state in GravityField, MediumField, MotionIntent, OwnSurface and QueryFilter. It added no shared pairs. These experiments do not reproduce historical builds. The hook and backfill use default platform references. Generated state remains outside the agreed source scope.

## Parent verification

- Full solution: Checked and Release passed across all eight modules, with 14 existing skipped tests per configuration. After the initializer fix, Slop passed 507 tests in Checked and 507 in Release. Python reporting passed all six tests.
- Coverage: all 417 changed executable C# lines were covered. The changed lines reported 287/306 branch outcomes. ClassAnalysis covered 194/194 lines and 138/142 branches. ClassMetricOptions improved from 31/39 lines and 8/18 branches to 39/39 and 17/18. ClassMetrics improved from 81/82 lines and 39/42 branches to 82/82 and 40/42. Uncovered branch outcomes include the unavailable runtime-directory guard, malformed syntax without a declared symbol, and combinations of legacy optional values. Existing tests cover unavailable metrics and mismatched configuration. No changed executable line remains untested.
- Nine additional hand mutations were killed, with no survivor or timeout: incorrect initializer receivers, ignored explicit references, ignored environment references, successful exit on invalid metadata, missing argument guidance, skipped unreadable notes, accepted unknown note sources, hidden binding warnings, and serialization of an unchanged incompatible note. One initial environment mutation failed compilation because its bound variable became unused. It was replaced with a compilable mutation that discarded the loaded references, and the behavior test killed it. Compile failures do not count as kills.
- Logs: `/tmp/ter-dyss-parent-tests`, `/tmp/ter-dyss-parent-slop-{checked,release}.log`, `/tmp/ter-dyss-parent-python.log`, `/tmp/ter-dyss-initializer-red.log`, `/tmp/ter-dyss-mutation-*.log`. Coverage: `/tmp/ter-dyss-coverage-final/Slop.cobertura.xml` and `/tmp/ter-dyss-coverage-summary.json`.

## Historical migration

The preservation audit found two experimental `type-coherence-v1` notes with a retired `methodGroupCount` field and no depth. The first isolated backfill rewrote those unchanged summaries and dropped that nested field. No batch result reached the original notes before the audit. A regression test reproduced the loss. Backfill now retains the exact original line when enrichment changes no values. The new hand mutation removed that guard and failed the regression test.

The migration enriched 498 legacy notes. For the two incompatible experimental notes, it kept each original line intact and appended a separately measured version-2 summary. The added line keeps all original erosion/verbosity values, absolute counts, branch, source and measurement time. Readers select its complete summary. No incompatible counts were combined. The initial version-1 implementation note already had complete counts and remains unchanged.

All 500 original note targets passed a comparison against `/tmp/tertius-ter-dyss-notes-before.json`. Every original JSON value and text line was preserved. At verification, 502 targets held 504 readable lines, and every target had a complete summary. The reference fingerprint matched the default hook configuration throughout. Local backup refs retain the notes before migration and the isolated batch results. Artifacts: `/tmp/ter-dyss-notes-verified.json`, `/tmp/ter-dyss-experimental-migration.log`, and `/tmp/ter-dyss-preservation-{red,mutation}.log`.

The repeated production backfill updated zero notes out of 502 examined and left `refs/notes/slop` unchanged. The CSV selected the complete version-2 line for both experimental commits. Final Slop Checked and Release runs each passed 507 tests. The normal commit hook also passed all 507 tests. Implementation through `cb205e01` was merged into main before this ticket was closed.
