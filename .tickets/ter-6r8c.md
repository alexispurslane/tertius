---
id: ter-6r8c
status: in_progress
deps: [ter-k8cg]
links: []
created: 2026-10-17T21:44:05Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, physics]
---
# Kepler, Spin and Static motion become components, and every frame follows its owner

Part of the epic on frames that follow their owners. Read the epic and its design doc first (docs/plans/rails-components.md, 'Order of the work', item 2). The driver state moves onto components of the frame's entity, which call the rails library and hand the engine a motion sample each tick. A frame follows its owner for every kind of motion, as it follows a Physics hull today.

Scope:
- The sample table, indexed by the frame's index, with three buffers (written, applied and followed). Self.MoveOnRails, Self.TimeAt and Self.OwnedFrame.
- KeplerMotion and SpinMotion in src/Tertius.Simulation/Components/, with their checks (a KeplerMotion whose parent has no G·M holds its frame still and logs once, and a rails component on a body that is not kinematic logs once and writes no sample), the ProperAcceleration field of the sample, and the start of a new frame (it holds still at its spawn pose for its first step, and the first run of the component puts it on its rails).
- FollowOwners in step 2 of the sync point, and the end of step 6b (AdvanceDrivenFrames).
- Physics reads the samples for targets and predictions, and snaps bodies on rails to their targets. DriveKinematicHulls, PredictMotion and OrbitMatchesTree go.
- The orbit frame follows its anchor's samples, and NumericBody.Orbits reads 'the hull is kinematic'. The tree's orbit and spin, PlaceOnOrbit, MoveOnOrbit, KeplerMotion, SetFrameOrbit and SetFrameSpin go.
- Static hulls take teleports, and owners without a body write their transforms.
- One motion record per frame (user decision 2026-10-18, design section "One motion record per frame"): every frame takes a MotionSample step record in the one loop FollowOwners, whoever fills it (physics for a dynamic hull and a static teleport, the component for rails, the sync point for a transform write). FrameState goes, physics predicts every frame from its step record, the tree keeps the instant velocity for every frame, and the followed buffer holds the record of every frame that moved.
- The scenes, the benchmark (1,000 Kepler frames without bodies in WorldTickBenchmarks, with the time of the tick and of the sync point before and after), and the sections of ARCHITECTURE.md that this changes.
- In this ticket the enum stays only to choose a hull's motion, so Kepler and Spin both mean a kinematic hull. Until ter-ru5u, a change of motion is SetFrameDriver plus a swap of components with World.AddComponent and World.RemoveComponent.
- Tests: the changes in the doc's section 'Tests' that concern samples, components and checks, and the new tests 1 to 7 and 9. The tests about drivers and state dumps belong to ter-ru5u, and new test 8 to ter-zg77.

## Notes

**2026-10-18T13:13:11Z**

Before the merge (user decision 2026-10-18): change the words in ARCHITECTURE.md and docs/plans/rails-components.md. A frame and its owner are one entity with two tables: the frame tree row is the exact table (doubles: pose in the tree parent, velocity, angular velocity, proper acceleration) and the transform row is the float view of the same entity. Replace 'the owner mirrors its frame' and similar wording with 'the entity's float view is filled from its exact table'. The user rejected widening TransformRow to make it the official store (about 36 bytes per row, reverses the one-float-vector decision, the tree keeps its own copy anyway).
