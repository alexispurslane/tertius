---
id: ter-s02r
status: closed
deps: [ter-06mu]
links: [ter-sv9y, ter-mpm6, ter-3pho, ter-gsvv, ter-ip1k, ter-dhkf]
created: 2026-10-12T20:08:35Z
type: epic
priority: 1
assignee: Alexis Purslane
tags: [physics, frames, design]
---
# Numeric frames: automatic free-falling frames around clusters of bodies, for precision

Design doc: docs/plans/numeric-frames.md.

The user's idea, from the end of the vertical slice: a frame today does two jobs at once. Its semantic job: a ship or a planet owns its contents through its bubble, has a hull, carries its own gravity and driver, and gameplay sees it (Self.Frame, the frame's topic, FrameChanged). Its numeric job: a local origin and a local velocity, so that Jolt's floats see small numbers. Split them: semantic frames stay explicit, as the user chose early on, and the engine creates numeric frames by itself around clusters of free-falling bodies. A numeric frame falls freely with tidal gravity, carries nothing and means nothing to gameplay, and the engine merges, splits and collects it.

Why: near the ship in the frame earth, everything moves at 7.7 km/s, and Jolt keeps velocities and many internals in floats. That caused the rounding of thrust (ter-fa7j), the character sweep of 128 m per tick (ter-r90w), and the crate jitter (ter-swgm, now fixed). Distance is not the limit inside a solar system (a double steps by 0.03 mm at 1 AU and 1 mm at 30 AU), but stacked numeric frames can also extend distance to interstellar scales later.

Questions for the design: what a cluster is, with hysteresis; when frames are created, merged, split and collected, and what that costs; how a numeric frame moves (a Kepler orbit fitted to its cluster, refitted when it drifts); what gameplay sees (an entity needs a semantic frame, the nearest meaningful ancestor, apart from the numeric frame that physics uses); and whether the same mechanism later covers distance.


## Notes

**2026-10-13T01:28:07Z**

Merged with every suite green in Debug (Core 387, Simulation 1348, Physics 345, Rendering 189, Host 506) and Release (357, 1370, 346, 189, 506). Coverage of the changed lines, Debug and Release together: 1243/1264 lines (98.3%), 677/740 branches (91.5%). NumericFrameRules 84/84 lines and 66/66 branches. The uncovered lines are mostly the guards of NumericFrameOptions. No Stryker run, by the user's choice for this push. An Opus review found 13 issues, and all are fixed, two in part: ter-sv9y and ter-mpm6 hold the rest.
