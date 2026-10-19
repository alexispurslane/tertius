---
id: ter-m13s
status: in_progress
deps: [ter-na4f]
links: []
created: 2026-10-18T14:08:42Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo]
---
# Demo launch: the ramp, the shuttle autopilot to orbit, the weightless cabin, the seat and the pen

Step 3 of docs/plans/blue-danube-demo.md. A launch ramp, a shuttle with thrusters and an attitude autopilot that flies a gravity turn to orbit, a seat in the cabin, and a pen that the floating character grabs.


## Notes

**2026-10-19T19:47:42Z**

2026-10-19: the dependency on the rails epic ter-ozm8 is removed. Its last open ticket, ter-zg77, is about following frames for fast kinematic bodies, and the launch flies a dynamic shuttle, which falling numeric frames carry. The motion intent (ter-xdt8) is merged: a hull changes from static to dynamic with Self.Body.SetMotion, and a frame's proper acceleration is derived, so the cabin of a shuttle in free fall is weightless by itself. The surface frame does not turn, so ter-vja8 is not needed (docs/plans/blue-danube-demo.md).
