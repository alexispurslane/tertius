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

**2026-11-04T20:16:33Z**

2026-11-04 (orchestrator, main): the review of run b4b63382 found that the share jumps when the main view's count crosses N. Its static form is worse: Share fades from 2 to 4 times the cut, so when N+1 main-view lights of one tier lie within a factor 2 of each other in rank irradiance (for example a ring of 25 equal lamps around the eye at N = 24), every kept light has x < 2 and share 0, and those lamps give no direct light at all, in every frame. The hall's 11 lit lamps of 24 are the mild form. The fade rule goes back to the user for a decision before the merge.

**2026-11-04T20:32:29Z**

2026-11-04, the user's decision on the fade rule (two designs, hybrid): rule A for the target share, with no memory: rank the main view's lights by LightRank; places 0 to K-1 get target 1; places K to M-1 get smoothstep(0.5, 1, x), x the light's rank irradiance over the K-th light's; places M and beyond get 0. And the user's second answer: the shown share moves toward its target by at most 1/F per drawn frame (F about 8), kept per light by entity, so a switch-on, a spot lamp's tier change and a count crossing become short fades (the spot-lamp pop goes away). Settled by the orchestrator for the build: the lights with a shown share above 0 never exceed M (an entrant waits for a free place, admitted in rank order; a leaver keeps its place until its share reaches 0); a frame that resets the history takes the targets at once; K is at least the deck's 22 lights so that the deck and the cabin stay byte-identical; M and F by measurement against the cost rule.
