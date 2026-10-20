---
id: ter-ftnj
status: open
deps: [ter-s9m0, ter-tzf6, ter-4xkz, ter-0pvs, ter-yywb, ter-guqz]
links: []
created: 2026-10-18T14:08:42Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo]
---
# Demo docking: the spin match, the axial entry and the landing on the barrel wall

Step 5 of docs/plans/blue-danube-demo.md. The shuttle matches the station's spin, enters the slot, and holds a co-rotating path out to the barrel wall, so its contents feel weight that grows with the radius.


## Notes

**2026-10-20T18:46:20Z**

The live world design (docs/plans/live-world.md) makes the docking the pilot's leg Docking, with its own custom union DockingState. It starts at the hold tolerances and ends parked static on the deck in the leg Docked. Its budget is 330 s, because the undocking must end before the deorbit window. The translation thrusters grow from 4,000 N to 30,000 N per axis. The attitude target takes a feed-forward angular velocity for the spin match, because the station turns at 0.313 rad/s and MaxTurnRate is 0.15 rad/s. The undocking of live world part 4 flies these laws in reverse. This ticket now waits for live world parts 1 and 2 (ter-yywb, ter-guqz).
