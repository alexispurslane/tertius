---
id: ter-bqun
status: in_progress
deps: [ter-9nrc]
links: []
created: 2026-11-04T14:24:28Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, lights]
---
# Runtime cap on the lights that the main view keeps

The user's decision of 2026-11-04: enforce the scene light budget at runtime. With light sampling removed (ter-9nrc), the exact loop's cost grows with each light that the main view keeps, and a dark exposure stretches every lamp's reach. SceneBatcher.CollectLights, the one choke point of the frame's light list, keeps at most N lights for the main view, ranked by the one order that the shadow budgets use (ARCHITECTURE.md, 'The rank': tier, then LightUnits.RankIrradiance, ties in list order, with the atlas's holder margin against flicker). A capped light is not kept by the main view, so every main-view consumer (the direct pass, glass, fringes, lit puffs) agrees; the environment faces keep their own membership. The lowest-ranked kept lights fade out over a band, so that no light pops when it crosses the cut. N is a RendererOptions setting with a default from ter-9nrc's measurement of the exact loop at the 12 ms target. Acceptance: the cost of the exact loop stays within its allowance on a scene with many more than N lights in reach (lamp-hall-64 and spin-station); scenes under N give the same images as before; a film where a light crosses the cut shows no pop beyond the stated tolerance; the cap is deterministic.


## Notes

**2026-11-04T17:28:07Z**

2026-11-04: started as a workflow, run 58c8b33e-188e-401c-98da-56afb5ddc98e (version 869b0b07), branch ter-bqun-runtime-light-cap from 8636f57b2. Settled: the cap only in CollectLights with the shadow budgets' rank and margin; a fade continuous in the rank measure; N's default makes the 64-lamp hall's direct light cost no more than demo-exit's deck's, and an N below the deck's light count is a fork.
