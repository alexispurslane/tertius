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
