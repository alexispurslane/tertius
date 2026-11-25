---
id: t88-aomd
status: open
deps: []
links: []
created: 2026-11-25T05:33:20Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, characters, demo]
---
# A character standing on a moving kinematic floor stands one tick of the floor's motion behind it: 0.33 m above the lift's floor at 20 m/s

Measured on ter-9wwx (2026-11-25), in demo-exit and in the long run of scripts/blue-danube.json: on the lift's ride down the spoke, the person's center stays StationExit.Standing + v/60 above the lift's floor in every tick, along the ride's direction. At the cruise of 20 m/s that is 0.333 m above where it stands at rest. DisembarkTests.ThePersonStandsOnTheLiftForTheWholeRide pins it to 0.02 m as a known fault. Across the floor the person keeps within 2 cm of its place, and physics reports it standing in every tick but the two of the frame hand-off. The cause is in the engine's character step: physics moves a standing character with its ground's velocity of the step before, and collides it against the kinematic floor at the floor's pose at the start of the step, so the character stands where the floor was one tick earlier. The Demo writes the lift's pose and velocity in every tick (Lift), so the lift itself follows its profile to 1 mm (DisembarkTests.TheLiftFollowsItsProfileInTheStationsAxes).

Visible effect: in the film ride-side (scripts/demo-exit.json, ride-side-camera on the lift beside its floor), the soles stand on the floor at rest and in the first seconds of the speed-up. From about 16 s into the ride, at cruise, the soles hang clearly above the floor, and the feet's shadows on the floor are gone. The film ride, which looks down the shaft past the person, does not show the gap.

Wanted: a physics test of a capsule character that stands on a kinematic box that moves at a constant velocity, which pins the gap; then a character step that moves the character with its ground's motion of the same step, so the gap is under the walk's normal float.

