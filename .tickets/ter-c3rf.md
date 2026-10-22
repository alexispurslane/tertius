---
id: ter-c3rf
status: open
deps: [ter-07e3, ter-297a, ter-mh11, ter-asoh, ter-kx3k, ter-m0db]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, analyzers]
---
# Union analyzer: rules TU0001 to TU0005

Add the rules of docs/plans/union-tooling.md, section The analyzer, to analyzers/Tertius.Unions, as errors. Tests with the Roslyn analyzer test driver: one case that reports and one that does not, per rule.

## Acceptance Criteria

Each rule reports its case. The build of the whole solution is clean.

