---
id: ter-jkqr
status: open
deps: [ter-dtw2]
links: []
created: 2026-10-11T13:52:10Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-se84
tags: [physics]
---
# Report every body velocity at the body origin, not at the center of mass

Jolt reports the pose of the origin with the velocity of the center of mass, and the World reads it as the velocity of the origin. See The velocity point in docs/plans/real-physics-in-tests.md.

