---
id: ter-l8ay
status: open
deps: []
links: []
created: 2026-10-20T07:48:29Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-s9m0
tags: [physics, tests]
---
# Test a following frame inside a turning frame on the real World

Found by the merge of main into ter-1bnq (2026-10-20). The end-of-step terms of a numeric home (HomeSteppedVelocityOverPlan, from ter-zsku) compose with the turn's part of the instant velocity (InstantAtEnd, from ter-1bnq) in JoltPhysicsBackend.InstantVelocity, HandedOffVelocities and TryHullRecord. Every line runs in the tests, but no physics test runs the two together: a following frame whose semantic parent turns. The merge checked the composition only by algebra. Add on/off tests (numeric frames on and off) on the real World and Jolt: a fast or far kinematic hull with a following frame in a turning frame (for example Earth's surface frame, or a large spinning frame), a crate that crosses its bubble in the step in which the hull stops or starts, and a dynamic ship inside it; each must move as it does without numeric frames.

