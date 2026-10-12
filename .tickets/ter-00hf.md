---
id: ter-00hf
status: open
deps: []
links: []
created: 2026-10-12T11:31:11Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, characters]
---
# A standing character reports a velocity with one step of gravity in it

Found in ter-upyg. Physics reports the velocity that it set for a standing character, which holds the step of gravity that presses the character onto the floor: -0.1635 m/s along its up at 9.81 m/s². The floor stops that motion, so the character does not move with it. Components read it through Self.Velocity: a crate that a standing person throws inherits the 0.16 m/s downward, and dumps show it for every standing person.

Design to decide: report the velocity along the floor (ground velocity plus the walk) for a standing character, or the velocity at which the character really moved in the step.

## Acceptance Criteria

A character that stands still on a static floor reports a zero velocity, and a thrown crate inherits none of the press.

