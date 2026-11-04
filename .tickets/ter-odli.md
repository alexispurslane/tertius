---
id: ter-odli
status: open
deps: [ter-s7c6, ter-7tnv, ter-rgg9]
links: []
created: 2026-11-03T14:53:48Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance]
---
# The budget and the defaults of the temporal renderer

Build ticket 6 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, section 11. Acceptance: section 14, item 6. If the frame exceeds 15.0 ms at the controller's scale, it asks the user what to cut before any default changes.


## Notes

**2026-11-04T12:20:46Z**

2026-11-04: the user's target is 12 ms, not 15 (ter-89xq's note). The acceptance becomes: if demo-exit's frame exceeds 12.0 ms at the windowed clock at 1280 x 720 rendering with 1920 x 1080 output, the ticket asks the user what to cut before any default changes. The controller keeps its runtime limit of 15.0 ms.
