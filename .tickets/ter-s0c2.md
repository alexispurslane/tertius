---
id: ter-s0c2
status: open
deps: []
links: []
created: 2026-10-27T12:40:04Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, precision]
---
# A hand-off narrows a fast body's velocity to a float in the target's own physics system

Found in the review of ter-2y6i (2026-10-27). A hand-off converts the body's state into its target frame in doubles, then narrows it: HandOffPlan.LinearVelocity is a Vector3 (src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs), and the body is created in the Jolt system of the target frame itself (WorldFor(plan.To)), where Jolt holds its velocity relative to that frame as floats. For a body that moves fast in the target, the velocity jumps once, by up to half a float's step in each component. In the launch of the demo, the shuttle's hull leaves earth-surface for earth at about 5.9 km/s: the jump is up to 2.7e-4 m/s, and the second difference of the hull's center over the hand-off's tick misses the expected acceleration by 0.0094 to 0.0104 m/s², against at most 8.9e-4 m/s² at the 20 ticks around it. A frame owner's frame takes the same float, so the frame tree holds the narrowed value from then on. The numeric update of the next sync point can move the body into a falling frame, but the narrowing has happened by then.

Carrying the plan's velocity in doubles is not enough alone, because the Jolt system of the target still holds floats at the full speed. Two directions: the hand-off lands the body in a numeric frame that moves with it, or the frame tree keeps the exact converted velocity of a hull and Jolt only the local remainder. A design pass decides.

## Acceptance Criteria

The hand-off's tick of DemoLaunchTests.TheShuttlesAccelerationInEarthStaysSmoothThroughTheHandOff passes the same tolerance as the ticks around it, and the test's separate bound for that tick is gone.

