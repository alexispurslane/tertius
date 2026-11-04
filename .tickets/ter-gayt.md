---
id: ter-gayt
status: open
deps: [ter-nmbm, ter-toaw, ter-8hbi, ter-jz2y, ter-sftw, ter-3ug8]
links: [ter-dzxf]
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# The screen tracer: reflections and many-bounce light through one trace

Build ticket 4 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, sections 6 and 12, and decisions 4 to 6, 9, 17, 25 to 27, 36, 38 and 39. Acceptance: section 14, item 4. It starts from ticket 9's trace. If the whole tracer exceeds the budget's 1.7 ms at 1280 x 720, it reports to the user before it goes on. The user sees reflections on every surface up to roughness 0.5 and bounce light with many bounces.


## Notes

**2026-11-04T12:10:08Z**

2026-11-04, the trace budget facts at the windowed clock (1296 MHz), from a read-only researcher (GPT 6.1 SOL), file /private/tmp/trace-budget-facts.md. At 1920 x 1080 output with 1280 x 720 rendering, the frame without the screen tracer is an estimate of 9.34 to 10.02 ms: section 11's rows, with the direct light's shadow-aware combine (3.0 to 3.1 ms with its candidates) and FSR at 1.93 to 1.99 ms. Inside 15.0 ms the whole tracer gets 4.98 to 5.66 ms. After 0.6 ms for the plan and the resolve and 0.015 ms for the finest depth level, the two trace dispatches get 4.36 to 5.05 ms. V6's two dispatches at 10 % own rays take an estimated 3.14 to 3.16 ms on the deck and 2.92 to 3.02 ms on the floor (frame 12.9 to 13.8 ms). At 25 % own rays they take 4.07 to 4.15 and 3.77 to 3.80 ms (frame 13.7 to 14.8 ms, worst margin 0.21 ms). So the old 1.1 ms covers about 35 % of the rays, but the frame holds all of them at 10 % own rays. Every number converts headless times at an assumed 800 MHz, within about 15 % either way. At native 1080p rendering, the frame has no room for the trace before FSR (-1.68 to -0.46 ms). The orchestrator's earlier figure of 2.7 ms of room subtracted today's frame (12.3 ms), which still holds the old occlusion and reflection passes that the tracer replaces, and was too low. The budget decision goes to the user together with the diffuse walk choice, after the profiling continuation (8e00fb0c).
