---
id: ter-toaw
status: open
deps: []
links: []
created: 2026-11-03T14:53:48Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, spike]
---
# A timing spike of the screen trace on the M1 Max

Build ticket 9 of epic ter-89xq, a spike on a branch of its own, as ter-afyv was. Design: docs/plans/temporal-renderer.md, section 6 ("The traversal", "The ray kinds", "The ray budget") and section 11. Acceptance: section 14, item 9. If the trace exceeds the budget's 1.7 ms, the orchestrator takes the result to the user before ticket 4 starts. Its report goes into docs/research/temporal-renderer/.

