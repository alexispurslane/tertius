# The real takes of the region's cap (t0-qvm1)

The build caps a region's histories by the size of its drift above the bar (https://github.com/alexispurslane/tertius/wiki/bounce-light-confidence, section 8.7, "The region's cap"). This record holds the acceptance takes of the ticket's design note of 2026-11-23: real engine runs of main's code against the build. The replay that chose the form is `../t0-qvm1-region-repair-replay/`.

## The sides and the builds

- Main's code is `76a4fbfeb`, the base of the branch `t0-qvm1-region-cap`. Main after it holds only ticket notes.
- The build is `d42c8cdf3`: the cap (`d7fae25b7`) and the survey's probe patch fitted to it.
- `tools/builds.sh` makes four builds into `/private/tmp/t0-qvm1c/<name>/probe-bin`:
  - `pm` and `pc`: main and the build, each with its own survey probe patch, the replay's history dump and region log, and t0-gumm's `scratch-first-filmed.patch` and `scratch-frozen.patch`.
  - `tm` and `tc`: the plain Release demos, for the timing.
- Every run is a Mac run at 1920 x 1080 output, drawn at the fixed render scale 2/3 (1280 x 720), except the lamp hall at 1072 x 603 (scale 0.5584), as the survey has it.
- On the build, the region log's b channel holds the cut 1 / n, not 1 for a move.

## The frozen stills

`tools/still.py` draws each still as the survey's still part does: 28 captures of 33 draws, the history kept through them, bloom off, the auto exposure. Capture k holds draw 33 k + 32. The history dump gives the lengths at draw 131 (capture 3, the survey's shown still) and draw 890 (capture 26). `tools/stills.py` measures them (`stills.md`):

- The share of the surface pixels whose slow diffuse history is under 31.5 draws, and under 24 draws.
- The grain: the survey's blotch score, its RMS and the specks of the capture, against the mean of captures 8 to 27 of the same run.
- The exposure of capture 26 and its step per draw from capture 25. Every run's step is under 0.0001 stops per draw, so the exposure is settled. Each run keeps its `exposure.json` under `/private/tmp/t0-qvm1c/runs/<build>-<view>/frames/`.

| Still (exposure at draw 890) | Under 31.5 draws at draw 890: main / cap | Blotch / RMS at draw 890: main / cap | Blotch at draw 131: main / cap |
|---|---|---|---|
| Lamp hall (+3.83 stops) | 21.27 / 8.32 % | 0.0061 / 0.0038, 0.0054 / 0.0033 | 0.0108 / 0.0170 |
| Still cabin (+3.83) | 17.85 / 10.08 % | 0.0049 / 0.0030, 0.0045 / 0.0027 | 0.0105 / 0.0163 |
| Bay (−4.24) | 38.64 / 19.44 % | 0.0235 / 0.0147, 0.0214 / 0.0132 | 0.0292 / 0.0286 |
| Cabin, lamps off (+5.50) | 18.05 / 8.73 % | 0.0082 / 0.0051, 0.0078 / 0.0048 | 0.0122 / 0.0137 |

- At draw 890 the cap halves the short histories in every still, and the blotch falls by 4 to 12 %.
- The maps `pics/lengths-<view>.png` show the still at draw 890 beside its lengths. The short histories come in blocks the size of the anchors' regions. On the cap fewer blocks remain, and those that remain are mostly 16 to 31.5 draws long.
- At draw 131 the blotch of the lamp hall and of the still cabin is worse on the cap, by about 55 %. Its map, `pics/blotch-cabin-3.png`, shows one smooth pattern on both builds: the ceiling and the walls darker and the floor brighter than the late mean. On the cap it is stronger. The level differs by less than 0.1 % on both. So it is lag of a true settle of the cabin's bounce light, which main's cuts to 8 draws follow faster. By capture 8 (draw 296) the blotch is equal (`tools/early.py`).

### The bay's late blotch

The bay's blotch at draw 890 stays at 0.0214 on the cap, against the survey's bar of 0.0089. `pics/blotch-bay-26.png` shows where it sits: over the whole image on both builds, a fine mottle in the dark upper left and larger blobs on the floor and the right wall. It does not follow the block grid of the remaining short histories. So it is the bounce light's own noise everywhere, and the cap lowers it only a little.

### Where the cap changes a history: crops at 1:1

`tools/crops.py` finds the two largest patches of the bay at draw 890 where main's history is 8 to under 16 draws and the cap's is 32. `pics/crops-bay.png` shows them at 1:1, main left and the cap right:

- Box 1, the wall right of the window (output pixels 786 to 1106 x 75 to 295): main 14 draws, the cap 32.
- Box 2, the right wall (1477 to 1797 x 375 to 595): main 15 draws, the cap 32.

At 1:1 the two sides show no visible difference. The walls are smooth, and the faint specks of box 2 show on both. The error against each run's own late mean (`tools/cropstat.py`) is a little lower on the cap: in box 1, a blurred RMS of 1.09 % against 1.20 % and a per-pixel RMS of 2.21 % against 2.46 %; in box 2, 1.14 % against 1.26 % and 1.89 % against 2.00 %. `pics/crops-bay-difference.png` stretches the difference twenty times: main's box 1 holds a brighter blob above the window that the cap does not.

## The fair cabin switch

The survey's switch part, case cabin (the glow goes off with the light), through the acceptance record's `take.sh` (`runs/switch-pm` and `runs/switch-pc`). The settle of the slowest region and of the whole image, in draws:

| Side | After off | After on |
|---|---|---|
| Main | 81 / 71 | 54 / 34 |
| Cap | 93 / 76 | 55 / 34 |

The cap settles 12 draws later after off. The replay predicted the direction (T 74 / 90, C1 80 / 98). Both sides fail the survey's settle bar after off, as main did before.

## The film rect-bay

The survey's motion part, case rect-bay (180 frames, the sunbeam leaves the bay), at the fixed exposure that each side's stills chose (−0.359 and −0.358 stops):

| Measure | Main | Cap |
|---|---|---|
| Level against the still of the same state, middle / end | +1.094 / +0.156 | +1.236 / +0.198 |
| Blotch, middle / end | 0.233 / 0.142 | 0.264 / 0.178 |
| Flicker, grain flicker | 0.00245, 0.00636 | 0.00234, 0.00623 |

The film is worse on the cap. It lags more behind the falling light: the replay's 90 % lag of the anchors in rect-bay was 18.7 draws for today's cut and 26.8 for the cap. `pics/rect-bay-leftwall.png` shows it at 1:1: at draws 130 and 179 the cap's left wall is visibly lighter than main's while the light falls. The specks are the same on both. So the bar "no worse than main" fails for rect-bay.

## The videos

`tools/film.py` films the frames, one PNG per draw, and `tools/video.py` puts main left and the cap right: H.264, yuv420p, CRF 12, 60 frames a second, one draw per frame, each pane 1920 x 1080 as drawn, with no filter. The band above each pane names the side, the render size (1280 x 720, 2/3), the draw and the fixed exposure. Bloom stays on, as the game draws it.

- `pics/bay-frozen.mp4`: the bay frozen (t0-gumm's `TERTIUS_X_FROZEN`: the world holds still, and each capture draws once), draws 420 to 659, −4.24 stops. The bay is the still whose short histories were most common on main. Its flicker over the 240 draws is 0.00057 on main and 0.00046 on the cap, and its grain flicker 0.00170 and 0.00155. Frames looked at 1:1 show no visible difference.
- `pics/rect-bay.mp4`: the film, draws 0 to 179, −0.359 stops. The cap's walls stay lighter while the sunbeam leaves.
- `pics/cabin-early.mp4`: the still cabin frozen from its first draw, draws 0 to 300, +3.83 stops. Both sides brighten as the bounce light settles: by draw 250, the floor by about 3.5 %, the ceiling by 7.5 % and the left wall by 9.7 % (`fade.txt`, `tools/fade.py`). The cap trails main by 0.2 to 0.75 points from draw 90 to 200, a lag of about 30 draws in the tail of the settle. At 1:1 (`pics/cabin-early-wall.png`) the two sides look the same at each draw, and the slow brightening is faint on both.

## The timing

`tools/timing.py` times the bounce light's accumulate pass ("accumulate indirect diffuse": its temporal and spatial dispatches, which no stamp splits; only the spatial dispatch changed) on the survey's timing film of two views, the bay and the cabin, 120 frames each. It times the plain demos `tm` and `tc` in paired rounds, the order turned in each round, under the exclusive GPU lock. A round starts below a one-minute load of 8, and each run prints its load (`timing.txt`). The cap's medians minus main's, in ms:

| Rounds | Cap's code | Bay | Cabin |
|---|---|---|---|
| 0 to 3 | `d42c8cdf3`: every invocation runs the cut's shared atomic | +0.033, +0.043, +0.045, +0.041 | +0.044, +0.031, +0.036 (loads 13.6 and 11.8), +0.036 |
| 4 to 7 | `1b1e30401`: the atomic only for a cut above 0 | +0.035, +0.039, +0.006, +0.031 | +0.050, +0.034, +0.045 (load 15.1), +0.034 (loads 12.3 and 25.3) |

- Leaving out the runs above a load of 10, the cap costs about +0.035 to +0.045 ms on a pass of 0.70 ms, 5 to 6 %. The pairs scatter by about 0.01 ms, so the rise is beyond the noise of the take.
- Round 6's bay pair is the outlier (+0.006): the cap's mean there was 0.687 against a median of 0.710, so one stretch of its run was fast.
- The guard of `1b1e30401` changed nothing measurable, so the shared atomics were not the cost.
- The frame's median span moves by −0.08 to +0.19 ms between the pairs, both ways, and shows no rise beyond its own scatter.

Then two rounds of an A/B on the probe builds `pm` and `pc` with the evidence's action dropped on both sides (`TIMING_BUILDS="pm pc"`, `TERTIUS_X_EVIDENCE_ACTION=0`, rounds 8 and 9). Then both sides cut and cap nothing, and their history lengths are the same. Only the code differs. The cap minus main, in ms:

| Round | Bay | Cabin |
|---|---|---|
| 8 | +0.049 (loads 6.7 and 5.2) | +0.047 (loads 17.0 and 14.2: does not count) |
| 9 | +0.033 (loads 4.8 and 7.2) | +0.083 (loads 4.9 and 4.4; main's median 0.690 lies far below its mean 0.769, so the pair is noisy) |

So the cost stays without the action: it belongs to the code, not to the different history lengths that the cap leaves. Not explained: which part of the code costs it. The candidates are the anchors' new arithmetic in `regionCutOf` (two powers, run by invocation 0 of the anchor workgroups while their other invocations wait at the next barrier), the `BlockEvidence` value that `accumulateAt` now carries, and the branch of `evidenceCappedLength`. The hunt ended with this A/B by the line orchestrator's order.

## The decision

The line orchestrator did not merge the cap (2026-11-23), and the orchestrator confirmed it. By the ticket's rules:

1. The frozen still shows no visible gain at 1:1 or in motion. The gain exists only in a measure: 19 % less flicker, and a blurred error of 1.09 % against 1.20 %.
2. rect-bay is worse than main, against its bar "no worse than main".
3. The bounce light's accumulate pass costs about 0.04 ms more.

## The code, kept as a patch

`region-cap.patch` is `git diff 76a4fbfeb..1b1e30401 -- src tests tools`: the cap, its tests, the probe patch's refit and the guarded atomic, from the branch `t0-qvm1-region-cap`, which stays unmerged. It applies to `76a4fbfeb` with `git apply docs/research/t0-qvm1-region-cap-takes/region-cap.patch`. On a later tree, expect conflicts in `accumulate_spatial.comp` and `region_drift.glsl`. The plan's and ARCHITECTURE.md's text for the cap is on that branch, in `d7fae25b7` and `1b1e30401`, and not in this record.

## Repeating rect-bay's left wall

`pics/rect-bay-leftwall.png` holds the left wall of rect-bay at 1:1, main above and the cap below, at draws 60, 90, 130 and 179: output pixels 0 to 400 by 100 to 500 of each 1920 x 1080 frame. The film's raw frames were deleted after the video. To make them again, from the checkout's root:

1. `bash docs/research/t0-qvm1-region-cap-takes/tools/builds.sh <cap commit> pm pc` builds the two sides (`pc` needs the cap's commit, such as `1b1e30401`).
2. `nix develop --command python3 -I docs/research/t0-qvm1-region-cap-takes/tools/film.py pm rect-bay -0.35925741`, and the same with `pc`, films each side, one PNG per draw (the fixed exposure is main's film exposure).
3. `nix develop --command python3 docs/research/t0-qvm1-region-cap-takes/tools/sheet.py rect-bay 0 0,100,400,500 60,90,130,179 <out.png>` makes the sheet.

## What the takes say

- In a frozen still the cap halves the short histories and lowers the grain a little. At 1:1, in the crops and in the frozen bay's video, there is no visible difference. The bay's frozen video flickers about 19 % less.
- In a light change it lags: the fair switch settles 12 draws later after off, and the film rect-bay trails its falling light visibly, with more level and blotch error than main. "No worse than main" fails there.
- In the cabin's early settle it trails by about 30 draws in the tail, with no difference visible at 1:1.
- It costs about +0.04 ms on the bounce light's accumulate pass, in its code and not in its different history lengths.
