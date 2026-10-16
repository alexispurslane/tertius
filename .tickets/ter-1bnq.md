---
id: ter-1bnq
status: open
deps: [ter-ediu]
links: []
created: 2026-10-16T23:00:45Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-s9m0
tags: [physics, frames]
---
# Apply the rotation terms to bodies in every turning frame

Design: docs/plans/spin-gravity.md. Section 'The rotation terms'. The exact update in doubles beside the gravity force, the inertial angular velocity summed along the chain, the angular acceleration from the change between steps, a wake of sleepers on a change of angular velocity, the contact velocity change turned by the previous step's rotation, and a double velocity shadow with position feedback. Every turning frame: Spin frames, Kepler frames that face their parent, frames that physics drives and that turn. Re-baseline WorldSpinFrameTests and HandOffTests.BallThrownOutOfSpinningPhysicsShip, which encode 'no Coriolis'.

