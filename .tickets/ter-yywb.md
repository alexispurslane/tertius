---
id: ter-yywb
status: open
deps: [ter-tzf6, ter-qipk]
links: []
created: 2026-10-20T18:45:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-xr2b
tags: [demo]
---
# Live world part 1: the shuttle pilot as one flat state machine

Part 1 of docs/plans/live-world.md, "Order of the work". Build ShuttlePilot with one PilotState union for the whole cycle, AscentFlight, RendezvousFlight, and the order Depart in place of Ignition. The rendezvous starts from the insertion. This replaces the layers of the flight program that the station step (ter-tzf6) built. Until part 8, the long script posts Depart where it posts Ignition today. Update the text on layers in docs/plans/blue-danube-demo.md. Tests: "Part 1: the shuttle pilot" in the design. Demo work only, no engine change.


## Notes

**2026-10-20T20:43:46Z**

From the coverage of ter-tzf6: two halts of RendezvousAutopilot have no test, the shuttle in a frame other than its station's (it needs a hand-off) and a hull without mass (RendezvousAutopilot.cs, lines 295 to 302). This part rewrites the autopilot as RendezvousFlight, so the leg's tests cover both halts.

**2026-10-20T23:09:16Z**

After ter-qipk: build PilotState and PilotEvent as custom unions, PilotState.Next with part 1's 8 rows and its tests (PilotNextTests, PilotStateTests), and the delegates AscentFlight and RendezvousFlight from today's AscentAutopilot and RendezvousAutopilot, with their tests ported. A change of state by a report flies the new state one tick later than today's code, so the flight moves by one tick at four points, and the long script is written again. See The pilot, One tick, and Part 1 in Tests and screenshots.
