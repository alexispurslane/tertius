---
id: ter-toaw
status: in_progress
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


## Notes

**2026-11-03T14:56:37Z**

2026-11-03: launched as workflow run f8a049a3-6b47-4209-a268-33931b73a9f5, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (Opus high builder, Opus xhigh reviewer and fixer), branch ter-toaw-trace-timing-spike from 70fe35650.
