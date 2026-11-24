# The adaptive reflection share in production (ter-fe63, stage 2)

Stage 2 builds Design 1 of stage 1 (`../README.md`) into the engine at Config B:

| Setting | Value |
|---|---|
| Ceiling | 0.25 |
| Step | 1/128 |
| Guard | 1/16 |
| Most ratio | 2 |
| Roughness ramp | 0.1 to 0.38 |

Every tile restarts at the ceiling when the frame resets its histories, and keeps its share at a new render size. ARCHITECTURE.md, "The reflection kind's share", describes the design. This record compares the production branch at commit `945f36790`, after the fixes of the stage 2 review, with unpatched main `56feef7d5` on the Mac M1 Max. The takes before the fixes (`afa41ef57`) gave the same numbers within 2 specks and 0.03 ms.

Both builds are Release builds. Every take ran at `1920 x 1080`, render scale `0.6667` (`1280 x 720`), with a fixed exposure from the CLI's `--exposure`. The untimed takes ran under `tools/gpu-lock.sh --shared` and filmed losslessly (`--video`, 60 fps). The films in `pics/` are H.264 CRF 12, `yuv420p`, 60 fps, with main on the left and the branch on the right. `tables/takes-summary.json` holds every number below, and `tables/timing.json` holds every timed run.

The measures are those of stage 1:

- **Specks:** the pixels of the nose's ceiling cone `[0:500, 480:1880]` whose 601 luma is more than 12 above the median of their 7 x 7 neighbourhood, outside the lamps.
- **Floor grain:** the RMS of the floor box's linear luminance less its Gaussian blur of sigma 2, divided by the box's mean. This differs from stage 1's grain, which subtracted a reference mean, so only the ratios compare across the two records.
- **Flicker:** the frame-to-frame RMS of a box's 601 luma, from 0 to 255.

## Results

| Take | Main | Branch |
|---|---:|---:|
| Mirror plate still (`mirror-balls.json`, frame 1) | | byte for byte the same image |
| Sunlit nose, settled specks, frames 120 to 359, median | 3,496 | 144 |
| Sunlit nose, specks of the first 30 frames, median | 10,484 | 1,008 |
| Sunlit nose box, flicker, frames 120 to 359 | 1.046 | 0.482 |
| Sunlit nose box, mean luma, frames 120 to 359 | 146.22 | 151.28 |
| rect-lamps glossy floor, settled grain, frames 120 to 359, median | 0.00553 | 0.00553 |
| rect-lamps glossy floor box, flicker, frames 120 to 359 | 0.1369 | 0.1369 |
| Camera cut onto the nose, specks d0 to d29, median | 11,674 | 4,322 |
| Camera cut onto the nose, specks d30 to d64, median | 10,259 | 981 |
| Slow pan, specks of the moving frames, median | 3,822 | 162 |
| Walk, specks of the moving frames, median | 1,073 | 7 |

Stage 1's probe gave the same numbers within a few percent:

- The settled nose had 146 specks.
- The cut onto the nose had 19,910 specks at d0 and 3,438 at d2. Production had 19,910 and 3,425.
- The pan and the walk had medians of 149 and 7.

The brighter nose box is not a bias. Stage 1 showed that main's clamp loses the light of the rare bright samples, and that the mixture keeps their mean (`../../reflection-mixture/README.md`). The probe gave the same 151.28.

### The camera cut onto rect-lamps' glossy floor

The floor's share starts at the ceiling after the cut and falls to 0 within about 30 draws. Until then the floor carries the documented transient grain, which the user accepted:

| Draw after the cut | d0 | d2 | d5 | d10 | d15 | d25 | d29 | d45 | d64 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Floor grain, branch / main | 1.59 | 1.26 | 1.31 | 1.26 | 1.21 | 1.14 | 1.11 | 1.02 | 1.00 |

Flicker of the floor box: main 0.435 and branch 0.509 over d0 to d14, main 0.239 and branch 0.252 over d15 to d29, and main 0.185 and branch 0.187 over d30 to d64. Stage 1's reset-on probe gave +59 % grain at d0 and +17 % flicker over d0 to d14, so production matches it.

### Determinism

Two runs of the walk on the same binary gave the same 240 frames (ffmpeg `framemd5`). The takes ran at a fixed render scale, where no render size changes.

## GPU time

Under one exclusive GPU lock, with a 1-minute load under 8 before each run, 300 frames, two frames in flight and pass timestamps off. Each view ran two rounds of main, branch, branch, main, and the second round started with the branch. Every run held the GPU clock at 1296 MHz.

| View | Main median (ms) | Branch median (ms) | Change |
|---|---:|---:|---:|
| Sunlit nose (`orbit`, person, tick 277003, -1.7507 stops) | 14.809 | 15.626 | +0.817 ms (+5.52 %) |
| Bay (`demo-surface`, bay-camera, -4.089 stops) | 14.798 | 15.742 | +0.944 ms (+6.38 %) |
| rect-lamps (`orbit`, person, tick 0, 3.83 stops) | 14.821 | 15.494 | +0.673 ms (+4.54 %) |

A separate set of runs on the nose compared the branch before the review fixes with stage 1's probe (`tools/probe_compare.sh`), all at 1296 MHz:

| Build (nose) | Round 0 median (ms) | Round 1 median (ms) |
|---|---:|---:|
| Unpatched main | 14.835 | 14.809 |
| Stage 1 probe, probe off | 15.455 | 15.490 |
| Stage 1 probe at Config B | 15.906 | 15.967 |
| Production branch | 15.575 | 15.638 |

So production costs 0.33 ms less than the probe. Stage 1 reported +0.34 to +0.52 ms because it measured against its probe build with the probe off. That build costs 0.65 ms more than main. The feature itself costs 0.67 to 0.94 ms against main, and 0.67 ms of it on rect-lamps, whose floor's share settles at 0. So most of the cost does not depend on the share. The parent orchestrator accepted the cost under the user's delegation of 2026-11-24. Ticket `tf3-g3rs` places it per pass and looks for a cheaper path at a share of 0.

## Pictures

- `pics/nose-film-settled-1x.png`: the nose's ceiling cone at frame 300, 1:1. Main's cone is a cloud of single bright specks. The branch's cone is a smooth glow with fine grain.
- `pics/real-cut-nose-strip-1x.png`: the cut onto the nose at d0, d1, d2, d5, d10, d15 and d29, 1:1. Main flashes bright square blotches across the ceiling through d29. The branch shows the coarse 2 x 2 block grid at d0 and an even cone from d2.
- `pics/real-cut-lamps-strip-1x.png`: the cut onto the glossy floor at d0 to d29, 1:1. The branch's extra grain shows as a slightly stronger dither at d0 and is hard to see after d2.
- Films: `nose-film-main-branch.mp4`, `lamps-film-main-branch.mp4`, `real-cut-nose-main-branch.mp4`, `real-cut-lamps-main-branch.mp4`, `pan-hall-main-branch.mp4` and `walk-hall-main-branch.mp4` (the last two at half size).

## Files

- `tools/make_scripts.py` writes the scripts of the takes into `scripts/`.
- `tools/takes.sh` runs the untimed takes, and `tools/analyze.py` measures them.
- `tools/timing.sh` runs the timing, and `tools/probe_compare.sh` the clocked comparison with the probe.
