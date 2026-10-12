---
id: ter-l6oo
status: in_progress
deps: []
links: []
created: 2026-10-11T23:58:26Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [physics, characters, slice]
---
# A person who drifts out of the ship's bubble loses all control

Found in ter-f8sh. A person who drifts out of the airlock keeps its momentum. Past the ship frame's 100 m bubble, a hand-off puts the person into the frame earth. There Earth's gravity, about 8.7 m/s² at 400 km, is far above FrameTree.ZeroGravityThreshold, so physics treats the person as falling: up turns away from Earth, MoveCharacter and Jump do nothing in the air, and PersonControl stops thrusting, because it reads the person as under gravity. The person then orbits near the ship with no control, and it does not collide with the ship's hull in earth, which is on the FrameHull layer, until it drifts back into the bubble.

In orbit, a free-falling person is weightless in every frame. The effective gravity of a character is the frame's field minus the frame's proper acceleration, and a Kepler frame such as earth has no proper acceleration, so the rule cannot see that the person falls freely.

Design to decide: a character in the air with no support counts as weightless whenever its frame's field pulls it on a free-fall path, or the person's frame stays the ship's for a person, or the ship's bubble grows.

## Acceptance Criteria

A person who drifts past the ship's bubble can still thrust, and comes back into the ship's frame under its own control, or the design records why it cannot.


## Notes

**2026-10-12T00:39:04Z**

Moved into the vertical slice at P1, at the user's request: the hand-off from the ship's frame to its parent is the slice's big test. A trace (2026-10-11) confirmed the bug: the person left the ship's frame at about 103 m, the hand-off kept its position and its 3.14 m/s drift, and its up turned by only 0.001, but holding S in earth changed nothing.

**2026-10-12T01:48:38Z**

Design A (User): a character is weightless whenever physics does not stand it on walkable ground, in any frame and any gravity. Physics reports for each character whether it stood, and PersonControl now walks only while self.Body.Standing is true, and fires its thrusters otherwise. The script scripts/spacewalk.json and its test SpacewalkSliceTests show the fix: the person drifts out of the ship's bubble into earth, thrusts toward the ship there, comes back into the ship's frame and lands on the chamber floor through the hatch. Jolt keeps velocities as floats, so at orbital speed a thrust in earth turns a little sideways: 0.003 m/s for a thrust of 0.4 s. So the script thrusts only briefly in earth. The ticket stays open for the user's review.
