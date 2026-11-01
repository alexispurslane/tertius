---
id: ter-ku9c
status: open
deps: []
links: []
created: 2026-11-01T03:28:53Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# gpu-trace: does the frame slot give the window's odd-even difference in a control

Found by ter-svcs, 2026-10-31. On the trace's own frame order, four controls of mars-air's view above (--window reflections bloom --control haze) carry an odd-even difference of the window from the end of the reflections to the start of the bloom. Measured as the median of the signed second difference over all frames, positive when the odd frames' window is longer, with a block bootstrap of 12 values: ter-0y0z above-control +0.0055 (0.0021) ms, ter-svcs c1 +0.0015 (0.0037), c1-shift -0.0004 (0.0012), c-fixedface -0.0045 (0.0023) (/tmp/ter-svcs/fix/parity.py). In ter-0y0z's control, the median window of the odd frames is 0.3289 ms and that of the even frames 0.3197 ms, and the pairing on even middles gives +0.0082 (0.0022) ms, +0.0080 and +0.0085 in the two halves of the trace. The size and the sign change from run to run, and the cause is not established. One candidate: RendererOptions.FramesInFlight = 2, and VulkanRenderer takes frames[frameNumber % frames.Length], so each parity of the frame order has its own frame resources, a cycle of 2 that every pairing aligns with. Test it with controls under a temporary probe FramesInFlight = 3, a cycle of 3 that a pairing of 2 does not align with, and keep the probe out of the commits. Compare the odd-even difference and the window per phase mod 3 with the controls above.

## Acceptance Criteria

The run-to-run odd-even difference of the window in a control is explained with evidence from traces, or shown not to come from the frame slot, and tools/README.md 'Reading the control' says what it means for an alternating trace.
