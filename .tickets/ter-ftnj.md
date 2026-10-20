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

**2026-10-20T19:54:34Z**

After the second review of docs/plans/live-world.md: the docking raises the translation thrusters to 30,000 N for every law, not only for its own legs, because the rendezvous gives the push off the nose to the thrusters. So this ticket also runs BlueDanubeRendezvousRun again, corrects the four times of LiveTimeline and adds DockedAfterPass, and writes the long script again. AttitudeTarget becomes one 48-byte struct: the DQuaternion attitude, a Vector3 feed-forward angular velocity and a float MaxTurnRate. The first test of a dynamic hull that enters and leaves a turning frame's bubble is this ticket's run test, because DynamicHullRecordTests' station does not turn. If that test fails, the failure is an engine bug for its own ticket, which goes to the user.
