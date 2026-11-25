---
id: t88-hjjm
status: open
deps: []
links: []
created: 2026-11-25T09:10:12Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [performance, simulation, demo]
---
# Blue Danube's ring approach and lift ride simulate at 46 to 66 ms per tick on the Runpod worker's CPU, 3 to 4 times the 16.7 ms of a 60 Hz tick

The ter-9wwx acceptance run on Runpod (2026-11-25, commit 11161a99b, AMD EPYC 7352, 48 threads, RTX 2000 Ada) timed every frame of scripts/blue-danube.json in one process. A film step with every = N runs N ticks and then draws one frame (ScriptRunner.RunTicks), so the wall time per frame of a section with a large N is mostly simulation.

Wall ms per tick (frame-arrival times, including one draw per frame and about 10 ms per frame for the run's frame pipe):

| Section | every | ms per frame | ms per tick |
|---|---:|---:|---:|
| ring-approach | 6 | 396.9 | 66.2 |
| glide | 6 | 298.5 | 49.8 |
| ride-side | 8 | 397.1 | 49.6 |
| ride | 8 | 370.0 | 46.3 |
| glide-window | 4 | 28.6 | 7.2 |
| entry | 4 | 28.2 | 7.1 |
| transfer (orbit) | 60 | 266.8 | 4.5 |

A draw costs about 13 ms of GPU time and about 25 ms of wall time with the pipe, so ring-approach, glide and the lift ride spend about 40 to 60 ms per tick in the simulation. The other sections near the ring (glide-window, entry, the bay) spend about 7 ms. At 60 ticks per second, a windowed game could not keep real time in these sections on this CPU.

Not yet known: which system costs the time, and whether the Mac shows the same. Find out with a CPU profile of a script that starts just before ring-approach and one just before ride (ring-approach starts at about tick 278,000 and ride at about tick 325,000, so the profile needs a saved state or a scene that starts there).

Raw data: the ter-9wwx note of 2026-11-25 and its result folder.

