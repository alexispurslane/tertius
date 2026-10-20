---
id: ter-yywb
status: open
deps: [ter-tzf6]
links: []
created: 2026-10-20T18:45:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-xr2b
tags: [demo]
---
# Live world part 1: the shuttle pilot, one leg value for the whole flight

Part 1 of docs/plans/live-world.md, "Order of the work". Build ShuttlePilot with one ShuttleLeg union for the whole cycle, AscentLeg, RendezvousLeg, and the order Depart in place of Ignition. The rendezvous starts from the insertion. This replaces the layers of the flight program that the station step (ter-tzf6) built. Until part 8, the long script posts Depart where it posts Ignition today. Update the text on layers in docs/plans/blue-danube-demo.md. Tests: "Part 1: the shuttle pilot" in the design. Demo work only, no engine change.

