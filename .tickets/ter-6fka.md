---
id: ter-6fka
status: open
deps: [ter-8pco, ter-9t54, ter-yglw, t2b-xeo7]
links: [ter-4iic, t2b-d2m6, t1-mybi, ter-13rm]
created: 2026-11-22T21:13:03Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, temporal]
---
# Off-screen bounce light that the eye's position does not change: what a diffuse miss reads and what a young history starts from

The structural step that the lighting system review recommends (docs/research/lighting-system-review-2026-11-22/README.md, sections 10 to 13). Today a diffuse ray that misses the screen reads the eye's environment map (missLightOf), and a young history starts from a closed form and buys its own rays. Both then depend on where the eye is. This ticket replaces their source with an estimate that the eye's position does not change, kept per semantic frame, written by rasterized views: capture points per semantic frame (design P) or senders from the lights' shadow views (design S, the user's proposal of 2026-11-22).

## Design

One model (review, section 11): one store of first-bounce and far light, written by rasterized views whose positions do not depend on the eye, read by a diffuse miss along a direction through one reader, missLightOf. One supplier per piece of light, split by the light's origin. The screen rays stay the near field, and the eye's map stays for reflections. Settled 2026-11-22: (1) No store that only the screen feeds: the light would depend on where the camera has been. This replaces the premise of ter-13rm. (2) No build before the three experiments and the film of t2b-xeo7. (3) The orchestrator chooses between P and S from their results (user, 2026-11-22: 'use your best judgement and try to hill climb toward a complete/good lighting system'). The orchestrator's prior is P, because it tests visibility by its stored distance, carries many bounces and lets FullRateFrames and the own rays of young histories go, while S leaks through walls behind the camera. (4) No new trust rule of the temporal stack while the ray census is open. Design X's merge and its restart at 8 draws are not in question.

## Acceptance Criteria

Decided after the experiments. At least: the film of t2b-xeo7 shows no visible change of the room's light when the eye moves around the block, no new leak through a wall in any survey scene, and no cost peak at a start or after a light change that moves the render scale.


## Notes

**2026-11-22T21:20:12Z**

2026-11-22, orchestrator: the review's claim that tuning cannot reach the bar for the ray noise is refuted for the scene that it cites, and holds elsewhere. Combined check's table (t3/combined-renderer-check, docs/research/t0-gbgj-combined-check/tables.txt, draw 132 at render scale 2/3, daub against the long reference with both clamps off): rect-lamps main 0.0119, Design X 0.0089, no clamp 0.0079 (ceiling 0.0114 / 0.0090 / 0.0083, against 0.0567 on 2026-11-14). deck-cuts 0.0076 / 0.0053 / 0.0034. exit-cabin 0.0289 / 0.0173 / 0.0131. docking-cabin 0.0263 / 0.0169 / 0.0137. bay-sunbeam 0.0315 / 0.0229 / 0.0149. cabin-light-cabin 0.0428 / 0.0348 / 0.0285. Reading: two scenes are at the bar of about 0.010. In four scenes the still with no trust rule at all stays 1.3 to 2.9 times above it, so that part is ray noise and no rule removes it. Design X closes most of the gap between main and that floor. The fall of rect-lamps from 0.0567 came before the back-face layer (the Design X line's guess: the room's shadow fix), so the review's cause for it is not supported.

**2026-11-22T21:31:35Z**

2026-11-22, orchestrator: experiment E2 (ter-9t54) is done. The senders' light is right to within 14 to 23 % in the lit-block room. The leak through a wall is 4.8 % and 12.6 % of the lit room's comparable light, above the review's limit. So design S as sketched fails its own condition, and a design that takes the lamps' views as writers needs a visibility test that works behind the camera. Capture points have one through their stored distance.

**2026-11-22T22:09:05Z**

2026-11-22, orchestrator: the film of t2b-xeo7 is on main (docs/research/back-face-depth-layer/stage-2/lit-back-film/). A floor point behind the block that no lamp reaches reads 0.078 at rest, 0.507 while the eye sees the lit face (6.5 times), and 0.081 after the walk back, dark again within about 40 draws. A turn of the camera with the eye at rest moves it by at most 8 %. So the eye-bound class is a visible change over time by the user's bar, not accepted lost light. The review's first condition for the step holds. Open: the ray census (ter-8pco) and the face cost (ter-yglw).
