---
id: tiunr-lzl4
status: in_progress
deps: []
links: []
created: 2026-11-06T12:27:32Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# Offline reference metrics: light loss, edges and temporal variation

Implement the confirmed seven-part descriptive metric specification in the existing reflection reference tool. Reuse sealed native captures and reference levels/shifts; no new renderer draws or light integration. Parent owns inspection/integration/closure.

## Design

docs/plans/unity-guided-indirect-light-research.md: fixed 2048 center basis; fixed 10/50/90 percent contrast; reference-defined bands; central 5-95 percent widths; explicit ambiguity/occlusion/crop coverage; temporal spread and mean error; reference spread. No quality score.

## Acceptance Criteria

Focused meaningful CPU assertion reds then greens; one offline saved-data report; existing full-crop calculation preserved; all original seals/controls remain exact; release bounded allocation and retain compact report with named units/limits.

## Offline metrics stage, 2026-11-06

The tool implements one frozen2048-center reference basis. The existing
ReferenceDifference and the new weighted regions share the same summation
mechanism. Every horizontal row retains its directed roots, plateau intervals,
invalid reasons and coverage. OFF/reference, ON/reference, ON/OFF and saved
reference candidates use the same thresholds and regions.

Exact attempts:3Release tool compilations and3separate affected-test
compilations, all BuildProjectReferences=false/no-restore. The baseline
ran16checks:12passes and4intended assertion reds. The next tool build failed
on3TS0002 Path.Combine calls. The corrected tool built successfully. Its
first test build failed on CS1061 because one test used region.Name instead
of region.MeanImage.Name. The final tests-only correction passed52/52pure
checks, including16metric,6report/coverage and30existing pure regressions.
No old numerical integration tests or full suites ran.

ONE saved-data report command exited0 on all4existing cases and128recorded
frames. No new renderer draws or light integrations ran. The surrounding
wrapper exited1 because retained evidence reached11224KiB, above the8192KiB
target. This is NOT a compliant retention verdict or green wrapper. Complete
report data and all failures remain. Cumulative comparison evidence was
107364KiB, below131072KiB. Final disk guard was34709780KiB, above5242880KiB.

The report records lower temporal variation but much greater rough-case
mean deficit with ON. All12800pixels remain in crop totals. Fixed combined
outer bands are valid on141/160rows. Their eligible rows contain99.765% of
smooth reference light and99.691% of rough reference light. The band itself
contains1.342%/13.953% of the full reference light. These are separate coverage
quantities, not a hidden crop mask. Full results and crossing sensitivity
remain descriptive, not quality acceptance.

Evidence: /private/tmp/tertius-unity-noise-research-20261105/live-reference-comparison/offline-metrics-20261106-0733/README.md.
Frozen tool SHA256:4f7661dc2628fd0fa2cb25cc6213761778a2c70906ec6b882b8d40eeea4b59a7.
All322/89/42/58/71-file seals,10production refs,10controls,35old runtime files
and newest frozen runtime verify unchanged before/after. The command also
hashed all33actual saved inputs/tool bytes before/after.

All owned heavy processes ended07:54:33EST. Allocation explicitly RELEASED
immediately. No later allocation held. Parent owns independent inspection,
retention disposition, integration and ticket closure. This ticket stays open.
