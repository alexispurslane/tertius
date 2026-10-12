---
id: ter-fa7j
status: open
deps: []
links: []
created: 2026-10-12T02:04:49Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [physics, characters, slice]
---
# Thrust in a frame where a character moves at orbital speed drifts sideways through float rounding

Found in ter-l6oo. Jolt keeps a character's velocity as floats. In the frame earth, the person moves at about 7.7 km/s, where a float has steps of 0.49 mm/s. Each tick of thrust rounds the person's velocity, and each tick of gravity rounds both the person's and the ship's, by about a quarter of a step. In a test, 5 s of thrust in earth added about 0.04 m/s of sideways velocity relative to the ship, and the person came back 1.7 m off the hatch's center. So scripts/spacewalk.json thrusts only 0.4 s in earth. A player who steers for a minute in earth drifts by tens of centimeters per second. The same rounding causes the known lag of the ship's own orbit.

Design to decide: keep a character's velocity relative to its frame's local free-fall motion, or in double precision on the Tertius side, or move a person who leaves a ship into a frame that moves with it.

## Acceptance Criteria

A person who thrusts for a minute in earth near the ship gains no more than 1 mm/s of velocity across the thrust, relative to the ship.


## Notes

**2026-10-12T10:57:26Z**

Moved into the vertical slice at P1, at the user's request: with the ship's bubble shrunk around its hull (ter-nx32), a person outside the hull spends most of a spacewalk in earth, where this drift shows.
