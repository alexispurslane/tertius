---
id: ter-nmbm
status: open
deps: [ter-ohqr]
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# The direct signal: the accumulator, the composite, ReSTIR DI and the still rule

Build ticket 3 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, sections 7, 8, 9 ("The composite") and 10 ("The still rule"), and decisions 7, 10, 18, 28, 29 and 32. Acceptance: section 14, item 3. The user sees direct light of many lamps at a lower cost, with noise that settles over a few frames.


## Notes

**2026-11-03T15:24:48Z**

2026-11-03: the user chose the count of exact lights: the lights whose shadow slot is a cascade set (RendererOptions.CascadedLights, 1 by default and 4 at most) take the exact loop. Every other light of the main view, far lights without a set included, is a sampled light in ReSTIR DI. Decision 28 and section 7 of docs/plans/temporal-renderer.md hold the rule, and known limit 19 holds its cost.

**2026-11-03T19:37:12Z**

2026-11-03: the build of ticket 3 (branch ter-nmbm-direct-light).

Built:
- TemporalSignal, the accumulator (Frame/TemporalSignal.cs, accumulate_temporal.comp, accumulate_spatial.comp), with the direct signal's two parts, diffuse and specular, demodulated and pre-exposed. The temporal dispatch caps the reprojected histories at MaxRadiance after the exposure ratio, whose exponent it clamps to ±64.
- CompositePass in place of AmbientLightPass (composite.frag, composite_scatter.frag): the histories times their material factors, today's ambient terms with the estimate, the emission and the sky. The diffuse image now exists only under the skin scatter, and the occlusion depth pass reads the direct signal's diffuse history as the bounce source.
- ReSTIR DI: LightTable (alias table, exact lights, remap), LightReservoirs (R0, R1), the light candidates pass and the direct pass's two variants (direct_every.frag, direct_resampled.frag). The exact lights are the lights with a cascade set.
- The still rule (StillRule, FrameHistory.DrawsOf and Begin) with RendererOptions.WarmUpFrames, 32 by default; the host's --direct-light resampled|every.

Bias and noise against EveryLight, Release, 1280 x 720, after a still's 32 warm-up draws (DirectLightBiasMeasurement): the hall of lamp-hall-64 bias -0.022, history error 0.124, sample error 0.419; demo-exit's deck at frame 120 -0.107, 0.104, 0.264; the cabin of cabin-light -0.004, 0.079, 0.074. The deck's still with SpatialNeighbors = 0 measured -0.006, so the spatial reuse's biased combine causes the deck's bias. Ticket ter-t8z7 holds the design of a visibility-aware combine.

GPU time, Release, M1 Max, Metal traces without counters, 120 frames, interleaved with the base 2d0ea65e8, loads 6 to 29 (ARCHITECTURE.md, "ReSTIR DI", has the table). At 1280 x 720: hall, light candidates 0.83 to 0.85 ms + direct light 0.82 to 0.84 ms, accumulate direct 0.67 to 0.69 ms, frame span 11.7 to 13.2 ms on the base and 7.9 to 8.1 ms on the branch; deck, 1.35 + 1.79 to 1.83 ms, accumulate 1.15 ms, span 12.2 to 13.0 against 12.2 ms. At 1920 x 1080: hall 1.47 to 1.53 + 1.51 to 1.55 ms, accumulate 1.46 to 1.48 ms; deck 2.22 to 2.24 + 3.06 ms, accumulate 2.39 to 2.40 ms. Both are over the budget of 0.6 to 1.3 ms and 0.15 to 0.30 ms. Ticket ter-9nrc holds the cost.

Screenshots (1280 x 720, headless, Release):
- /private/tmp/nmbm-shots/base-lamp-hall-64-still/hall.png and after-lamp-hall-64-still/hall.png: the hall of 64 lamps. After, the floor shows grain and the pillars' shadows lose most of their contrast.
- /private/tmp/nmbm-shots/base-demo-exit-deck/deck.png and after-demo-exit-deck/deck.png: demo-exit's deck. After, the thin shadows of the glasshouse's frame on the floor nearly vanish. branch-every-deck/deck.png, the branch under --direct-light every, matches the base.
- /private/tmp/nmbm-shots/base-cabin/cabin.png and after-cabin/cabin.png: the cabin of cabin-light under the star, an exact light. Before and after look the same.
