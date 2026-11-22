---
id: ter-8pco
status: open
deps: []
links: []
created: 2026-11-22T21:13:03Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-6fka
tags: [rendering, experiment]
---
# Ray census: which share of the bounce light's mean and variance comes from misses, far hits and near hits

Experiment E1 of docs/research/lighting-system-review-2026-11-22/README.md, section 12. Stills rect-lamps, the cabin of orbit and bay-sunbeam on main, at the render scales 2/3 and about 0.96, 400 draws after 240 settling draws, clamps on. A patch of the survey's probe records per diffuse sample whether it hit or missed, the hit distance and its weighted light. Report per region the share of the mean light and of the variance from misses, hits beyond 2 m and hits within 2 m.

## Acceptance Criteria

A table per still and region, the probe patch committed as evidence, no engine change merged. Reading: if misses and far hits carry under about 10 % of the variance, no far-field store eases the mottle, and the step's case rests on the eye-bound faults alone.


## Notes

**2026-11-22T21:20:12Z**

2026-11-22, orchestrator: take the census in the scenes whose no-clamp floor is above the bar: cabin-light-cabin (daub 0.0285 with no clamp), bay-sunbeam (0.0149) and exit-cabin or the cabin of orbit (0.0131). Keep rect-lamps only as the control that is at the bar (0.0079).
