---
id: ter-b2da
status: open
deps: []
links: []
created: 2026-11-01T16:01:19Z
type: epic
priority: 1
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, reflections, screen-space-light]
---
# Screen-space artifacts: correct reflections and screen-space light before more optimization

Design: docs/plans/screen-space-artifacts.md. The user decided on 2026-11-01 to pause the performance epic ter-pq5y and fix the artifacts of the reflections and the screen-space light first, because the fixes change the starting point of the optimization. Each effect gets a diagnosis ticket that attributes every artifact to its stage before any fix is designed.

## Acceptance Criteria

Demo-exit's nine films and the review scenes of both effects show no artifact that comes from a fault of the implementation. Each remaining limit of a screen-space method is recorded in ARCHITECTURE.md with the scene that shows it.

