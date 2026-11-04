---
id: ter-nmbm
status: in_progress
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

**2026-11-03T17:53:57Z**

2026-11-03: launched as workflow run 46d28d1d-5d6f-4186-b518-019afd9643f0, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-nmbm-direct-light from 2d0ea65e8 (ter-ohqr's branch merged with main, before ter-ohqr's hardening). ter-ohqr's hardening runs at the same time as delegated task ter-ohqr-hardening-1, with one fix inside the surface test: the last view depth moves from the half-float surface history to a kept R32F device depth, so surfaces beyond 65,504 m pass.

**2026-11-03T21:28:46Z**

2026-11-03: the user chose the full fix of the review's shadow fork in this ticket. The default ResampledLights(4, 2) washes out thin lamp shadows (the deck's glasshouse frame, the hall's pillars), because the spatial combine takes a neighbor's light with no visibility test at this pixel. Before the merge, ticket 3 builds the visibility-aware combine that ticket ter-t8z7 describes: Bitterli's unbiased combine, each neighbor's light weighted with its visibility at this pixel, and 1 / Z over the reservoirs whose surface sees the chosen light. The visibility tests go through lightVisibility, the one rule of every pass, so an atlas light's test ends at the shadow atlas's bounds (ShadowAtlas.Bounds, tileShadow) wherever its 5 x 5 texels agree, and only pixels near a shadow edge take the gathers. The user asked about screen tiles of 16 x 16 pixels for this; the atlas bounds already decide each pixel at a finer grain, so no screen-tile pass. The default keeps 2 neighbors. The merge waits for the combine's GPU time, its bias on the three views, new screenshots and its review. A GPU test of a scene with thin shadows under the default fails if they wash out.

**2026-11-03T22:50:15Z**

2026-11-03: the first run (46d28d1d-5d6f-4186-b518-019afd9643f0) ended at the fork after its fix stage, head 253e5f149. The continuation that builds the visibility-aware combine is workflow run 5af508ba-4e08-4585-9c48-f5b9a8ae3478 (version 869b0b07-3413-4b06-8707-a88a91bf6e91), on the same worktree. It first merges main for the motion vectors' far-depth fix.

**2026-11-04T11:52:04Z**

2026-11-04: the user decided to merge the shadow-aware combine now and fix after. The sparkle and grain bug (ter-x31x, filed on the branch) becomes priority 1 when the branch merges, and starts in the next free slot with the reviewer's test of bias against variance (average the hot spot over many independent runs). The added cost goes to ter-9nrc.
