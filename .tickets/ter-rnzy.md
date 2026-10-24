---
id: ter-rnzy
status: open
deps: [ter-2x99]
links: []
created: 2026-10-24T16:22:41Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Slop metrics: record erosion and verbosity for every commit

The pre-commit hook runs tools/Tertius.Slop on every commit and stores erosion and verbosity under a key that stays stable after a merge, so that sloppiness over time maps to commits, branches, tickets and epics. A script prints the history as CSV.

