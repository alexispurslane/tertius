# The remedy for the bay film's trail: region evidence (t0-acxm)

Design X let the bounce light of the film `rect-bay` trail the sunbeam that narrows and goes out (take T16). Decision Y1 of `docs/plans/bounce-light-confidence.md` (section 2.3) gives a block a second source of evidence for a change: its region. This record holds the remedy's takes in the bay, its timing, the rule that chose its action, and the forms that the takes and the timing rejected. Section 8.7 of the design doc holds the design as built.

The remedy as built: the region's drift test runs for the bounce light only, at anchor blocks, every 8th block of 8 × 8 pixels in x and in y, inside the bounce light's spatial dispatch. An anchor keeps its region's light of the last 36 draws in a ring, and it tests whether the mean of the last 18 draws moved by more than 3 standard deviations of the 18 before. A block whose anchors found a move in the draw before takes `RegionEvidence`. Region evidence caps the bounce light's history at 8 draws on top of the anti-lag: `min(antiLagged, FullRateFrames)`. The reflections run none of the region's code.

## The answers in short

| Question | Measured |
|---|---|
| Does the built form close the trail against main? | Mostly yes. R's film level is +0.85 / +0.29 against main's +1.17 / +0.53, and its blotch 0.187 / 0.162 against 0.238 / 0.231. In the lag film, R's median lag is 14.0 draws against 28.3, and its lag error 0.150 against 0.270. Its last-frame specks are 0.27 against main's 0.89. The stills have less daub than main. One measure is worse than main: R's flicker, 0.00273 against 0.00204 (the survey's limit is 0.006). The trail is not closed: the last frame is still 29 % too bright. |
| And against the line without the remedy? | The line L trails much more than main: level +2.11 / +2.27, median lag 40.6 draws, lag error 0.354. L has the least flicker (0.00098) and the fewest last-frame specks (0.09), because its histories stay long. |
| Which signals run the test | The bounce light only. B, with the test for both signals, equals R in level, blotch and lag error, but its last-frame specks are 1.07, above main's 0.89. The reflections' test made the film cheaper by 0.22 ms and cost nothing measurable on the deck. B is the rejected form. |
| The anti-lag under region evidence | Kept. The reviewer's form R (anti-lag, then the cap) and the settled form S (the cap alone) gave the same images to the last digit in the film, the lag film and the stills. R's film span is +0.012 ms against S's (spread 0.047). R's deck span is +0.044 ms against the line (spread 0.105). All four points of the rule hold (section "The rule for the anti-lag"). |
| The cut to 6 draws without own rays | Not taken. B6 beats the cut to 8 in level (+0.56 / +0.22), and its span is within budget. But its last-frame specks are 2.23, above the bar of 1.28. |
| The cost on the deck | R against the line's head, paired over 8 rounds: deck +0.044 ms (spread 0.105), film +0.174 ms (spread 0.041). B against main: deck +0.279 ms (spread 0.072). That is all of Design X with the remedy against main. |

The values "a / b" are at the film's middle and last frame.

## The sides

All sides except L hold the shadow fix (`t8-rjno`, merged into main as `dcd738552`).

| Side | What it is | Build |
|---|---|---|
| M | Main at `c4c1f1f2f` | The survey's probe (`tools/renderer-survey/probe.patch`) |
| L | The line's head `36c3fd0e8`, the line without the remedy | The survey's probe of that tree |
| R | The built form: the test for the bounce light only, and region evidence caps the anti-lag's length at 8 draws. Built from the working tree of 2026-11-22, `41e6119e1` (`git stash create`) | `probe-acxm.patch`, the survey's probe fitted to the remedy's push constants |
| S | R with `settled-antilag.patch`: region evidence takes no anti-lag, only the cap at 8 draws | As R |
| B | The rejected form: the line's head merged into the remedy, `c98d3d2da`, with the region's test for both signals | As R |
| B1 | B with the region's test for the bounce light only: `spike/switches.patch` and `TERTIUS_SPIKE_REGION=one`. Its action is S's | As R |
| B6 | B with the scratch cut to 6 draws (`cut6.patch`): region evidence cuts to at most 6 draws, and the ray plan counts such a pixel as mature, so that it keeps the block's shared ray | As R |
| A | B with the probe's clamps off (`TERTIUS_A_NO_CLAMP=1`): the stills' reference for the daub | As R |

`probe-acxm.patch` differs from the survey's probe in two hunks only. The probe adds five push constants after `retainedSigmas`. The fitted patch puts them after the remedy's push constants in both the GLSL block and `AccumulatePushConstants`, and passes the draw index through the probe's `Of`. Under `TERTIUS_A_NO_CLAMP`, the probe drops every evidence level, so the reference A has no region evidence either.

How B6 marks a cut pixel. The ray plan reads a pixel's history length through a weighted blend of 4 taps of the slow history, so it cannot read a flag beside the length. B6 cuts a history with region evidence to `floor(min(L, 6)) − 0.5`, a length with the fraction one half, and the plan counts a length with that fraction as mature. Two limits of the scratch: a history cut so grows 6.5, 7.5, so it counts as mature for 2 draws after its evidence ends; and an anti-lag length whose fraction happens to lie within 1/64 of one half counts as mature too. Both make B6 trace fewer own rays than an exact flag, never more.

## The takes

Tool code: `tools/queue-bay.sh` ran the takes of M, B, B1, B6 and A on 2026-11-22 from 00:44 to 01:01, at loads between 5 and 9. `tools/queue-antilag.sh` ran the takes of L, R and S from 03:16 to 03:27, at loads between 1.8 and 3.1. Each take is one survey run per side and case, through `tools/take.sh`. `tools/measure.sh` printed `tables.txt`, and `tools/measure-antilag.sh` printed `tables-antilag.txt`. `tools/pics.sh` draws `pics/`.

### The film rect-bay

| Side | Level | Blotch | Specks | Flicker | Grain flicker |
|---|---|---|---|---|---|
| M | +1.17 / +0.53 | 0.238 / 0.231 | 0.18 / 0.89 | 0.00204 | 0.00638 |
| L | +2.11 / +2.27 | 0.372 / 0.502 | 0.15 / 0.09 | 0.00098 | 0.00487 |
| R | +0.85 / +0.29 | 0.187 / 0.162 | 0.22 / 0.27 | 0.00273 | 0.00709 |
| S | +0.85 / +0.29 | 0.187 / 0.162 | 0.22 / 0.27 | 0.00273 | 0.00709 |
| B | +0.84 / +0.28 | 0.180 / 0.162 | 0.21 / 1.07 | 0.00263 | 0.00738 |
| B1 | +0.85 / +0.29 | 0.187 / 0.162 | 0.22 / 0.27 | 0.00273 | 0.00709 |
| B6 | +0.56 / +0.22 | 0.129 / 0.123 | 0.39 / 2.23 | 0.00341 | 0.00807 |

- The level = the survey's median ratio of the film frame's blurred light to the still's of the same state, minus 1. +0.85 means 85 % too bright: that is the trail.
- The blotch = the survey's measure of soft patches against the still.
- Specks = the survey's count of bright single pixels per 10,000 visible pixels, without the specks that the still holds (`t0-xdck`'s `filmspecks.py`).
- The flicker = the survey's median over the frames of each pixel's change against its two neighbor frames, low frequency. The survey's limits are 0.006 for the flicker and 0.05 for the grain flicker.

R, S and B1 give the same numbers. B1 ran from another build at another load, with S's action, so the region's test gives the same images in two builds and two runs.

### The lag film bay-sun

| Side | Median lag | Largest lag | Median lag error | Largest lag error |
|---|---|---|---|---|
| M | 28.3 draws | 73.0 draws | 0.270 | 0.556 |
| L | 40.6 draws | 121.0 draws | 0.354 | 0.628 |
| R | 14.0 draws | 30.3 draws | 0.150 | 0.395 |
| S | 14.0 draws | 30.3 draws | 0.150 | 0.395 |
| B | 12.7 draws | 26.2 draws | 0.150 | 0.391 |
| B1 | 14.0 draws | 30.3 draws | 0.150 | 0.395 |

The lag error = the median error of the light against the moving sunbeam's state. The survey fails every side on it ("lag error"), main too.

### The stills

| Still | Side | Specks | Blotch | Daub | Broad |
|---|---|---|---|---|---|
| bay-sunbeam | A | 0.15 | 0.0315 | 0.0148 | 0.0329 |
| | M | 0.14 | 0.0342 | 0.0311 | 0.0583 |
| | L | 0.14 | 0.0301 | 0.0207 | 0.0365 |
| | R | 0.14 | 0.0294 | 0.0225 | 0.0374 |
| | B | 0.17 | 0.0292 | 0.0228 | 0.0371 |
| exit-cabin | A | 0.14 | 0.0147 | 0.0128 | 0.0109 |
| | M | 0.24 | 0.0201 | 0.0273 | 0.0519 |
| | L | 0.20 | 0.0153 | 0.0150 | 0.0135 |
| | R | 0.24 | 0.0176 | 0.0166 | 0.0140 |
| | B | 0.27 | 0.0177 | 0.0167 | 0.0135 |

The daub and the broad measure = the survey's measures of soft clouds against the reference A. The stills are at the automatic exposure. R's stills equal B1's of the first takes. R has a little more daub than L in both stills (0.0225 against 0.0207, 0.0166 against 0.0150), and much less than main.

## The rule for the anti-lag

Two forms of the action under region evidence were open:

- The reviewer's form R: `historyLength = min(antiLagged, FullRateFrames)`, where `antiLagged = max(fastCap, L·(1 − min(1, lag/antiLagShare)))` is the length that the anti-lag gives without evidence. So the anti-lag can still shorten a history below 8 draws.
- The settled form S: `historyLength = min(historyLength, FullRateFrames)`. Region evidence takes no anti-lag.

The line orchestrator set a rule on 2026-11-22: keep R if all four points hold, else keep S.

| Point | R | S | Holds |
|---|---|---|---|
| 1. The film's last-frame specks at or under main's 0.89, and its flicker under 0.006 | 0.27, 0.00273 | 0.27, 0.00273 | yes |
| 2. Level and lag error no worse than S's beyond the scatter between runs | +0.85 / +0.29, 0.150 | +0.85 / +0.29, 0.150 | yes, equal |
| 3. The film span rises by at most +0.30 ms against S's | +0.012 ms (spread 0.047) | | yes |
| 4. The deck stays within +0.10 ms of the line's base | +0.044 ms (spread 0.105) | +0.038 ms (spread 0.060) | yes |

So `src` keeps R. In these takes, the anti-lag under region evidence never gave a length below the cap. The reason: the bounce light's box widens by the retained deviation, which tracks the lag. Near a length of 9 draws, the anti-lag cuts below 8 only where the lag exceeds about 0.056 of the mean. The test `UnderARegionEvidenceTheAntiLagStillShortensAHistoryBelowTheRegionsCap` makes the case with the reflections' narrow box and no noise.

## What the pictures show

![The film's middle and last frames](pics/rect-bay-frames.png)

`pics/rect-bay-frames.png`: the film `rect-bay` at its middle frame (top row) and last frame (bottom row), at 0.4 scale. The columns are the still of the same state, M, L and R. The mean gray of each tile, from 0 to 255, follows each name below.

- In the middle frame, every film is brighter than the still (118). L (186) is the brightest, flat and pale on every wall and the floor: that is the trail. M (159) is bright on the left wall and the floor. R (153) is a little darker than M. The still shows the sunlit band on the floor more clearly than any film.
- In the last frame, L (116) is still far brighter than the still (69). M (86) is lighter than the still, most on the left wall and the floor below it. R (77) is close to the still.
- At this scale, no tile shows a blotch, a daub or an edge along the anchors' grid.

![The film's last frame, floor at 1:1](pics/rect-bay-end-floor.png)

`pics/rect-bay-end-floor.png`: the floor above the hole in the last frame at 1:1, output pixels 700 to 1100 by 600 to 1000. The columns are the still, M, L and R.

- The still shows a faint cloud of fine specks above the hole.
- M shows a denser cloud of brighter specks and a lighter band on the left, which is the trail.
- L is pale gray all over, with a faint cloud: the trail at its strongest.
- R shows a cloud like the still's, a little denser, at the still's level, and no band.

![The still bay-sunbeam, floor at 1:1](pics/bay-sunbeam-floor.png)

`pics/bay-sunbeam-floor.png`: the still `bay-sunbeam`, output pixels 480 to 980 by 520 to 1020 at 1:1. The columns are the reference A, M, L and R. The reference is smooth. M shows soft light and dark clouds in the sunlit cone, which is the daub. L and R show a smoother cone, close to the reference, with fine grain low on the floor, as M has too.

![The still exit-cabin](pics/exit-cabin.png)

`pics/exit-cabin.png`: the still `exit-cabin`, the whole image at 0.35 scale. The columns are the reference A, M, L and R. The survey writes the reference of this case without its color, so only its shapes compare. At this scale, M, L and R look the same: the blue sky light in the windows, the lit floor and the lamps. No tile shows a blotch or specks at this scale.

The pictures show no consecutive frames, so they cannot show the flicker. The flicker is in the film's table.

## The cost

Each timing round took the exclusive GPU lock and ran every side's deck (`scripts/temporal-upscale-timing.json`) and film `rect-bay`, one after the other, at 1920 × 1080 with two frames in flight. The order of the sides turned round each round. Every round waited for a load below 8, and every run held the top clock, 1296 MHz. `tools/table.py` prints each round.

The GPU frame span is the median over a run's frames. A paired difference is the median over the rounds of one side's span minus the other's in the same round. Its spread is the highest minus the lowest per-round difference.

The rounds of the anti-lag rule: `tools/timing-antilag.sh` ran rounds a1 to a8 on 2026-11-22 from 03:27 to 03:35, sides line (L), reviewer (R) and settled (S), at loads between 1.87 and 4.96. `timing-antilag.txt` holds every round.

| Pair | Deck | Film `rect-bay` |
|---|---:|---:|
| R − L | +0.044 ms (spread 0.105) | +0.174 ms (spread 0.041) |
| S − L | +0.038 ms (spread 0.060) | +0.159 ms (spread 0.072) |
| R − S | −0.005 ms (spread 0.154) | +0.012 ms (spread 0.047) |

The rounds of the first takes: `tools/timing-bay.sh` ran rounds b1 to b8 from 01:05 to 01:16, sides M, B, B1 and B6, at loads between 4.05 and 7.48. `timing-bay.txt` holds every round.

| Pair | Deck | Film `rect-bay` |
|---|---:|---:|
| B − M | +0.279 ms (spread 0.072) | −0.245 ms (spread 0.043) |
| B1 − M | +0.254 ms (spread 0.145) | −0.023 ms (spread 0.060) |
| B1 − B | −0.026 ms (spread 0.106) | +0.223 ms (spread 0.043) |
| B6 − B | +0.003 ms (spread 0.060) | −0.052 ms (spread 0.055) |
| B6 − M | +0.288 ms (spread 0.125) | −0.294 ms (spread 0.053) |

- R − L on the deck is the remedy's own cost as built. It is within the budget of +0.10 ms.
- R − L in the film, +0.174 ms, is the cost of the bounce light's region evidence while the light moves: the cut histories trace more own rays.
- B − M on the deck is the whole of Design X with the remedy against main.
- B1 − B: the film is cheaper with the reflections' test, because their evidence cuts the reflections' histories while the light moves. The deck shows no difference beyond the scatter.

## The GPU trace of the deck

Tool code: `tools/tracepair.sh` recorded one trace of the deck per revision with `tools/gpu-trace.sh --no-counters`: frames 399 to 518 of `scripts/demo-exit.json`, 1920 × 1080, two frames in flight. The base is the line's head `36c3fd0e8`, the side before the remedy. The other is B, `c98d3d2da`, the rejected form with the test for both signals. Both ran at 1296 MHz, at loads of 7.3 and 7.6 when the recording started. The script sets `TERTIUS_DIFFUSE_FILTER=on`, the default kernel, because `gpu-trace.sh` passes an empty `--env` value otherwise, and xctrace refuses it (`t0-8jfj`). `trace/` holds both tables and `trace/difference.txt`.

| Pass | Base | B | Difference |
|---|---:|---:|---:|
| accumulate indirect diffuse | 0.598 ms | 0.638 ms | +0.040 ms |
| accumulate indirect specular | 1.417 ms | 1.466 ms | +0.049 ms |
| transparent | 2.040 ms | 2.014 ms | −0.026 ms |
| exposure | 0.262 ms | 0.280 ms | +0.018 ms, all of it shared with other passes |
| Frame span, mean / median | 20.197 / 19.706 ms | 20.233 / 19.774 ms | +0.036 / +0.068 ms |

Every other pass moved by less than 0.01 ms. A pass's time is its busy time, the union of its encoders' GPU intervals (tools/README.md, "GPU trace"). The two accumulation passes hold the temporal and the spatial dispatch of their signal, and the region's test runs in the spatial dispatch. So the rise lies in the region's test of each signal, about evenly split. The built form R runs only the bounce light's share, +0.040 ms in this trace. No pass after the reflections' spatial dispatch rose, so this record did not measure the share of the deck's block draws in which the reflections' region evidence fires.

## The forms that the takes and the timing rejected

| Form | Why it was rejected |
|---|---|
| B, the test for both signals | Its last-frame specks are 1.07, above main's 0.89. The reflections' test gives about 7 % false fires on a still glossy surface (section 8.7 of the design doc, and the note on `t3-5r0z`), and it brought specks into the film's last frame. The bounce light alone gives B's level, blotch and lag error. |
| B6, the cut to 6 draws without own rays | Its last-frame specks are 2.23, above the bar of 1.28. |
| S, the cap alone | It equals R in every image and in the span. The rule keeps R, which keeps the anti-lag's one meaning under every evidence level but the light's. |

The design doc's section 8.7 holds the table of the forms that the timing rejected and their numbers, and `spike/timing.txt` every round. `spike/` holds each such form as a patch:

| Patch | Form |
|---|---|
| `plain.patch` | A separate region pass per signal, 17 × 17 loads per block |
| `box-and-switches.patch` | A separate region pass per signal, box sum in two steps through shared memory, with the switch for one signal and the tap store alone |
| `fused-serial.patch` | The test at every block inside the spatial dispatch, invocation 0 sums |
| `fused-parallel.patch` | The same, with the sums in parallel |
| `anchored-with-switches.patch` | The test at anchors, with the scratch switches |
| `switches.patch` | Puts the scratch switches back on B: `TERTIUS_SPIKE_REGION=one` (bounce light only), `off` and `reads` |

The reason for the fused form: each compute dispatch behind a barrier costs about 0.06 to 0.13 ms on MoltenVK, whatever work it holds, because MoltenVK ends a Metal compute encoder at each barrier. The reason for the anchors: inside the spatial dispatch, a test at every block cost +0.27 ms, because each of the 14,400 workgroups of a signal summed 289 taps and 36 ring layers.

## Tool code

| Tool | What it does |
|---|---|
| `tools/vars.sh` | The takes' folder and the sides |
| `tools/probe.sh` | Builds one side's survey probe or plain timing build from a revision and patches |
| `tools/take.sh`, `tools/queue-bay.sh`, `tools/queue-antilag.sh` | One survey run, and the queues of the first takes and of the anti-lag rule |
| `tools/m.sh` | Runs a measuring script of this record or of an older one (`t0-xdck`, `t0-ion0`, `t0-xlcu`, `t0-y46f`, `t0-mzhr`) in the takes' folder |
| `tools/measure.sh`, `tools/measure-antilag.sh`, `tools/pics.sh`, `tools/filmcrop.py` | The tables and the pictures |
| `tools/round.sh`, `tools/round-inner.sh`, `tools/table.py` | One timing round under the exclusive GPU lock, and its table |
| `tools/timing-bay.sh`, `tools/timing-antilag.sh` | The 8 timing rounds of the first sides and of the anti-lag rule |
| `tools/tracepair.sh` | The GPU traces of the deck for two revisions |
| `tools/mutants.py` | The mutant lists of `mutants/` |
