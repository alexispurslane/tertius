---
id: ter-tstw
status: open
deps: [ter-3oer]
links: []
created: 2026-10-22T00:45:58Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
tags: [tooling]
---
# Complexity and length limits: CA1502 and MA0051

Add Meziantou.Analyzer with only MA0051 on, and CA1502 with CodeMetricsConfig.txt. Record every existing violation as a named suppression in GlobalSuppressions.cs with a baseline justification.

## Acceptance Criteria

The build fails on a new method that breaks a limit and passes on the current code.

