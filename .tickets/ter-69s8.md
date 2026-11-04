---
id: ter-69s8
status: open
deps: [ter-9nrc]
links: []
created: 2026-11-04T13:42:25Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, lights]
---
# Tile light lists: each pixel loops only over the lights near it

The user's decision of 2026-11-04: with light sampling removed (ter-9nrc), every light of the main view is shaded exactly, and its cost grows with each light that a pixel loops over. Build light culling per screen tile now, so that the exact loop and the forward passes of the main view loop only over the lights whose reach touches the tile. Design first: the orchestrator presents two designs (for example 2D tiles with depth bounds from the G-buffer against 3D clusters) after a fact-finding read of every consumer of the light list, the depth layout and the exact loop's cost. Acceptance: lamp-hall-64 costs no more than the removed sampling did (1280 x 720, at the windowed clock), demo-exit's deck costs no more than the exact loop without tiles, and the images equal the exact loop's.


## Notes

**2026-11-04T14:17:19Z**

2026-11-04, deferred by the user: light budget only for now. A read-only fact-finding (GPT 6.1 SOL, /private/tmp/tile-lights-facts.md) found that in lamp-hall-64 every lamp's reach covers the whole hall: ARCHITECTURE.md records a 2.5 W hall lamp reaching about 130 m at about +5 stops, and the grid spans about 32 m. Reach is where a light's pre-exposed radiance falls below 2^-13 (LightUnits.cs), and walls do not shorten it. So tile or cluster lists cannot drop a lamp there, and they help only scenes whose lights are spread out beyond their reach, such as spin-station (129 lamps). Other facts for when this ticket returns: main-view lists can serve the direct pass (2D tiles with depth bounds from the G-buffer's distance target) or also glass and fringes (3D clusters); lit puffs (vertex-lit at their centers, which can lie outside the view), environment faces and impostor captures keep the full loop. The exact loop sums in light-buffer order (lighting.glsl:411), so lists must keep that order for identical images; an atomic append does not. Memory at 1920 x 1080 with 16 x 16 tiles: 2 to 8 MiB for 2D tiles with a cap of 64 to 256 lights, 32 to 256 MiB for dense clusters of 16 to 32 slices. Published assignment costs: Olsson et al. 2012 on a GTX480, 32 lights 0.71 ms clustered against 0.24 ms tiled; Harada 2012's 2.5D mask adds under 10 %. The engine has no fixed cap on the light list. Trigger to resume: a real scene whose lights are spread out and whose exact loop is too slow for the 12 ms target. Then measure first: count the lights that each tile and each cluster keeps on that scene before any design. The light budget itself comes from ter-9nrc's measurement of the exact loop's cost per scene; the engine enforces no limit.
