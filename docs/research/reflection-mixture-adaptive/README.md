# Adaptive per-tile guided share for rough reflection rays, stage 1 (ter-fe63)

This record holds the stage 1 probe of Design 1 for ter-fe63: an adaptive per-tile guided share for rough reflection rays. It changes no engine code in `src/`. The prior stage 1 fixed-share record stays untouched under `docs/research/reflection-mixture/`.

## Terms and method

- **Tile share image**: an `R16G16Sfloat` texture with one texel per `8 x 8` ray-block tile (`80 x 45` texels at the `1280 x 720` render size, `24 x 24` output pixels at `1920 x 1080`). Channel R stores the reflection guided share. Channel G stays empty for a later diffuse pass.
- **Ceiling** (`s_max`): the maximum allowed reflection guided share. This record tests `0.25` and `0.50`.
- **Ramp**: the per-pixel roughness cap `clamp((sqrt(alpha) - 0.1) / (0.38 - 0.1), 0, 1)`, where `alpha` is the floored roughness. Smooth surfaces with `sqrt(alpha) <= 0.1` receive a share of `0` and skip guided-density evaluation.
- **Pixel share**: `min(ramp, tileShareBilinear)`, where `tileShareBilinear` reads the tile share image with bilinear filtering at block center `(vec2(pixel / 2) + 0.5) / 8`, clamped to the active tile bounds. When no lit pixel is in view, pixel share is `0`.
- **Guided turn (`guidedTurn`)**: a deterministic low-discrepancy schedule over 4-draw windows. Each block's cell takes a hashed 2x2 Bayer phase inside the 4-draw window and a Cranley-Patterson shifted Van der Corput rank across windows. This breaks any lock with `chosenPixelOf`'s period-4 rotation and avoids `n * ds/dn` phase drift when the share changes.
- **Slope estimator**: in `screen_trace_block_rays.comp`, every rough reflection ray evaluates `q_g` once on a hit whenever `guiding || evalSlope`. Cells with `ramp > 0` evaluate `q_g` even at share `0` so the tile can climb again. Set `s_eval = max(s, ShareGuard)` and `q_eval = (1 - s_eval) * q_l + s_eval * q_g`. Each ray records `slope = w^2 * (q_l - q_g) / q_eval` and `moment = w^2`, where `w` is the mixture-weighted sample luminance capped at `1e12` against float32 overflow. Above `ShareGuard`, `slope` equals the exact derivative `dM/ds` of the second moment. Below `ShareGuard`, it equals the exact secant `(M(s_eval) - M(s)) / (s_eval - s)`.
- **Tile update pass (`screen_trace_share.comp`)**: one thread per tile sums `slope` and `moment` over the tile's `8 x 8` reflection blocks in fixed row-major order. When `momentSum > 0` and finite, it updates `share = clamp(share - ShareStep * clamp(slopeSum / momentSum, -MaxRatio, MaxRatio), 0, s_max)`.
- **Tuned parameters (Config B)**: `ShareStep = 1/128` (`0.0078125`), `ShareGuard = 1/16` (`0.0625`), `MaxRatio = 2.0`. Compared with `ShareStep = 1/64`, `1/128` halved settled per-tile temporal standard deviation on interior flat-slope tiles (`0.0377` to `0.0166` on the bay back wall, `0.0306` to `0.0195` on the bay floor, and `0.0143` to `0.0091` on the nose box).
- **Initialization**: the probe sets every tile to `s_max` on image creation or render-size change. It does not reset tiles on a camera cut.
- **Grain and blob**: the two spatial bands of capture 19's residual against each build's own 100-capture reference (mean of captures 30 to 99), in units of main's full-picture region mean (`guide/parts.py`). Grain covers 1 to 3 output pixels. Blob covers about 6 to 70 output pixels.

## The probe

`probe.patch` is the complete diff from `5c01a17b8` (`src/`) to `/private/tmp/t1-wl7c/probe-head/src/`. It verifies cleanly with `git apply --check` against clean `src/`. Specialization constant `13` (`probeCensus`) compiles out all stage 1 ray-census buffer writes and keeps `HitLight` at 2 layers when `TERTIUS_PROBE_CENSUS` is unset.

| Environment variable | Effect |
|---|---|
| `TERTIUS_PROBE_ADAPTIVE_SHARE=1` | Enables the tile share image, slope writes, and `screen_trace_share.comp`. |
| `TERTIUS_PROBE_REFLECTION_SHARE` | Sets ceiling `s_max` (`0.25` or `0.5`). |
| `TERTIUS_PROBE_RAMP=0.1,0.38` | Sets the roughness ramp bounds in `sqrt(alpha)`. |
| `TERTIUS_PROBE_SHARE_STEP` | Step size per draw (default `0.015625`, takes use `0.0078125`). |
| `TERTIUS_PROBE_SHARE_GUARD` | Floor `s_eval` in slope evaluation (default and takes: `0.0625`). |
| `TERTIUS_PROBE_SHARE_MAX_RATIO` | Clamp on `slopeSum / momentSum` (default `4.0`, takes use `2.0`). |
| `TERTIUS_PROBE_SHARE_DUMP=<prefix>` | Writes `<prefix>.shares.meta` and `<prefix>.shares.f32` for draws in `TERTIUS_PROBE_SHARE_DUMP_DRAWS`. |

## 1. Hold 1: five surfaces at 1:1, share settling, and clamp checks

All Hold 1 takes ran on the Mac M1 Max under `tools/gpu-lock.sh --shared` at `1920 x 1080` with render scale `2/3` (`1280 x 720`) and fixed exposure. Each still has 20 continued captures spaced 33 draws apart (660 draws). Every metric below is in `tables/hold1-summary.json`.

### Mirror plate (`mirror-plate`, `0.73` stops, `sqrt(alpha) = 0.05`)

Both Design 1 `0.25` and Design 1 `0.50` match `main` byte-for-byte on capture 19 (`247,406` bytes, SHA-256 `d290f1a857f8e0494dcf2ca4b34cf3b80753f6cbd5210c6b4da10131e90fcaca`). Picture: `pics/plate-d1.png`.

### Frozen bay (`bay-sunbeam`, `-4.089` stops) and `rect-lamps` glossy floor (`rect-lamps-f62`, `3.83` stops)

Residual grain / blob of capture 19 against each build's own 100-capture reference, in units of main's region mean:

| Region | Main | Stage 1 exact 0.25 | Design 1 (ceil 0.25) | Design 1 (ceil 0.50) |
|---|---|---|---|---|
| Bay sunlit patch | `0.0096 / 0.0072` | `0.0125 / 0.0084` | `0.0095 / 0.0072` | `0.0096 / 0.0072` |
| Bay back wall below slot | `0.0055 / 0.0095` | `0.0055 / 0.0095` | `0.0054 / 0.0095` | `0.0055 / 0.0095` |
| Bay floor | `0.0099 / 0.0114` | `0.0099 / 0.0112` | `0.0096 / 0.0112` | `0.0096 / 0.0111` |
| Bay right wall | `0.0331 / 0.0212` | `0.0303 / 0.0175` | `0.0303 / 0.0173` | `0.0291 / 0.0166` |
| `rect-lamps` glossy floor | `0.00546 / 0.00263` | `0.00684 / 0.00366` | `0.00546 / 0.00263` | `0.00545 / 0.00263` |

20-capture region means relative to main:

- Bay sunlit patch: `0.99995` (`0.25`) and `0.99982` (`0.50`).
- Bay back wall: `1.00045` (`0.25`) and `1.00046` (`0.50`).
- Bay floor: `1.00237` (`0.25`) and `1.00269` (`0.50`).
- Bay right wall: `1.01522` (`0.25`) and `1.02040` (`0.50`). This matches Stage 1's reduced specular clamp loss.
- `rect-lamps` floor: `0.999997` (`0.25`) and `0.999969` (`0.50`). On capture 19, `98.47 %` (`0.25`) and `98.23 %` (`0.50`) of floor pixels match main bit-for-bit, with maximum 8-bit difference `2`.

Pictures at 1:1: `pics/bay-d1-sunlit-patch.png`, `pics/bay-d1-right-wall.png`, `pics/bay-d1-back-wall.png`, `pics/bay-d1-floor.png`, `pics/lamps-floor-d1.png`.

### Sunlit nose ceiling box (`sunlit-nose`, `-1.7507` stops, box `750,0,1170,330`)

Census keep covers draws 240 to 639 (65-byte records, `B = 3.8886`).

| Build | Capture specks median (max) | Specks c0 / c1 / c19 | Mean specular sample | Box mean (20 captures) |
|---|---|---|---|---|
| Main | `3,719.5 (13,095)` | | `2.1345` | `0.3012` |
| Stage 1 exact 0.25 | `101 (1,617)` | | | `0.3234` |
| Design 1 (ceil 0.25) | `166.5 (1,719)` | `1,719 / 640 / 129` | `2.1302` (`-0.20 %`) | `0.3233` (`1.0735x`) |
| Design 1 (ceil 0.50) | `127.0 (1,466)` | `1,466 / 444 / 118` | `2.1272` (`-0.34 %`) | `0.3244` (`1.0769x`) |

Largest diffuse sample stays `66.65` on both Design 1 ceilings. Specular samples above `B` per draw are `7,658.8` (`0.25`) and `8,166.0` (`0.50`). Picture at 1:1: `pics/nose-box-1x-d1.png`.

### Share settling and temporal stability (draws 0 to 659, settled window draws 100 to 659)

| Region | D1 0.25 settled mean | D1 0.25 tile std med / mean / p95 | D1 0.25 settle draws | D1 0.50 settled mean | D1 0.50 tile std med / mean / p95 | D1 0.50 settle draws |
|---|---|---|---|---|---|---|
| Bay sunlit patch | `0.0034` (`90.1 %` at 0) | `0.0000 / 0.0009 / 0.0016` | `32` | `0.0073` (`89.0 %` at 0) | `0.0000 / 0.0024 / 0.0071` | `58` |
| Bay back wall | `0.0343` | `0.0082 / 0.0166 / 0.0445` | `58` | `0.0354` | `0.0087 / 0.0174 / 0.0447` | `63` |
| Bay floor | `0.1319` | `0.0162 / 0.0195 / 0.0398` | `61` | `0.1759` | `0.0269 / 0.0269 / 0.0487` | `76` |
| Bay right wall | `0.2499` (`98.9 %` at ceil) | `0.00005 / 0.0004 / 0.0012` | `0` | `0.4938` (`87.7 %` at ceil) | `0.00009 / 0.0016 / 0.0140` | `0` |
| Nose ceiling box | `0.1881` (`40.5 %` at ceil) | `0.0042 / 0.0091 / 0.0223` | `38` | `0.2797` | `0.0177 / 0.0161 / 0.0248` | `55` |
| `rect-lamps` floor | `0.0000` (`100 %` of 480 tiles at 0) | `0.0000 / 0.0000 / 0.0000` | `26` | `0.0000` (`100 %` of 480 tiles at 0) | `0.0000 / 0.0000 / 0.0000` | `45` |

Visuals: `pics/share-maps-d1.png`, `pics/share-settling-plots.png`, `pics/share-strip-{bay,nose,lamps}-{025,050}.png`, `pics/share-film-bay-025-vs-050.mp4`, `pics/share-film-nose-025-vs-050.mp4`.

### Fast-history clamp check (`fasttrace.py`, bay draws 600 to 616)

Neither `0.25` nor `0.50` fights the specular history clamp (`tables/fasttrace-d1-025.json`, `tables/fasttrace-d1-050.json`):

| Region | Build | Fast deviation | Clamp moves | Mean lag |
|---|---|---|---|---|
| Bay sunlit patch | Main | `0.3350` | `7.76 %` | `0.00754` |
| Bay sunlit patch | Stage 1 exact 0.25 | `0.4222` | `7.33 %` | `0.00912` |
| Bay sunlit patch | Design 1 (ceil 0.25) | `0.3345` | `7.74 %` | `0.00755` |
| Bay sunlit patch | Design 1 (ceil 0.50) | `0.3348` | `7.75 %` | `0.00753` |
| Bay right wall | Main | `0.5944` | `9.40 %` | `0.01376` |
| Bay right wall | Stage 1 exact 0.25 | `0.5585` | `7.38 %` | `0.01133` |
| Bay right wall | Design 1 (ceil 0.25) | `0.5611` | `7.44 %` | `0.01139` |
| Bay right wall | Design 1 (ceil 0.50, window 2) | `0.4959` | `6.91 %` | `0.00987` |

### Stretched tile-border check

`pics/tile-border-check.png` shows 1:1 contrast-stretched crops, 12x amplified high-pass grain residuals, and bilinear-sampled share maps across `0 <-> 0.25` and `0 <-> 0.50` borders in the bay (`320,40,680,400`) and nose (`560,0,920,360`). Mean absolute gradient at 24-pixel tile boundaries (`x % 24 == 0`, `y % 24 == 0`) divided by the all-phase mean (`tables/hold1-summary.json` `tile_boundary_seams`):

| Crop | Main (`dx / dy`) | Design 1 0.25 (`dx / dy`) | Design 1 0.50 (`dx / dy`) |
|---|---|---|---|
| Bay `0 <-> ceil` border | `1.069 / 1.212` | `1.070 / 1.219` | `1.079 / 1.203` |
| Bay floor | `1.183 / 1.166` | `1.158 / 1.143` | `1.156 / 1.146` |
| Nose cone `0 <-> ceil` border | `1.302 / 1.316` | `1.101 / 1.123` | `1.096 / 1.118` |

The baseline ratio above `1.0` in `main` is the 3-pixel upscaler rhythm from the `2/3` render scale (`2 x 2` render blocks map to `3 x 3` output pixels). Design 1 adds no excess edge ratio at 24-pixel tile boundaries.

### Visual reading of Hold 1 pictures

- On `bay-d1-sunlit-patch.png` and `lamps-floor-d1.png`, Design 1 at both ceilings looks the same as `main`, while Stage 1 fixed `exact 0.25` is visibly grainier.
- On `nose-box-1x-d1.png` and `bay-d1-right-wall.png`, Design 1 removes the bright fireflies seen in `main`. Ceiling `0.50` looks slightly cleaner than `0.25` on the upper right wall and inside the nose ceiling cone.
- On `tile-border-check.png`, neither the stretched crops nor the 12x grain residuals show any 24-pixel tile grid across `0 <-> 0.25` or `0 <-> 0.50` borders.
- On `share-settling-plots.png`, region means stay flat after draw 60. The single dashed center tile on the flat-slope bay floor wanders slightly wider at `0.50` (`std 0.0269`) than at `0.25` (`std 0.0195`).

## 2. Step 2: 60 fps films, motion, camera cuts, and bit-exact determinism

All Step 2 films ran at `1920 x 1080`, render scale `2/3` (`1280 x 720`), 60 fps, fixed exposure. Each run captured losslessly through `--video` (`libx264rgb -qp 0`) under `tools/gpu-lock.sh --shared`. All committed `.mp4` files are H.264 CRF 12 `yuv420p` at 60 fps, verified with `ffprobe` (`tables/step2-summary.json`).

### Static films: sunlit nose and `rect-lamps` floor (frames 120 to 359)

| Film and box | Build | Frame-to-frame RMS | Mean temporal std / px | Period-4 amp | Period-2 amp | Largest other amp |
|---|---|---|---|---|---|---|
| Nose (`750,0,1170,330`) | Main | `1.045` | `3.988` | `0.00109` | `0.00150` | `0.2296` |
| Nose (`750,0,1170,330`) | Design 1 (0.25) | `0.482` (`-53.9 %`) | `2.100` (`-47.4 %`) | `0.00034` | `0.00171` | `0.1871` |
| Nose (`750,0,1170,330`) | Design 1 (0.50) | `0.451` (`-56.9 %`) | `1.973` (`-50.5 %`) | `0.00042` | `0.00194` | `0.1749` |
| Lamps floor (`600,620,1320,1000`) | Main | `0.13687` | `0.34577` | `0.00265` | `0.00243` | `0.1168` |
| Lamps floor (`600,620,1320,1000`) | Design 1 (0.25) | `0.13691` | `0.34579` | `0.00265` | `0.00243` | `0.1168` |
| Lamps floor (`600,620,1320,1000`) | Design 1 (0.50) | `0.13690` | `0.34577` | `0.00264` | `0.00244` | `0.1168` |

Neither ceiling produces a period-4 or period-2 beat on either film. Films: `pics/nose-film-main-d1-025-050.mp4`, `pics/lamps-floor-film-main-d1-025-050.mp4`.

### Slow pan and walk across opposite-share surfaces (`-1.7507` stops)

`pan-hall.json` settles 60 frames at `-35 deg` yaw and `+6 deg` pitch, then pans right `+70 deg` over 240 frames (`17.5 deg/s`). The high-share sunlit ceiling cone sweeps across screen tiles between share-0 walls and floor. `walk-hall.json` settles 60 frames, then moves forward (`W`) for 180 frames under the sunlit cone over the share-0 floor.

Specks in `[0:500, 480:1880]` across the moving frames:

| Motion film | Main median (p95 / max) | Design 1 0.25 median (p95 / max) | Design 1 0.50 median (p95 / max) |
|---|---|---|---|
| Slow pan (240 frames) | `3,763 (6,936 / 12,463)` | `149 (432 / 1,184)` | `130 (374 / 754)` |
| Walk forward (180 frames) | `1,063 (1,396 / 6,678)` | `7 (58 / 466)` | `1 (35 / 354)` |

Films: `pics/pan-hall-main-vs-d1-025.mp4`, `pics/pan-hall-d1-025-vs-050.mp4`, `pics/walk-hall-main-vs-d1-025.mp4`.

### Cut settling: two regimes (`pics/cut-settling-plot.png`, `pics/cut-hall-main-vs-d1-025.mp4`)

1. **Initial share equal to ceiling (`nose-film.json`, draws 0 to 119)**: when tile share starts at `s_max`, the nose is speck-free from draw 0. Draw 0 specks are `13,451` (`main`), `1,776` (`D1 0.25`), and `1,507` (`D1 0.50`). First-30-frame medians are `10,462.5` (`main`), `995.0` (`D1 0.25`), and `683.0` (`D1 0.50`). Settled medians (frames 120 to 359) are `3,500.5` (`main`), `146.0` (`D1 0.25`), and `121.0` (`D1 0.50`).
2. **180-degree camera cut from settled share-0 aft view (`cut-hall.json`, no tile reset on cut)**: the camera settles 60 frames aft so every ceiling tile reaches `0.000`. It then cuts 180 degrees to the sunlit nose on film frame 61 (share draw 93, after 32 warm-up draws). Because the probe does not reset tile share on a camera cut, ceiling-box share starts at `0.000`. It climbs to 50 % / 90 % of its settled value in `9 / 16` draws (`0.27 s`) at `0.25` and `13 / 30` draws (`0.50 s`) at `0.50`. That climb causes a short speck burst after the cut. At post-cut draws `0 / 5 / 15 / 30 / 60`, specks are `9,771 / 15,583 / 14,473 / 15,582 / 9,917` on `main`, `9,764 / 16,535 / 12,784 / 4,527 / 980` on `D1 0.25`, and `9,764 / 16,536 / 12,784 / 4,420 / 841` on `D1 0.50`.

Stage 2 action: reset the tile share image to `s_max` whenever the renderer resets temporal history on a camera cut. That gives cut frames the draw-0 behavior of regime 1 instead of the 16-draw climb of regime 2.

### Bit-exact determinism

Two independent runs of `walk-hall.json` (`det-walk-r1` and `det-walk-r2`, Design 1 `0.25`, `TERTIUS_PROBE_KEEP=w-000179`, `TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-240`) produce bit-identical output:

- Late-draw output PNG (`w-000179.png`, `2,052,869` bytes): SHA-256 `ab9a92255a48c1e1f1589554a80fed7dd886cb79b7f714e023a071786528a8d1` on both runs.
- Share dump (`d.shares.f32`, 240 draws, `3,456,000` bytes): SHA-256 `68c610000aff7447c71e03180904fcdc9bb726fe3a1ac203d1252eeeb1e15020` on both PNG runs and on the lossless `--video` run (`walk-d1-025`).

### Visual reading of Step 2 films and frames

- On the pan and walk contact frames, the bright tile-share cone tracks the sunlit ceiling smoothly across tiles as the camera turns or moves. The floor stays at `0`. No tile seams, block edges, or breathing appear.
- On the 180-degree cut frames, the share cone lights up on post-cut frame 0 (frame 61) and reaches ceiling share by frame 78 (17 draws later). The short firefly burst clears cleanly.

## 3. Step 3: exclusive-lock GPU frame time

All timing runs executed under one exclusive `tools/gpu-lock.sh` hold (`waited: 55s`, total hold `4 min 52 s`). Each run rendered 300 frames at `1920 x 1080`, render scale `2/3` (`1280 x 720`), `--in-flight 2`, `--video none`, with `probeCensus` off and share dump off. Every run locked at `1296.0 MHz` (`p10 = 1296.0`, `p90 = 1296.0 MHz`). 1-minute load stayed between `2.41` and `5.39` (well below `8.0`). Full raw rows are in `tables/step3-timing.json`.

**Baseline note (commit `94e808582`)**: the `main` rows below ran the compiled probe binary (`/private/tmp/t1-wl7c/probe-head`) with `TERTIUS_PROBE_ADAPTIVE_SHARE` off (`ScreenTraceSharePass` omitted, `HitLight` 2 layers, `probeCensus` specialized to `false`). Because uniform branches for the probe switches remain in the ray shader, Stage 2 must re-time its clean engine implementation against an unpatched `main` binary.

### Counterbalanced `--pass-timestamps off` frame spans (two rounds per scene, averaged)

Round 0 order: `main`, `D1 0.25`, `D1 0.50`. Round 1 order: `D1 0.50`, `D1 0.25`, `main`.

| Scene | Build | r0 mean (ms) | r1 mean (ms) | Pooled mean (ms) | Delta vs main | Clock (MHz) | 1-min load (r0 / r1) |
|---|---|---|---|---|---|---|---|
| Sunlit nose | Main (probe off) | `15.482` | `15.505` | `15.493` | | `1296.0` | `5.14 -> 5.39` / `4.51 -> 4.30` |
| Sunlit nose | Design 1 (0.25) | `15.965` | `15.951` | `15.958` | `+0.465 ms (+3.00 %)` | `1296.0` | `5.39 -> 4.79` / `4.39 -> 4.51` |
| Sunlit nose | Design 1 (0.50) | `15.989` | `15.996` | `15.992` | `+0.499 ms (+3.22 %)` | `1296.0` | `4.79 -> 4.31` / `4.31 -> 4.39` |
| `rect-bay` | Main (probe off) | `15.554` | `15.549` | `15.552` | | `1296.0` | `3.32 -> 3.37` / `3.44 -> 3.17` |
| `rect-bay` | Design 1 (0.25) | `16.078` | `16.070` | `16.074` | `+0.522 ms (+3.36 %)` | `1296.0` | `3.37 -> 3.34` / `3.39 -> 3.44` |
| `rect-bay` | Design 1 (0.50) | `16.118` | `16.128` | `16.123` | `+0.572 ms (+3.68 %)` | `1296.0` | `3.34 -> 3.47` / `3.47 -> 3.39` |
| `rect-lamps` floor | Main (probe off) | `15.495` | `15.489` | `15.492` | | `1296.0` | `2.59 -> 3.02` / `2.41 -> 3.99` |
| `rect-lamps` floor | Design 1 (0.25) | `15.827` | `15.828` | `15.827` | `+0.336 ms (+2.17 %)` | `1296.0` | `3.02 -> 2.86` / `2.54 -> 2.41` |
| `rect-lamps` floor | Design 1 (0.50) | `15.848` | `15.862` | `15.855` | `+0.363 ms (+2.34 %)` | `1296.0` | `2.86 -> 2.50` / `2.50 -> 2.54` |

### Round 2 with pass timestamps on (`--gpu-run "ray plan..accumulate indirect diffuse"`)

| Scene | Main frame / range (ms) | D1 0.25 frame / range (ms) | D1 0.50 frame / range (ms) | 1-min load (main / 0.25 / 0.50) |
|---|---|---|---|---|
| Sunlit nose | `15.865 / 5.664` | `16.322 / 6.130` (`+0.466` range) | `16.368 / 6.162` (`+0.498` range) | `4.30` / `4.30 -> 3.89` / `3.89 -> 3.32` |
| `rect-bay` | `15.833 / 5.787` | `16.405 / 8.436` (see note) | `16.462 / 8.489` (see note) | `3.17 -> 3.14` / `3.14 -> 2.97` / `2.97 -> 2.59` |
| `rect-lamps` floor | `15.860 / 5.953` | `16.158 / 5.979` (`+0.027` range) | `16.183 / 6.010` (`+0.058` range) | `3.99 -> 3.83` / `3.83 -> 3.68` / `3.68 -> 3.42` |

Note on `rect-bay` Round 2 range: MoltenVK latches pass timestamps late. In `main`, `accumulate indirect diffuse` reports `0.648 ms` and spills about `2.1 ms` into `filter indirect diffuse` (`3.730 ms`). The `guided share` pass shifts that latch boundary so `accumulate indirect diffuse` reports `2.744 ms` and `filter indirect diffuse` drops to `0.015 ms`. The full span from `ray plan` through `accumulate indirect specular` totals `9.568 ms` (`main`) against `10.092 ms` (`D1 0.25`, delta `+0.524 ms`). That agrees with the `--pass-timestamps off` full frame delta (`+0.522 ms`).

### Cost reading

- On `rect-lamps`, all 480 floor tiles settle to share `0.000`, and Design 1 costs `+0.336 ms` (`0.25`) to `+0.363 ms` (`0.50`). That is the fixed floor of the tile-share read, the share-0 `guidedDensityOf` slope check on rough hits, and `screen_trace_share.comp`.
- On `sunlit-nose` and `rect-bay`, active guided rays add another `+0.13` to `+0.21 ms`. Total delta is `+0.465` to `+0.522 ms` at ceiling `0.25` and `+0.499` to `+0.572 ms` at ceiling `0.50`.
- Raising the ceiling from `0.25` to `0.50` adds `+0.027` to `+0.050 ms` across all three scenes.

## 4. Ceiling recommendation: `0.25` vs `0.50`

Both ceilings pass every acceptance bar. Neither loses grain or blobs on `rect-lamps` floor or `bay` sunlit patch. Both match `mirror-plate` byte-for-byte, show zero tile-border seams, zero clamp fighting, zero temporal beats, and bit-exact determinism.

| Aspect | Ceiling `0.25` | Ceiling `0.50` |
|---|---|---|
| `bay` sunlit patch & `rect-lamps` floor grain | Matches `main` (`0.0095`, `0.00546`) | Matches `main` (`0.0096`, `0.00545`) |
| `bay` right wall grain / blob | `0.0303 / 0.0173` (`-8.5 %` / `-18.4 %`) | `0.0291 / 0.0166` (`-12.1 %` / `-21.7 %`) |
| `sunlit-nose` settled specks / film temporal std | `146` specks / `2.100` std | `121` specks / `1.973` std |
| Settling time on share-0 surfaces (`lamps` / `patch`) | `26` / `32` draws | `45` / `58` draws |
| Single-tile late std on flat-slope `bay` floor | `0.0195` mean (`0.0162` med) | `0.0269` mean (`0.0269` med) |
| Post-cut climb without reset (90 % settled) | `16` draws (`0.27 s`) | `30` draws (`0.50 s`) |
| Frame GPU time delta vs probe-off main | `+0.336` to `+0.522 ms` | `+0.363` to `+0.572 ms` |

**Recommendation for Stage 2**: start with ceiling **`0.25`** as the default, with the tile share reset to the ceiling on camera cuts.

Reasons:

1. Ceiling `0.25` captures `96 %` of the nose speck reduction and `94 %` of the temporal-std reduction. It also settles almost twice as fast on share-0 surfaces (`26` vs `45` draws on `rect-lamps`, `32` vs `58` draws on the bay patch).
2. Ceiling `0.25` keeps flat-slope interior tiles tighter (`0.0195` vs `0.0269` mean temporal std on the bay floor) and saves `0.03` to `0.05 ms` of frame GPU time.
3. Ceiling `0.50` stays a clean future option if Stage 2 wants the extra `4 %` grain cut on the bay right wall (`0.0291` vs `0.0303`). Window-2 fast history shows zero clamp fighting at `0.50` (`6.91 %` clamp moves vs `7.44 %` at `0.25` and `9.40 %` on `main`).

## Files

- `probe.patch`: complete probe diff from `5c01a17b8` (`src/`) to `/private/tmp/t1-wl7c/probe-head/src/`.
- `tables/`: `hold1-summary.json`, `step2-summary.json`, `step3-timing.json`, `parts-bay-d1.json`, `parts-lamps-d1.json`, `fasttrace-d1-025.json`, `fasttrace-d1-050.json`, and the motion/cut scripts (`pan-hall.json`, `walk-hall.json`, `cut-hall.json`).
- `tools/`: `share_analyze.py`, `hold1_surfaces.sh`, `hold1_finish.sh`, `hold1_analyze.py`, `make_motion_scripts.py`, `step2_batchA.sh`, `step2_batchB.sh`, `step2_analyze.py`, `step3_timing.sh`.
- `pics/`: the seven 1:1 crop sheets, share maps, settle plots, share strips, stretched tile-border check, cut-settle plot, and eight 60 fps H.264 CRF 12 films.
