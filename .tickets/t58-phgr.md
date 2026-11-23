---
id: t58-phgr
status: open
deps: []
links: [t58-np60]
created: 2026-11-22T20:09:17Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [dynamic-scale]
---
# On the first run of a probe build on a fresh card, draw 0 costs 230 to 239 ms of GPU time, and the render scale then falls to 0.53

Evidence of t0-3vwx (the same logs as the ticket on the start's fall). On the RTX 2000 Ada, the first run of each probe build in a cloud job (walk m r1, walk x r1) has draw 0 at 230.5 and 238.7 ms of GPU span: 211 to 220 ms at the render size (diffuse_rays 60.0 ms, reflection_rays 50.5 ms, direct_light 21.5 ms) and 16.9 ms before the render size. The second run of the same binary has draw 0 at 26.3 ms. Draw 1 of the first run is 33 to 35 ms. The controller answers with a fall to 0.528 at draw 11 (against 0.771 on later runs), and climbs back to 0.92 only after about 60 draws. This is why every walk log of the card falls to about 0.52 in its first 60 draws (the old job of t0-5tyj and the new one of t0-3vwx alike): the walk ran first for each binary. A first use of the shaders on the GPU (the driver's shader cache per binary) is the likely cause, not shown. Not checked: whether a game window on Linux or on the Mac pays it once per install or once per launch.

## Acceptance Criteria

The record names the cause of the first run's draw 0 (shader cache or another first use), and either a game's first frames do not pay it, or the scale controller does not take the first draw of a launch as the scene's cost.

