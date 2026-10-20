---
id: ter-fsst
status: open
deps: [ter-1bnq]
links: []
created: 2026-10-20T07:42:03Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-s9m0
tags: [engine, physics, demo]
---
# A public read of the gravity that a body feels in a turning frame, for the last finished step

Found by the review of stage 2 of ter-1bnq (2026-10-19). The user chose to build it right after stage 2 merges. Game code has no public read of the felt gravity with the rotation terms: ter-1bnq made FrameTree.EffectiveGravity internal, so the demo's Clip and Passenger now read Gravity(frame).AtPrecise(p) - ProperAcceleration(frame), the field minus the frame's proper acceleration. They leave out the centrifugal and Euler terms, up to 0.27 m/s^2 at the seat while the attitude autopilot stops the cabin's turn, so the clip lets the pen go about 1.25 s after engine cut while the cabin still turns. The design (docs/plans/spin-gravity.md) says this read gets its own name and describes the last finished step. It can be composed from stored values: AngularVelocityRelative(frame, FrameHandle.Null) gives omega and AngularAccelerationRelative(frame, FrameHandle.Null) gives alpha from V3 and the rows; the read is the field at the place, minus the proper acceleration, minus omega x (omega x r), minus alpha x r, turned into the frame's axes. Then move Clip and Passenger onto it, so that the clip waits until the turn settles, regenerate scripts/blue-danube.json, and keep DemoLaunchTests' check of the pen's straight line in inertial space.

