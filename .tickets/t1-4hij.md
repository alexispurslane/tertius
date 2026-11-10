---
id: t1-4hij
status: in_progress
deps: []
links: []
created: 2026-11-10T09:34:16Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# The direct light trails and flickers around moving lamps in the lamp hall

The survey split (ter-6k3k, /private/tmp/tertius-split-20261109/results/split) shows that the direct light makes 94 % of the blotch of lamp-hall-4-spot-moving (0.058 middle, 0.054 end, limit 0.04) and nearly all the flicker of lamp-hall-16-moving (0.0075) and lamp-hall-64-moving (0.0189, limit 0.006). The bounce light and the reflections do not cause it. Find the cause with experiments (FSR 3.1 history, local shadow tiles, per-tile light budget, other) and fix it.

## Acceptance Criteria

lamp-hall-4-spot-moving: direct blotch below 0.04 at the middle and end frames. lamp-hall-16-moving and lamp-hall-64-moving: flicker at most 0.006. No other lamp-hall film, bay-sunbeam or rect-lamps gets worse by the same split. GPU frame span of lamp-hall-64-moving rises by at most 0.2 ms (Metal System Trace).


## Notes

**2026-11-10T10:42:04Z**

Acceptance adds rigs-crowd (moving figures, no moving lamps) to the films that must not get worse. Step 1 split it: whole 0.024 and 0.028, direct 0.025 and 0.029 (scene luminance), bounce and reflections under 0.01. Report its split on main and on the branch.

**2026-11-10T11:47:24Z**

Diagnosis, 2026-11-10 (main 35f37171f, probe with the direct light alone, fixed exposure, scripts in /private/tmp/t1-4hij):

| Film, configuration | Direct blotch middle, end | Flicker, split measure | Flicker away from emitters |
|---|---|---|---|
| 4-spot, FSR | 0.068, 0.060 | 0.0069 | 0.0104 |
| 4-spot, native 1280x720 | 0.000, 0.000 | 0.0003 | 0.0004 |
| 16, FSR | 0.027, 0.025 | 0.0077 | 0.0020 |
| 16, 96 tiles | 0.020, 0.017 | 0.0079 | 0.0022 |
| 16, native | 0.018, 0.015 | 0.0054 | 0.0007 |
| 64, FSR | 0.041, 0.030 | 0.0195 | 0.0028 |
| 64, 384 tiles | 0.024, 0.024 | 0.0200 | 0.0031 |
| 64, local shadows off | 0.020, 0.019 | 0.0192 | 0.0024 |
| 64, native | 0.030, 0.018 | 0.0175 | 0.0008 |

1. The 4-spot trail is FSR 3.1's history: without FSR the film's direct light equals the still's exactly. The heat map shows the trail behind the moving edges of the spot cones and the pillar shadows on the still floor.
2. The split's flicker of the 16 and 64 halls is the moving lamp bulbs: it stays at 0.0175 without FSR and with every lamp shadowed, and falls to 0.0007 to 0.0031 away from emitters. The split's signal_flicker keeps the pixels near emitters, which the survey's flicker() leaves out (survey: 0.0029 and 0.0036). No renderer change can bring the split measure of those films to 0.006.
3. Tile holders change 0 to 2 times in 180 frames. A bigger atlas lowers the 16 and 64 blotch (0.027 to 0.020, 0.041 to 0.024), because the holder margin gives the film and its reference still different holders. That is the hysteresis by design, not a fault of the film.
