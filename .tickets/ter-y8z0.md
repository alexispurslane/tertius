---
id: ter-y8z0
status: in_progress
deps: [ter-rjts]
links: []
created: 2026-11-01T16:01:19Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections]
---
# Reflections: find and attribute every artifact on demo-exit and the review scenes

Diagnosis ticket of docs/plans/screen-space-artifacts.md (section 'What a diagnosis delivers'). Known artifacts: the blocky fragments of the greenhouse's reflection on the deck (captures/artifacts-2026-11-01/deck-reflection-blocks.png), and the stair steps, hard cut lines and floating green line on the floor film (ter-eqtb, captures/artifacts-2026-11-01/floor-reflection-steps.png). Starts after ter-rjts merges, because ter-rjts changes the reflection passes. Build a gallery of captures at 1920 x 1080 (frames of each of demo-exit's nine films, and the review scenes of docs/plans/screen-space-reflections.md: reflections, mirror-balls, pbr-spheres, demo-surface, hair-rugs), find every artifact of the reflections, and attribute each to its stage (depth pyramid, radiance pyramid, half-resolution trace, upsample, own rays, cone, fallback) with probes and the A/B switches. No fix goes into the code.

## Acceptance Criteria

A ticket note lists every artifact found, with its crop, its stage, the probe evidence, whether it is a fault or a limit of the method, and the candidate fixes with their cost. The probes are not in the commits.


## Notes

**2026-11-01T17:30:42Z**

2026-11-01, from ter-xgra's review: structures that also show with the screen-space light off, so they probably belong to the reflections: bright dotted specks along the far creases of the floor and chute films, and sawtooth reflections on the floors of brake-run and floor. Not confirmed. ter-xgra's light-off gallery (captures/xgra/ssl-off after the merge) shows them.
