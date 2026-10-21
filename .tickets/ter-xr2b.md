---
id: ter-xr2b
status: open
deps: []
links: []
created: 2026-10-20T17:40:45Z
type: epic
priority: 2
assignee: Alexis Purslane
tags: [demo]
---
# Live world: three shuttles on a timetable between the pad and the spinning station, and free play in the demo's world

The user decided on 2026-10-20 that the demo must also be a world that the user can join live as a character on a stream, because a split screen or a cinematic alone reads as animation. The cinematic run and free play run the same world: the cinematic's script drives only its person and the cameras, and free play drives a person by the user's input through PersonControl, in real time.

Settled by the user:
- The route stays the textbook route of the station step (ter-tzf6): about 81 minutes from the ignition to the hold point, with no time warp.
- A real return to the pad: the shuttle undocks, burns out of orbit, lands on its engine on the pad, and a tug puts it back on the ramp.
- A timetable entity posts Depart to a shuttle at fixed times, every N station orbits (the Earth does not turn, so the station passes over the pad every 92.4 minutes), and shares the pad, the ramp and the station's slot between the shuttles.
- Two shuttles fly the route.
- Free play can start at the launch terminal or on the station's wheel floor.
- A seat that a player uses straps the player in.

Design: docs/plans/live-world.md, written by the design ticket. The docking (ter-ftnj) waits for that design.


## Notes

**2026-10-20T18:46:20Z**

The design (docs/plans/live-world.md) found that one cycle of a shuttle takes three station orbits, 277.2 minutes: the docking ends after the first deorbit window, and the landing falls 120 s after a pass, after that orbit's departure. With two shuttles, departures come in two orbits of every three, with gaps of 92.4 and 184.8 minutes. A third shuttle gives a departure in every orbit. The timetable keeps no table of shuttles, so the number of shuttles is a choice of part 7 (ter-51ee) only. The parts are tickets ter-yywb (1), ter-guqz (2), ter-4xkz (3), ter-ftnj (the docking), ter-o2j7 (4), ter-irrj (5), ter-z7ka (6), ter-51ee (7), ter-k05j (8), ter-dd9z (9) and ter-hro7 (10).

**2026-10-20T19:10:36Z**

The user chose three shuttles on 2026-10-20, after the finding above. One cycle takes three orbits, so three shuttles offset by one orbit give a ground departure in every orbit, every 92.4 minutes. This replaces the decision of two shuttles. The engine already spawns a dynamic hull with a spawn velocity (ARCHITECTURE.md, SpawnDesc.LinearVelocity), so a shuttle can start in the parking orbit with no engine change.

**2026-10-20T23:19:28Z**

The user decided on 2026-10-20 to postpone the live world until the Blue Danube demo is done. The demo's tickets no longer wait for any live world part. The live world's pilot (ter-yywb) later turns the demo's layered autopilots, the docking's included, into the flat state machine.
