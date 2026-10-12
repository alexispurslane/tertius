---
id: ter-f28b
status: open
deps: [ter-2fn5]
links: []
created: 2026-10-12T22:20:38Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-s02r
tags: [physics, frames]
---
# Cluster bodies into numeric frames automatically: create, merge, split, drain and collect

Design: docs/plans/numeric-frames.md. Step 4. Rules and starting values are in the doc: a speed threshold and a distance threshold, each with hysteresis, that choose a falling or a resting frame, the change between the two modes, a merge test that looks ahead in time, a split margin and cooldown, drains in place that count sleepers as zero, and collection. All decisions at the sync point, in a fixed order.

## Acceptance Criteria

Two runs with the same input give the same clusters in the same order. No merge and split thrashing in the spacewalk and throw scripts.

