---
id: ter-6r8c
status: open
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
- The scenes, the benchmark (1,000 Kepler frames without bodies in WorldTickBenchmarks, with the time of the tick and of the sync point before and after), and the sections of ARCHITECTURE.md that this changes.
- In this ticket the enum stays only to choose a hull's motion, so Kepler and Spin both mean a kinematic hull. Until ter-ru5u, a change of motion is SetFrameDriver plus a swap of components with World.AddComponent and World.RemoveComponent.
- Tests: the changes in the doc's section 'Tests' that concern samples, components and checks, and the new tests 1 to 7 and 9. The tests about drivers and state dumps belong to ter-ru5u, and new test 8 to ter-zg77.
