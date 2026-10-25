---
id: ter-nydu
status: in_progress
deps: []
links: []
created: 2026-10-25T22:29:46Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# GPU profiling tools: the frame's GPU span, and a Metal System Trace of a headless run

Part 1 of docs/plans/rendering-performance.md, "Order of the work". gpu-timings.json gains the frame's GPU span. A script records a Metal System Trace of a headless run with xctrace and reports each pass's limiter, so that a profile says why a pass is slow, not only how long it takes.

## Acceptance Criteria

The span in gpu-timings.json and its tests, the script and its doc in tools/README.md, and one trace of demo-exit read with it, as the part's Done says.

