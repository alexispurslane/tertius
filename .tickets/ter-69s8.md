---
id: ter-69s8
status: open
deps: [ter-9nrc, ter-bqun]
links: []
created: 2026-11-04T13:42:25Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, lights]
---
# Per-tile light cap: each screen tile keeps the lights that light it most

The user's decision of 2026-11-04: with light sampling removed (ter-9nrc), every light of the main view is shaded exactly, and its cost grows with each light that a pixel loops over. Build light culling per screen tile now, so that the exact loop and the forward passes of the main view loop only over the lights whose reach touches the tile. Design first: the orchestrator presents two designs (for example 2D tiles with depth bounds from the G-buffer against 3D clusters) after a fact-finding read of every consumer of the light list, the depth layout and the exact loop's cost. Acceptance: lamp-hall-64 costs no more than the removed sampling did (1280 x 720, at the windowed clock), demo-exit's deck costs no more than the exact loop without tiles, and the images equal the exact loop's.


## Notes

**2026-11-04T14:17:19Z**

2026-11-04, deferred by the user: light budget only for now. A read-only fact-finding (GPT 6.1 SOL, /private/tmp/tile-lights-facts.md) found that in lamp-hall-64 every lamp's reach covers the whole hall: ARCHITECTURE.md records a 2.5 W hall lamp reaching about 130 m at about +5 stops, and the grid spans about 32 m. Reach is where a light's pre-exposed radiance falls below 2^-13 (LightUnits.cs), and walls do not shorten it. So tile or cluster lists cannot drop a lamp there, and they help only scenes whose lights are spread out beyond their reach, such as spin-station (129 lamps). Other facts for when this ticket returns: main-view lists can serve the direct pass (2D tiles with depth bounds from the G-buffer's distance target) or also glass and fringes (3D clusters); lit puffs (vertex-lit at their centers, which can lie outside the view), environment faces and impostor captures keep the full loop. The exact loop sums in light-buffer order (lighting.glsl:411), so lists must keep that order for identical images; an atomic append does not. Memory at 1920 x 1080 with 16 x 16 tiles: 2 to 8 MiB for 2D tiles with a cap of 64 to 256 lights, 32 to 256 MiB for dense clusters of 16 to 32 slices. Published assignment costs: Olsson et al. 2012 on a GTX480, 32 lights 0.71 ms clustered against 0.24 ms tiled; Harada 2012's 2.5D mask adds under 10 %. The engine has no fixed cap on the light list. Trigger to resume: a real scene whose lights are spread out and whose exact loop is too slow for the 12 ms target. Then measure first: count the lights that each tile and each cluster keeps on that scene before any design. The light budget itself comes from ter-9nrc's measurement of the exact loop's cost per scene; the engine enforces no limit.

**2026-11-04T14:24:28Z**

2026-11-04: the user chose to enforce the light budget at runtime, by a cap on the lights that the main view keeps (ter-bqun), over a development-time check. Tile lists stay deferred.

**2026-11-04T20:07:04Z**

2026-11-04, the user's decision ("View cap now, tile next"): this ticket returns now, widened to a per-tile cap, and runs after ter-bqun merges. Why: ter-bqun's view-wide cap of 24 keeps the same 24 lights (by rank irradiance, for a point lamp its irradiance at the eye) for every pixel. On lamp-hall-64 it lights only about 11 lamps (the fade starts at twice the cut), the automatic exposure rises from about 4.6 to 8.6 stops, and the lamp panels bloom. On spin-station (FLIP 0.015 to 0.025) the far lamps lose the light on their own walls. Comparison images: /private/tmp/bqun-shots/compare/. Costs (depth pyramid + direct light + occlusion depth, windowed clock): hall uncapped 3.73 ms, capped 1.60 ms; station uncapped 7.44 ms, capped 2.01 to 2.31 ms; deck 2.28 to 2.31 ms. New scope: a GPU pass keeps, for each screen tile, the K lights that give that tile the most light, with a fade by share cut at the tile's own cut, so every area keeps its own nearby lamps; reach culling as in the old scope comes with it. ter-bqun's view cap stays as the outer bound, above all for the readers that have no tile (the lit puffs, the impostor captures), probably with a larger N. Design first: the orchestrator presents two designs (for example 2D tiles with depth bounds against 3D clusters, which also serve glass and fringes; and how the tile fade avoids visible steps at tile edges). The facts of the old note still hold, above all the light-buffer order of the sum. Acceptance to settle in the design: the hall's look close to the uncapped hall's at a cost near the deck's, spin-station's far lamps keep their light, the deck unchanged.

**2026-11-04T20:32:29Z**

2026-11-04: the view cap's rule (ter-bqun) is now: the top K lights in full, a one-stop fade below the K-th light, a ceiling M, and a per-entity rate limit on the shown share. The per-tile cap can use the same target rule on the GPU; the rate limit needs per-light memory, which per-tile lists do not have, so the design of this ticket must say how the tile cap avoids pops (the user accepted that the tile cap may need a rule of its own for this).
