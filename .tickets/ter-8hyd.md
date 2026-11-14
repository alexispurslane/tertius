---
id: ter-8hyd
status: open
deps: []
links: []
created: 2026-11-09T15:08:08Z
type: epic
priority: 0
assignee: Alexis Purslane
parent: ter-icn3
---
# Finish the renderer: every scene clean, settled within 60 draws, under 12 ms

User goal 2026-11-09: keep working until the whole rendering system is reasonable, so that the renderer is finished. Design doc: docs/plans/renderer-finish.md (written by the survey ticket). Acceptance, as the parent reads the goal: (1) No terrible noise or blotches in any scene that scripts/*.json renders, still or moving. The survey sets the measures (speck count, blotch score against a long-accumulated reference) and the parent also looks at frames and videos. (2) Updates settle within about 60 draws: lamp switches, a moving sun, moving lamps, and surfaces that a walk reveals. (3) Every scene that rendered before keeps rendering reasonably, with a GPU frame span under 12 ms at 1280 x 720 rendering with 1920 x 1080 output (FSR 3.1, render scale 2/3), Release build, M1 Max, as ter-odli's note of 2026-11-04 and docs/plans/rendering-performance.md decision 1 define the budget. A pass over its share first gets optimizations that keep the image; then its default may drop quality, with the full quality kept as a setting (rendering-performance.md decision 2). (4) No ReSTIR of any kind (user, 2026-11-09). Prior work this epic builds on: ter-icn3 and ter-89xq.


## Notes

**2026-11-12T00:23:04Z**

Alexis, 2026-11-11: removing the mottle (the cloudy blotches of the bounce light, ticket t7-3e22) matters more than performance, because the image must first be fit to show people. Reserve about 1.5 ms of the deck's frame for the mottle fixes, and plan the frame-time savings so that the budget holds with those 1.5 ms added. The mottle's acceptance bar is a daub score of about 0.008 to 0.010 against a true reference (blind-tested by the t7-3e22 agent), not the survey's old blotch limit of 0.04.

**2026-11-12T20:49:42Z**

2026-11-12: main's survey probe patch put its probe signal at specialization constant 11, which the shadow code's TileCornerMeans now uses, so the patch failed on main; it moves to 12, with the same text as on t7-3e22-retained-deviation.
