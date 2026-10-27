---
id: ter-j8lh
status: open
deps: []
links: []
created: 2026-10-27T04:54:24Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics]
---
# Measure a dynamic hull's proper acceleration at its frame origin, not its center of mass

Found in ter-jkqr. ComputeProperAccelerations (JoltPhysicsBackend.Step.cs) takes a dynamic hull's proper acceleration and its StepAcceleration from the change of Jolt's velocity of the center of mass. ARCHITECTURE.md says a frame feels what a body at its origin feels. For a hull that spins with its center of mass off its origin, the origin also has the centripetal term w x (w x r) and the Euler term a x r, with r the offset of the origin from the center of mass: 0.75 m/s^2 at 0.5 rad/s and 3 m. So the contents of such a hull feel a wrong push, and HullRecordOf's instant velocity (chord + StepAcceleration * dt/2) misses half a step of those terms. Write a test with such a hull first, then decide whether the backend adds the terms or the frame tree derives them.

