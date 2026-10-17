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

Part of the epic on frames that follow their owners. Read the epic and its design doc first. A refactor without a change of behavior: the Kepler orbit's place, velocity and angular velocity at a time (KeplerOrbit, AngularVelocityAt), the orbit's chord and acceleration (NumericFrameRules.OrbitChord, OrbitAcceleration), a spin's turn (FrameSpin), and the prediction of a rails frame at the end of a step (FrameTree.KeplerMotion) move into static methods in one namespace under the frame tree code. Their tests move with them. Every suite stays green.

