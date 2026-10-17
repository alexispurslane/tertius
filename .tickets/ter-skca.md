---
id: ter-skca
status: open
deps: [ter-4psc]
links: []
created: 2026-10-17T21:44:05Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, architecture, docs]
---
# Write the design of rails motion as entity components

Part of the epic on frames that follow their owners. Read the epic first. Write docs/plans/rails-components.md: settle the six points of the epic, with the user at each real fork. Then adjust the tickets of the epic to the design.


## Notes

**2026-10-17T21:57:32Z**

2026-10-17, design sketch from the chat with the user:
- A rails component (for example KeplerMotion) holds its exact description as a function of time (orbit elements and epoch, or spin rate and phase), not integration state. On each tick it computes a motion sample for the end of the next step with the rails library and hands it over in one intent (for example MoveOnRails). The engine never calls into components at the sync point: it reads the intent.
- The sample: the pose at the end of the next step (the kinematic target), the chord velocity over the step, the instant velocity at its end (for reads in a frame), the acceleration over the step (an orbit frame falls with it), and the exact angular velocity and angular acceleration in doubles (spin gravity needs them; Jolt's float values from a kinematic target are not exact enough).
- Jolt reaches a kinematic target only to the float rounding of its velocity (about 4 um per step at 7.6 km/s), so the sync point puts a body on rails exactly on its target. The place then still follows the orbit to the last bit.
- A frame without a body takes the same intent on its entity's transform. A static frame needs no component: gameplay teleports its owner. The MotionDriver enum and BodyDesc.FrameHullMotion go: a frame follows its owner, and the owner body's motion says how.
- New point 7: today no API changes a body's motion after spawn, except through frame drivers (BodyDesc.FrameHullMotion). A driver change, such as a station that starts its engines and goes from kinematic on rails to dynamic, then needs a SetMotion on the body or a respawn of it.

**2026-10-17T22:38:46Z**

2026-10-17: the design doc docs/plans/rails-components.md is written and final. The user decided questions 1 to 4 (how rails motion starts, how a body changes its motion after spawn, where the checks of rails motion live, the proper acceleration of a frame on rails). Questions 5 and 6 (where the motion sample lives, what a state dump records about a frame) took the doc's recommendation. The epic's tickets are adjusted: ter-k8cg, ter-6r8c, the new ticket ter-ru5u (a body's motion changes after spawn, and the MotionDriver enum goes) and ter-zg77.
