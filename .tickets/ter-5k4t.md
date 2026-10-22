---
id: ter-5k4t
status: open
deps: [ter-c3rf]
links: []
created: 2026-10-22T12:58:26Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [core, frames]
---
# Numeric frame data as a union per mode

FrameTree keeps numeric, stepAcceleration, stepPlan and anchor in parallel arrays, and the last three mean something only in some modes (docs/plans/union-tooling.md, section Closed sets that are not unions yet). Design a union of one case per numeric mode, with the hot path in mind.

