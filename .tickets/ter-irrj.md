---
id: ter-irrj
status: open
deps: [ter-yywb, ter-guqz, ter-4xkz, ter-ftnj]
links: []
created: 2026-10-20T18:45:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-xr2b
tags: [demo]
---
# Live world part 5: the way down and the landing pad

Part 5 of docs/plans/live-world.md. Build DescentFlight, DescentPlan and DescentGuidance (the deorbit burn, the coast, the powered braking and the vertical descent), the entity landing-pad, landing-pad-camera, the touchdown on the tail, the slot's release, the ramp's request, and the states OnTheLandingPad and AwaitingTug (the ramp's grant sends TowRequest). It adds UndockedAfterPass, BurnEndAfterPass and TouchdownAfterPass to LiveTimeline. It comes after the docking (ter-ftnj), because the docking raises the thrusters that every law uses. See "The way down" and "The landing pad". Tests: "Part 5: the way down and the landing pad".

