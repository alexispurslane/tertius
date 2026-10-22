---
id: ter-tstw
status: closed
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


## Notes

**2026-10-22T03:32:45Z**

Folded into ter-3oer on 2026-10-21: the user chose a report on the changed code over a build gate with a baseline. CA1502 (limit 15) and MA0051 are suggestions, and the pre-commit hook prints the ones in changed methods.
