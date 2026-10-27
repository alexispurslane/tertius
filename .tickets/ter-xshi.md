---
id: ter-xshi
status: in_progress
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


## Notes

**2026-10-11T18:48:00Z**

The acceptance read of the orbit scene (ter-fe04) measured the effect. At 60 ticks a second, the float tick is 8.7e-10 s (5.2e-8 relative) longer than 1/60 s. The clock gains 0.29 ms per orbit of 92 minutes, 4.5 ms per simulated day (about 35 m along the orbit) and 1.6 s per simulated year. Of the 22.7 m by which the ship passes each quarter mark in scripts/orbit.json, 0.55 m comes from the float tick. The other 22.2 m comes from the script, which rounds the quarter period of 83,172.83 ticks up to 83,173. A tick rate of 64 is exact as a float, but 50 and 60 are not.

**2026-10-27T07:02:36Z**

2026-10-27: the orchestrator settled the design by the one-store rule. The one store is the tick rate. World.Time and TimeAt are StartTime + tick / (double)TickRate. DeltaTime becomes a double that is derived from the tick rate, not a second store beside a float. Jolt's float step casts at the physics seam.

**2026-10-27T07:21:24Z**

2026-10-27: the first builder stopped on a fork: the numeric-frame clocks summed dt, and 30 sums of 1/60 give 0.49999999999999994 < ThrustOnset 0.5, so the onset moved to tick 31. The orchestrator settled it: the numeric-frame clocks count whole ticks and compare as count / tick rate, the same rule as the world clock, so the onset stays at tick 30. The seam rule for Jolt: what feeds or predicts Jolt's step uses Jolt's float step; frame motion, inertial lines, Kepler places and time use the double tick.
