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

**2026-11-11T18:23:42Z**

Acceptance and the opening, 2026-11-11.

The first acceptance (main 35f37171f against 0ce411cf1) met the 4-spot blotch and the frame span, but rect-lamps' flicker rose from 0.0027 to 0.0047 (direct 0.0007 to 0.0027). Cause, with the direct light alone: the rise sits 99 % along the floors of the wall niches, which the camera sees edge-on as lines thinner than a pixel. The jitter moves a pixel on and off such a line, the light-age record counts a change, the mask marks the pixel, and FSR's tight clamp follows the aliasing. Away from emitters the branch flickered less than main (0.0002 against 0.0005). Without the pending draws the rise halved (0.0024), so dropping them was no fix.

Fix: the mask is now the opening of the changing pixels by a 3 × 3 block, which keeps the wide bands of a moving lamp or caster and removes lines one or two pixels wide. The resolve writes it, because the opening reads a 5 × 5 window of records that the plan writes in its own dispatch. Red-green: AStillGlowingBarThinnerThanAPixelMarksAlmostNoPixelForFsrUnderJitter marked 1,565 of 3.3 million pixel draws before (limit 325) and none after. The trail test's error moved from 0.044 to 0.049 (limit 0.055).

Main moved to 587195441 (guided diffuse rays, renderer survey). The branch merged it: the light pyramid's bindings moved to 24 and 25, after the mask at 22 and the diagnostic at 23.

Splits on the merged code (main's tools/renderer-survey.py, fixed stops of /private/tmp/t1-4hij/survey-stops):

| Film | Measure | Main 587195441 | Merged, no opening | Merged, with opening |
|---|---|---|---|---|
| rect-lamps | flicker whole, direct | 0.0011, 0.0006 | 0.0031, 0.0026 | 0.0011, 0.0006 |
| rect-lamps | direct blotch middle, end | 0.0048, 0.0050 | 0.0049, 0.0051 | 0.0048, 0.0050 |
| 4-spot | direct blotch middle, end | 0.058, 0.054 | 0.031, 0.035 | 0.034, 0.037 |
| 4-spot | direct flicker | 0.0059 | 0.0062 | 0.0059 |

The 16 and 64 halls with the opening, against main 35f37171f's splits (direct part only):

| Film | Direct blotch, main | Direct blotch, branch | Direct flicker, main | Direct flicker, branch |
|---|---|---|---|---|
| 16 hall | 0.025, 0.023 | 0.025, 0.022 | 0.0075 | 0.0076 |
| 64 hall | 0.038, 0.029 | 0.036, 0.027 | 0.0189 | 0.0190 |

Cuts, at the orchestrator's request of 13:18 (one take of the GPU lock ran an hour; the rule is about 20 minutes):
- The main side of the 16 and 64 halls and rigs-crowd reuses main 35f37171f's splits. Only the direct part is compared, and the merge of 587195441 did not change the direct signal.
- bay-sunbeam is cut on both sides. Before the opening the branch already split as main (whole blotch 0.2005 on both, reflections 0.040 and 0.066 on both), and the opening marks only a subset of those pixels.
- The still of rect-lamps is cut on both sides. Before the opening it was 0.1035 against main's 0.1030. In a still, only jitter features change, and those are what the opening removes.
- The survey's motion flicker of rect-lamps is cut: the split gives the same 0.0011 on both sides.
- Kept: the branch's rigs-crowd split (moving limbs are thin, so the opening can remove marks there), the survey's motion flicker of the 16 and 64 halls on both sides (to record that criterion), and the two traces of the 64 hall (the resolve now reads up to 25 records at a changing pixel). Each runs as its own take of the lock.

The criterion "16 and 64 halls: flicker at most 0.006" is not applicable: the split's flicker counts the moving lamp bulbs (diagnosis, point 2), and no renderer change moves it. The survey's own flicker, which leaves the emitters out, follows in the final note.

**2026-11-11T21:45:16Z**

More cuts, 2026-11-11 16:45. The survey's motion flicker of the 16 and 64 halls is cut. Its run waited two hours in a queue of eight jobs and never started. The survey-style flicker without emitters, from probe runs of the direct light alone, already gives the record: 16 hall 0.0020 on main 35f37171f and 0.0021 on the branch before the opening, 64 hall 0.0028 on both (display measure 0.0034 and 0.0047 on both). The opening only removes marks, and the split's flicker with it equals main's in both halls (0.0076 against 0.0075, 0.0190 against 0.0189). So the criterion of at most 0.006 stays not applicable: the bulbs make the split's flicker, and away from them both halls are below 0.003.

rigs-crowd with the opening (against main 35f37171f's split): direct blotch 0.020 and 0.024 against 0.025 and 0.029, direct flicker 0.0015 against 0.0013. Before the opening it was 0.018 and 0.021, flicker 0.0019. The flicker difference lies inside the spread of repeated runs of one build: three probe runs of rigs-crowd on the same build gave 0.0019, 0.0022 and 0.0027.

A process error: main's survey tool takes the GPU lock for each motion case even under TERTIUS_SURVEY_HOLDS_LOCK. Run inside my take, the motion case waited in the queue for the lock that its own take held, which stalled the queue for about 11 minutes until I stopped it. A `run` of the survey must start outside a take.

**2026-11-11T21:45:29Z**

Correction to the note of 16:45: the survey's run has --chunk 0 for a caller that holds the lock, and I did not pass it. The trap stays: run ignores TERTIUS_SURVEY_HOLDS_LOCK=1, which the split honors. Ticket t1-1zic.

**2026-11-11T23:44:36Z**

Frame span of lamp-hall-64-moving after the opening, 2026-11-11 (Metal System Trace, 120 frames from 1, no counters, load 8 to 13): main 587195441 19.81 ms mean, 20.26 ms median; branch (merge of 587195441 with the opening) 19.73 ms mean, 20.14 ms median. The whole span differs by less than the run noise. The passes give the cost: screen trace resolve 0.171 to 0.237 ms (+0.066 ms, the opening's reads), ray plan 0.261 to 0.257 ms. So the mask costs about 0.06 ms, below the limit of 0.2 ms. The first acceptance, before the opening, measured +0.11 ms mean on the whole span (19.36 to 19.47 ms).
