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
bash (zsh passes a column's two knobs as one word: see "Faults found on the way").

1. `bash tools/builds.sh ya yc` builds the two probe builds into /private/tmp/t1-yi11/{ya,yc}/probe-bin.
2. The frozen bay, per column, with the column's knobs: `bash tools/bayrun.sh <build> bay-<column> <knobs>`, the same with
   `TERTIUS_PROBE_SIGNAL=2` as `bay-<column>-dif`, and `DRAWS=1660 EVERY=50 bash tools/bayrun.sh <build>
   bay-<column>-long-dif <knobs> TERTIUS_PROBE_SIGNAL=2`.
3. `nix develop --command python3 -I tools/bay.py` prints the bay's table, writes `bay-numbers.json` and the bay pictures.
4. The films: `bash tools/films.sh rect-bay a b c d`, `bash tools/films.sh switch a b c d` (the settle counts) and
   `bash tools/films.sh switch-film a b c d` (the pictures).
5. `nix develop --command python3 -I tools/rbcurve.py` prints rect-bay's brightness per draw and writes
   `rect-bay-curve.json`.
6. `python3 -I tools/grid.py <out.mp4> <first draw> <note> "<label>|<folder>|1280 x 720"...` makes a film in a 2 x 2 grid,
   and `tools/sheet.py <out.png> <draws> <note> "<label>|<folder>|<first draw>"...` a frame sheet.

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

## Faults found on the way

- The first column d ran as column a. The take loop ran in zsh, which does not split an unquoted variable, so the two
  knobs reached bayrun.sh as one word and `TERTIUS_X_DIFFUSE_CAP` read "128 TERTIUS_X_REGION_OFF=1", which is no number.
  Its pictures and dump were byte-identical to column a's. Every number here comes from the run again at 17:01 with
  separate words. `tools/films.sh` holds the columns' knobs and runs in bash.
- The first switch films ran without the survey's probe options (`--bloom off --tone-curve hable`). Column a then
  settled after 197 draws, against main's 81 in t0-qvm1. yifilm.py now passes the options. With them, column a gives
  t0-qvm1's numbers exactly.
- A first blob measure against one shared reference take (cap 512, region evidence off, 1,660 draws) failed. That
  take's own frames brightened the floor by 1.3 % from draw 660 to draw 1,460, and its mean differed from every column's
  still by the same large smooth shape, which leaked into the band. The cause (the early draws in a history of 512, or a
  slow settle of the bounce light) is not measured. The table above uses each column's own late mean, as the ticket does.
