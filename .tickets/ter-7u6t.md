---
id: ter-7u6t
status: open
deps: []
links: []
created: 2026-10-12T13:00:57Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [demo, physics]
---
# A body that dips below 100 km falls through the planet

Found in the investigation of the crate jitter. In the orbit scene, earth-surface draws the planet but has no body, so it has no ground. A person who burns retrograde by 90 m/s or more at 400 km enters earth-surface below 100 km of altitude and falls through the planet, down to -867 km in a probe. earth-surface also turns with no centrifugal or Coriolis force, as the scope decisions accept.

Design to decide: give the planet a ground shape (a sphere, or a ground patch near the person), or keep bodies out of earth-surface in this scene.

## Acceptance Criteria

A body that falls toward the planet in the orbit scene stops at its surface, or the design records why it may not.

