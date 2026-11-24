# Region evidence and a longer diffuse history: the pictures of t1-yi11, steps 1 and 2

Ticket t1-yi11 found that region evidence fires falsely in the frozen bay. Each firing cuts the slow diffuse history to 8
draws, and so a cap of 128 leaves the bay's diffuse blobs as they are. This record holds the pictures and numbers that
the orchestrator ordered before any design: four columns, the frozen bay at 1:1, and two light changes beside main. No
rule of the temporal stack changed.

## The columns

| Column | Build | Knobs | What it is |
|---|---|---|---|
| a | ya | none | main: cap 32, today's region evidence |
| b | ya | `TERTIUS_X_DIFFUSE_CAP=128` | cap 128, today's region evidence |
| c | yc | `TERTIUS_X_DIFFUSE_CAP=128` | cap 128, the repaired region test of the unmerged branch t0-qvm1-region-cap (d7fae25b7) |
| d | ya | `TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_OFF=1` | cap 128, no region evidence for the diffuse history: the upper bound of the gain |

Both builds are the line's head 5144cbe9a with one scratch patch, never for src:

- `scratch-ya.patch`: the survey probe, t0-gumm's first-filmed and frozen patches, and three knobs read once at start.
  `TERTIUS_X_DIFFUSE_CAP` sets the diffuse signal's Cap and SmoothCap. `TERTIUS_X_REGION_OFF=1` skips the region
  evidence's cut of the diffuse history (bit 3 of the frame uniform's unused `shadowParams.w`). The anchors still run their
  test. `TERTIUS_X_DUMP_DIFFUSE` with `TERTIUS_X_DUMP_AT` writes the slow diffuse history's length at one draw.
- `scratch-yc.patch`: d7fae25b7's `region-cap.patch` applied with fuzz, the probe as that branch changed it, the frozen
  patches, the cap knob and the dump.

The order asked for a column at a cap of 64 if the candidate lagged visibly at 128. Column c does not lag visibly (below),
so this record has no such column. A column e (cap 64 with region evidence off) was started and then stopped by the
orchestrator's order, because column d lags at any cap.

## How to make the takes again

All raw data lives in /private/tmp/t1-yi11 and is deleted after the analysis. Run these from the checkout's root, with
bash (zsh passes a column's two knobs as one word: see "Faults found on the way"). Every tool below lives under
`docs/research/t1-yi11-region-evidence/tools/`.

1. `bash docs/research/t1-yi11-region-evidence/tools/builds.sh ya yc` builds the two probe builds into
   /private/tmp/t1-yi11/{ya,yc}/probe-bin.
2. The frozen bay, per column, with the column's knobs:
   `bash docs/research/t1-yi11-region-evidence/tools/bayrun.sh <build> bay-<column> <knobs>`, the same with
   `TERTIUS_PROBE_SIGNAL=2` as `bay-<column>-dif`, and `DRAWS=1660 EVERY=50 bash
   docs/research/t1-yi11-region-evidence/tools/bayrun.sh <build> bay-<column>-long-dif <knobs> TERTIUS_PROBE_SIGNAL=2`.
3. `nix develop --command python3 -I docs/research/t1-yi11-region-evidence/tools/bay.py` prints the bay's table, writes
   `bay-numbers.json` and writes the bay pictures.
4. The films: `bash docs/research/t1-yi11-region-evidence/tools/films.sh rect-bay a b c d`,
   `bash docs/research/t1-yi11-region-evidence/tools/films.sh switch a b c d` (the settle counts) and
   `bash docs/research/t1-yi11-region-evidence/tools/films.sh switch-film a b c d` (the pictures).
5. `nix develop --command python3 -I docs/research/t1-yi11-region-evidence/tools/rbcurve.py` prints rect-bay's brightness
   per draw and writes `rect-bay-curve.json`.
6. `python3 -I docs/research/t1-yi11-region-evidence/tools/grid.py <out.mp4> <first draw> <note> "<label>|<folder>|1280 x 720"...`
   makes a film in a 2 x 2 grid, and
   `python3 -I docs/research/t1-yi11-region-evidence/tools/sheet.py <out.png> <draws> <note> "<label>|<folder>|<first draw>"...`
   makes a frame sheet.

All takes are 1920 x 1080 at the headless render scale of 2/3 (1280 x 720), each holding the GPU lock in its shared mode.

## The frozen bay

The bay is the survey's still bay-sunbeam (demo-surface's bay camera after 420 ticks) in a frozen world at a fixed
-4.089 stops. Every capture draws once with no step of time, so nothing changes and every firing of region evidence is
false. The regions are the back-face line's boxes (t1-nyjl).

### Pictures

Each picture holds the four columns, a and b above c and d, cropped at 1:1 from draw 659.

- `pictures/bay-floor-dif.png`, `bay-back-wall-dif.png`, `bay-right-wall-dif.png`, `bay-sunlit-patch-dif.png`: the
  diffuse signal alone.
- `pictures/bay-floor-full.png`, `bay-back-wall-full.png`: the full picture.
- `pictures/bay-<region>-dif-residual.png`: draw 660 of the column's long take minus the late mean of that take, in
  display-linear luminance, times 10 on mid gray. This makes the blobs visible. The fine contour lines are the 8-bit
  steps.
- `exposure/`: the exposure files of every bay and rect-bay take.

### Numbers

Measured. "Mean length" is the slow diffuse history's mean length at draw 639, in draws, from the dump. "Blob" is the
ticket's measure on the diffuse signal alone: a still minus the late mean of its own take, the band from 3 to 36 px,
as a standard deviation over the region in units of main's full mean there. Each column's long take keeps every 50th
draw from 660 to 1,610. Each of those 20 draws is a still against the mean of the other 19, and the table gives the mean
of the 20 values. "Rounding" is the same band of the 8-bit rounding alone (the back-face line's band.py). "Mottle" is the
standard deviation of draw 659 of the full picture minus its Gaussian blur of sigma 4 px, in 8-bit gray.

| Region | Column | Mean length | At the cap | Under 96 draws | Blob | Rounding | Mottle |
|---|---|---:|---:|---:|---:|---:|---:|
| Sunlit patch | a | 31.1 | 92.8 % | 100 % | 0.0078 | 0.0002 | 2.558 |
| | b | 96.4 | 58.6 % | 34.7 % | 0.0078 | 0.0002 | 2.551 |
| | c | 99.4 | 58.6 % | 34.7 % | 0.0061 | 0.0002 | 2.536 |
| | d | 128.0 | 100 % | 0 % | 0.0025 | 0.0002 | 2.503 |
| Back wall below the slot | a | 30.6 | 85.8 % | 100 % | 0.0109 | 0.0006 | 1.101 |
| | b | 76.4 | 20.0 % | 50.5 % | 0.0092 | 0.0007 | 1.094 |
| | c | 91.3 | 20.0 % | 27.6 % | 0.0071 | 0.0007 | 1.097 |
| | d | 128.0 | 100 % | 0 % | 0.0038 | 0.0008 | 1.096 |
| Floor | a | 31.0 | 85.3 % | 100 % | 0.0116 | 0.0008 | 2.294 |
| | b | 99.6 | 54.5 % | 23.1 % | 0.0103 | 0.0009 | 2.307 |
| | c | 106.8 | 54.5 % | 23.1 % | 0.0078 | 0.0009 | 2.305 |
| | d | 128.0 | 100 % | 0 % | 0.0041 | 0.0008 | 2.305 |
| Right wall | a | 19.7 | 31.1 % | 100 % | 0.0061 | 0.0009 | 2.257 |
| | b | 33.7 | 9.6 % | 90.4 % | 0.0055 | 0.0009 | 2.257 |
| | c | 65.4 | 22.3 % | 74.3 % | 0.0039 | 0.0008 | 2.250 |
| | d | 128.0 | 100 % | 0 % | 0.0021 | 0.0006 | 2.249 |

- The lengths of a and b equal the ticket's to within 0.1 draws. The blobs of a, b and d equal the ticket's to within
  0.0017.
- The kept draws lie 50 draws apart, and a blob of a history of 128 draws lives longer than that. So the late mean keeps a
  part of every long blob, and the blobs of b, c and d are lower bounds.
- b and c have the same shares at the cap and under 96 draws on three regions, with different means. The measure does
  not say why.
- The mottle is the full picture's fine grain, which the specular signal carries. It does not change between the columns.

### Reading

- Floor and sunlit patch, diffuse alone: a and b show soft blotches. c shows weaker ones. d shows an even falloff.
- Back wall, diffuse alone: a has a faint vertical streak near its left edge. The other columns differ little at 1:1.
  The residual maps show the same order as the numbers: a, b, c, d.
- Right wall, diffuse alone: no column shows a patch that I see at 1:1. The residual map shows c between b and d.
- Full picture: the specular grain on the floor's lower left covers most of the differences at 1:1.
- Against the bar (no soft patch at 1:1 on the floor and the back wall): d passes. c's patches are weaker than a's, but
  the floor's residual still shows them, and its blob is 1.9 times d's.

## The film rect-bay

The survey's motion film rect-bay: 180 draws one tick apart, the sunbeam leaves the bay, a fixed -0.359 stops (main's
exposure in t0-qvm1's takes), bloom on as the game draws it.

- `videos/rect-bay.mp4`: the four columns in a 2 x 2 grid, 60 frames a second, H.264 at CRF 12.
- `pictures/rect-bay-sheet.png`: the four columns at draws 30, 60, 90 and 150, scaled to 400 px wide.

Measured, the mean 8-bit gray of the whole picture (every draw and three more boxes in `rect-bay-curve.json`):

| Column | Draw 30 | 60 | 90 | 120 | 150 | 179 |
|---|---:|---:|---:|---:|---:|---:|
| a | 217.4 | 195.7 | 155.5 | 110.3 | 89.0 | 78.6 |
| b | 217.6 | 196.0 | 155.6 | 110.4 | 89.0 | 78.7 |
| c | 218.3 | 200.7 | 159.0 | 111.9 | 90.1 | 80.3 |
| d | 218.7 | 215.2 | 209.6 | 202.3 | 195.7 | 184.0 |

c's largest excess over a is on the floor: 4.2 gray at draw 60 and 3.0 at draw 179. a falls about 1.5 gray per draw
there, so c trails by about 2 to 3 draws.

Reading: b follows main to within 0.6 gray. c trails a little, and I see no lag beside main. d lags badly: at draw 150
its room is still lit as if the sunbeam were there.

## The fair cabin switch

The survey's switch part, case cabin: orbit's person camera after 60 ticks, the lamps `z:-12:4` off from film draw 160
to 360, a fixed 4.08 stops. As in t0-qvm1, three runs per column (the switch, and controls with the lamps always off and
always on), with the survey's options `--bloom off --tone-curve hable`. `settle-<column>.json` holds survey.switch_result
and the region series.

Measured, draws to settle, the slowest changed region / the whole picture:

| Column | After off | After on |
|---|---|---|
| a | 81 / 71 | 54 / 34 |
| b | 83 / 72 | 53 / 34 |
| c | 104 / 81 | 53 / 33 |
| d | not settled in 200 / not settled | not settled in 100 / 54 |

Column a equals t0-qvm1's main. Every column fails the survey's settle bar after off, as main does.

The whole picture against its control run (switch / control - 1), by draws after the switch:

| Column | Off +40 | +100 | +140 | +199 | On +20 | +40 | +60 | +99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| a | +0.145 | +0.024 | +0.013 | +0.005 | -0.092 | -0.037 | -0.018 | -0.006 |
| b | +0.147 | +0.026 | +0.015 | +0.008 | -0.093 | -0.037 | -0.017 | -0.006 |
| c | +0.154 | +0.038 | +0.026 | +0.018 | -0.091 | -0.036 | -0.017 | -0.006 |
| d | +0.351 | +0.238 | +0.206 | +0.170 | -0.086 | -0.058 | -0.047 | -0.036 |

For the eye, the switch run of each column ran once more without the survey's options (bloom on, the game's tone curve),
because the probe's kept PNGs come out as a false-colour pattern under them (bug tc-380i). Its measures are sound.

- `videos/cabin-switch.mp4`: the four columns in a 2 x 2 grid, film draws 150 to 459.
- `pictures/cabin-switch-sheet.png`: draws 165, 200, 260 and 380.

Reading: c settles 23 draws later than main after off, 11 more than the repaired test's 12 at a cap of 32 in t0-qvm1.
After on it equals main. In the sheet, d's floor and walls are a little lighter than the others at draws 200 and 260,
and a, b and c look the same.

## The probe of the region test's two designs

After the four columns, the orchestrator ordered a probe of two designs of the region test, from one scratch build. Nothing
here goes into src.

### The build and its columns

Build yg is ya plus two switches and a log, each read once at start (`scratch-yg.patch`). The switches ride in the frame
uniform's `shadowParams.w`: bit 3 region evidence off, bit 4 the rank test, and the gate's length times 256.

- `TERTIUS_X_REGION_GATE=<draws>`, the cause gate. A counted tap brings a cause where `evidenceOfSample` of its sample
  age is not NoEvidence (a recent light code or the motion bit). The anchor keeps `causeAge` in its ring's `a`: 0 in a
  draw that is not valid, that does not continue the draw before, or whose region has a counted tap with a cause, else
  the draw before's plus 1, at most 4,096. The answer is today's test while `causeAge` is under the gate, else no.
- `TERTIUS_X_REGION_RANK=1`, the signed rank test in place of today's test: the 18 differences between draws of the same
  jitter phase, `d_i = x(t - i) - x(t - i - 18)`, their signed ranks, and "moved" where `max(V+, V-) >= 166` of 171.
- Both set: today's test while the gate is open, the rank test while it is closed.
- `TERTIUS_X_REGION_LOG=<folder>`: each draw appends the anchors' ring layer of that draw to `region-ring.log`. The
  ring's `a` holds `causeAge + 8192 x` the count of counted taps with a cause. `tools/regionlog.py` reads it.

Build yh is yg plus a fixed turn of the camera, `TERTIUS_X_YAW_DEGREES` and `TERTIUS_X_PITCH_DEGREES`
(`scratch-yh.patch`), for the turned take only.

| Column | Build | Knobs |
|---|---|---|
| b | yg | `TERTIUS_X_DIFFUSE_CAP=128` (draws what ya draws) |
| d | yg | `TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_OFF=1` |
| g | yg | `TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128` |
| r | yg | `TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_RANK=1` |
| g+r | yg | `TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=36 TERTIUS_X_REGION_RANK=1` |
| n32, n128 | yg | `TERTIUS_X_DIFFUSE_CAP=32` or `128`, `TERTIUS_X_REGION_OFF=1 TERTIUS_A_NO_CLAMP=1` |

`TERTIUS_A_NO_CLAMP=1` is the survey probe's clamp switch. It sets ClampSigmas to -1 for every signal, the diffuse and the
specular one.

Checks, measured: yg with no switch draws the same bytes as ya in one frozen bay still (20 of 20 PNGs and the length dump).
In column g's frozen bay, no anchor-draw holds a cause, after draw 128 or before it, so the stop condition did not fire.
In rect-bay, column g's log shows causes in 4 to 48 anchors per draw, so the count works.

How to make them again from the checkout's root: run `bash docs/research/t1-yi11-region-evidence/tools/builds.sh yg yh`,
then `bash docs/research/t1-yi11-region-evidence/tools/gtakes.sh <film|log|long|longfull> <column>...` for the frozen
bay. For the light-change films, run
`bash docs/research/t1-yi11-region-evidence/tools/films.sh <rect-bay|rect-bay-settled|switch|switch-film> <column>...`
for columns bl, g, r and gr (add `TURN=90` to turn the camera with build yh). Run
`python3 -I docs/research/t1-yi11-region-evidence/tools/regionlog.py <folder> <after draw>` for the logs. Their per-draw
summaries live in `logs/`.

### The frozen bay

The answers in draws 301 to 659, measured from the ring logs (20 x 12 anchors). A tested anchor-draw is an anchor with 36
valid draws in a row. An answer run is an anchor that answers yes after a draw without an answer. A run that acts cuts
the bounce light's history in its region's blocks.

| Column | Tested anchor-draws | Yes answers | Rate | Answer runs | Runs on the floor's anchors |
|---|---:|---:|---:|---:|---:|
| b | 83,647 | 840 | 1.00e-2 | 369 | 21 |
| d | 83,647 | answers do not act | | 0 that act | 0 |
| g | 83,647 | 0 | 0 | 0 | 0 |
| r | 83,647 | 12 | 1.43e-4 | 8 | 0 |
| g+r | 83,647 | 12 | 1.43e-4 | 8 | 0 |

r's rate is 1.9 times the rank test's level of 7.6e-5. g+r gives r's 12 answers at the same anchors and draws, because
its gate stays closed. r's 8 runs lie on the right wall's anchors (16 to 18, 0 to 5) and the left wall's (1 to 2, 2 to 10).

The blob, the lengths and the mottle, as in the table above (`bay-numbers-probe.json`):

| Region | b | c | d | g | r | g+r |
|---|---:|---:|---:|---:|---:|---:|
| Sunlit patch, blob | 0.0078 | 0.0061 | 0.0025 | 0.0025 | 0.0031 | 0.0031 |
| Back wall, blob | 0.0092 | 0.0071 | 0.0038 | 0.0038 | 0.0038 | 0.0038 |
| Floor, blob | 0.0103 | 0.0078 | 0.0041 | 0.0040 | 0.0041 | 0.0041 |
| Right wall, blob | 0.0055 | 0.0039 | 0.0021 | 0.0021 | 0.0022 | 0.0022 |
| Mean length at draw 639: floor, back wall, right wall, patch | 99.6, 76.4, 33.7, 96.4 | 106.8, 91.3, 65.4, 99.4 | 128 | 128 | 128 | 128 |
| Mottle, floor (8-bit gray) | 2.307 | 2.305 | 2.305 | 2.304 | 2.304 | 2.304 |

Pictures: `pictures/bay-<floor|back-wall|right-wall|sunlit-patch>-dif-probe.png` (diffuse alone at 1:1),
`bay-<floor|back-wall>-full-probe.png` (full picture) and `bay-<region>-dif-residual-probe.png` (residual times 10), each
b, c, d, g, r and g+r in a 2 x 3 grid.

The frozen film of the floor, d | g over r | g+r, draws 300 to 659, a 1:1 crop of output x 500 to 1460, y 540 to 1080, at
-4.089 stops: `videos/frozen-floor-dif.mp4` (bounce light alone) and `videos/frozen-floor-full.mp4` (full picture), with
`pictures/frozen-floor-<dif|full>-sheet.png` at draws 300, 420, 540 and 659.

Reading: g, r and g+r reach d's blob in every region, within 0.0006. Their residual maps look like d's. In the films, the
four columns show the same even falloff, and I see no patch appear or fade on the floor.

### The level per column and the clamp pair

Each column's long take (1,660 draws, captures at draws 660, 710, ..., 1,610) as a ratio to column a's mean, in the
region's mean display-linear luminance at -4.089 stops (`tools/level.py`, `level.json`):

| Column | Diffuse: patch | back wall | floor | right wall | Full: patch | back wall | floor | right wall |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| b | 0.9988 | 0.9989 | 0.9986 | 0.9994 | 0.9999 | 0.9990 | 0.9989 | 0.9999 |
| c | 0.9991 | 0.9980 | 0.9977 | 0.9988 | 1.0000 | 0.9982 | 0.9981 | 0.9996 |
| d | 1.0026 | 0.9963 | 0.9955 | 0.9977 | 1.0007 | 0.9968 | 0.9962 | 0.9992 |
| g | 1.0034 | 0.9977 | 0.9963 | 0.9981 | 1.0008 | 0.9981 | 0.9970 | 0.9996 |
| r | 1.0016 | 0.9964 | 0.9955 | 0.9978 | 1.0005 | 0.9968 | 0.9963 | 0.9993 |
| g+r | 1.0016 | 0.9964 | 0.9955 | 0.9978 | 1.0005 | 0.9968 | 0.9963 | 0.9993 |
| n32 | 1.0004 | 0.9994 | 0.9995 | 0.9997 | 1.0024 | 0.9995 | 1.0093 | 1.0399 |
| n128 | 1.0026 | 0.9963 | 0.9955 | 0.9977 | 1.0023 | 0.9963 | 1.0051 | 1.0380 |

In every take and region, the first and last captures lie within 1 % of the take's mean. The standard deviation of the
20 capture means is 0.05 % to 0.5 % of the mean. With the clamp off, the diffuse level still moves with the cap.
Specifically, n128 against n32 is 1.0022 on the sunlit patch, 0.9969 on the back wall, 0.9960 on the floor and 0.9980
on the right wall. Those shifts are 2 to 5 times the naive 20-capture standard error. By the orchestrator's rule, that
part stopped there.

In the diffuse signal alone, n128 equals d to 4 digits across all four regions. On draw 1,610, 33,000 8-bit gray pixels
(72,087 RGB pixels) differ, with a mean absolute difference of 0.016 of an 8-bit gray step. In the full picture,
`TERTIUS_A_NO_CLAMP=1` also turns the specular clamp off, which brightens the floor and the right wall in both n32 and
n128. Even there, n128 against n32 stays at 0.9999 on the sunlit patch, 0.9968 on the back wall, 0.9959 on the floor
and 0.9982 on the right wall.

Comparison with the back-face line's measurement on main (`146464c2b`, `/private/tmp/t1-wl7c/fe63/means-lever.json` and
`means-bay-full.json`), measured read-only:

- Build and metric: `git diff 5144cbe9a..146464c2b -- src/` is empty. Both lines render at -4.089 stops and 2/3 scale
  (1280 x 720 in 1920 x 1080), use the same four region boxes and decode 8-bit sRGB PNGs to display-linear Rec. 709
  luminance. At draw 659 (`c-s19.png` there against `f-000659.png` here), main agrees to 0.00001 in display-linear
  luminance (back wall 0.19697 against 0.19698, floor 0.14559 against 0.14560). `lever` and column d agree to 0.00001
  too (back wall 0.19394 against 0.19393, floor 0.14459 against 0.14460).
- Cause of the size difference (2.6 % and 2.0 % there against 0.32 % and 0.38 % here): the draw range averaged. The
  back-face line's `means.py` averaged captures `c-s00` to `c-s19` (draws 32, 65, ..., 659, every 33 draws from draw 0).
  Each diffuse bounce reads the draw before's filtered diffuse history, so a cap of 128 without region evidence slows
  multi-bounce warm-up from draw 0 by about 4 times.
- Warm-up numbers in `lever`: capture `c-s00` (draw 32) is 0.17071 on the back wall (12.2 % below its late mean 0.19454)
  and 0.13179 on the floor (9.0 % below 0.14482). Captures `c-s00..09` (draws 32 to 329) average 0.9458 of main's late
  mean on the back wall and 0.9558 on the floor, and captures `c-s10..19` (draws 362 to 659) average 0.9876 and 0.9895.
  Draws 32 to 659 pull `lever` down more than `main`: `lever(0..19) / lever(30..99)` is 0.9681 on the back wall and
  0.9760 on the floor, against main's 0.9921 and 0.9924. That ratio gives the reported 0.9744 (0.974) and 0.9801
  (0.980).
- Late draws on the back-face line's 100-capture reference (`reference-mean.npy`, captures `c-s30..99`, draws 1,022 to
  3,299): `lever` against `main` in display-linear luminance is 1.0009 on the sunlit patch, 0.9985 (-0.15 %) on the back
  wall, 0.9965 (-0.35 %) on the floor and 0.9996 (-0.04 %) on the right wall. Captures `c-s90..99` (draws 3,002 to
  3,299) of `lever-ref`, divided by `main`'s 70-capture `reference-mean.npy` (`c-s30..99`), sit at 1.0009, 0.9993,
  0.9992 and 1.0004.
- Draws 660 to 1,610 in our long takes (diffuse alone): between the first 10 captures (draws 660 to 1,110) and the last
  10 (draws 1,160 to 1,610), both d and n128 drop slightly on the back wall (0.18189 to 0.18162, -0.15 %) and the floor
  (0.13183 to 0.13162, -0.16 %). The ratios d / a and n128 / a move from 0.9949 to 0.9977 on the back wall and from
  0.9947 to 0.9963 on the floor only because the noisier cap-32 baseline a drops more over those draws (-0.43 % back
  wall, -0.31 % floor; n32 drops -0.58 % and -0.37 %). At draw 659 alone (`bay-back-wall-dif.png`), raw crop means in
  8-bit gray are a 117.88, b 117.40, c 117.36 and d 117.01.

Reading: the clamp does not cause the darkening, because n128 matches d to 4 digits in the diffuse signal. Most of the
back-face line's 2.0 % to 2.6 % darkening comes from slower multi-bounce warm-up in draws 32 to 659 at cap 128 without
false resets. Draws 660 to 1,610 show no monotonic warm-up rise in d or n128, and a 0.15 % to 0.40 % gap remains on the
back wall and floor in settled draws (1,022 to 3,299 there, 660 to 1,610 here). Because n128 against n32 still differs
by -0.31 % on the back wall and -0.40 % on the floor with the clamp off, the cause of that small residual shift stays
open and the orchestrator's stop rule holds.

### rect-bay

As it is, the take cannot test the gate. Its run starts 32 draws before the film, `causeAge` starts at 0, so every gate
of 128 stays open until film draw 96 whatever the causes. Its films of g, r and g+r are in /private/tmp only.

The settled take (`rect-bay-settled` in yifilm.py) puts 200 frozen draws of the first state before the film. Measured: 380
draws, every valid anchor's gate closed before the film, causes from film draw 4. The turned take is the same with the
camera turned 90 degrees left (build yh). At -4.089 stops the sun patch lies on the right wall by the back corner, and
the turned view (the left wall and the floor) does not hold it at film draws 0, 90 and 179.

The whole picture's mean 8-bit gray (`rbs-curve.json`, `rbst-curve.json`):

| Take | Column | Film draw 30 | 45 | 60 | 75 | 90 | 120 | 150 | 179 |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Settled | a | 219.0 | 210.0 | 196.2 | 178.0 | 155.0 | 110.4 | 88.8 | 78.7 |
| | b | 219.7 | 211.1 | 197.0 | 178.5 | 155.3 | 110.5 | 88.8 | 78.7 |
| | g | 220.5 | 215.7 | 208.2 | 197.4 | 183.6 | 155.2 | 137.7 | 125.4 |
| | r | 220.7 | 217.7 | 208.4 | 190.0 | 163.7 | 113.3 | 89.6 | 80.0 |
| | g+r | 220.0 | 214.0 | 199.2 | 179.7 | 155.9 | 110.6 | 88.9 | 79.2 |
| Turned | b | 207.3 | 202.1 | 187.9 | 168.5 | 146.1 | 105.3 | 73.8 | 71.2 |
| | g | 208.0 | 206.7 | 204.0 | 200.2 | 195.4 | 184.6 | 172.0 | 160.7 |
| | r | 208.0 | 205.6 | 197.6 | 173.2 | 147.4 | 105.4 | 73.8 | 71.2 |
| | g+r | 208.0 | 205.6 | 197.6 | 173.2 | 147.4 | 105.4 | 73.8 | 71.2 |

In the settled take, column g's right wall (x 1100 to 1800, y 100 to 800) stays at 159.2 at film draw 179, against a's
71.4. Its ring log shows 91 of the 234 valid anchors with no cause and a closed gate through the whole 180-draw film.
Those 91 anchors are all 12 in each of the rightmost 6 columns (14 to 19), plus 8 in column 13, 8 in column 12 and 3 in
column 11. Across the film, 234 anchors are valid at the start and end, and 233 are valid on every draw. In the turned
take, no tap of any column brings a cause after the settle. After draw 200 (film draws 1 to 179), b answers at a rate of
0.605 per tested anchor-draw (0.601 across all 180 film draws), r and g+r at 0.469 (0.467 across all 180) and g never.

- `videos/rect-bay-settled.mp4` (a | b over g | r) and `pictures/rect-bay-settled-sheet.png` (a, b, g, r and g+r at film
  draws 45, 75, 105 and 150).
- `videos/rect-bay-turned.mp4` (b | g over r | g+r) and `pictures/rect-bay-turned-sheet.png` (draws 0, 60, 90 and 150).

Reading: g fails both takes. In the settled take, a hard staircase edge along the anchors' blocks splits the lit right
part from the darkening left part, from film draw 75 to 150. The sun patch lies on the right wall itself, so the right
wall's rays read surfaces whose direct light does not change. In the turned take, g stays lit. Column r trails b by
about 5 to 8 draws and shows a blocky patchwork of cut and uncut regions at film draws 60 to 75. Column g+r follows b to
within 3.1 gray across all 180 draws in the settled take (largest gap 3.06 gray at film draw 48) and equals r in the
turned one.

### The fair cabin switch

As for columns a to d (`settle-<column>.json`), draws to settle, slowest region / whole picture:

| Column | After off | After on |
|---|---|---|
| b | 83 / 72 | 53 / 34 |
| g | 85 / 74 | 52 / 33 |
| r | not settled in 200 / 132 | 48 / 31 |
| g+r | not settled in 200 / 128 | 47 / 31 |

The whole picture against its control:

| Column | Off +40 | +100 | +140 | +199 | On +20 | +40 | +60 | +99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| g | +0.152 | +0.028 | +0.017 | +0.012 | -0.094 | -0.036 | -0.016 | -0.004 |
| r | +0.176 | +0.059 | +0.048 | +0.040 | -0.090 | -0.031 | -0.011 | -0.002 |
| g+r | +0.172 | +0.058 | +0.048 | +0.039 | -0.087 | -0.030 | -0.011 | -0.002 |

Column g's ring log (`logs/sw-g-switch.json`) shows 239 or 240 anchors with a cause within 5 draws of each switch (film
draws 163 to 169 after off, 363 to 369 after on). Those anchors carry a median of 52 to 62.5 taps and a largest count
of 186 to 204 taps, so every gate opens. With its 128-draw gate, g stays open through the settle and follows b (85 / 74
against 83 / 72 after off, 52 / 33 against 53 / 34 after on).

In g+r (`logs/sw-gr-switch.json`), the 36-draw gate closes between film draws 205 and 210 (45 to 50 draws after off)
while the bounce light still decays. Once the gate closes, the signed rank test takes over and anchor answers drop from
229 at film draw 204 to 56 at film draw 217 (against g's 155 at film draw 217). After on, r and g+r follow b (48 / 31
and 47 / 31). After off, both r and g+r stay 3.9 % to 4.0 % too bright at draw +199, against b's 0.8 % and g's 1.2 %. We
left out the turned switch because the lamps light nearly every surface in that view, and a view that avoids them needs
its own camera check and 12 runs.

For the eye, the switch run of columns b, g, r and g+r ran once more without the survey's switch options (bloom on, the
game's tone curve). That take kept film draws 150 to 459 (310 frames at 4.08 stops):

- `videos/cabin-switch-probe.mp4`: b | g over r | g+r in a 2 x 2 grid, 60 frames a second, H.264 at CRF 12.
- `pictures/cabin-switch-probe-sheet.png`: b, g, r and g+r at film draws 165, 200, 260 and 380.

Measured on the viewable film frames (`swf-b`, `swf-g`, `swf-r`, `swf-gr`), mean 8-bit gray of the whole picture (and
lower-half floor, y 540 to 1080):

| Column | Draw 165 (off +5) | Draw 200 (off +40) | Draw 260 (off +100) | Draw 359 (off +199) | Draw 380 (on +20) |
|---|---:|---:|---:|---:|---:|
| b | 73.58 (58.09) | 53.11 (42.46) | 47.49 (37.65) | 46.77 (37.00) | 93.88 (99.71) |
| g | 74.25 (58.28) | 53.27 (42.59) | 47.51 (37.66) | 46.81 (37.02) | 93.47 (99.47) |
| r | 72.91 (57.20) | 52.93 (42.32) | 47.84 (37.91) | 47.27 (37.39) | 93.30 (99.24) |
| g+r | 72.90 (57.19) | 52.76 (42.18) | 47.80 (37.87) | 47.24 (37.37) | 93.62 (99.57) |

Reading: in `cabin-switch-probe-sheet.png` and in full-resolution frames at film draws 200 and 260, all four columns
(b, g, r and g+r) look alike by eye. None shows the visibly lighter floor and walls that column d showed in
`cabin-switch-sheet.png` at draws 200 and 260. The 3.9 % to 4.0 % residual that r and g+r keep in the linear switch
series after off is under 0.5 of an 8-bit gray step on the darkened cabin floor and walls (47.24 to 47.27 against b's
46.77 at draw 359). That difference does not show side by side.

## Faults found on the way

- The first column d ran as column a. The take loop ran in zsh, which does not split an unquoted variable, so the two
  knobs reached bayrun.sh as one word and `TERTIUS_X_DIFFUSE_CAP` read "128 TERTIUS_X_REGION_OFF=1", which is no number.
  Its pictures and dump were byte-identical to column a's. Every number here comes from the run again at 17:01 with
  separate words. `tools/films.sh` holds the columns' knobs and runs in bash.
- The first switch films ran without the survey's probe options (`--bloom off --tone-curve hable`). Column a then
  settled after 197 draws, against main's 81 in t0-qvm1. yifilm.py now passes the options. With them, column a gives
  t0-qvm1's numbers exactly.
- A first rect-bay take of the probe left every gate open, because its run starts 32 draws before the film (see
  "rect-bay" in the probe). The settled take fixes that.
- I overwrote a running bash script in place once (gtakes.sh). The take it ran was stopped and run again. The tools now
  replace a script by renaming a new file over it.
- A first blob measure against one shared reference take (cap 512, region evidence off, 1,660 draws) failed. That
  take's own frames brightened the floor by 1.3 % from draw 660 to draw 1,460, and its mean differed from every column's
  still by the same large smooth shape, which leaked into the band. The cause (the early draws in a history of 512, or a
  slow settle of the bounce light) is not measured. The table above uses each column's own late mean, as the ticket does.

## Validation of the selected combined rule (g128+r)

The parent's decision of 2026-11-23, 19:11 selected the combined rule with a gate of 128 draws: column g128+r, build yg,
`TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_RANK=1`. While an anchor's cause age is under 128
draws, today's test answers. After that, the signed rank test answers. The takes above ran g+r only with a gate of 36
draws (here called g36+r, folder suffix `gr`). This section runs the 128-draw rule in the four scenes and compares it with
b and g36+r. Nothing in `src/` or `tests/` changed. The tools and folders use the column name `g128r`.

Two facts frame every scene:

- A start or a reset sets every anchor's cause age to 0. So under g128+r the gate is open from draw 0 to draw 127 and today's
  test answers in draws 36 to 127, as in column b. Under g36+r the gate closes at draw 36.
- After the last cause, g128+r keeps today's test for 128 draws, where g36+r keeps it for 36 draws.

### How to make them again

From the checkout's root, with bash (all tool paths sit under `docs/research/t1-yi11-region-evidence/`):

1. Films: run `bash tools/films.sh rect-bay-settled g128r`, `TURN=90 bash tools/films.sh rect-bay-settled g128r`, `KEEP=none bash tools/films.sh switch g128r`, and `bash tools/films.sh switch-film g128r`. `KEEP=none` drops the false-colour switch PNGs, which otherwise fill 2 GiB.
2. Frozen bay (sparse captures, with `KEEP_LIST` naming the kept draws):
   - Run `KEEP_LIST=<list> bash tools/gtakes.sh sparse g128r`. The list holds the early draws (`24` to `300`), the draws `run - 1`, `+ 2`, `+ 6`, `+ 12`, and `+ 24` of its nine answer runs, and draws `400`, `500`, `600`, `639`, and `659`.
   - Run `KEEP_LIST=24,32,36,40,48,56,64,72,80,88,96,104,112,120,127,136,152,168,200,240,300,639,659 bash tools/gtakes.sh sparse b gr`.
   - Run `bash tools/gtakes.sh long g128r` and `bash tools/gtakes.sh longfull g128r`.
3. Analysis tools (run inside `nix develop`):
   - `python3 -I tools/regionlog.py <folder> <after draw>` for each log.
   - `python3 -I tools/g128r.py <settled|turned|switch>` for curves, tiles, flicker, and 1:1 crops (with `CROPS`, `CROPBOX`, `CROPTAG`, and `CROPCOLS`).
   - `python3 -I tools/switchratio.py b g r gr g128r`.
   - `WALL=1 RUNS=<draw:x:y,...> python3 -I tools/baycold.py`.
   - `SP=g128r TAG=-g128r python3 -I tools/bay.py b d gr g128r`.
   - `python3 -I tools/level.py a b c d g r gr n32 n128 g128r`.

The sparse reruns of b and g36+r match the earlier takes byte for byte (stills at draws 300, 639 and 659, the ring logs
and the length dumps). So the earlier frames of draws 300 to 659 serve as g36+r's frames there.

Measures used below:

- Tile difference: the largest difference of a 96 x 96 output tile's mean 8-bit gray between two columns at one draw,
  over the 11 x 20 full 96 x 96 tiles (output y 0 to 1055, omitting the partial bottom 24 px of anchor row 11). 96 output
  pixels are the anchors' spacing. A staircase along the anchors' blocks shows as a large tile difference.
- Flicker: the largest |m(k + 1) - 2 m(k) + m(k - 1)| of a box's or a tile's mean 8-bit gray over the film's draws k.
  A smooth decay gives a small value.
- Patch (cold start): the band from 3 to 36 px of a still minus column d's late mean (bay-d-long, draws 660 to 1,610),
  as a standard deviation over the region, in units of main's full mean there. It is bay.py's blob against one shared
  reference, so the columns compare at each draw.

### Settled rect-bay

Files: `g128r-settled.json`, `logs/rbs-g128r.json`, `rbs-curve.json` (g128r added, with the old columns unchanged),
`videos/rect-bay-settled-g128r.mp4` (a | b over g36+r | g128+r), `pictures/rect-bay-settled-g128r-sheet.png` (a, b,
g36+r and g128+r at film draws 45, 63, 75, 105 and 150), `pictures/rect-bay-settled-g128r-crops.png` (1:1, the right
wall's anchor boundary, output x 960 to 1600, y 100 to 580, a, b, g36+r, g128+r and g at film draws 48, 75, 105 and 150)
and `pictures/rect-bay-settled-g128r-crops-floor.png` (1:1, the floor's lower right, x 1120 to 1920, y 680 to 1080, at
film draws 55, 63 and 75).

Measured, mean 8-bit gray (film draw k is log draw 200 + k):

| Box | Column | 30 | 45 | 60 | 75 | 90 | 120 | 150 | 179 |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Whole | b | 219.7 | 211.1 | 197.0 | 178.5 | 155.3 | 110.5 | 88.8 | 78.7 |
| | g36+r | 220.0 | 214.0 | 199.2 | 179.7 | 155.9 | 110.6 | 88.9 | 79.2 |
| | g128+r | 220.4 | 214.3 | 199.3 | 179.7 | 155.9 | 110.6 | 88.8 | 78.8 |
| | g | 220.5 | 215.7 | 208.2 | 197.4 | 183.6 | 155.2 | 137.7 | 125.4 |
| Right wall (x 1100 to 1800, y 100 to 800) | b | 214.1 | 203.5 | 186.0 | 164.5 | 139.7 | 97.6 | 79.1 | 71.4 |
| | g36+r | 214.5 | 209.3 | 189.2 | 166.1 | 140.5 | 97.7 | 79.1 | 71.9 |
| | g128+r | 215.4 | 209.9 | 189.4 | 166.1 | 140.5 | 97.7 | 79.1 | 71.6 |
| | g | 215.4 | 213.3 | 209.1 | 203.2 | 196.5 | 182.5 | 170.3 | 159.2 |

- Whole picture: g128+r minus b is at most +3.33 gray (film draw 48). Across all boxes, g128+r minus g36+r stays within
  0.94 gray (+0.94 on the right wall at draw 0, -0.81 on the floor at draw 168).
- Tiles: g128+r minus b peaks at +20.93 gray at film draw 63 in the floor tile at x 1344, y 960. 39 draws (film draws 37
  to 75) have a tile over 8 gray. g36+r peaks at +21.57 gray at the same tile and draw, with 40 draws over 8 gray. g peaks
  at +111.47 gray (draw 161). g128+r minus g36+r is at most 8.35 gray.
- Flicker: the same in a, b, g36+r and g128+r (whole 0.24 to 0.25 gray, largest tile 10.38 to 10.43 gray at film draw
  114, where the sunbeam's edge passes).
- Ring log: in the 200 frozen warm-up draws before the film, g128+r answers 314 times in log draws 36 to 127 (matching b)
  and 0 times in log draws 128 to 199. g36+r answers 5 and 0 times. From film draw 30 to 179, 144 to 149 of 240 anchors
  have an open gate under g128+r, against 11 to 147 under g36+r. Yes answers over the film: b 28,408, g36+r 24,884,
  g128+r 26,284, g 15,948.

Reading:

- g128+r shows no staircase. g's staircase edge shows at film draws 75 to 150 in the crops, and the same boxes under
  g128+r follow b and g36+r.
- The floor's lower right shows a faint lighter block with straight edges at film draws 55 and 63 under g128+r. It is
  weaker than g36+r's block at the same place, and gone at draw 75. b shows none. At sheet scale (400 px per pane) I do
  not see it.
- I see no flicker in the video beyond the sunbeam's own edge.

### Turned rect-bay

Files: `g128r-turned.json`, `logs/rbst-g128r.json`, `rbst-curve.json` (g128r added, with the old columns unchanged),
`videos/rect-bay-turned-g128r.mp4` (b | g36+r over g128+r | g), `pictures/rect-bay-turned-g128r-sheet.png` (film draws
0, 60, 75, 90 and 150) and `pictures/rect-bay-turned-g128r-crops.png` (1:1, x 960 to 1920, y 480 to 1080, b, g36+r and
g128+r at film draws 60, 70, 80 and 90).

Measured:

- No anchor has a cause after draw 200, as for g36+r. Yes answers per tested anchor-draw after draw 200: 0.469, as for
  g36+r.
- g128+r's frames are not byte-identical to g36+r's, because their histories differ after the frozen draws 36 to 127.
  The largest channel difference is 12 codes, and the largest mean absolute difference of a frame is 0.18 codes.
  The largest tile difference between them is 0.45 gray. The box curves agree to 0.1 gray.
- Against b, the largest tile difference is +25.62 gray at film draw 80 (tile x 1824, y 864), with 55 draws over 8 gray
  (within film draws 39 to 94, all except draw 40 at 7.93 gray). g36+r gives +25.57 gray and the same 55 draws.

Reading: g128+r equals g36+r in the turned take, as expected with no cause. Both show a soft blocky patchwork of lighter
rectangles on the wall ahead and the floor at film draws 60 to 80. I see it at sheet scale, and clearly at 1:1. It is
gone by film draw 90. b shows none. This is the rank test's later answer in regions whose cause no ray sees (the sun
patch is behind the camera).

### The fair cabin switch

Files: `settle-g128r.json`, `switch-ratio.json` (b, g, r, gr and g128r), `logs/sw-g128r-switch.json`, `-off.json`,
`-on.json`, `logs/swf-g128r.json`, `g128r-switch.json`, `videos/cabin-switch-g128r.mp4` (a | b over g36+r | g128+r,
film draws 150 to 459), `pictures/cabin-switch-g128r-sheet.png` (film draws 165, 200, 230, 260, 295, 380 and 420) and
`pictures/cabin-switch-g128r-crops.png` (1:1, x 480 to 1440, y 480 to 1080, b, g36+r and g128+r at film draws 210, 250,
290, 410 and 440). All three runs of the settle count ran for g128+r. The control runs depend on the column's rule, so I
reused none.

Measured, draws to settle, slowest changed region / whole picture:

| Column | After off | After on |
|---|---|---|
| b | 83 / 72 | 53 / 34 |
| g36+r | not settled in 200 / 128 | 47 / 31 |
| g128+r | 85 / 74 | 52 / 33 |

The whole picture against its control, switch / control - 1:

| Column | Off +40 | +45 | +70 | +100 | +135 | +199 | On +20 | +40 | +60 | +99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| b | +0.147 | +0.121 | +0.053 | +0.026 | +0.016 | +0.008 | -0.093 | -0.037 | -0.017 | -0.006 |
| g36+r | +0.172 | +0.146 | +0.081 | +0.058 | +0.048 | +0.039 | -0.087 | -0.030 | -0.011 | -0.002 |
| g128+r | +0.152 | +0.125 | +0.055 | +0.028 | +0.017 | +0.012 | -0.094 | -0.036 | -0.016 | -0.004 |

The interval after each cause leaves the rays (causes at film draws 163 to 169 and 363 to 369):

- After off, g36+r's valid gates close at film draws 205 to 210 (2 invalid anchors stay open). g128+r's stay open (240
  anchors) to film draw 296, and all 238 valid gates close by draw 302 (2 invalid anchors stay open). In film draws 205
  to 295, g128+r answers 4,335 times (today's test, gate open) and g36+r answers 1,935 times (mostly the rank test after
  its valid gates close at draws 205 to 210). In draws 296 to 359, g128+r answers 8 times and g36+r answers 0 times.
- After on, g128+r's gates stay open through film draw 459. In film draws 405 to 459 g128+r answers 3,045 times, g36+r
  843 times.
- Largest |switch / control - 1| in film draws 205 to 295: whole 0.125 (g128+r), 0.146 (g36+r), and 0.121 (b). Across
  changed regions: 0.195 (g128+r), 0.238 (g36+r), and 0.187 (b). In draws 405 to 459, the whole-picture peak is 0.030,
  0.023, and 0.030.
- Viewable film, mean 8-bit gray of the whole picture (lower half): draw 230: b 48.72 (38.72), g36+r 48.71 (38.71),
  g128+r 48.77 (38.76). Draw 295: 47.05 (37.23), 47.48 (37.57), 47.06 (37.23). Draw 359: 46.77 (37.00), 47.24 (37.37),
  46.81 (37.02). Draw 420: 103.18 (106.49), 103.07 (106.42), 103.13 (106.45).
- Flicker inside film draws 205 to 295: the largest tile value is 1.51 (b), 1.49 (g36+r) and 1.50 (g128+r) gray. Inside
  405 to 459: 1.37, 1.36 and 1.38.
- In both the switch film and the settle count's switch run, g128+r's answers equal g's on every draw. The viewable
  film's 310 frames of g128+r are byte-identical to g's. The gate stays open while the light still moves, and after it
  closes the rank test answers no on every draw, where g answers no by rule.

Reading: the 128-draw gate removes g36+r's fault after off. g128+r settles like b. In the sheet, the crops and the video
I see no blotch, false cut or flicker in film draws 205 to 295 or 405 to 459, in any of the three columns.

### The frozen bay from the start

Files: `baycold.json`, `logs/bay-g128r.json` (the sparse take's log, draws 0 to 659), `logs/bay-g128r-long-dif.json`,
`bay-numbers-g128r.json`, `level.json` (g128r added, with the old columns unchanged), `pictures/bay-cold-<floor|back-wall>-
<dif|full>.png` and `pictures/bay-cold-<floor|back-wall>-dif-residual.png` (1:1, b, g36+r and g128+r at draws 40, 64, 96,
127, 152, 200, 300 and 659), and `pictures/bay-<region>-<kind>-g128r.png` (bay.py's crops at draw 659 and the residuals
of the long takes, b, d, g36+r and g128+r).

Measured, the ring logs (draws 0 to 659 of the diffuse take, which matches the full take's counts after draw 300):

| Draws | b: yes / tested | g36+r | g128+r |
|---|---:|---:|---:|
| 36 to 127 | 358 / 21,436 | 3 / 21,436 | 358 / 21,436 |
| 128 to 300 | 482 / 40,309 | 6 / 40,309 | 6 / 40,309 |
| 301 to 659 | 840 / 83,647 | 12 / 83,647 | 14 / 83,647 |

No anchor-draw of g128+r holds a cause in draws 0 to 659 or in the long take's draws 660 to 1,659. At draw 127 all
240 of its gates are open. At draw 128, 7 remain open, and 234 anchors are valid.

Measured, the patch on the diffuse signal alone, floor / back wall:

| Draw | b | g36+r | g128+r |
|---:|---|---|---|
| 40 | 0.0135 / 0.0166 | 0.0136 / 0.0164 | 0.0135 / 0.0166 |
| 96 | 0.0150 / 0.0173 | 0.0078 / 0.0101 | 0.0150 / 0.0173 |
| 127 | 0.0141 / 0.0122 | 0.0071 / 0.0088 | 0.0141 / 0.0122 |
| 136 | 0.0151 / 0.0135 | 0.0070 / 0.0086 | 0.0151 / 0.0126 |
| 168 | 0.0101 / 0.0110 | 0.0058 / 0.0078 | 0.0089 / 0.0101 |
| 200 | 0.0086 / 0.0131 | 0.0054 / 0.0072 | 0.0071 / 0.0084 |
| 240 | 0.0071 / 0.0126 | 0.0050 / 0.0073 | 0.0060 / 0.0077 |
| 300 | 0.0082 / 0.0110 | 0.0049 / 0.0056 | 0.0052 / 0.0055 |
| 659 | 0.0085 / 0.0066 | 0.0038 / 0.0034 | 0.0038 / 0.0033 |

- In draws 36 to 127, g128+r's patch equals b's at every kept draw, because both run today's test. The full picture
  gives the same order (floor at draw 127: b 0.0141, g36+r 0.0083, g128+r 0.0141).
- In draws 128 to 300, g128+r's patch falls from b's to g36+r's. It is within 0.0003 of g36+r's on the floor and the
  back wall at draw 300.
- The floor level (diffuse alone, against d's late mean) reaches 0.9808 (b), 0.9534 (g36+r), and 0.9808 (g128+r) at draw
  127. By draw 300, those levels reach 0.9973 (b), 0.9775 (g36+r), and 0.9915 (g128+r). The cuts of draws 36 to 127 make
  g128+r's bounce light warm up faster than g36+r's.
- Settled (the long take, draws 660 to 1,610, and draw 659): mean length is 128.0 in all four regions at draw 639. Blob
  values are 0.0031 (sunlit patch), 0.0038 (back wall), 0.0040 (floor), and 0.0022 (right wall). g36+r gives 0.0031,
  0.0038, 0.0041, and 0.0022, and d gives 0.0025, 0.0038, 0.0041, and 0.0021. Mottle on the floor is 2.304, matching
  g36+r.

Reading:

- Draws 64 to 127: on the floor and the back wall, diffuse alone, g128+r shows b's soft patches at 1:1 (a lighter
  patch in the floor's middle at draw 96, faint soft vertical bands on the back wall at draws 64 and 96). g36+r is
  smoother. In the full picture the specular grain covers most of the difference, and I see it only side by side.
- Draws 136 to 200: the patches fade. At draw 152 a faint patch remains on the back wall's lower right. By draw 300
  g128+r looks like g36+r in the stills and in the residuals.
- Settled: g128+r's crops and residual maps at draw 659 look like g36+r's and d's.

### The signed rank test's runs in the frozen bay

The ticket flagged a slow part of the bay's region light (variance ratio 1.074 at 18 draws and 1.103 at 36 draws).
It gave r and g36+r 12 yes answers in 8 runs in 83,647 tested anchor-draws of draws 301 to 659 (1.43e-4, 1.9 times the
level of 7.6e-5).

Measured for g128+r:

- Draws 301 to 659: 14 yes answers in 9 runs in 83,647 tested anchor-draws, 1.67e-4, 2.2 times the level. 8 runs are
  g36+r's runs at the same draw and anchor, and the ninth is new at draw 416, anchor (10, 11) on the floor's bottom edge.
- Draws 661 to 1,659 (the long take): 23 yes answers in 14 runs in 232,767 tested anchor-draws, 9.88e-5, 1.3 times the
  level.

Patch of g128+r's diffuse still minus d's still of the same draw, in a 288 x 288 crop around each run's anchor (pictures:
`pictures/bay-wall-runs-g128r-dif.png` and `pictures/bay-wall-runs-g128r-full.png`, showing draw - 1, + 2, + 6, + 12,
and + 24):

| Run draw | Anchor | Patch at draw - 1 | Patch at draw + 6 | Shared with g36+r |
|---:|---|---:|---:|---|
| 333 | (18, 4) | 0.0023 | 0.0051 | yes |
| 335 | (2, 10) | 0.0066 | 0.0063 | yes |
| 360 | (18, 5) | 0.0046 | 0.0059 | yes |
| 376 | (17, 0) | 0.0009 | 0.0037 | yes |
| 395 | (1, 2) | 0.0010 | 0.0011 | yes |
| 415 | (16, 4) | 0.0016 | 0.0053 | yes |
| 416 | (10, 11) | 0.0016 | 0.0061 | new (floor bottom edge) |
| 433 | (17, 3) | 0.0034 | 0.0044 | yes |
| 558 | (2, 10) | 0.0051 | 0.0045 | yes |

Reading, kept apart from the numbers:

- In the residual times 10, six runs (333, 360, 376, 415, 416, 433) make a soft patch appear 4 to 6 draws after the run.
  It lasts past draw + 24. Runs 335, 395 and 558 leave nothing that I see.
- In the plain diffuse stills I see no patch at the wall runs, side by side over the five draws. In the full picture
  the walls' specular grain covers everything.
- The new floor run at draw 416 makes a faint unevenness at the floor's bottom edge at draws 422 to 440, diffuse alone
  and in the full picture. I see it only when I compare the frames side by side.

### The darkening residual

Measured (`level.json`, draws 660 to 1,610): g128+r against column a, diffuse alone, sunlit patch 1.0023, back wall
0.9977, floor 0.9963, right wall 0.9982. Full picture: 1.0006, 0.9981, 0.9970, 0.9996. These lie inside the band of
the earlier columns (d: 0.9963 and 0.9955 on the back wall and the floor, diffuse alone).

The cause of this -0.15 % to -0.40 % shift is unresolved. This record does not claim zero bias. By the parent's
decision of 19:11, the residual is acceptable for the visual experiment.

### The ticket's stop conditions and open points

| Point on t1-yi11 | Status, from the takes above |
|---|---|
| The cause gate's condition (17:58): no tap brings a cause in the frozen bay after the start | Holds for g128+r: 0 anchor-draws with a cause in draws 0 to 1,659. |
| The rule of the pick (17:58) | The parent picked g+r (rule 2). g alone fails the settled and turned takes (above). |
| Fork 4, the gate's length | 128 draws. It fixes g36+r's unsettled cabin switch after off (85 / 74 against b's 83 / 72). Its cost is the cold start (draws 36 to 127 act as b, patches fade by draw 300). |
| The darkening and the clamp-off pair (18:16 stop rule) | Unresolved, -0.23 % back wall and -0.37 % floor for g128+r, diffuse alone. Accepted by the parent for the experiment. No claim of zero bias. |
| The slow part of the bay's light (the logged against the shuffled order) | Its source is still not measured. Effect on g128+r: 1.67e-4 in draws 301 to 659, 9.88e-5 in draws 661 to 1,659. Visible only in the residual times 10, except one faint floor run. |
| Bar: no soft patch at 1:1 on the floor and the back wall of the frozen bay | Settled (draw 300 on): passes, like d. Cold start (draws 64 to about 200): b's patches, diffuse alone. |
| Bar: no lag that a viewer sees beside main in the light change films | Settled rect-bay: a faint lighter block on the floor at film draws 55 to 63, weaker than g36+r's. Turned rect-bay: a blocky patchwork at film draws 60 to 80, as g36+r. Cabin switch: passes. |
| Not known 4: a live scene | Not measured. Every scene here is a frozen still, two films and one switch. |

## Comparison of two closed-gate tests, first take: w36 and the pair rule (build yi)

Correction (2026-11-24, later the same day): the second column of this first take is NOT the specified hyst rule. It
ran a pair rule that I read from a one-line summary: it tests the neighbors' raw t, needs a neighbor even for a strong
seed, ignores the anchor's own earlier answer, and cannot pass through a confirmed weak anchor. This section calls it
"pair". Its folders and its tool column keep the old name `hyst` (`bay-hyst-*`, `rbst-hyst`, `ws-hyst`; knob
`TERTIUS_X_REGION_CLOSED=hyst` in build yi, `pair` in build yj). The specified rule is column hx of build yj, in the
section "The specified hyst rule (build yj, column hx)" below, which also replaces this section's verdict. The w36
column and the controls of this section stand: build yj reproduces them byte for byte.

On 2026-11-24 the parent ordered one scratch comparison after the hold on g128+r. g128+r was held because turned
rect-bay shows lighter rectangles that b does not show. The two candidates change only what answers while the gate is
closed, and both use the same new reset. Nothing in `src/` or `tests/` changed.

### The build and its columns

Build yi is yh plus the changes in the header of `scratch-yi.patch`:

- The ring keeps 72 draws in place of 36. Today's test and the rank test still read the newest 36. Two more layers
  hold each anchor's Welch statistics, by the parity of the draw's slot.
- `TERTIUS_X_REGION_CLOSED=w36` or `hyst` sets the closed reset and the closed test together.
- Closed reset, the same for both: a draw that does not continue the draw before (a reset or a new size) sets the cause
  age to 4,096, so the gate is closed. A draw that is not valid keeps counting from the draw before. Only a counted tap
  with a cause sets 0. The old reset set 0, an open gate, in both cases.
- w36: Welch's t of the newest 36 draws against the 36 before them, `(m_new - m_old) / sqrt((s_new^2 + s_old^2) / 36)`.
  The anchor answers yes where |t| >= 4.1. It tests only after 72 valid draws in a row, with no fallback to 18 draws.
- pair (yi's knob `hyst`): Welch's t of two windows of 18 draws. A same-direction pair of neighbor anchors (of the 8 around) makes this
  anchor answer yes: one of them at |t| >= 5.0 and the other at s t >= 2.6, with s the sign of the first. The
  neighbor's t is that of the draw before, because workgroups of one dispatch do not see each other's writes. A lone
  anchor at 5.0 does not answer.
- Both: a gate of 128 draws after a real cause (today's test while it is open) and a cap of 128.

I did not find the replay code that proposed the two candidates. The pair rule above was my reading of "W = 18
same-direction neighbor confirmation (seed |t_18| >= 5.0, confirm s t_18 >= 2.6)". It is wrong: see the correction
above.

| Column | Build | Knobs |
|---|---|---|
| byi, g128ryi | yi | b's and g128r's knobs |
| w36 | yi | `TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_CLOSED=w36` |
| pair (tool column `hyst`) | yi | `TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_CLOSED=hyst` |

Checks, measured: yi with b's knobs and with g128r's knobs draws the same bytes as yg in the frozen bay (stills 127, 300
and 659 and the length dump, diffuse alone). In turned rect-bay, yi's g128+r frames equal yh's seven kept frames byte
for byte. The ring logs differ only in the valid-draw count, which now goes to 72. So the old takes of b and g128+r
serve as same-build controls. A second w36 take gave the same ring log byte for byte.

### How to make them again

From the checkout's root, with bash (tool column `hyst` is the pair rule):

1. `bash docs/research/t1-yi11-region-evidence/tools/builds.sh yi`
2. Frozen bay: `KEEP_LIST=$(cat tools/keep-yi.txt) bash tools/gtakes.sh sparse w36 hyst` and
   `bash tools/gtakes.sh long w36 hyst`. `tools/keep-yi.txt` is the cold-start list of the g128+r section plus draws
   run - 1, + 2, + 6, + 12 and + 24 of each answer run, found in a first take with the cold-start list alone.
3. Turned rect-bay: `TURN=90 bash tools/films.sh rect-bay-settled g128ryi w36 hyst` (b is the old `rbst-bl`).
4. The walk: `bash tools/films.sh walk-switch byi g128ryi w36 hyst` and `bash tools/films.sh walk-off byi`.
5. `python3 -I tools/yi.py <turned|walk|bay|look>` in the dev shell, and the two films with `tools/grid.py` (`CROP`,
   `PREFIX=h` for the walk).

### The new scene: a lamp switch while the camera moves

`walk-switch` (yifilm.py): orbit's person camera after 60 ticks, the still, 160 standing frames, then 120 walking frames
(key W), then 200 standing frames. The cabin lamps `z:-12:4` go off at walk frame 60 and stay off. The control
`walk-off` holds them off from draw 0 with the same walk. Fixed 4.08 stops, bloom on, the game's tone curve. It keeps
walk frames 50 to 119 and stand frames 0 to 159. During the walk every tap moves on screen, so no region is valid and no
ray brings a counted cause. When the walk stops, the old reset leaves every gate open (cause age 0 from each draw that
was not valid). Under the closed reset a gate opens only where a counted tap brings a cause after the stop: 22
anchor-draws hold one, and up to 14 gates are open (14 at stand frame 40, 8 at frame 100).

The change shows in the controls. In b the floor box (y 600 to 1080) falls from 117.8 gray at walk frame 55 to 73.2 at
walk frame 65, and the near floor goes dark in `walk-switch-yi-sheet.png`.

### Turned rect-bay

Files: `yi-turned.json`, `videos/rect-bay-turned-yi.mp4` (first take: b | g128+r over w36 | pair, film draws 30 to 110; the file now shows hyst (hx) in place of pair, 1:1 crop of
output x 0 to 960, y 540 to 1080), `pictures/rect-bay-turned-yi-sheet.png`, `rect-bay-turned-yi-crops.png` (1:1, x 960
to 1920, y 480 to 1080, film draws 60, 70, 80 and 90), and `rect-bay-turned-yi-1to1-b-w36.png` and
`-pair-g128r.png` (renamed from `-hyst-g128r.png`; unlabeled, left column then right column, rows film draws 57, 65 and 75, output x 0 to 960, y 600
to 1080).

Measured, mean gray of the wall ahead (y 0 to 500) / the floor (y 700 to 1080):

| Column | Film draw 45 | 60 | 75 | 90 |
|---|---|---|---|---|
| b | 203.5 / 199.6 | 190.2 / 184.6 | 171.6 / 164.5 | 149.5 / 141.7 |
| g128+r | 206.9 / 203.2 | 198.4 / 196.5 | 175.2 / 171.1 | 150.6 / 143.6 |
| w36 | 207.2 / 203.1 | 192.0 / 185.8 | 172.2 / 164.8 | 149.6 / 141.8 |
| pair | 206.0 / 202.7 | 194.6 / 194.7 | 173.3 / 168.9 | 150.0 / 142.9 |

| Column | Largest tile difference against b | Draws over 4 gray | Over 8 gray | Yes answers in the film | First answer |
|---|---|---:|---:|---:|---:|
| g128+r | +25.62 at draw 80, tile x 1824, y 864 | 69 | 55 | 20,151 | draw 28 |
| w36 | +13.35 at draw 57, tile x 96, y 960 | 39 | 18 | 31,031 | draw 36 |
| pair | +21.41 at draw 65, tile x 480, y 864 | 57 | 39 | 23,599 | draw 32 |

b answers 25,980 times. No gate opens in the film for any column. g128+r's row repeats the earlier record's numbers. The
grid step measure in `yi.py` (the step of column minus b across the anchors' tile lines, against lines between them) does
not separate the columns: 0.30 to 0.47 gray at both kinds of line. I do not use it.

Reading:

- b is smooth at 1:1. g128+r shows its blocky patchwork at film draws 57 to 80, as before.
- w36 shows faint lighter blocks with straight edges on the floor at film draws 57 to 65, at the bottom left and along a
  vertical line near output x 300. I see them only at 1:1 and only when I look for them. They are gone by draw 75.
- pair shows soft blocks with straight edges at draws 57 to 65, a darker block at the upper left and a lighter one in
  the middle of the crop. They are weaker than g128+r's and clearer than w36's.

### The walk

Files: `yi-walk.json`, `videos/walk-switch-yi.mp4` (first take: b | g128+r over w36 | pair; the file now shows hyst (hx) in place of pair; stand frames 0 to 159, 1:1 crop of
output x 960 to 1920, y 0 to 540), `pictures/walk-switch-yi-sheet.png`, `walk-switch-yi-crops.png`,
`walk-switch-yi-seams.png` (b's frame at stand frame 100 with output x 1440 onward from g128+r, w36, hyst (hx, added later) and pair, one row
each, crop x 960 to 1920, y 0 to 540) and `walk-switch-yi-w36-minus-b-x20.png` (w36 minus b at stand frame 100,
Gaussian sigma 8 px, times 20 on 128, half size).

Measured, the whole picture's mean gray minus the control's, by stand frame (frame 0 is the first draw after the walk):

| Column | 0 | 40 | 80 | 100 | 130 | 159 |
|---|---:|---:|---:|---:|---:|---:|
| b | +5.11 | +3.67 | +2.56 | +2.16 | +1.67 | +1.33 |
| g128+r | +5.15 | +3.70 | +2.53 | +2.13 | +1.63 | +1.40 |
| w36 | +5.05 | +3.64 | +3.22 | +3.07 | +2.84 | +2.57 |
| pair | +4.98 | +3.59 | +3.07 | +2.89 | +2.74 | +2.56 |

- Ring logs after the stop: b answers 457 times and g128+r 345 times, both first at stand frame 37. w36 answers 38
  times (first at frame 49) and pair 10 times (first at frame 41). 22 anchor-draws hold a counted cause. g128+r has 238
  open gates from frame 1, and w36 and pair have at most 14.
- Column minus b, Gaussian sigma 8 px, 99th percentile over the frame: w36 +3.26, +3.21 and +3.08 gray at stand frames
  80, 100 and 159, pair +2.07, +2.44 and +2.81. g128+r stays within +0.10 to frame 130.
- At stand frame 159, w36's whole-picture excess (+2.57) equals b's at about frame 80. So w36 trails b by about 38
  draws at frame 100 and by about 77 at frame 159. The difference image shows the excess on the ceiling and the walls,
  in blocks of the anchors' regions where b cut and w36 did not.

Reading: w36 and pair show no blocks of their own in the walk. They keep the old light longer, on the ceiling and the
walls, after b's cuts. In the seam picture I see no seam at x 1440 for any column. Side by side, the right-hand pane
looks lighter to me whatever it holds, even g128+r beside b, which measure within 0.15 gray. So I do not claim that a
viewer sees this lag. The lag goes with fewer cuts after the stop: b answers 457 times, w36 38 and pair 10, and the
closed reset opens at most 14 of 238 gates where the old reset opens all of them. No take isolates the cause: no column
has the closed reset with today's test, so this record does not say how much of the lag the reset alone makes.

### The frozen bay

Files: `yi-bay.json`, `pictures/bay-cold-yi-<floor|back-wall>-<dif|full>.png` (1:1; first take b, g128+r, w36 and pair; now also hyst (hx); at draws 40,
72, 96, 127, 168, 240, 300 and 659), `bay-runs-yi-dif.png` and `bay-runs-yi-full.png` (each run after draw 300: 288 x
288 crops around its anchor at draws - 1, + 2, + 6, + 12 and + 24, diffuse still and still minus d's still of the same
draw times 10, and the full picture).

Measured, yes answers / tested anchor-draws (36 valid draws in a row, 72 for w36):

| Draws | b | g128+r | w36 | pair |
|---|---|---|---|---|
| 0 to 127 | 358 / 21,436 | 358 / 21,436 | 6 / 13,048 | 0 / 21,436 |
| 128 to 300 | 482 / 40,309 | 6 / 40,309 | 4 / 40,309 | 0 / 40,309 |
| 301 to 659 | 840 / 83,647 | 14 / 83,647 | 10 / 83,647 | 8 / 83,647 |
| 661 to 1,659 (long take) | | 23 in 14 runs / 232,767 | 6 in 6 runs / 232,767 | 4 in 4 runs / 232,767 |

- No anchor-draw of w36 or pair holds a cause.
- w36's runs: draws 72 (two anchors), 90, 157, 215, 222, 395, 397, 464, 469, 485, 489, 658 and 659. Those before draw
  300 fall in the bounce light's warm-up: the level table below shows the light still rising there.
- pair's one event: draws 406 and 407 at the four neighbor anchors (11, 3), (11, 4), (12, 3) and (12, 4), output x
  1056 to 1152, y 288 to 384, the wall with the small dark window. The pair rule cuts all four together.
- The runs' patch against d's still of the same draw rises from 0.0003 to 0.0006 at draw - 1 to 0.004 to 0.010 at draws
  + 6 to + 24, for w36 and for pair. In the residual times 10 they leave soft patches with straight edges. In the plain
  diffuse and full stills I see nothing at the runs.

Measured, the cold start, diffuse alone, patch floor / back wall and level floor / back wall (against d's late mean):

| Draw | b and g128+r: patch | level | w36: patch | level | pair: patch | level |
|---:|---|---|---|---|---|---|
| 72 | 0.0121 / 0.0146 | 0.954 / 0.953 | 0.0093 / 0.0123 | 0.939 / 0.922 | 0.0093 / 0.0123 | 0.939 / 0.922 |
| 96 | 0.0150 / 0.0173 | 0.972 / 0.977 | 0.0079 / 0.0100 | 0.946 / 0.932 | 0.0079 / 0.0101 | 0.946 / 0.932 |
| 127 | 0.0141 / 0.0122 | 0.981 / 0.985 | 0.0072 / 0.0088 | 0.954 / 0.943 | 0.0072 / 0.0087 | 0.953 / 0.942 |
| 200 | b 0.0086 / 0.0131, g128+r 0.0071 / 0.0084 | 0.993 / 1.005, 0.987 / 0.994 | 0.0053 / 0.0071 | 0.967 / 0.960 | 0.0054 / 0.0071 | 0.966 / 0.959 |
| 300 | b 0.0082 / 0.0110, g128+r 0.0052 / 0.0055 | 0.997 / 1.002, 0.991 / 0.994 | 0.0049 / 0.0056 | 0.978 / 0.972 | 0.0049 / 0.0056 | 0.977 / 0.971 |
| 659 | b 0.0085 / 0.0066, g128+r 0.0038 / 0.0033 | 1.005 / 1.003, 1.001 / 1.002 | 0.0038 / 0.0034 | 0.998 / 0.997 | 0.0038 / 0.0034 | 0.998 / 0.997 |

Reading: from the start, w36 and pair show the even falloff that d showed, without b's soft blotches at draws 72 to 127.
They warm up slower. At draw 127 the floor and the back wall are 4.6 % and 5.7 % under d's late mean, against b's 1.9 %
and 1.5 %, and at draw 300 they are 2.2 % and 2.8 % under, against g128+r's 0.9 % and 0.6 %. In the sheets their floor's
left side is visibly darker than b's from draw 96 to draw 240.

### Verdict and cost of the first take (superseded by the hx section below)

Neither candidate passes the bar that held g128+r: no straight-edged patch at 1:1 that b does not show.

- w36 comes closest. Its turned blocks are faint and short (film draws 57 to 65), and its settled frozen bay answers 6
  times in 232,767 anchor-draws, against g128+r's 23. It keeps the closed reset's costs: the slower warm-up from a cold
  start and the longer light after the walk.
- pair's turned blocks are clearer, and its one frozen-bay event cuts four regions at once.

So that take had no timing. The read counts per variant are in the hx section below. The counts that stood here mixed
the scratch build's paths with the variants' own paths.

Limits:

- One scene per question, one take each, on one machine. Thresholds come from the replay, unchanged, with no sweep.
- The pair rule was a wrong reading of the brief (see the correction at the top of this section).
- The walk's lag is measured, but my eye could not judge it, because pane position biases the brightness I see.
- The walk scene has one take per column and no column that separates the closed reset from the closed test.
- The frozen bay's level gap of the earlier section (-0.15 % to -0.40 %) is not studied here.

## The specified hyst rule (build yj, column hx)

The parent's correction of 2026-11-24 gave the second column's exact rule, from the Design X line's replay report of
00:17. This section runs that rule as column hx, in the same build as w36 and the controls. It replaces the verdict of
the first take.

### The rule and the build

Build yj is yi plus the changes in the header of `scratch-yj.patch`. With `TERTIUS_X_REGION_CLOSED=hyst`, a closed-gate
anchor answers by `probeHystDecisionOf`:

- An anchor is tested after 36 valid draws in a row (arming). An untested anchor does not answer.
- |t18| >= 5.0 fires with the sign of t18, alone.
- Else |t18| >= 2.6 fires with the sign of t18 only if the anchor itself or one of its 8 neighbors inside the anchors
  fired with the same sign in the draw before.
- "Fired in the draw before" is the signed answer in the ring's b of `layerBefore()`. In hyst mode b holds 1 or -1
  whatever answered: this rule, or today's test while the gate is open (its sign is that of the newer window's mean
  minus the older one's). Every reader of b asks only b != 0, so a negative answer cuts as a positive one does.
- A draw that does not continue the draw before (a reset or a new size) reads no earlier answer.
- A weak anchor that fired can so make a weak neighbor fire one draw later.

The thresholds, the closed reset, the gate of 128 draws after a real cause and the cap of 128 are the ones of w36. The
earlier pair rule stays in yj as `TERTIUS_X_REGION_CLOSED=pair`, only to reproduce the first take.

### Checks

1. The rule through the shader: `TERTIUS_X_HYST_TRACE=1` takes t18, the gate and today's signed answer from a fixed
   table at slots 40 to 46 (`probeTraceOf`), in a 60-draw frozen run (slot = draw there). `tools/hysttrace.py` holds its
   own copy of the table and the rule. It compares every anchor's logged answer on the whole 20 x 12 grid at all 60
   draws. Measured: 16 nonzero answers and 0 differences. Its cases, each with the expected and the logged answer equal:

   | Case | Draw, anchor | Answer |
   |---|---|---:|
   | isolated + strong seed (+5.5) | 40, (2, 2) | +1 |
   | isolated - strong seeds (-6.0, -5.2) | 40, (10, 5); 43, (15, 8) | -1, -1 |
   | self persistence: +3.0 after its own + answer | 41, (2, 2) | +1 |
   | same-sign weak hops from a seed, then from confirmed weak anchors | 41 to 44, (3..6, 2) | +1 each |
   | weak + with no fired neighbor; weak + two draws after the neighbor fired | 44, (8, 2); 46, (7, 2) | 0, 0 |
   | weak - hops from a - seed | 41, (11, 5); 42, (12, 5) | -1, -1 |
   | weak + beside a - seed (opposite sign); -2.0 beside a - seed (under the bar) | 41, (9, 5); 41, (10, 6) | 0, 0 |
   | open gate, today's test answers - / no answer | 40, (5, 9); 40, (8, 9) | -1, 0 |
   | weak - seeded by the open gate's - answer; weak + beside it; weak + beside an open gate without answer | 41, (6, 9); (4, 9); (9, 9) | -1, 0, 0 |
   | diagonal weak hops at the first and the last anchor (no read outside the anchors) | 41, (1, 1); (18, 10) | +1, -1 |

2. The rule on the real takes: `hysttrace.py check` recomputes every closed-gate anchor-draw of the four hx takes from
   the logged t18 (`region-t.log`) and the logged signed answers of the draw before. Measured: 769,320 closed-gate
   anchor-draws, 0 differences. Among them: 15,771 - seeds and 11,852 - weak firings in turned rect-bay, 11 seeds and
   146 weak firings in the walk, 5 seeds and 28 weak firings in the two frozen-bay takes. The walk's 1,800 open-gate
   anchor-draws all hold -1, 0 or 1 (2 answers, both -1).
3. yj reproduces yi: in the frozen bay (stills 127, 300 and 659, the length dump, `region-ring.log` and `region-t.log`)
   for b, g128+r, w36 and the pair rule; in the walk (all 230 kept frames and both logs) for g128+r and w36; in turned
   rect-bay (all 180 frames and both logs) for w36. All byte for byte. So the first take's w36 and control takes serve
   here.
4. A second hx frozen-bay take gave the same ring log byte for byte.

Not checked: a reset in the middle of a take. The trace's first draw is a reset with an empty ring, and no take here
resets later. The rule reads no earlier answer on such a draw by its code (`regionContinues()`), not by a measurement.

### How to make them again

From the checkout's root, with bash:

1. `bash docs/research/t1-yi11-region-evidence/tools/builds.sh yj`
2. Trace: `DRAWS=60 KEEP_LIST=59 bash tools/bayrun.sh yj bay-hx-trace TERTIUS_X_DIFFUSE_CAP=128
   TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_CLOSED=hyst TERTIUS_X_HYST_TRACE=1 TERTIUS_PROBE_SIGNAL=2
   TERTIUS_X_REGION_LOG=/private/tmp/t1-yi11/runs/bay-hx-trace`, then `python3 -I tools/hysttrace.py
   /private/tmp/t1-yi11/runs/bay-hx-trace` in the dev shell.
3. The equivalence takes: `KEEP_LIST=127,300,659 bash tools/gtakes.sh sparse-dif byj g128ryj w36yj pairyj`,
   `bash tools/films.sh walk-switch g128ryj w36yj` and `TURN=90 bash tools/films.sh rect-bay-settled w36yj`, compared
   with `cmp` against the yi folders.
4. hx: `KEEP_LIST=$(cat tools/keep-yi.txt) bash tools/gtakes.sh sparse hx`, `bash tools/gtakes.sh long hx`,
   `TURN=90 bash tools/films.sh rect-bay-settled hx` and `bash tools/films.sh walk-switch hx`. Then
   `python3 -I tools/hysttrace.py check <the four folders>` and `python3 -I tools/yi.py <turned|walk|bay|look>`.
5. Timing: `bash tools/yitime.sh /private/tmp/t1-yi11/runs/time-yj2`, then `python3 -I tools/yitime.py
   /private/tmp/t1-yi11/runs/time-yj2` in the dev shell.

### Turned rect-bay

Measured, mean gray of the wall ahead / the floor, and the tile difference against b:

| Column | Film draw 45 | 60 | 75 | 90 | Largest tile difference against b | Draws over 4 / 8 gray |
|---|---|---|---|---|---|---|
| b | 203.5 / 199.6 | 190.2 / 184.6 | 171.6 / 164.5 | 149.5 / 141.7 | | |
| g128+r | 206.9 / 203.2 | 198.4 / 196.5 | 175.2 / 171.1 | 150.6 / 143.6 | +25.62 at draw 80 | 69 / 55 |
| w36 | 207.2 / 203.1 | 192.0 / 185.8 | 172.2 / 164.8 | 149.6 / 141.8 | +13.35 at draw 57 | 39 / 18 |
| hyst (hx) | 204.6 / 201.4 | 190.3 / 184.5 | 171.6 / 164.4 | 149.5 / 141.7 | -7.02 at draw 54, tile x 960, y 0 | 29 / 0 |
| pair (first take) | 206.0 / 202.7 | 194.6 / 194.7 | 173.3 / 168.9 | 150.0 / 142.9 | +21.41 at draw 65 | 57 / 39 |

hx answers 27,623 times in the film, every answer -1 (the light falls), first at film draw 32. No gate opens.

Pictures: `rect-bay-turned-yi-crops.png` and `-sheet.png` (now with hx), `rect-bay-turned-yi-1to1-b-hyst.png` (b left,
hx right, output x 0 to 960, y 600 to 1080, film draws 57, 65 and 75), and the video `rect-bay-turned-yi.mp4`.

Reading: at 1:1 on the floor at film draws 57 to 75, hx is as smooth as b. I see none of the straight-edged blocks of
g128+r, pair or w36. At its largest tile difference (the wall ahead at the top, film draw 54) I looked at x 480 to 1440,
y 0 to 480, at draws 48, 54 and 60: both b and hx show the same faint soft unevenness of the change, and hx adds no
edge that b lacks.

### The walk

Measured, the whole picture minus the control, by stand frame:

| Column | 0 | 40 | 80 | 100 | 130 | 159 |
|---|---:|---:|---:|---:|---:|---:|
| b | +5.11 | +3.67 | +2.56 | +2.16 | +1.67 | +1.33 |
| w36 | +5.05 | +3.64 | +3.22 | +3.07 | +2.84 | +2.57 |
| hyst (hx) | +5.02 | +3.61 | +3.05 | +2.84 | +2.68 | +2.50 |

- After the stop hx answers 36 times (first at stand frame 41), against b's 457. Its gates: up to 14 open, as w36.
- hx minus b, Gaussian sigma 8 px, 99th percentile: +0.20, +2.04, +2.42 and +2.81 gray at stand frames 40, 80, 100
  and 159.
- The seam picture `walk-switch-yi-seams.png` now has an hx row. I see no seam in any row.

Reading: hx keeps the old light on the ceiling and the walls as long as w36 and pair do, 2 to 3 gray above b from
stand frame 80. My eye could not judge this lag (see the first take's reading). The same limit holds: no take
separates the closed reset from the closed test.

### The frozen bay

| Draws | yes answers / tested anchor-draws (36 valid draws in a row) |
|---|---|
| 0 to 127 | 0 / 21,436 |
| 128 to 300 | 0 / 40,309 |
| 301 to 659 | 10 / 83,647: one event, the pair's four anchors (11, 3), (11, 4), (12, 3) and (12, 4) at draws 406 and 407; pair gave 8 answers at the same event |
| 661 to 1,659 | 14 / 232,767 in 5 runs: two events, anchors (0, 6), (0, 7) and (1, 7) from draw 882, and (13, 3) and (13, 4) from draw 1,313 |

Against the other columns in draws 661 to 1,659: g128+r 23 answers in 14 runs, w36 6 in 6, pair 4 in 4.

- Patch of the event at draw 406, against d's still of the same draw: 0 at draw - 1, then 0.006 to 0.007 at draws + 6
  to + 24, as pair's (`bay-runs-yi-dif.png`, `-full.png`, now with hx rows). In the residual times 10 the patch is
  soft with straight edges. In the plain diffuse and full stills I see nothing.
- Cold start: hx's patch and level equal w36's and pair's to 0.0001 and 0.001 at every kept draw (`yi-bay.json`):
  floor / back wall level 0.953 / 0.942 at draw 127, 0.977 / 0.971 at draw 300, 0.998 / 0.997 at draw 659. hx makes no
  answer before draw 301, so its cold start shows the closed reset's slower warm-up alone, as the sheets do.

### Timing

`tools/yitime.sh` ran the frozen bay (660 draws, 1920 x 1080 at the render scale 2/3) once per column in the order
yh-b, b, g128+r, w36, hx, then in the reverse order, each take under the GPU lock's exclusive mode. Each take started at
a 1-minute load under 8 (4.30 to 7.44 at the starts, `yi-time.json` holds the log). yh-b is build yh with the cap of 128:
today's test without yj's 72-layer ring and Welch statistics. The other four columns are build yj. Measured, the mean of
the two takes, in ms per draw:

| Column | Frame span, median | Frame span, mean | accumulate + filter indirect diffuse, median |
|---|---:|---:|---:|
| yh-b | 16.248 | 17.151 | 4.454 |
| b | 16.179 | 16.367 | 4.444 |
| g128+r | 16.115 | 16.310 | 4.439 |
| w36 | 16.175 | 16.992 | 4.468 |
| hx | 16.249 | 16.475 | 4.462 |

- The two takes of one column differ by up to 0.46 ms in the frame span's median (yh-b: 16.477 and 16.020) and by up
  to 0.10 ms in the pair sum (yh-b: 4.506 and 4.403).
- The columns' means differ by at most 0.13 ms in the frame span's median and 0.03 ms in the pair sum.

Reading: no column differs from another by more than the spread between the two takes of one column. These takes
cannot resolve a cost of the decision path. The region test runs in "accumulate indirect diffuse"; MoltenVK's time of
that pass alone is not valid, so the table gives the sum with its neighbor.

Limits of this measure: all yj columns run the scratch build's paths (a 72-layer load and two stores per anchor per
draw, see below), so yj's columns differ only in the decision path. yh-b against yj-b is the only measure of the scratch
overhead, and it too is inside the spread. Two takes per column, on one machine, with other agents' work at a low load.

### Read cost per variant

Unit: RGBA32F texel loads and stores of `regionRing` per draw, 16 B per texel, at 1280 x 720 (240 anchors). These are
the loads of the region test of each variant as production would build it. The blocks' later reads of the answers (up
to 9 per block) are the same in every variant and are not counted.

| Variant | Loads per anchor per draw | Stores per anchor per draw | Loads per draw (240 anchors) |
|---|---:|---:|---:|
| b (today's test) | 36 | 1 | 8,640 (about 138 KB) |
| g128+r | 36 | 1 | 8,640 (about 138 KB) |
| w36 | 72 (a ring of 72 layers) | 1 | 17,280 (about 276 KB) |
| hx | 36, plus up to 9 for a closed-gate tested anchor with 2.6 <= \|t18\| < 5.0 | 1 | 8,640 plus the extra below |

hx's extra loads read b of `layerBefore()` at the anchor and its neighbors inside the anchors. The loop stops at the
first neighbor that fired with the same sign. Measured anchor-draws in that band, per draw (`hysttrace.py check`): about
3.4 in the frozen bay (5,705 in 1,660 draws), about 33.9 in turned rect-bay (12,900 in 380 draws) and about 3.4 in the
walk (1,723 in 513 draws). So at most about 31, 305 and 31 extra loads per draw.

The scratch build yj itself makes 72 loads and 2 stores per anchor per draw in every column (the ring and the stat log),
and its pair mode adds up to 8 stat loads. These are the scratch build's paths, not a variant's cost, and they are why
the timing above cannot show the variants' difference in reads.

### Verdict of the comparison

The bar is the one that held g128+r: no straight-edged patch at 1:1 in motion that b does not show.

| Column | Turned rect-bay: largest tile difference against b, draws over 8 gray | Frozen bay, draws 661 to 1,659: answers in runs | Walk, stand frame 159, minus the control | Passes the turned bar |
|---|---|---|---:|---|
| b | 0 (the reference) | not counted here | +1.33 | the reference |
| g128+r | +25.62, 55 | 23 in 14 | +1.41 | no |
| pair (first take) | +21.41, 39 | 4 in 4 | +2.56 | no |
| w36 | +13.35, 18 | 6 in 6 | +2.57 | no (faint blocks at draws 57 to 65) |
| hx | -7.02, 0 | 14 in 5 (2 events) | +2.50 | yes |

- hx passes the turned bar: at 1:1 it is smooth like b on the floor and the wall ahead, and all its 27,623 answers there
  are negative cuts that follow the light's fall.
- In the settled frozen bay hx has 2 false events in 232,767 tested anchor-draws. Each patch reaches 0.006 to 0.007 of
  the diffuse and shows only in the residual times 10.
- The closed reset's costs stay with hx, as with w36: the slower warm-up from a cold start, and the old light that stays
  2 to 3 gray above b after the walk's lamp switch. That lag is measured, but my eye could not judge it.
- The timing cannot resolve a cost; the read cost of hx is about b's plus a few hundred loads per draw at most.

This record does not decide the integration or the ticket. It leaves both to the parent.

Limits:

- One take per column per scene, on one machine. Thresholds come from the replay, unchanged, with no sweep.
- The rule's source is the parent's text from the Design X line's report of 00:17. I did not find the replay's code, so
  the checks prove the shader matches that text, not that it matches the replay.
- A reset in the middle of a take is not exercised (see Checks).
- The walk has no column with the closed reset and today's test, so no take separates the closed reset's effect from
  the closed test's. The walk's gate counts (22 anchor-draws with a counted cause, up to 14 gates open) describe the
  takes; they do not give a cause of the lag. g128+r, which has no closed reset, stays near b (+1.41 against +1.33 at
  stand frame 159), but it also differs from the three closed-reset columns in its test, so it is no such control.
- The timing comes from the scratch build, whose own paths hide the variants' read difference.
- The frozen bay's level gap of the earlier section (-0.15 % to -0.40 %) is not studied here.
