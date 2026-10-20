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
# Live world: two shuttles on a timetable between the pad and the spinning station, and free play in the demo's world

The user decided on 2026-10-20 that the demo must also be a world that the user can join live as a character on a stream, because a split screen or a cinematic alone reads as animation. The cinematic run and free play run the same world: the cinematic's script drives only its person and the cameras, and free play drives a person by the user's input through PersonControl, in real time.

Settled by the user:
- The route stays the textbook route of the station step (ter-tzf6): about 81 minutes from the ignition to the hold point, with no time warp.
- A real return to the pad: the shuttle undocks, burns out of orbit, lands on its engine on the pad, and a tug puts it back on the ramp.
- A timetable entity posts Depart to a shuttle at fixed times, every N station orbits (the Earth does not turn, so the station passes over the pad every 92.4 minutes), and shares the pad, the ramp and the station's slot between the shuttles.
- Two shuttles fly the route.
- Free play can start at the launch terminal or on the station's wheel floor.
- A seat that a player uses straps the player in.

Design: docs/plans/live-world.md, written by the design ticket. The docking (ter-ftnj) waits for that design.

