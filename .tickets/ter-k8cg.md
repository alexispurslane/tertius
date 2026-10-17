---
id: ter-k8cg
status: open
deps: [ter-skca]
links: []
created: 2026-10-17T21:44:05Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, refactor]
---
# Move the rails math into a library of static methods under the frame tree

Part of the epic on frames that follow their owners. Read the epic and its design doc first (docs/plans/rails-components.md, section 'The rails library'). A refactor without a change of behavior.

Scope:
- Create the namespace Tertius.Frames.Rails in src/Tertius.Core/Frames/Rails/. It holds the data types KeplerOrbit, KeplerAttitude, FrameSpin and MotionSample, and two static classes, KeplerRails and SpinRails. Everything is public and pure. The records keep their IsValid checks.
- Move the functions in the table of 'The rails library': KeplerOrbit.StateAt, RotationAt, AngularVelocityAt, MeanMotion and Period, NumericFrameRules.OrbitChord and OrbitAcceleration, the body of FrameTree.KeplerMu, and FrameSpin.RotationAt and AngularVelocity. Their tests move with them: KeplerOrbitTests, KeplerAttitudeTests, FrameSpinTests, and the chord and acceleration cases of NumericFrameRulesTests.
- Add the new functions KeplerRails.AngularAccelerationAt, KeplerRails.Sample and SpinRails.Sample, and the record MotionSample (the pose, the chord velocity, the velocity, the acceleration, the angular velocity, the angular acceleration and the proper acceleration of the frame). They come with tests against today's FrameTree.PlaceOnOrbit, FrameTree.KeplerMotion and ApplySpin.
- The tree and the Demo call the library. FrameTree.KeplerMotion stays until ter-6r8c, and calls KeplerRails.Sample.
- Every suite stays green.
