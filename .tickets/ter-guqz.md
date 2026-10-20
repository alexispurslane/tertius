---
id: ter-guqz
status: open
deps: [ter-yywb]
links: []
created: 2026-10-20T18:45:50Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-xr2b
tags: [demo]
---
# Live world part 2: the timetable and its places

Part 2 of docs/plans/live-world.md. Build the Timetable entity and component, the places Ramp, LandingPad and Slot, the messages, the departures at fixed times before each pass, the ramp's release at the climb, the slot's request at the brake, the leg case AwaitingSlot, the claims in the leg cases and ShuttleLeg.HeldPlaces (the held places derive from the leg), the release of every place on Halted, the freeing of places whose holder is dead, and the class LiveTimeline with its first four times. The timetable spawns in demo-surface in this part, with the shuttle as the ramp's first holder, so from this part on the script posts no Depart. See "The timetable" and "The timeline of one cycle" in the design. Tests: "Part 2: the timetable and the places".

