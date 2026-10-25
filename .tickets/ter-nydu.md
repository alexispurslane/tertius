---
id: ter-nydu
status: closed
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


## Notes

**2026-10-25T23:39:59Z**

Merged into main on 2026-10-25 (441fa88c, fix 3e39466e). gpu-timings.json has the frame's GPU span (mean, median, p95), and tools/gpu-trace.sh records a Metal System Trace of a headless run with the 'Metal GPU Counters' instrument and reports each pass's GPU time and its two top limiters, splitting encoders at the timer's own stamp encoders. Findings for the profiles: on the M1 Max most passes are ALU-bound, not bandwidth-bound; and the timer's per-pass times can differ from the trace's (occlusion depth 0.27 against 0.03 ms) because passes overlap, while the frame span matches the trace within 0.5 %. So take pass times from the trace, and the frame time from the timer. Coverage of the changed C# lines: 38 of 38, branches 8 of 12 (the early return for a run with no timed frame, and the unavailable-results arm).
