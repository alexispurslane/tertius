---
id: ter-prnr
status: open
deps: []
links: []
created: 2026-10-21T02:43:42Z
type: bug
priority: 3
assignee: Alexis Purslane
---
# At a hand-off, a thrusting hull's force direction may lag one step more than in other steps

Found by the Earth world builder (ter-yhu8) on 2026-10-20 in the run of scripts/demo-launch.json. The shuttle thrusts at 3 g while its nose turns, and it hands off from the turning earth-surface into earth 178.4 km up. In the hand-off tick, the residual of its acceleration in earth is 5.0e-3 m/s². In other ticks the one-step lag of the nose's turn gives 2.5e-3 m/s², and about 1e-4 for the next tick's rotation. So in the hand-off step the force direction seems about two steps stale, about 1e-4 rad. No viewer sees it, and no check of the demo fails. But with a harder steer (DepartureLead 0) the residual reached 0.0154 m/s² and failed the bound of 0.01 in TheShuttlesAccelerationInEarthStaysSmoothThroughTheHandOff. Find out whether the hand-off converts the pending force with the rotation of the wrong step. This is an engine question: it goes to the user before any change.

