---
id: t1-mybi
status: open
deps: []
links: [ter-13rm]
created: 2026-11-15T13:18:54Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering, lighting]
---
# Closed rooms are 2.0 to 2.5 times darker than a path trace without the sun: the engine carries less light of several bounces

Found by the reference experiment of the back-face layer (docs/research/back-face-depth-layer/stage-2/reference.md, part 1). Without the sun, the neutral floor region of a Cycles path trace is 2.5 times as bright as the engine's in the cabin of demo-exit: 0.4602 against 0.1815 W/(m² sr). In rect-lamps it is 2.0 times as bright: 0.03274 against 0.01615. Both rooms are closed boxes of light-gray surfaces, lit by rectangle lamps. The reference traces up to 32 diffuse bounces. The lead's reading: the remaining factor is light of several bounces that the engine does not carry. In the cabin, every rule of the screen walk gives the pillar faces 24 % to 50 % of the reference's ratio to the floor. This is evidence for the world-space radiance cache (ter-13rm). Not checked: how much of the factor is the multiple-scattering term of the material, which differs between the engine and Cycles.

