---
id: ter-svcs
status: open
deps: []
links: []
created: 2026-11-01T01:39:41Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# gpu-trace: why the control's window and environment faces medians sit about 3 standard errors from 0

Found by ter-2fri, 2026-10-31. In ter-0y0z's control trace (every frame runs the haze, the control pairs each odd frame with its two even neighbors), two paired medians lie about 2.9 moving-block standard errors from 0, where a control must give 0.

1. The window from the end of the reflections to the start of the bloom: +0.0077 ms, standard error 0.0027 ms (blocks of 6 pairs). The error does not grow with the block length (0.0027, 0.0026, 0.0026 and 0.0023 ms for blocks of 1, 5, 10 and 20 pairs), and the lag-1 autocorrelation of the differences is 0.03, so the error is not too small through a drift. The paired median is +0.0049 ms in the first half of the trace and +0.0085 ms in the second. The median window of the even frames is 0.3264 ms and that of the odd frames 0.3215 ms. The mean difference is -0.0021 ms, so the differences are skewed. The cause of this offset between the even and the odd frame slots is unknown.
2. The environment faces: +0.0040 ms, standard error 0.0014 ms. Their busy time follows a cycle of 12 frames in all three kept traces, about 6 frames near 0.03 ms and 6 near 0.16 to 0.27 ms (frame autocorrelation +0.27 to +0.52 at lag 12 and down to -0.41 at lag 6). ARCHITECTURE.md, 'The environment faces', says that a frame draws only some faces. A block of 6 pairs spans one cycle, so the bootstrap error leaves out the spread that the cycle's phase gives, and the error depends on the block length (0.0054 ms for single pairs, 0.0014 ms for 6, 0.0010 ms for 20).

Every alternating trace whose frames without the pass are the odd frames carries the same offsets. Work: find the source of each, and decide whether the paired mode subtracts the control's median of each row, or states the control's medians beside the alternating trace's.

## Acceptance Criteria

The cause of each offset is named with evidence from a trace, and tools/README.md 'GPU trace' says how the paired mode handles it.

