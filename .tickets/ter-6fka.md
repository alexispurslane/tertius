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

