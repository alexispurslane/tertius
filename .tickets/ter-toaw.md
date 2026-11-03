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

**2026-11-03T15:44:05Z**

2026-11-03, the spike's result (report: docs/research/temporal-renderer/trace-timing-spike.md). The screen trace does not fit the budget. Section 11 gives the whole tracer 0.8 to 1.7 ms at 1280 x 720. The trace alone takes, as the median GPU busy time of a Metal System Trace over 120 frames in Release: deck 10 % own rays 6.79 to 6.81 ms reflection + 3.24 ms diffuse = 10.04 to 10.05 ms (3 runs), deck 25 % 9.82 + 4.63 to 4.64 = 14.45 to 14.46 ms, floor 10 % 6.13 to 6.14 + 3.82 = 9.95 to 9.96 ms, floor 25 % 8.79 to 8.82 + 5.46 to 5.47 = 14.25 to 14.29 ms (2 runs each). That is 5.9 to 8.5 times the budget's high end. The runs of one case agree within 0.3 %, at a 1-minute load of 1.66 to 4.27 on 10 cores, each under the GPU lock. The time grows with the number of rays: 19 to 23 ns per reflection ray and 11 to 12 ns per diffuse ray, against about 2 to 3 ns that the budget leaves. No step limit fits: 32 and 8 steps still take 3.53 ms on the deck, with 5 % of the diffuse hits. The spike leaves out the design's hit reads, the misses' map reads, the real plan and the resolve, so the whole tracer costs more. The spike code is commit 91a20f20c on branch ter-toaw-trace-timing-spike: ticket 4 takes screen_trace.glsl and trace_spike.glsl from there. By the ticket, the orchestrator takes this result to the user before ticket 4 starts.
