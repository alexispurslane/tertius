---
id: t0-anp3
status: in_progress
deps: [t0-hfo4, t0-q6jl]
links: []
created: 2026-11-12T13:56:23Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Lock in the impulse, the light edges, the quiet pixel, the panel at 4.95 m and the measured variance

Design: docs/plans/bounce-light-confidence.md, section 22. Branch t7-3e22-retained-deviation. Step 2c.


## Notes

**2026-11-12T14:43:37Z**

2026-11-12, step 2c tests. DiffuseFilterRenderTests: ASingleBrightRayBlockSampleNeverGrowsMoreConspicuousOverThe32DrawsAfterIt (red: 0.199 against 0.0082, 24 times; green: peak 1.015 times the first, 0.36 after 32 draws), TheExcessOfASingleBrightRayBlockSampleFadesAtTheSlowBlendsPace (on S), AQuietPixelKeepsTheLightThatTheFilterMovedIntoIt (every length stays 32 in every draw; narrow-box mutant red at draw 0), AStableEdgeOfLightUnderNoiseKeepsItsWidth, TheRetainedDeviationIsTheDeviationOfTheSlowHistorysSamples (1.107 for the step from F), AfterAStepOfASampleWithoutNoiseTheRetainedDeviationHalvesWithin45DrawsOfTheHistoryReachingIt. ScreenTraceRenderTests+BounceLightConfidence (process environment, filter off by its switch): TheRetainedDeviationPredictsTheSpreadOfTheSlowHistory (measured over predicted 0.945), TheBounceLightOfALitPoolKeepsItsEdgeWithRealRays. The panel at 4.95 m stays green.
Findings for the user: (1) the synthetic edge rises over 18 px (design bound 16; CPU model with this image and measure 15; the old filter at most 16) and the pool edge is 6 px wider filtered (37 against 31; design bound 4). The tests hold 20 px and 8 px until the user decides. A CPU variant that stores the linear rule but passes the squared rule to step 2 gives an 11 px edge but a peak 1.34 times the first at draw 2, so the trade-off is the design's point 3 of section 18. (2) This GPU rounds half-float stores toward zero: the quiet scene's history reads 1.8 % low with the filter's store, 0.9 % without, with no clamp move at all. The design's 1 % light bound holds only for round-to-nearest; the test bounds the light at 4.7 % below and tests the clamp by the lengths.
