---
id: ter-xxmj
status: open
deps: []
links: []
created: 2026-10-17T02:58:53Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, frames]
---
# Static bodies of a parent frame are invisible to the bodies of a child frame

Found in ter-3euw, ter-ip1k, ter-wllb and ter-ejx9. A static body that is not a hull gets no ghost in a child frame whose bubble it touches, and a ghost is kinematic, so Jolt never makes a contact between a ghost and a static body (the engine never sets CollideKinematicVsNonDynamic). So a crate inside a ship, or one that sticks out of its bubble, passes through a static rock of space at the bubble edge, and a query that starts inside a bubble does not meet the parent's static bodies inside it (a known limit in docs/plans/queries-and-sensors.md). A kinematic pusher behind a crate that enters a frame also loses its contact until its own ghost appears. Decide whether static and kinematic bodies near a bubble get ghosts, and whether ghosts collide with them.

