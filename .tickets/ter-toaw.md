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

**2026-11-03T16:42:16Z**

2026-11-03, the review's fix stage. The result stands: the trace alone takes about 10 ms at 10 % own rays and 14.3 to 14.5 ms at 25 %, against 1.7 ms for the whole tracer. New evidence in the report: (1) the step experiment ran again as one batch of two runs per setting, the design's 256 and 64 steps included (10.07 ms at a load of 20.77 to 25.48, against 10.04 ms in the main runs at 2.41 to 4.27). No setting fits: 32 and 8 steps take 3.20 to 3.36 ms. (2) One counters run per view: in every trace pass the top limiter is the ALU at 49 to 61 %, and the pass reads only 5 to 9 GB/s, so the trace is not bound by memory bandwidth, and no unit is saturated. (3) The count runs now save their counts in captures/toaw/count2-*.txt, with the same counts as before. (4) The report defines the floor window (ter-f3g2's time-floor, frame 2552), drops the load claims that the logs cannot show, and states that the GPU clock was not recorded. The run script (commit a53a6e87e) now terminates and kills a hung xctrace, so the GPU lock always goes. The code for ticket 4 stays commit 91a20f20c. The report's section 'What ticket 4 must change in the spike's code' lists four defects of that code: the same direction for a block ray and its pixel's own ray (ticket ter-jz2y, which ticket 4 now waits for), the second copy of the own-ray list, three rules copied from the reflection_* shaders with MaxRoughness as a GLSL constant, and a density that can exceed the half-float range.
