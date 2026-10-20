---
id: ter-hpqo
status: open
deps: []
links: []
created: 2026-10-20T20:05:40Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, engine]
---
# A dynamic hull's frame prediction repeats a velocity change of the last step as an acceleration

Found by ter-wnrh. FrameTree.HullPrediction (src/Tertius.Core/Frames/FrameTree.MotionInStep.cs near line 120) predicts where a dynamic hull's frame ends the next step from the row's velocities and the last step's proper and angular accelerations. ComputeProperAccelerations (src/Tertius.Physics/JoltPhysicsBackend.Step.cs near line 1185) measures the linear proper acceleration from VelocityBefore, which CaptureHullVelocities takes before ApplyIntents. So a SetVelocity, an impulse or a steer of the last step counts as an acceleration, and the prediction repeats it over the next step. The prediction serves the conversion of intents at a hand-off (ARCHITECTURE.md, Ownership of transforms), so such an intent misses by about that velocity change times the step. No test pins the size. Decide whether the prediction should take the acceleration without the jolt of the last step's intents, or whether the miss is acceptable. An engine change, so it goes to the user first.

