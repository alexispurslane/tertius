---
id: ter-hjr7
status: open
deps: []
links: []
created: 2026-10-20T12:31:47Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, character]
---
# A dynamic hull's accelerometer does not read the press of a character that stands on it

Found by ter-1b21 part 4 (2026-10-20). A standing character presses on a dynamic ground body through Jolt's weight input (since ter-1b21: V5 plus the Coriolis term, along the ground's normal). A plain dynamic body moves under that press. But a dynamic hull's accelerometer, FrameTree.ProperAcceleration of a frame owner, does not read it: a 700 kg plate set up as a hull read only g cos 40 along its normal, without the person's 0.98 m/s^2. The behavior existed before ter-1b21. Find where the hull's proper acceleration (ComputeProperAccelerations, V4) leaves out the character's impulse on the hull, and pin it with a test of a person standing on a dynamic hull.

