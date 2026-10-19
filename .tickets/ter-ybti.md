---
id: ter-ybti
status: open
deps: []
links: [ter-6htl]
created: 2026-10-19T20:57:50Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics]
---
# A fast dynamic hull is thrown at a joint of a static compound of planks

Found by ter-m13s (2026-10-19). A dynamic hull that slides fast over the joints of a static compound of planks (a curved launch ramp of 1-degree planks) is stopped and thrown at a joint, with a pitch rate of 7 rad/s. It starts once the hull moves more than 0.75 of its inner radius per step (about 90 m/s for the shuttle), when Jolt's linear cast takes over; the enhanced internal edge removal of ter-fb3w does not prevent it. The demo uses a straight ramp with one joint, crossed at 27 m/s. A curved ramp needs a fix in Physics.


## Notes

**2026-10-19T23:13:24Z**

2026-10-19, measured by ter-6htl on one static MeshShapeDesc (branch ter-6htl-curved-launch-ramp, afddb0e, LaunchRampTests): the mesh does not stop the throw. (1) At 89.9 m/s, 15.6 degrees round a 500 m curve, the sweep (CCD linear cast) stops the hull within one step (89.9 to 41.4 to 18 m/s, velocity turned 88 degrees up) and pitches it nose-down at 3.46 rad/s, where the curve turns at 0.18 rad/s. (2) At all speeds: the normals of the two corner contacts differ by 1.8 degrees, under the 5 degrees at which Jolt's manifold reduction merges manifolds, so the hull is turned by position correction (angular velocity 0.01 to 0.04 rad/s while its rotation follows the curve at 0.12 to 0.24 rad/s) and its front corner sinks 2 to 6 cm. (3) With UseManifoldReduction off for bodies whose shape holds a mesh (a reverted experiment), the hull turned with the curve, but the sweep still kicked the nose up at 91.4 m/s and again at 113 m/s, so the sweep has a second cause. (4) With an 8 m strip, wider than the 6 m hull, no throw up to 143 m/s, but a 5 to 6 cm sink, yaw and roll wobble up to 0.4 rad/s and an energy gain (118.5 m/s where about 107 m/s is expected), which points at the active boundary edges at the strip's sides. The user kept the straight ramp, so this bug has no demo need now.
