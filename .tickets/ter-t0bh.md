---
id: ter-t0bh
status: closed
deps: []
links: []
created: 2026-10-22T00:45:58Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
tags: [tooling]
---
# Doc comment checks and recommended analyzers

Turn on GenerateDocumentationFile and AnalysisLevel latest-recommended in Directory.Build.props. Add a root .editorconfig. Keep CS1591 off, CA1707 off in tests, CA1051 off. Fix every doc comment error the compiler reports (broken and ambiguous crefs, wrong paramref names, partly documented parameters, bad XML) and the remaining recommended-analyzer hits, or record a reason to turn a rule off.

## Acceptance Criteria

dotnet build Tertius.slnx passes in Debug and Release with the new settings; tools/test-all.sh passes.

