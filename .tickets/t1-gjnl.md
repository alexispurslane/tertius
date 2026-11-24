---
id: t1-gjnl
status: open
deps: []
links: [ter-fe63]
created: 2026-11-23T23:46:30Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering]
---
# No reflection guide mass below the shading pixel's plane (ter-fe63 design 2)

Deferred alternative (Design 2) from the 2026-11-23 comparison for ter-fe63's guided reflection share. Held in reserve by orchestrator decision (56feef7d5, 2026-11-23 18:25): do not start unless a surface under Design 1 (per-tile adaptive guided share) shows a visible loss that the adaptive share cannot remove.

## System design

- Cause addressed: own-plane guided draws only (78.6 % of guided draws on bay sunlit patch, 82.9 % on rect-lamps floor). Does not address box misses (t1-cyhy) or missing emissive lamps in the guide (t1-d9k3).
- State owned: none. No new stored image or history.
- Choke point: traceFrom in screen_trace.glsl computes the guide mass strictly above the shading pixel's geometric plane at one coarse pyramid level (about 60 cells), draws the guided target from those above-plane cells in proportion to light, and rescales guidedDensity by guideTotal() / above so the one-sample mixture density stays exact.
- Both Design 1 and Design 2 share: one-sample mixture and exact density, the existing sent-diffuse-light pyramid, the exact-count time pattern, the roughness ramp, the ray record's guided density component, and no change to the diffuse kind.

## Type / shader signatures

In screen_trace.glsl:
- float guideAbove(TracingPixel p): sum over one coarse level of the light pyramid (~60 cells) of each cell's light whose surface point lies above p's geometric plane.
- GuidedTarget guidedTargetOfAbove(ivec2 pixel, TracingPixel p, float above): today's pyramid descent starting from coarse cells above p's plane.
- mixtureDensity(share, lobeDensity, guidedDensity * guideTotal() / above).

## Comparison that deferred this ticket (ter-fe63 replay, 2026-11-23)

One draw's relative variance (variance over squared mean; lower is better):
- Nose's box: today 7.15, fixed 0.25 1.41, Design 1 alone 1.11 (s=0.58), Design 2 alone at 0.25 1.47, Both 1.09.
- Bay's right wall: today 5.76, fixed 0.25 3.66, Design 1 alone 2.76 (s=0.69), Design 2 alone at 0.25 3.60, Both 2.73.
- Wall beside the stripe: today 3.67, fixed 0.25 4.52, Design 1 alone 3.67 (s=0), Design 2 alone at 0.25 3.30, Both 3.28 (s=0.20).
- rect-lamps' floor: today 0.391, fixed 0.25 0.834, Design 1 alone 0.391 (s=0), Design 2 alone at 0.25 0.782, Both 0.391 (s=0).
- Mirror plate: 0.084 across all.

Cost per traced reflection ray: ~60 light reads, ~60 depth reads, and ~60 plane tests (about 120 reads per ray, for lobe draws too), versus 1 filtered read of the tile share image for Design 1. Adding Design 2 on top of Design 1 improves only one surface (wall beside the stripe) by at most 11 % in variance (5.5 % in deviation, upper bound before box misses where 92 % of above-plane draws miss).

## Acceptance and out of scope

- Do not start unless Design 1 fails to prevent a visible loss on an own-plane surface and the orchestrator re-authorizes this ticket.
- Out of scope: box-miss target model (t1-cyhy), emissive lamp guide light (t1-d9k3), and adaptive diffuse share.

