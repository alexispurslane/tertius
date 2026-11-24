# Adaptive per-tile guided share for rough reflection rays, stage 1 (ter-fe63)

This record holds the stage 1 probe of Design 1 for ter-fe63: an adaptive per-tile guided share for rough reflection rays. It changes no engine code in `src/`. The prior stage 1 fixed-share record stays untouched under `docs/research/reflection-mixture/`. Stage 2 production and merge remain held pending fixed-cost acceptance against an unpatched `main` binary.

## Terms and method

- **Tile share image**: an `R16G16Sfloat` texture with one texel per `8 x 8` ray-block tile (`80 x 45` texels at the `1280 x 720` render size, `24 x 24` output pixels at `1920 x 1080`). Channel R stores the reflection guided share. Channel G stays empty for a later diffuse pass.
- **Ceiling** (`s_max`): the maximum allowed reflection guided share. Hold 1 to Step 3 test `0.25` and `0.50`. Section 5 tests `0.25` only.
- **Ramp**: the per-pixel roughness cap `clamp((sqrt(alpha) - 0.1) / (0.38 - 0.1), 0, 1)`, where `alpha` is the floored roughness. Smooth surfaces with `sqrt(alpha) <= 0.1` receive a share of `0` and skip guided-density evaluation.
- **Pixel share**: `min(ramp, tileShareBilinear)`, where `tileShareBilinear` reads the tile share image with bilinear filtering at block center `(vec2(pixel / 2) + 0.5) / 8`, clamped to the active tile bounds. When no lit pixel is in view, pixel share is `0`.
- **Guided turn (`guidedTurn`)**: a deterministic low-discrepancy schedule over 4-draw windows. Each block's cell takes a hashed 2x2 Bayer phase inside the 4-draw window and a Cranley-Patterson shifted Van der Corput rank across windows. This breaks any lock with `chosenPixelOf`'s period-4 rotation and avoids `n * ds/dn` phase drift when the share changes.
- **Slope estimator**: in `screen_trace_block_rays.comp`, every rough reflection ray evaluates `q_g` once on a hit whenever `guiding || evalSlope`. Cells with `ramp > 0` evaluate `q_g` even at share `0` so the tile can climb again. Set `s_eval = max(s, ShareGuard)` and `q_eval = (1 - s_eval) * q_l + s_eval * q_g`. Each ray records `slope = w^2 * (q_l - q_g) / q_eval` and `moment = w^2`, where `w` is the mixture-weighted sample luminance capped at `1e12` against float32 overflow. Above `ShareGuard`, `slope` equals the exact derivative `dM/ds` of the second moment. Below `ShareGuard`, it equals the exact secant `(M(s_eval) - M(s)) / (s_eval - s)`.
- **Tile update pass (`screen_trace_share.comp`)**: one thread per tile sums `slope` and `moment` over the tile's `8 x 8` reflection blocks in fixed row-major order. When `momentSum > 0` and finite, it updates `share = clamp(share - ShareStep * clamp(slopeSum / momentSum, -MaxRatio, MaxRatio), 0, s_max)`.
- **Tuned parameters (Config B)**: `ShareStep = 1/128` (`0.0078125`), `ShareGuard = 1/16` (`0.0625`), `MaxRatio = 2.0`. Compared with `ShareStep = 1/64`, `1/128` halved settled per-tile temporal standard deviation on interior flat-slope tiles (`0.0377` to `0.0166` on the bay back wall, `0.0306` to `0.0195` on the bay floor, and `0.0143` to `0.0091` on the nose box).
- **Initialization and camera-cut reset toggle**: on image creation or render-size change, every tile starts at `s_max` before draw 0 traces. With `TERTIUS_PROBE_SHARE_RESET_ON_CUT=1`, any draw where `context.History.Reset == true` (real camera-cut invalidation from `FrameHistory.PendingFrame.Reset`) also sets `probeShareReset = 1` before `screen_trace_block_rays.comp`, `screen_trace_own_rays.comp`, `screen_trace_resolve.comp`, and `screen_trace_share.comp`. Without that toggle (`0`, default), camera cuts inherit the prior view's tile share.
- **Counted specks**: pixels in `[0:500, 480:1880]` where 0 to 255 luma exceeds a 7x7 median filter by more than `12` gray levels (`g - median_filter(g, size=7) > 12`), outside a 13x13 dilation of ceiling lamps (`g > 200`). This detector counts both stark isolated fireflies and fine high-contrast grain during short history windows. A nonzero count does not by itself mean stark firefly flashes.
- **Grain and blob**: the two spatial bands of a frame's residual against each build's 100-capture reference (mean of captures 30 to 99), in units of main's full-picture region mean (`guide/parts.py`). Grain covers 1 to 3 output pixels. Blob covers about 6 to 70 output pixels.

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
| `TERTIUS_PROBE_SHARE_RESET_ON_CUT` | `1` resets tile share to `s_max` before draw 0 whenever `context.History.Reset == true`. `0` keeps the prior share. |
| `TERTIUS_PROBE_SHARE_DUMP=<prefix>` | Writes `<prefix>.shares.meta`, `.shares.f32`, and `.shares.events.tsv` for draws in `TERTIUS_PROBE_SHARE_DUMP_DRAWS`. |

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

| Build | Counted specks median (max) | Specks c0 / c1 / c19 | Mean specular sample | Box mean (20 captures) |
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
- On `nose-box-1x-d1.png` and `bay-d1-right-wall.png`, Design 1 suppresses the stark isolated bright fireflies seen in `main` (counted specks fall from `3,719.5` to `166.5` at `0.25` and `127.0` at `0.50`). Ceiling `0.50` looks slightly cleaner than `0.25` on the upper right wall and inside the nose ceiling cone.
- On `tile-border-check.png`, neither the stretched crops nor the 12x grain residuals show any 24-pixel tile grid across `0 <-> 0.25` or `0 <-> 0.50` borders.
- On `share-settling-plots.png`, region means stay flat after draw 60. The single dashed center tile on the flat-slope bay floor wanders slightly wider at `0.50` (`std 0.0269`) than at `0.25` (`std 0.0195`).

## 2. Step 2: 60 fps films, motion, `mouseMove` flick, and bit-exact determinism

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

Counted specks in `[0:500, 480:1880]` across the moving frames:

| Motion film | Main median (p95 / max) | Design 1 0.25 median (p95 / max) | Design 1 0.50 median (p95 / max) |
|---|---|---|---|
| Slow pan (240 frames) | `3,763 (6,936 / 12,463)` | `149 (432 / 1,184)` | `130 (374 / 754)` |
| Walk forward (180 frames) | `1,063 (1,396 / 6,678)` | `7 (58 / 466)` | `1 (35 / 354)` |

Films: `pics/pan-hall-main-vs-d1-025.mp4`, `pics/pan-hall-d1-025-vs-050.mp4`, `pics/walk-hall-main-vs-d1-025.mp4`.

### Cold startup (`nose-film.json`) and 180-degree `mouseMove` flick (`cut-hall.json`)

1. **Cold startup with initial share equal to ceiling (`nose-film.json`, frames 0 to 119 after 32 initial still warm-up draws)**: counted specks start far below `main`, though nonzero fine grain remains early. Frame 0 counted specks are `13,451` (`main`), `1,776` (`D1 0.25`), and `1,507` (`D1 0.50`). First-30-frame medians are `10,462.5` (`main`), `995.0` (`D1 0.25`), and `683.0` (`D1 0.50`). Settled medians (frames 120 to 359) are `3,500.5` (`main`), `146.0` (`D1 0.25`), and `121.0` (`D1 0.50`). Because `nose-film.json` includes 32 warm-up draws before frame 0, it is a cold startup check, not a camera-cut check.
2. **180-degree `mouseMove` flick (`cut-hall.json`, same `person` camera, `context.History.Reset == false`)**: `cut-hall.json` rotates `person` by `180 deg` through `mouseMove` on the same camera. `RenderSnapshot.CameraCuts` does not increment, and temporal history does not reset. Ceiling-box share starts at `0.000` on frame 61 (share draw 93) and climbs to 50 % / 90 % of its settled value in `9 / 16` draws (`0.27 s`) at `0.25` and `13 / 30` draws (`0.50 s`) at `0.50`. Counted specks at post-flick draws `0 / 5 / 15 / 30 / 60` are `9,771 / 15,583 / 14,473 / 15,582 / 9,917` on `main`, `9,764 / 16,535 / 12,784 / 4,527 / 980` on `D1 0.25`, and `9,764 / 16,536 / 12,784 / 4,420 / 841` on `D1 0.50`. Section 5 tests real `context.History.Reset == true` camera cuts (`DrawsOf == 1`) with `reset-off` vs `reset-on` against unpatched `main`.

Visuals: `pics/cut-settling-plot.png`, `pics/cut-hall-main-vs-d1-025.mp4`.

### Bit-exact determinism

Two independent runs of `walk-hall.json` (`det-walk-r1` and `det-walk-r2`, Design 1 `0.25`, `TERTIUS_PROBE_KEEP=w-000179`, `TERTIUS_PROBE_SHARE_DUMP_DRAWS=0-240`) produce bit-identical output:

- Late-draw output PNG (`w-000179.png`, `2,052,869` bytes): SHA-256 `ab9a92255a48c1e1f1589554a80fed7dd886cb79b7f714e023a071786528a8d1` on both runs.
- Share dump (`d.shares.f32`, 240 draws, `3,456,000` bytes): SHA-256 `68c610000aff7447c71e03180904fcdc9bb726fe3a1ac203d1252eeeb1e15020` on both PNG runs and on the lossless `--video` run (`walk-d1-025`).

### Visual reading of Step 2 films and frames

- On the pan and walk frames, the bright tile-share cone tracks the sunlit ceiling smoothly across tiles as the camera turns or moves. The floor stays at `0`. No tile seams, block edges, or breathing appear.

## 3. Step 3: exclusive-lock GPU frame time

All timing runs executed under one exclusive `tools/gpu-lock.sh` hold (`waited: 55s`, total hold `4 min 52 s`). Each run rendered 300 frames at `1920 x 1080`, render scale `2/3` (`1280 x 720`), `--in-flight 2`, `--video none`, with `probeCensus` off and share dump off. Every run locked at `1296.0 MHz` (`p10 = 1296.0`, `p90 = 1296.0 MHz`). 1-minute load stayed between `2.41` and `5.39` (well below `8.0`). Full raw rows are in `tables/step3-timing.json`.

**Baseline and acceptance status (commit `94e808582`)**: the `main` rows below ran the compiled probe binary (`/private/tmp/t1-wl7c/probe-head`) with `TERTIUS_PROBE_ADAPTIVE_SHARE` off (`ScreenTraceSharePass` omitted, `HitLight` 2 layers, `probeCensus` specialized to `false`). Uniform probe branches still exist in that shader. **Fixed-cost acceptance remains OPEN.** Stage 2 must time a clean engine build against an unpatched `main` binary before any shipping decision.

### Counterbalanced `--pass-timestamps off` frame spans (two rounds per scene, averaged)

Round 0 order: `main`, `D1 0.25`, `D1 0.50`. Round 1 order: `D1 0.50`, `D1 0.25`, `main`.

| Scene | Build | r0 mean (ms) | r1 mean (ms) | Pooled mean (ms) | Delta vs probe-off main | Clock (MHz) | 1-min load (r0 / r1) |
|---|---|---|---|---|---|---|---|
| Sunlit nose | Main (probe off) | `15.482` | `15.505` | `15.493` | | `1296.0` | `5.14 -> 5.39` / `4.51 -> 4.30` |
| Sunlit nose | Design 1 (0.25) | `15.965` | `15.951` | `15.958` | `+0.465 ms (+3.00 %)` | `1296.0` | `5.39 -> 4.79` / `4.39 -> 4.51` |
| Sunlit nose | Design 1 (0.50) | `15.989` | `15.996` | `15.992` | `+0.499 ms (+3.22 %)` | `1296.0` | `4.79 -> 4.31` / `4.31 -> 4.39` |
| `rect-bay` | Main (probe off) | `15.554` | `15.549` | `15.552` | | `1296.0` | `3.32 -> 3.37` / `3.44 -> 3.17` |
| `rect-bay` | Design 1 (0.25) | `16.078` | `16.070` | `16.074` | `+0.522 ms (+3.36 %)` | `1296.0` | `3.37 -> 3.34` / `3.39 -> 3.44` |
| `rect-bay` | Design 1 (0.50) | `16.118` | `16.128` | `16.123` | `+0.572 ms (+3.68 %)` | `1296.0` | `3.34 -> 3.47` / `3.47 -> 3.39` |
| `rect-lamps` (floor share 0) | Main (probe off) | `15.495` | `15.489` | `15.492` | | `1296.0` | `2.59 -> 3.02` / `2.41 -> 3.99` |
| `rect-lamps` (floor share 0) | Design 1 (0.25) | `15.827` | `15.828` | `15.827` | `+0.336 ms (+2.17 %)` | `1296.0` | `3.02 -> 2.86` / `2.54 -> 2.41` |
| `rect-lamps` (floor share 0) | Design 1 (0.50) | `15.848` | `15.862` | `15.855` | `+0.363 ms (+2.34 %)` | `1296.0` | `2.86 -> 2.50` / `2.50 -> 2.54` |

### Round 2 with pass timestamps on (`--gpu-run "ray plan..accumulate indirect diffuse"`)

| Scene | Main frame / range (ms) | D1 0.25 frame / range (ms) | D1 0.50 frame / range (ms) | 1-min load (main / 0.25 / 0.50) |
|---|---|---|---|---|
| Sunlit nose | `15.865 / 5.664` | `16.322 / 6.130` (`+0.466` range) | `16.368 / 6.162` (`+0.498` range) | `4.30` / `4.30 -> 3.89` / `3.89 -> 3.32` |
| `rect-bay` | `15.833 / 5.787` | `16.405 / 8.436` (see note) | `16.462 / 8.489` (see note) | `3.17 -> 3.14` / `3.14 -> 2.97` / `2.97 -> 2.59` |
| `rect-lamps` | `15.860 / 5.953` | `16.158 / 5.979` (`+0.027` range) | `16.183 / 6.010` (`+0.058` range) | `3.99 -> 3.83` / `3.83 -> 3.68` / `3.68 -> 3.42` |

Note on `rect-bay` Round 2 range: MoltenVK latches pass timestamps late. In `main`, `accumulate indirect diffuse` reports `0.648 ms` and spills about `2.1 ms` into `filter indirect diffuse` (`3.730 ms`). The `guided share` pass shifts that latch boundary so `accumulate indirect diffuse` reports `2.744 ms` and `filter indirect diffuse` drops to `0.015 ms`. The full span from `ray plan` through `accumulate indirect specular` totals `9.568 ms` (`main`) against `10.092 ms` (`D1 0.25`, delta `+0.524 ms`). That agrees with the `--pass-timestamps off` full frame delta (`+0.522 ms`).

### Cost reading

- On `rect-lamps`, `+0.336 ms` (`0.25`) to `+0.363 ms` (`0.50`) is the whole-scene frame delta on a view whose floor has zero share, not an isolated per-floor cost. That whole-scene delta covers the tile-share reads, the share-0 `guidedDensityOf` slope checks on rough hits across the view, and `screen_trace_share.comp`.
- On `sunlit-nose` and `rect-bay`, active guided rays add another `+0.13` to `+0.21 ms`. Total whole-scene delta is `+0.465` to `+0.522 ms` at ceiling `0.25` and `+0.499` to `+0.572 ms` at ceiling `0.50`.
- A ceiling of `0.50` adds `+0.027` to `+0.050 ms` over `0.25` across all three scenes.

## 4. Ceiling comparison: `0.25` vs `0.50`

Both ceilings avoid grain or blob loss on settled `rect-lamps` floor and `bay` sunlit patch. They match `mirror-plate` byte-for-byte, show zero tile-border seams, zero clamp fighting, zero temporal beats, and bit-exact determinism.

| Aspect | Ceiling `0.25` | Ceiling `0.50` |
|---|---|---|
| `bay` sunlit patch & `rect-lamps` floor grain | Matches `main` (`0.0095`, `0.00546`) | Matches `main` (`0.0096`, `0.00545`) |
| `bay` right wall grain / blob | `0.0303 / 0.0173` (`-8.5 %` / `-18.4 %`) | `0.0291 / 0.0166` (`-12.1 %` / `-21.7 %`) |
| `sunlit-nose` settled specks / film temporal std | `146` specks / `2.100` std | `121` specks / `1.973` std |
| Settling time on share-0 surfaces (`lamps` / `patch`) | `26` / `32` draws | `45` / `58` draws |
| Single-tile late std on flat-slope `bay` floor | `0.0195` mean (`0.0162` med) | `0.0269` mean (`0.0269` med) |
| Frame GPU time delta vs probe-off main | `+0.336` to `+0.522 ms` | `+0.363` to `+0.572 ms` |

Ceiling `0.25` is the recommended Stage 1 choice:

1. Ceiling `0.25` captures `96 %` of the nose speck reduction and `94 %` of the temporal-std reduction. It also settles almost twice as fast on share-0 surfaces (`26` vs `45` draws on `rect-lamps`, `32` vs `58` draws on the bay patch).
2. Ceiling `0.25` keeps flat-slope interior tiles tighter (`0.0195` vs `0.0269` mean temporal std on the bay floor) and saves `0.03` to `0.05 ms` of frame GPU time.
3. Ceiling `0.50` stays a clean option if a later stage wants the extra `4 %` grain cut on the bay right wall (`0.0291` vs `0.0303`). Window-2 fast history shows zero clamp fighting at `0.50` (`6.91 %` clamp moves vs `7.44 %` at `0.25` and `9.40 %` on `main`).

## 5. Follow-up: real `context.History.Reset` camera cuts at ceiling `0.25`

Unlike Step 2's `cut-hall.json` (`mouseMove` flick on one camera, where `context.History.Reset` stayed `false`), this follow-up tests **real camera cuts** that increment `RenderSnapshot.CameraCuts` and fire `context.History.Reset == true` with `DrawsOf == 1` (zero warm-up draws at the cut).

All runs compare an **unpatched clean `main` binary** (rebuilt from clean `src/` at `5c01a17b8`, zero warnings, zero probe edits) against the updated probe binary at ceiling `0.25` with `TERTIUS_PROBE_SHARE_RESET_ON_CUT=0` (`reset-off`) and `TERTIUS_PROBE_SHARE_RESET_ON_CUT=1` (`reset-on`). All three builds run the exact same scripts at `1920 x 1080`, render scale `2/3`, 60 fps, with CLI `--exposure`.

Script structure (`tables/real-cut-nose.json`, `tables/real-cut-lamps.json`):

1. Settle `person` 180 degrees aft for 60 frames (`aft-000000..000059`). All ceiling and floor tiles reach `0.000`.
2. Post `mouseMove [-1256.637, 0]` and advance 2 sim ticks (`run: 2`, no render).
3. Render 1 frame on camera `far` (`far1`, `step = 0.050 s < 0.09 s`, `DrawsOf == 1`).
4. Cut back to `person` for 65 post-cut frames (`post-000000..000064` = `d0..d64`, `step = 0.0167 s`, `DrawsOf == 1`).

Event logs (`d.shares.events.tsv`) confirm on film frame 61 (`d0`, draw 93) for both cuts: `historyReset = 1`, `drawsOf = 1`, `drawIndex = 0`, `cameraCuts = 3`. Pre-cut ceiling-box and floor-box shares on `aft-000059` and `far1` equal `0.000000` under `reset-off`. All numbers live in `tables/real-cuts-summary.json`.

### Forward real cut: settled aft view to sunlit nose (`-1.7507` stops)

Counted specks (`g - median_filter(g, size=7) > 12` in 0 to 255 luma outside lamp mask on `[0:500, 480:1880]`) and ceiling-box tile share (`750,0,1170,330`):

| Build | d0 | d1 | d2 | d5 | d10 | d15 | d29 | d0..d29 median (max) | d60 |
|---|---|---|---|---|---|---|---|---|---|
| Unpatched `main` specks | `26,342` | `24,190` | `14,008` | `11,705` | `10,821` | `10,692` | `13,610` | `11,755 (26,342)` | `8,681` |
| D1 `0.25` `reset-off` specks | `26,337` | `24,446` | `13,687` | `11,323` | `11,605` | `9,338` | `3,517` | `9,816 (26,337)` | `787` |
| D1 `0.25` `reset-on` specks | `19,910` | `10,716` | `3,438` | `5,742` | `6,133` | `4,694` | `2,123` | `4,351 (19,910)` | `731` |
| D1 `0.25` `reset-off` box share | `0.0059` | `0.0127` | `0.0212` | `0.0494` | `0.1049` | `0.1591` | `0.1956` | | `0.1945` |
| D1 `0.25` `reset-on` box share | `0.2500` | `0.2494` | `0.2478` | `0.2433` | `0.2341` | `0.2257` | `0.2083` | | `0.1958` |

On `d0`, `reset-on` traced from `0.250` before the draw (and stored `0.2500` after `d0`), while `reset-off` traced from `0.000` (and stored `0.0059` after `d0`). Over `d0..d29`, `reset-on` cuts the median counted specks by `55.7 %` against `reset-off` and `63.0 %` against unpatched `main`. By `d60`, both `reset-off` (`787`) and `reset-on` (`731`) converge together.

Film and strip: `pics/real-cut-nose-main-off-on.mp4` (`1936 x 388`, 60 fps, H.264 CRF 12, `ffprobe` verified), `pics/real-cut-nose-strip-1x.png`, `pics/real-cut-plots.png`.

### Reverse real cut: settled aft view to `rect-lamps` glossy floor (`3.83` stops)

Floor box (`600,620,1320,1000`) residual grain RMS against `daub-lamps-main` reference (in units of main's floor mean) and floor tile share:

| Build | d0 | d1 | d2 | d5 | d10 | d15 | d25 | d29 | d60 |
|---|---|---|---|---|---|---|---|---|---|
| Unpatched `main` grain | `0.01226` | `0.00741` | `0.00556` | `0.00866` | `0.00937` | `0.00860` | `0.00711` | `0.00669` | `0.00580` |
| D1 `0.25` `reset-off` grain | `0.01226` | `0.00741` | `0.00556` | `0.00866` | `0.00937` | `0.00861` | `0.00711` | `0.00669` | `0.00580` |
| D1 `0.25` `reset-on` grain | `0.01951` | `0.01061` | `0.00704` | `0.01132` | `0.01178` | `0.01045` | `0.00809` | `0.00743` | `0.00580` |
| D1 `0.25` `reset-off` share | `0.0000` | `0.0000` | `0.0000` | `0.0000` | `0.0000` | `0.0000` | `0.0000` | `0.0000` | `0.0000` |
| D1 `0.25` `reset-on` share | `0.2398` | `0.2300` | `0.2202` | `0.1916` | `0.1459` | `0.1028` | `0.0220` | `0.0000` | `0.0000` |

Temporal stability on the glossy floor box (`600,620,1320,1000`, 0 to 255 gray):

| Window | Build | Frame-to-frame RMS | Mean temporal std / px | Mean grain | Mean high-pass RMS |
|---|---|---|---|---|---|
| `d0..d14` | Unpatched `main` | `0.4345` | `0.5577` | `0.00870` | `0.00870` |
| `d0..d14` | D1 `0.25` `reset-off` | `0.4345` | `0.5577` | `0.00870` | `0.00871` |
| `d0..d14` | D1 `0.25` `reset-on` | `0.5084` (`+17.0 %`) | `0.6322` (`+13.4 %`) | `0.01137` (`+30.6 %`) | `0.01137` |
| `d15..d29` | Unpatched `main` | `0.2392` | `0.3466` | `0.00755` | `0.00755` |
| `d15..d29` | D1 `0.25` `reset-off` | `0.2392` | `0.3466` | `0.00755` | `0.00755` |
| `d15..d29` | D1 `0.25` `reset-on` | `0.2522` (`+5.4 %`) | `0.3653` (`+5.4 %`) | `0.00878` (`+16.3 %`) | `0.00878` |
| `d0..d29` | Unpatched `main` | `0.3485` | `0.8129` | `0.00812` | `0.00813` |
| `d0..d29` | D1 `0.25` `reset-off` | `0.3485` | `0.8129` | `0.00812` | `0.00813` |
| `d0..d29` | D1 `0.25` `reset-on` | `0.3985` (`+14.3 %`) | `0.8525` (`+4.9 %`) | `0.01007` (`+24.0 %`) | `0.01008` |
| `d30..d64` | Unpatched `main` | `0.1853` | `0.4241` | `0.00605` | `0.00607` |
| `d30..d64` | D1 `0.25` `reset-on` | `0.1871` (`+1.0 %`) | `0.4296` (`+1.3 %`) | `0.00623` (`+3.0 %`) | `0.00625` |

Film and strips: `pics/real-cut-lamps-main-off-on.mp4` (`2176 x 408`, 60 fps, H.264 CRF 12, `ffprobe` verified), `pics/real-cut-lamps-strip-1x.png`, `pics/real-cut-lamps-residual-16x.png`, `pics/real-cut-plots.png`.

### Visual reading and trade-off of `reset-on` vs `reset-off`

- **Forward cut (`pics/real-cut-nose-strip-1x.png`)**: on `d0` (1-spp fresh history), every build shows a coarse 2x2 block-ray grid pattern. That grid accounts for most of the `19,910` counted pixels on `reset-on`. Visually, `unpatched main` and `reset-off` flash stark white square firefly blotches across the dark outer ceiling from `d0` through `d10`. `reset-off` starts to catch up around `d15..d29`. Under `reset-on`, those outer white firefly squares drop sharply from `d0`, and the sunlit cone fills in evenly by `d1..d2`. Fine bright grain stays visible inside the cone across `d5..d29` while temporal history is short (`2,123` counted pixels at `d29`, `731` at `d60`). It does not flash with the coarse blotches of `main` and `reset-off`.
- **Reverse cut (`pics/real-cut-lamps-strip-1x.png`, `pics/real-cut-lamps-residual-16x.png`)**: `reset-off` matches `unpatched main` to 4 to 5 decimals across every draw. Under `reset-on`, `d0` shows a visibly stronger 2x2 dither grid at 1:1 (`+59.1 %` grain RMS, `0.01951` vs `0.01226`). From `d2` through `d29` at normal exposure (`3.83` stops), the added fine grain (`+30.7 %` at `d5`, `+25.7 %` at `d10`, `+21.5 %` at `d15`, `+17.0 %` frame-to-frame RMS over `d0..d14`) is subtle at 1:1. It shows clearly in the 16x residual strip and fades away by `d25..d29` with no mottle or brightness shift.
- **Open trade-offs before Stage 2**: `reset-on` trades a ~`0.4 s` subtle transient fine-grain penalty on cuts onto share-0 glossy floors to remove a ~`0.25 s` stark firefly-blotch burst on cuts onto firefly-prone views. Fixed-cost acceptance remains open until Stage 2 times a clean engine build against an unpatched `main` binary.

## Files

- `probe.patch`: complete probe diff from `5c01a17b8` (`src/`) to `/private/tmp/t1-wl7c/probe-head/src/`.
- `tables/`: `hold1-summary.json`, `step2-summary.json`, `step3-timing.json`, `real-cuts-summary.json`, `parts-bay-d1.json`, `parts-lamps-d1.json`, `fasttrace-d1-025.json`, `fasttrace-d1-050.json`, and all motion/cut scripts (`pan-hall.json`, `walk-hall.json`, `cut-hall.json`, `real-cut-nose.json`, `real-cut-lamps.json`).
- `tools/`: `share_analyze.py`, `hold1_surfaces.sh`, `hold1_finish.sh`, `hold1_analyze.py`, `make_motion_scripts.py`, `step2_batchA.sh`, `step2_batchB.sh`, `step2_analyze.py`, `step3_timing.sh`, `make_real_cut_scripts.py`, `run_real_cuts.sh`, `analyze_real_cuts.py`.
- `pics/`: Hold 1 1:1 crop sheets, share maps, settle plots, share strips, stretched tile-border check, cut-settle plot, and eight Step 1/2 CRF 12 films. Also holds the Section 5 real-cut 1:1 strips (`real-cut-nose-strip-1x.png`, `real-cut-lamps-strip-1x.png`, `real-cut-lamps-residual-16x.png`), plots (`real-cut-plots.png`), and two 60 fps CRF 12 films (`real-cut-nose-main-off-on.mp4`, `real-cut-lamps-main-off-on.mp4`).
