---
id: ter-1k3c
status: closed
deps: []
links: []
created: 2026-10-30T20:50:35Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
---
# Document how to interpret and calibrate project quality metrics

Add concise guidance to CLAUDE.md for erosion, verbosity, direct TCC and depth. Ground interpretation in the original erosion/verbosity source, current counting rules and representative type evidence. Explain structural anchors, limits of pooled ratios and a qualitative calibration procedure without inventing quality gates.

## Expanded empirical scope

User requested well-known C# projects from before generative-AI coding, measured by GPT-6.1 Sol agents at high reasoning effort. Use six selected mature libraries/frameworks: Noda Time, Newtonsoft.Json, Dapper, Serilog, NUnit and Autofac. Prefer stable release snapshots before 2021-01-01. Historical dates establish a pre-Copilot comparison period, not proof of authorship or perfect quality.

Three research agents each own two projects and separate /tmp output folders. They download pinned source archives, preserve production .cs files without edits under a normalized src directory, record inclusion/exclusion manifests, run the existing Slop binary with default metadata, and retain raw details and binding diagnostics. They do not modify this checkout. Compare pooled project ratios separately from distributions of per-type ratios. Report unavailable values and denominator sizes. Inspect diverse scored types and source-analysis limitations before drawing quality conclusions.

Documentation changes are drafted but remain uncommitted until the external comparison is integrated. Local baseline: bf5bf784, type-metrics-v2, 974 types, shared/possible pairs 1887/10179, WMC/API entries 10122/4771, 3344 binding errors with default runtime metadata. Measurement: /tmp/ter-1k3c-details.json.
## Completed evidence: Dapper and Serilog

Reviewed the completed data/logging report and raw JSON. Independently recomputed pooled sums and P10/P25/P50/P75/P90, checked diagnostic counts, and verified all 147 included source files against their byte-preservation manifests. Dapper 2.0.78: TCC 789/1549, depth 1526/506, 61 types. Serilog 2.10.0: TCC 58/3500, depth 1217/690, 101 types. Both have zero compilation errors under the common source-only configuration.

Interpretation evidence: Dapper's two reader types contribute 1444/1549 possible pairs. Serilog.Logger contributes 3160/3500. Direct field forwarding versus forwarding through helper methods changes TCC without establishing different quality. Dapper.BasicWrappedReader has depth 36/1 because explicit interface implementations add function weight but no API entries. This is a concrete reason to avoid calling high measured depth inherently good.

Artifacts: /tmp/ter-1k3c-calibration/data-logging/{report.md,result.json,artifact-index.json}.

## Completed evidence: Noda Time and Newtonsoft.Json

Independently checked pooled sums, all five per-type quantiles, diagnostic counts, archive hashes and all 404 included source file hashes. Noda Time 3.0.3: TCC 412/3674, depth 3332/2261, 259 types. Newtonsoft.Json 12.0.3: TCC 1809/8946, depth 6074/2288, 244 types. The counter and metadata fingerprints match the local reference and the first pair.

Noda Time's 379 errors concern excluded JetBrains annotations. Newtonsoft.Json has 43 errors, but its 548 conditional directives in 108 files are a stronger comparison limit: the no-symbol baseline is not a historical build target. LocalDate has zero direct TCC despite a coherent date value; TraceJsonWriter has near-maximum direct TCC through repeated delegation.

Durable evidence is saved in docs/research/type-metric-reference-2026-10-30/. It retains pinned archive metadata, C# inclusion/exclusion manifests, all per-type primitive counts, pooled summaries and quantiles. Third-party source and full method arrays remain temporary artifacts.

## Completed evidence: NUnit and Autofac

Independently recomputed pooled counts and all five quantiles, checked diagnostic counts and common provenance, and verified archive hashes and all 653 included source file hashes. NUnit v3.12: TCC 836/2917, depth 4553/2714, 481 types. Autofac v6.0.0: TCC 191/1105, depth 2006/1230, 232 types.

NUnit's four compiler errors include two explicit preprocessor errors. Missing historical constants disable 5926 nonblank lines including comments; it is not a historical build configuration. Autofac's 124 errors concern excluded generated resource classes. Both supply more examples of low direct TCC through helpers/properties and maximum direct TCC through forwarding.

## Integrated guidance and verification

CLAUDE.md now defines all four project metrics, includes local reviewed examples, gives the six historical project ratios, and explains how to calibrate judgments through role-aware source review. The detailed research report preserves exact pooled components and per-type distributions separately. Six-project medians are TCC 0.1875319792 and depth 1.7206828789, with ranges 0.0165714286–0.5093608780 and 1.4736842105–3.0158102767. These are descriptive values, not quality thresholds. No metric, hook behavior or history changed.

Across all six projects, 1204 included files passed byte-hash validation. Pooled sums, quantiles, compiler-error counts and counter/reference provenance passed independent checks. Local documentation links resolve and git diff --check passes. A phone-width chart preview has no console errors. This is documentation and retained research data only; no engine test rerun is needed for changed behavior.
