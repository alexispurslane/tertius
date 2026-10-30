---
id: ter-dyss
status: in_progress
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

## Implementation handoff

Implementation is complete on `ter-dyss-class-metrics`, ready for parent review. No design choices remain open for the accepted TCC/depth scope. Type population, member visibility, field/property counting, body ownership and historical limitations are documented.

The source compilation binds handwritten snapshot source against platform metadata and optional explicit third-party metadata. It never builds historical projects or discovers current engine outputs. Partial declarations merge, nested types own their functions, extension blocks belong to their containing types, and enum/delegate evidence is retained without inventing generated functions. Counting version `type-metrics-v1` and metadata content fingerprints control comparisons. Binding errors remain visible in summaries/details.

## Verification

- Full Slop Checked: 495 passed, zero failed/skipped.
- Full Slop Release: 495 passed, zero failed/skipped.
- Python commit report: 6 passed.
- Red-green tests first demonstrated missing depth/API JSON, unwanted method-group report output, missing partial-depth enrichment and a no-change backfill incorrectly reported as updated. Earlier tests demonstrated absent coherence evidence, wrong inherited-state eligibility, missing enum/delegate evidence and `nameof(field)` incorrectly counted as runtime state access. Every failure was corrected and the complete suites rerun.
- Final C# hand mutations: 15/15 killed. Cases: zero denominator as zero; maximum instead of pooled TCC denominator; dropping zero-API types from WMC; maximum instead of pooled API total; function count instead of existing CC; dropping fields; dropping internal members; including private API; dropping properties; counting `nameof`; including constructors in TCC; omitting depth enrichment; ignoring depth completeness; rewriting an unchanged backfill; comparing incompatible metadata.
- Final Python hand mutations: 4/4 killed. Cases: comparing incompatible metadata, showing zero for not-applicable ratios, erasing raw count changes, and showing missing depth as not applicable. All mutated files were restored and touched before the final full runs.
- Earlier retained coherence/history behavior also has successful hand mutations in `obj/revised-mutations`; the three obsolete method-group mutations and the abandoned four-score run are not acceptance evidence for this scope.
- Logs: `/tmp/ter-dyss-final-{checked,release,python,mutations}.log`, `/tmp/ter-dyss-depth-*.log`, `/tmp/ter-dyss-backfill-incompatible-red.log`; final mutation mappings in ignored `tools/Tertius.Slop/obj/final-mutations/`.

Final production measurement over unchanged engine source from 0eb95afa, using the parent's explicit external metadata manifest: 974 types, 1,887/10,179 shared/possible pairs (TCC 0.1853816681), 10,122 WMC / 4,771 API entries (depth 2.1215678055), 3,994 function measures. The full `all` report took 10.071 seconds. Its 764 binding errors reflect intentionally absent generators and combined-source limitations, not a successful historical build. Erosion, verbosity and their existing raw counts exactly match the earlier report over this source. Artifact: ignored `tools/Tertius.Slop/obj/calibration/final-production.json`. No obsolete threshold calibration is used.

Parent owns wider-suite testing, changed-code coverage, review and any later production backfill/merge/push. No production historical backfill, merge, push, ticket closure or engine change was performed. Normal commit hooks remain enabled. Parent-authored ter-c8lk, ter-gnwg and ter-c4eb are included unchanged as independent future engine tasks. Preexisting untracked `.DS_Store` files and `nohup.out` are preserved.

## Commands

Read-only measurement (prints a note; does not attach it):

```sh
nix develop --command dotnet run --project tools/Tertius.Slop -- measure HEAD --branch ter-dyss-class-metrics --source seed --details /tmp/slop-details.json --parent HEAD --reference-list /absolute/path/references.json
```

Parent-only future backfill, after review; use the same reviewed metadata profile for the historical series:

```sh
nix develop --command dotnet run --project tools/Tertius.Slop -- backfill --reference-list /absolute/path/references.json
```
