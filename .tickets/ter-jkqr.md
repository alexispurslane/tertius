---
id: ter-jkqr
status: closed
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


## Notes

**2026-10-27T06:41:13Z**

Merged (13834731). Every linear body velocity on the seam belongs to the body's origin; the Jolt backend converts at every crossing (BodySnapshot.OriginVelocity, PointVelocity). The numeric frames' mass sums use the center of mass. blue-danube.json was regenerated: four filmed phases changed by 1 to 13 frames. Coverage of the changed lines 98.2 percent: the misses are moved code, the recovery when Jolt's body creation throws and the early return for an attached part in AddNumericBodies. Full Checked on main after the merges: 8,938 total, 0 failed.
