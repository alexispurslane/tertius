---
id: ter-ru5u
status: in_progress
deps: [ter-6r8c]
links: []
created: 2026-10-17T22:37:50Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, physics]
---
# A body's motion changes after spawn, and the MotionDriver enum goes

Part of the epic on frames that follow their owners. Read the epic and its design doc (docs/plans/rails-components.md) first. After ter-6r8c, the MotionDriver enum stays only to choose a hull's motion, and a change of motion is SetFrameDriver plus a swap of components. This ticket gives a body a way to change its motion after spawn, makes the hull's motion come from its body, and removes the enum.

Scope (the design doc, sections 'Changes of motion', 'Numeric frames', 'State dumps and saved state' and 'Tests'):
- The change of motion: the intent self.Body.SetMotion(BodyMotion), which the entity's own components write in a tick. The motion row is double-buffered, as the collision groups are, and changes at step 4 of the sync point. Physics changes the Jolt motion type at the start of the next step (ApplyHullMotion). Only a hull can change its motion. SetMotion refuses, with an always-on guard, a body that is no hull, a dynamic motion for a hull without mass, a motion other than static for a mesh hull, and a character. A hull keeps its body id, its system and its contacts through every change.
- The hull's motion comes from its body: BodyDesc.Motion at spawn, and SetMotion after that. FrameTree.HullMotion(frame) is a BodyMotion? (null without a hull) that the World sets at spawn and at a change of motion. Since ter-4psc, FrameTree.Heir sends every frame to the dying frame's semantic parent, so it needs no hull motion. The invariants of the tree change as the design doc says. NumericBody.Fixed comes from the body's motion.
- Remove MotionDriver, BodyDesc.FrameHullMotion, BodyDesc.FrameHullProblem, World.SetFrameDriver, BodyRecord.Driver, BodyRecord.DriverVersion, FollowDriver, and the driver in FrameSpec and FrameTree.Create. The test edits are rote.
- Rewrite DriverChangeTests (eight scenarios as changes of motion and swaps of components, and its two tests of a hull that does not match its driver as tests of the SetMotion refusals) and FrameDriverTests (a test of SetMotion). FrameTreeDriverTests, FrameHullMotionTests and KeplerParentMuTests go with the driver.
- State dumps: StateDump.FrameRecord(Id, Parent, Follows), where Follows is 'rails', 'physics' or 'static'. Update StateDumpTests, OrbitSliceTests and DumpFileTests.
- Add a row to the table 'Where the user changed my proposals' in CLAUDE.md.

Depends on ter-6r8c. ter-zg77 depends on this ticket.

