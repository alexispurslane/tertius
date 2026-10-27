---
id: ter-5pd7
status: open
deps: []
links: []
created: 2026-10-27T12:19:39Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, frames]
---
# A stepped dynamic hull gives its group its instant velocity, not its chord

From the review of ter-2cmo. The numeric update sums each body's chord over the step that ended into its group's chord (NumericGroup.Chord), which a new falling frame, a drain and the speed rules read. A body that physics stepped reports Jolt's chord, but a dynamic hull's velocity comes from its frame's tree row, which keeps the record's instant velocity: Jolt's chord plus half a step of the record's acceleration (JoltPhysicsBackend.HullRecordOf, from BodyRecord.StepAcceleration). So the group of a stepped hull reads a velocity half a step of its acceleration, about 72 mm/s in low orbit at 60 Hz, ahead of the chord, and a frame that drains to it leaves the hull moving at that speed in it. That is the state before ter-2cmo, and ter-2cmo left it so.

The builder of ter-2cmo first derived the hull's chord the way CreateFollowingFrame does, from the instant velocity less half a step of the gravity plus the tree's proper acceleration. That is wrong: the tree's proper acceleration comes from ComputeProperAccelerations and includes the jolt of a SetVelocity, a teleport or an impulse, (after - before) / dt, which the record's StepAcceleration leaves out. A ship that set its velocity 10 m/s higher got a group chord 5 m/s behind its real chord, its frame drained to that, and the ship moved at 5 m/s in it for one step (test AShipWhoseOwnerSetsItsVelocityInItsFallingFrameMovesWithItsFrameAfterTheDrain in WorldNumericFrameTests). A ship teleported to rest from orbit read a chord of 3.75 km/s.

A fix derives the chord from the acceleration that the record used, without the intents' jolt. Two shapes, a fork for the user: release the record's chord or its StepAcceleration to the tree (a new stored value beside the proper acceleration), or have physics release the difference. The builder of ter-2cmo had a test for this in WorldNumericFrameTests, in commit d8cb9758, AShipThatFallsPastTheCreateSpeedStaysAtRestInItsNewFallingFrame: a ship spawned at 995 m/s straight down gets a falling frame after about 0.6 s and should rest in it (to 1e-4 m/s relative to the frame's instant velocity) in the next three steps. Restore it with the fix. Also expect RailsFollowTests (a dynamic hull's chord passes 1,000 m/s one step later, Tick 4 -> 5) and the demo timings to move.

