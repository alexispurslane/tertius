---
id: ter-gpee
status: open
deps: [ter-3oer]
links: []
created: 2026-10-22T00:45:58Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [tooling]
---
# Bring the existing methods under the complexity and length limits

CA1502 and MA0051 are reports, not a gate (ter-3oer), so there is no baseline to remove. After the first cleanup on 2026-10-21, 55 methods were over the complexity limit of 15 and 108 were over the length limit, in src, tests and benchmarks. The analyzers.sarif file of each project lists them. Split or simplify them, one module at a time, src first.

