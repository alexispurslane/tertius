---
id: ter-xshi
status: open
deps: []
links: []
created: 2026-10-11T18:20:16Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [simulation, determinism]
---
# Compute simulation time from whole ticks in doubles, not from a float tick length

World.DeltaTime is the float nearest 1/60 s, 0.016666667535901070 s, and World.Time is StartTime + tick * (double)DeltaTime (World.Tick.cs, TimeAt). So 60 ticks last 1.000000052 s, and simulation time gains about 4.5 ms per simulated day against whole seconds. Seen in ter-hynj (30 ticks gave time 0.5000000261 s) and in the orbit slice (ter-fe04), where the user asked to look into it for the long term.

Kepler orbits, the dumps and the window pacing (TickPacer gets world.DeltaTime) all use the same clock, so nothing disagrees today. But a timer, a script or content that counts seconds as 60 ticks is off by that amount, and the error grows with time.

Design to decide: compute time as StartTime + tick / (double)TickRate, and give components a double tick length beside the float DeltaTime (which Jolt's float step needs), or make DeltaTime a double. Check every use of DeltaTime and Time, the physics step, and Tick.DeltaTime in events.

## Acceptance Criteria

After N ticks at 60 ticks a second, World.Time is StartTime + N / 60.0 in doubles, exactly as that division gives it. The physics step and every test still pass.

