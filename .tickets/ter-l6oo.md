---
id: ter-l6oo
status: open
deps: []
links: []
created: 2026-10-11T23:58:26Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, characters]
---
# A person who drifts out of the ship's bubble loses all control

Found in ter-f8sh. A person who drifts out of the airlock keeps its momentum. Past the ship frame's 100 m bubble, a hand-off puts the person into the frame earth. There Earth's gravity, about 8.7 m/s² at 400 km, is far above FrameTree.ZeroGravityThreshold, so physics treats the person as falling: up turns away from Earth, MoveCharacter and Jump do nothing in the air, and PersonControl stops thrusting, because it reads the person as under gravity. The person then orbits near the ship with no control, and it does not collide with the ship's hull in earth, which is on the FrameHull layer, until it drifts back into the bubble.

In orbit, a free-falling person is weightless in every frame. The effective gravity of a character is the frame's field minus the frame's proper acceleration, and a Kepler frame such as earth has no proper acceleration, so the rule cannot see that the person falls freely.

Design to decide: a character in the air with no support counts as weightless whenever its frame's field pulls it on a free-fall path, or the person's frame stays the ship's for a person, or the ship's bubble grows.

## Acceptance Criteria

A person who drifts past the ship's bubble can still thrust, and comes back into the ship's frame under its own control, or the design records why it cannot.

