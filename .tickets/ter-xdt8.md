---
id: ter-xdt8
status: in_progress
deps: []
links: []
created: 2026-10-18T23:50:45Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, physics]
---
# Rails motion for every entity: samples keyed by entity, MoveKinematic as a pose-only sample

User decision (2026-10-18): frames follow their owner entity, and the rails components move their entity, so rails motion must apply to any entity, not only to frame owners. Today Self.MoveOnRails refuses an entity that owns no frame, the sample table lives in the frame tree keyed by frame, and physics reads samples only for hulls (owner.OwnedFrame). Scope: (1) one motion sample per entity, keyed by entity with the generation check; a frame's step record reads its owner's sample; (2) every kinematic body with a sample follows it and ends its step exactly on it, hull or not; an entity without a body and without a frame has its transform row follow the sample; (3) MoveKinematic becomes a sample that gives only its end pose, so one intent drives every kinematic body, and physics works out the velocity at the step start as Jolt does now; (4) the docs. The design section goes into docs/plans/rails-components.md first, with the analogue of each new value, every reader and writer with the step it needs, and the flow per kind; open forks go to the user before any builder starts. ter-zg77 depends on this ticket and covers every fast kinematic body, not only hulls. Build it after ter-sn28 merges, because both change the frame tree and the sync point.


## Notes

**2026-10-19T00:38:55Z**

Design settled with the user (2026-10-18), draft in the session scratchpad xdt8/design.md: the motion intent is PhysicsIntent.Move, a custom union with three cases: an end pose (MoveKinematic), a full sample (MoveOnRails) and a steer target (SteerTo, for a dynamic body: physics gives it the velocities that carry it to the pose over the step, today's dynamic branch of MoveKinematic). Fork 1: a field of PhysicsIntent (it replaces Target). Fork 2: the frame tree row keeps what FrameTree.Follow writes today plus a follow-kind byte; the third sample buffer goes. Fork 3: every kinematic body moves only by motion intents and teleports; SetVelocity and SetAngularVelocity throw for any kinematic body. The guard allows end pose and full sample for kinematic bodies and entities without a body, and steer only for dynamic bodies.

**2026-10-19T00:59:57Z**

Amended with the user (2026-10-18): a frame's proper acceleration is derived by one rule for every step record (acceleration minus the gravity at its place, in the frame's axes), never written. MotionSample loses its ProperAcceleration field, FrameSpin.Sample and SpinMotion lose their proper-acceleration input, and FrameTree.ProperAcceleration becomes a value read. The demo surface branch (ter-na4f) must then drop Shuttle.Park's write of it.
