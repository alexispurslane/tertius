---
id: ter-1bnq
status: in_progress
deps: [ter-ediu, ter-xdt8, ter-6azq]
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


## Notes

**2026-10-18T14:08:43Z**

From the Blue Danube demo survey (2026-10-18): the design is silent on the proper acceleration of a ship (a frame with a dynamic hull) inside a turning parent frame. ComputeProperAccelerations subtracts only GravityApplied (JoltPhysicsBackend.Step.cs ~599-615). The weight of a landed or co-rotating ship's cargo (omega^2 r) depends on it. Settle it in this ticket, with a test.

**2026-10-19T19:27:45Z**

2026-10-19: stage 1 is done on branch ter-1bnq-rotation-terms (FrameTurn, FrameTurns, FrameMotion's acceleration terms, reviewed twice). Stage 2 waits for ter-6azq, because both change the proper acceleration code. The ticket's pointer 'Section The rotation terms' now goes to 'The two-turn update', 'Values' and 'The flow per kind of entity' of docs/plans/spin-gravity.md. Read 'the inertial angular velocity summed along the chain' and 'the angular acceleration from the change between steps' as V1 and V2 from BeginStep, and the list of turning kinds as every turning frame (FrameTurns.Turns).

**2026-10-19T21:59:47Z**

2026-10-19, stage 2 part 4: the design contradicted itself on the contact turn. Its rule kept the contact turn out of V4's Total, but its stated result is that a ship on a station floor reads omega^2 R toward the axis, and a sleeping hull's Total is V5. Builder 4 measured both: with the contact turn out, the ship read 0.0507 m/s^2 across the floor; with it in, 4.4e-5 m/s^2 across and omega^2 R within 5.1e-4 m/s^2. The orchestrator settled it by the design's stated result: the contact turn joins Total. The docs part corrects the V4 paragraph and the Decisions row of docs/plans/spin-gravity.md. The two HandOffTests of a ball in a station that faces its planet also change, which the design missed; part 8 keeps what they check and changes their scenes where the premise fails.
