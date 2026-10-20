---
id: ter-1bnq
status: closed
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

**2026-10-19T22:28:00Z**

2026-10-19: when stage 2 ends, merge main into ter-1bnq-rotation-terms. Main then holds ter-zg77 (following frames for every fast kinematic body, the rename Orbiting to Following, and a wake rule: the members of a following frame wake when its anchor's push changes, FrameTree PushInStep and JoltPhysicsBackend.Step.cs) and soon ter-zsku. Seven files conflict, among them FrameTree.Numeric.cs, JoltPhysicsBackend.Step.cs and World.Tick.cs. The wake rule of ter-zg77 and V8 of the spin design are two wake rules for one cause, a change of the acceleration that a frame's members feel, and the merge must make them one rule.

**2026-10-19T23:43:42Z**

2026-10-19, stage 2 part 6: the design accepted a cross term of the linear gravity and the turn in the two-turn update, estimated at 2e-7 m/s per step. Inside a frame at rest in a spinning station the linear gravity is the frame's push reversed, omega^2 R, so the cross term is omega^3 R dt sideways (0.026 m/s^2 at R = 50 m, 60 Hz), and a free crate in a static room left its inertial line by 2.9 cm in 1.5 s. Turning the field part by V1's step turn (R_n g dt^2) brought the miss to 1.2e-5 m. The orchestrator settled it by the design's goal (a free body keeps its inertial line): the field part turns, in the two-turn update, the chord rule's half step, a falling frame's origin and V4's split. The workflow now also merges main into the branch before its review, and makes the wake rule of ter-zg77 and V8 one rule.

**2026-10-20T00:41:25Z**

2026-10-19, stage 2 part 8: an intent's force in a turning frame (a ship's thrust, a jetpack) kept the cross term that the ruling on part 6 removed for the field. Jolt adds the intent force in the frame's axes, unturned. At 50 m in the 0.31321 rad/s station with a thrust of 4.9 m/s^2, a ship drifted 0.026 m/s^2 sideways, 0.11 m off its path in 3 s, linear in the thrust. Turning the force by V1's step turn in ApplyTurningUpdate (added to ahead, so the shadow's chord carries it) kept the path within 5e-5 m, and the accelerometer reading did not change. The orchestrator settled it by the same rule as part 6 (a body's path in inertial space is exact): an intent's force turns too, and builder 9 applies the rule to every other force fixed in the frame's axes.

**2026-10-20T07:48:29Z**

2026-10-20, merged. Stage 2 was built in 9 parts by 9 builders, reviewed (12 findings, all fixed or recorded), and merged with main twice (the second time for ter-zsku, with no test changed). Coverage of the changed lines in src/ (Core, Simulation, Physics and Host tests, Debug): 661 of 665 lines (99.4%), 276 of 294 branches. The uncovered lines need no test: VelocityShadow.Value and ToString are debug helpers; FrameTree.MotionInStep.cs line 173 is a resting numeric frame, which does not move; JoltPhysicsBackend.Step.cs line 733 clears the shadow when Jolt's damping stops a body in one step (linear damping of 60 per second or more), which no material uses. Two allocation tests fail only under coverage instrumentation, as known. Follow-ups: ter-fsst (the public felt-gravity read, which the user chose to build next), ter-04lz (the wake test's reach), notes on ter-1b21, and ter-l8ay (a following frame inside a turning frame, tested only by algebra).
