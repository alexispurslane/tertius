---
id: ter-2fri
status: in_progress
deps: []
links: []
created: 2026-11-01T01:02:23Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# gpu-trace: a block bootstrap for the paired mode's standard errors

Found by the review of ter-e91p, 2026-10-31. The paired mode of tools/gpu-trace.py takes a bootstrap of single pair differences. Neighboring pairs share a frame: in an alternating trace, the pairs at frames 1 and 3 both use frame 2. If the noise of each frame is independent, neighboring differences correlate by 1/6, and the real standard error of their mean is about sqrt(4/3), 1.15 times what the independent bootstrap gives. ter-0y0z's control window median, +0.0077 ms with a standard error of 0.0027 ms, lies 2.9 standard errors from 0, which fits an error that is too small. ter-e91p only states this in passes.md and tools/README.md. A moving-block bootstrap over the pairs gives the right error, but it changes the numbers so that they no longer match ter-0y0z's scripts.

## Acceptance Criteria

The paired mode's standard errors come from a moving-block bootstrap over the pairs, with the block length and the seed stated in the output. A test on synthetic frames with independent noise gives an error near the analytic one. tools/README.md drops the warning that the errors are somewhat too small, or gives the remaining limit.

## Notes

**2026-11-01T01:16:09Z**

2026-10-31: workflow run 781250fd-3942-424f-bb2f-4decb10cc1fd (version 869b0b07), branch ter-2fri-gpu-trace-block-bootstrap from 15d33693.
