---
id: ter-okrp
status: in_progress
deps: []
links: []
created: 2026-10-26T22:18:40Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, performance]
---
# On MoltenVK the GPU timer's per-pass times move work into the pass before: a stamp is sampled after later work ran

Found in ter-izsu's review (2026-10-26). The same headless runs of demo-exit's deck (120 frames from 399, 1920x1080, Release, M1 Max, MoltenVK 1.4.2), read both ways: tools/gpu-trace.sh's busy time of each pass's encoders, and the renderer's own gpu-timings.json of that run. The frame span agrees within 1 % (for example 19.70 against 19.71 ms). The per-pass times do not: the timer gives 'occlusion sectors' 7.08 to 8.52 ms and 'occlusion far rays' 0.26 to 0.84 ms, where the trace gives 1.86 to 2.22 ms and 5.76 to 6.55 ms; their sums agree within about 0.6 ms. So the stamp after the sectors pass is sampled near the end of the far rays, and the far rays' work counts in the sectors pass. 'depth pyramid' (timer 0.69 to 1.78 against trace 0.23) takes part of 'direct light' (timer 0.67 to 1.82 against 2.03 to 2.28) the same way. MoltenVK writes each stamp as its own blit encoder ('Record GPU Counter Sample BlitEncoder'), and Metal can run the next pass's encoder before it. ARCHITECTURE.md, 'GPU timings', said that the passes report real times on this Mac. Part 3 of the performance epic (ter-pq5y) reads gpu-timings.json per pass. Find whether a stamp can be ordered on MoltenVK (for example a pipeline barrier after the stamp, or a MoltenVK setting), what it costs, and otherwise state in ARCHITECTURE.md and in the tools that the timer's per-pass times on MoltenVK hold only for sums of neighbouring passes and the span, and that per-pass profiles read the trace.

## Acceptance Criteria

Either the timer's per-pass times on this Mac match the trace's busy times within 0.2 ms for the occlusion passes on demo-exit's deck, with the cost of the change measured, or the docs and the readers of gpu-timings.json (Part 3) use the trace for per-pass times on MoltenVK and the timer only for the span.


## Notes

**2026-10-27T02:35:31Z**

Reproduced on main (2026-10-26, demo-exit's deck, 120 frames from 399, 1080p, Release, load 6.0): seven passes differ from the trace by more than 0.2 ms, in pairs of neighbours whose sums agree (occlusion upsample 1.459 against 0.300 and lighting 8.770 against 9.906, sums 10.229 and 10.205). Cause: MoltenVK 1.4.2 samples each stamp at the end of its own blit encoder, which waits for the encoder before it; the next pass's first encoder does not wait for it, starts beside it and holds it back by up to about 1 ms. The spec allows a late write (any logically later stage; the stamp's second scope holds only its write). The stamp's stage is ignored by MoltenVK, no MoltenVK setting is about timestamps on Apple GPUs, and MoltenVK's barriers are fences between command encoders that the stamp encoder takes no part in. Measured a barrier at all commands after each stamp: mean span 22.23 and 22.60 ms against 21.92 and 22.14 ms on main, and five passes still more than 0.2 ms off. Outcome: the docs' route. ARCHITECTURE.md, tools/README.md and the two plans say that on MoltenVK the timer holds only for the span and neighbour sums; VulkanRenderer.StampsLatchLate (driver id MoltenVK) makes a headless run print ScriptCommand.LateStampsNote after the pass times; gpu-trace.py's table says to read its per-pass times.
