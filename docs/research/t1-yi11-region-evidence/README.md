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
