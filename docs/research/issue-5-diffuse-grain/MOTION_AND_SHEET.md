# Continuous motion, multi-phase reference and physical-sheet accumulation, issue #5

Physical-point accumulation of the isolated diffuse-ray-sourced specular term shrinks visible clumps across genuinely fresh draws. It keeps the cockpit panel's cyan and orange wash intact. Per-draw spreading alone turns rare bright hits into moving pools even with an exact physical-area, no-cutoff heat-flow operator. Physical-point accumulation with a gentle reciprocal spread lowers every measured clump band from 1 to 8 output pixels below normal and keeps the windshield boundary sharp.

Every engine edit in this study is a temporary diagnostic patch restored after each run. The committed engine source tree has no diff. This is diagnostic evidence, not a finished production fix. PR #14 stays draft and issue #5 stays open.

## Setup and continuous draw phases

Every capture uses the same cooked Blue Danube cockpit art (`assets.json`), 1280-by-720 output, 848-by-477 internal FSR render size and fixed exposure of 6.0043726 stops. Every run starts from the same simulation state at tick 87,581.

Prior cockpit clips used `"every": 120` (2.0 seconds per frame). That step exceeded `MeteringStepSeconds` (0.09 seconds), triggered the headless still rule, and reset every history to sampling phase 32 on each displayed frame. All moving runs here use `"every": 1` (1/60 second per frame) for 96 displayed frames (ticks 87,582 through 87,677, 1.60 seconds of continuous 60 Hz motion). Frame 0 runs the normal 32-draw warm-up (draw indices 0 through 32). Frames 1 through 95 advance one ordinary draw per frame with zero resets (`sampleDraw` 33 through 127). Each run writes `draws.jsonl` to confirm zero resets after draw 0.

A temporary camera component moves `cockpit-camera` smoothly inside the shuttle's `cabin` frame with no camera cut. For linear progress `s` from 0 at frame 0 to 1 at frame 95:

- Eye position in `cabin` metres moves from `(1.60, 1.15, -9.00)` by `s * (-0.12, -0.02, -0.08)`.
- Look target in `cabin` metres moves from `(0.00, 0.58, -11.55)` by `s * (+0.32, +0.10, 0.00)`.

This sweep shifts the sloped panel and windshield edge across 30 to 55 output pixels over 96 frames. The warm orange area, cool cyan area, windshield boundary and pilot helmet stay inside the frame throughout.

## Multi-phase fixed-pose references and batch convergence

Three fixed poses match exact frames and simulation ticks of the motion path: P0 (`s = 0`, tick 87,582, frame 0), P1 (`s = 48/95`, tick 87,630, frame 48) and P2 (`s = 1`, tick 87,677, frame 95). Each fixed run sets `WarmUpFrames = 159` and records 160 consecutive draw phases (`sampleDraw` 0 through 159) of the exact same snapshot with zero resets after draw 0. Draws 0 through 31 warm incoming bounce histories. Draws 32 through 159 supply 128 genuinely fresh draw phases of the isolated outgoing diffuse-ray-sourced specular RGB (`factors.specular * probeDiffuseSpecular`). Those 128 draws span two full 64-frame blue-noise periods and 32 full 2-by-2 block rotation cycles.

Each pose splits its 128 settled draws into two 64-draw halves in two ways. Early (32 through 95) versus late (96 through 159) checks both Monte Carlo error and warm-up drift. Even versus odd (64 draws each) checks matched-age Monte Carlo variance. All measurements run on the stably observed 2 cm physical cells of the panel sheet.

| Fixed pose | Observed sheet area | Pixel physical area P99 / P01 | Early vs late mean diff | Early vs late 8 cm wash RMS | Early vs late 2 cm cell RMS | Even vs odd 8 cm wash RMS | Even vs odd 2 cm cell RMS |
|---|---:|---:|---:|---:|---:|---:|---:|
| P0 (tick 87,582) | 1.167 m^2 | 68.7x | 0.76% | 11.62% | 81.29% | 8.94% | 82.06% |
| P1 (tick 87,630) | 1.159 m^2 | 69.1x | 1.67% | 10.51% | 73.78% | 8.31% | 77.02% |
| P2 (tick 87,677) | 1.153 m^2 | 69.5x | 1.61% | 8.84% | 72.29% | 8.75% | 71.51% |

Three facts follow from the convergence check:

- Global panel mean RGB converges to within 0.76% to 3.48% across independent 64-draw halves.
- Local 8 cm wash agrees to 8.31% to 11.62% between 64-draw halves, and early-vs-late tracks even-vs-odd closely. Thus the 32-draw warm-up removes almost all bounce-history drift. The 128-draw wash reference separates 24% to 39% per-draw spread errors from 6% to 9% accumulation errors cleanly.
- Single 2 cm cells still differ by 71.51% to 82.06% RMS between independent 64-draw halves. Even 128 independent draws of the reused 2-by-2 diffuse rays do not converge at 2 cm cell resolution.

Across camera poses on their 1.117 m^2 common visible sheet, the 128-draw reference itself shifts with viewing angle. Wash RMS is 10.38% from P0 to P1, 11.11% from P1 to P2, and 18.16% from P0 to P2 (global mean shift +6.91%).

## Actual panel geometry and area-aware no-cutoff heat flow

Rays from `cockpit-camera` against the cooked `lunarshuttle.model/model.glb` (`shuttle` primitive 0, `ArtScale = 1.6`, `ArtPlace = (0, 0, 1)`) hit a connected 3-triangle mesh sheet on the sloped starboard panel. Triangles 1343 and 2548 are coplanar to 0.2 mm (area 3.8404 m^2). Adjacent triangle 641 shares one edge at a 5.2-degree hinge (area 0.6762 m^2). Total physical sheet area is 4.5166 m^2. Because a two-plane hinge has zero Gaussian curvature, unfolding triangle 641 across that shared edge yields an exact isometric 2D sheet coordinate system in metres with zero length or area distortion.

The camera views the panel at a steep grazing angle. As a result, the physical surface area of one render pixel varies by 69x across the visible panel, from about 0.02 cm^2 at top-right to 0.89 cm^2 near the windshield. The earlier equal-screen-cell heat model (`WHOLE_SHEET.md`) did not model that 69x area ratio.

`physical_sheet.py` discretizes the exact 3-triangle polygon at 2 cm spacing (11,541 active cells) with exact clipped polygon areas `areas[i]` in square metres. Each render pixel unprojects through `p_cabin = eye + normalize(viewRay) * gbufferDistance` and matches the 3 triangles within 1 cm of their planes. That test rejects `Cabin_Liner`, `Cabin_Frame` and the windshield sky opening. Area-aware heat flow evolves the isolated source on active observed cells with:

```python
    op = build_heat_operator(active, areas, spacing) if operator is None else operator
    vec = signal[active]
    moved = expm_multiply(diffusion_amount * op, vec)
    out[active] = moved
```

where

- `op` is the sparse finite-volume matrix `M^{-1} L` on active cells. `M` is the diagonal matrix of physical cell areas `areas` in square metres. `L` is the symmetric four-neighbor conductance matrix with insulating zero flux at every inactive boundary.
- `signal` is the outgoing diffuse-ray-sourced specular RGB field on the 2 cm physical grid.
- `active` is the boolean mask of observed connected sheet cells.
- `diffusion_amount` is the spatial diffusion parameter in square metres. An interior impulse on a uniform grid has physical RMS radial width `sqrt(4 * diffusion_amount)` in metres.
- `expm_multiply` applies the exact matrix exponential `exp(diffusion_amount * M^{-1} L)` to `vec` with no dense matrix and no distance cutoff.
- `out` is the diffused RGB field. It keeps `sum(areas * out)` equal to `sum(areas * signal)` to floating-point precision.

## Physical-sheet comparison: accumulation versus per-draw heat flow

On the 96 continuous motion frames (`moving-source`, verified 96/96 PNGs byte-identical to `moving-normal`), we test three groups of controls on the isolated source. The first group applies single-draw area-aware no-cutoff heat flow at 4 cm, 8 cm and 16 cm physical RMS widths. The second group accumulates at the same physical points over 16, 32 and 64 fresh draws with no spatial spread. The third group follows 32- or 64-draw physical accumulation with gentle 4 cm area-aware heat flow. We score every control against the 128-draw fixed references at P1 (frame 48) and P2 (frame 95), and measure physical-sheet fluctuation bands across frames 32 through 95 (1.140 m^2 common observed area).

| Isolated-source physical-sheet control | Max area-weighted RGB drift | Band s=1 (2 cm) | Band s=4 (8 cm) | Band s=8 (16 cm) | vs 128-draw ref P1 cell RMS / wash | vs 128-draw ref P2 cell RMS / wash |
|---|---:|---:|---:|---:|---:|---:|
| raw-per-draw | 0.0e+00 | 0.02460 | 0.00811 | 0.00544 | 339.7% / 33.0% | 438.4% / 39.2% |
| per-draw-heat-4cm | 4.3e-16 | 0.00966 | 0.00740 | 0.00529 | 91.1% / 31.0% | 119.2% / 36.2% |
| per-draw-heat-8cm | 1.4e-15 | 0.00277 | 0.00588 | 0.00492 | 54.7% / 27.6% | 62.6% / 30.4% |
| per-draw-heat-16cm | 3.2e-15 | 0.00054 | 0.00329 | 0.00397 | 47.4% / 24.8% | 47.2% / 24.2% |
| physical-accum-16 | 0.0e+00 | 0.00482 | 0.00384 | 0.00456 | 76.8% / 6.1% | 77.4% / 6.1% |
| physical-accum-32 | 0.0e+00 | 0.00397 | 0.00374 | 0.00443 | 67.4% / 6.8% | 61.3% / 6.3% |
| physical-accum-64 | 0.0e+00 | 0.00386 | 0.00373 | 0.00434 | 67.9% / 7.5% | 56.5% / 9.0% |
| physical-accum32-heat4cm | 2.9e-16 | 0.00191 | 0.00353 | 0.00432 | 39.3% / 6.6% | 37.8% / 6.3% |
| physical-accum64-heat4cm | 3.5e-16 | 0.00191 | 0.00351 | 0.00424 | 39.7% / 7.3% | 38.5% / 8.7% |

Every heat-flow control conserves physical-area-weighted RGB below `3.2e-15`. Per-draw heat flow alone still produces bright round pools at 8 cm and moves local color at 16 cm (24.2% to 36.2% wash error against the 128-draw reference). Physical-point accumulation over 16 to 64 fresh draws drops wash error to 6.1% to 9.0% and preserves the crisp warm-orange versus cool-cyan structure in `evidence/motion-sheet/physical-sheet-P2-comparison.png`. A 32-draw window beats 64 draws on P2 wash error (6.3% versus 9.0%) because the longer window lags the 18.16% view-angle shift from P0 to P2. Gentle 4 cm area-aware heat flow after 32- or 64-draw accumulation cuts cell RMS against the 128-draw reference to 37.8% to 39.7% while keeping wash error at 6.3% to 8.7%.

## Live continuous moving-camera controls

We ran five matched 96-frame (`every = 1`, `sampleDraw` 32 through 127) captures through the full live Vulkan pipeline (screen trace, temporal accumulation, FSR 3 upscale, bloom and tone mapping):

- `moving-normal`: unmodified renderer with the slow camera sweep.
- `moving-connected`: live per-draw connected disk spreading (`connected.glsl`, radius 11 render pixels).
- `moving-source-accum`: temporary RGBA32F ping-pong accumulator in `screen_trace_resolve.comp` (`source_accum.glsl`). It isolates only outgoing diffuse-ray-sourced specular RGB (`handOver > 0`), reprojects it to the same physical surface point through `motionTarget` and `specularPassBits`, and averages up to 64 fresh draws with no narrow-box 1.5-sigma clamp. Diffuse light, dedicated reflection rays and glossy surfaces (`handOver == 0`) stay unchanged.
- `moving-source-accum-spread`: the same isolated 64-draw physical-point accumulator with a gentle radius-5 reciprocal connected spread (`26.1968` full-disk normalizer) applied to the isolated source before accumulation.
- `moving-no-diffuse-ray-specular`: zeroes only the diffuse-ray-sourced specular term. It gives the moving edge mask and the wash-erasure baseline.

The core live update in `source_accum.glsl` is:

```glsl
    uint bits = imageLoad(surfaceTest, pixel).r;
    vec4 motion = imageLoad(motionTarget, pixel);
    vec2 surfacePlace = readPlace(vec2(pixel) + 0.5, motion);
    Reprojection r = reprojectionOf(surfacePlace, specularPassBits(bits));

    vec3 accumOutgoing = rawOutgoing;
    float count = 1.0;
    if (holdsHistory(r))
    {
        vec4 prev = probeReadPrevAccum(r);
        float prevCount = prev.a;
        if (prevCount > 0.0)
        {
            float ratio = historyRatio(exposureStops, previousStops);
            vec3 prevRgb = exposed(prev.rgb, ratio);
            count = min(prevCount + 1.0, PROBE_ACCUM_CAP);
            accumOutgoing = prevRgb + (rawOutgoing - prevRgb) / count;
        }
    }
```

where

- `bits` holds the surface-test pass bits at `pixel`. Its `specularPassBits` bits reject taps across depth, geometric normal or roughness discontinuities such as the windshield boundary.
- `motion` is the G-buffer motion vector at `pixel`.
- `surfacePlace` is the reprojected normalized coordinate of the same physical surface point in the previous draw.
- `r` holds the four bilinear tap indices and normalized weights of `surfacePlace` filtered by `specularPassBits(bits)`.
- `prev` is the previous draw's reprojected RGBA32F record: outgoing diffuse-ray-sourced specular RGB in `rgb` and sample count in `a`.
- `ratio` rescales previous pre-exposed light to this draw's exposure (1.0 at fixed exposure).
- `PROBE_ACCUM_CAP` is 64.0 draws.
- `accumOutgoing` is the updated running mean of outgoing diffuse-ray-sourced specular RGB at that physical point.

### Live display-linear grain, temporal flicker and wash (frames 16 through 95)

| Live moving control | Sloped-panel mean / spatial / temporal | Panel-warm wash error | Panel-cool mean / spatial / temporal | Panel-cool wash error | Helmet mean change |
|---|---:|---:|---:|---:|---:|
| moving-normal | 0.00% / 0.00% / 0.00% | 0.00% | 0.00% / 0.00% / 0.00% | 0.00% | 0.00% |
| moving-connected | +6.40% / -81.58% / -56.17% | 2.70% to 3.96% | +13.00% / -81.52% / -75.84% | 12.58% to 17.20% | +0.00% |
| moving-source-accum | +7.05% / -75.59% / -58.86% | 2.18% to 4.44% | +6.13% / -84.09% / -86.11% | 4.21% to 10.27% | -0.13% |
| moving-source-accum-spread | +7.41% / -84.18% / -59.06% | 2.65% to 5.30% | +9.45% / -92.66% / -86.89% | 6.06% to 13.86% | -0.12% |
| moving-no-diffuse-ray-specular | -23.28% / -86.41% / -65.79% | 9.56% to 16.15% | -41.51% / -95.43% / -90.98% | 42.79% to 45.88% | -0.97% |

### Live multiscale color fluctuation on the moving sloped panel

Values are display-linear RGB RMS (total, with temporal component in parentheses) on edge-free sloped-panel pixels across frames 16 through 95.

| Live moving control | s=1 output px | s=2 output px | s=4 output px | s=8 output px | s=16 output px |
|---|---:|---:|---:|---:|---:|
| moving-normal | 0.00197 (0.00185) | 0.00170 (0.00157) | 0.00106 (0.00097) | 0.00056 (0.00050) | 0.00056 (0.00029) |
| moving-connected | 0.00024 (0.00023) | 0.00035 (0.00033) | 0.00059 (0.00056) | 0.00063 (0.00058) | 0.00068 (0.00041) |
| moving-source-accum | 0.00048 (0.00040) | 0.00067 (0.00052) | 0.00065 (0.00044) | 0.00045 (0.00025) | 0.00063 (0.00018) |
| moving-source-accum-spread | 0.00020 (0.00017) | 0.00037 (0.00028) | 0.00050 (0.00033) | 0.00041 (0.00023) | 0.00062 (0.00019) |
| moving-no-diffuse-ray-specular | 0.00013 (0.00012) | 0.00011 (0.00010) | 0.00011 (0.00008) | 0.00019 (0.00008) | 0.00042 (0.00013) |

### Local warm and cool RGB vectors (frames 16 through 95, display-linear)

| Crop patch | moving-normal | moving-connected | moving-source-accum | moving-source-accum-spread | moving-no-diffuse-ray-specular |
|---|---|---|---|---|---|
| panel-warm r0-c0 | 0.04720 / 0.04245 / 0.03406 | 0.04873 / 0.04334 / 0.03465 | 0.04800 / 0.04331 / 0.03488 | 0.04838 / 0.04347 / 0.03497 | 0.04221 / 0.03849 / 0.03104 |
| panel-warm r0-c1 | 0.02301 / 0.02216 / 0.01824 | 0.02380 / 0.02281 / 0.01877 | 0.02389 / 0.02307 / 0.01906 | 0.02399 / 0.02311 / 0.01908 | 0.01988 / 0.01898 / 0.01549 |
| panel-warm r1-c0 | 0.03528 / 0.03276 / 0.02650 | 0.03684 / 0.03384 / 0.02729 | 0.03708 / 0.03415 / 0.02754 | 0.03755 / 0.03441 / 0.02772 | 0.03061 / 0.02856 / 0.02302 |
| panel-warm r1-c1 | 0.02206 / 0.02164 / 0.01822 | 0.02269 / 0.02251 / 0.01912 | 0.02286 / 0.02223 / 0.01865 | 0.02305 / 0.02243 / 0.01887 | 0.01894 / 0.01807 / 0.01491 |
| panel-cool r0-c0 | 0.03534 / 0.04539 / 0.04368 | 0.03741 / 0.05101 / 0.05025 | 0.03679 / 0.04837 / 0.04688 | 0.03736 / 0.05008 / 0.04893 | 0.02658 / 0.02593 / 0.02148 |
| panel-cool r0-c1 | 0.03588 / 0.04709 / 0.04604 | 0.03883 / 0.05532 / 0.05579 | 0.03778 / 0.05121 / 0.05063 | 0.03842 / 0.05307 / 0.05284 | 0.02631 / 0.02543 / 0.02113 |
| panel-cool r1-c0 | 0.04017 / 0.05360 / 0.05283 | 0.04232 / 0.05992 / 0.06042 | 0.04037 / 0.05390 / 0.05303 | 0.04090 / 0.05542 / 0.05486 | 0.02989 / 0.03000 / 0.02555 |
| panel-cool r1-c1 | 0.04040 / 0.05292 / 0.05157 | 0.04313 / 0.06074 / 0.06090 | 0.04250 / 0.05854 / 0.05816 | 0.04319 / 0.06062 / 0.06069 | 0.03004 / 0.02992 / 0.02517 |

### Visual inspection, windshield boundary and why brightness rises

Visual inspection of `evidence/motion-sheet/moving-live-contact.png`, `evidence/motion-sheet/moving-windshield-zoom.png` and full-frame P2 shots (`moving-normal-095.png`, `moving-source-accum-095.png`, `moving-source-accum-spread-095.png`) shows four results:

1. `moving-normal` retains glittering cyan and orange speckles throughout continuous 60 Hz motion. The default specular accumulator's 4-frame fast history and narrow 1.5-sigma clamp repeatedly clip rare bright diffuse-ray hits and shorten history length.
2. `moving-connected` removes fine speckles but replaces them with broad, visibly drifting cyan blobs across the panel. Its scale-8 and scale-16 bands stay above `moving-normal` (0.00063 and 0.00068 versus 0.00056).
3. `moving-source-accum` shrinks broad blobs below `moving-normal` (`s8 = 0.00045`, temporal `s8 = 0.00025` versus `0.00050`). It preserves both warm orange and cool cyan wash (`panel-cool` wash error 4.21% to 10.27% versus 42.79% to 45.88% when the term drops to zero). Faint small-scale mottling and slight motion-aligned streaks remain because 64 fresh draws of the 2-by-2 reused rays do not converge at single-pixel scale and reproject through bilinear taps.
4. `moving-source-accum-spread` softens that residual fine mottling. It lowers every band from `s=1` through `s=8` below `moving-normal` (`s1 = 0.00020`, `s2 = 0.00037`, `s4 = 0.00050`, `s8 = 0.00041`, temporal `s8 = 0.00023`). It keeps both cyan and orange wash visible and leaves the glossy pilot helmet essentially unchanged (-0.12% mean).

At the windshield boundary, both geometry and light stay clean:

- In `moving-windshield-zoom.png`, the diagonal windshield strut and the corner meeting `Cabin_Liner` (`Cabin_Console`) stay sharp with no cyan halo or bleed across the frame.
- On the true windshield glass opening (`[660, 230, 750, 300]`, sky behind the glass), mean luminance changes by only -0.003% for `moving-connected`, +0.005% for `moving-source-accum`, and +0.008% for `moving-source-accum-spread` (paired RMS at most 0.00029).
- In the earlier `window-interior` crop (`[1120, 360, 1240, 435]`), mesh raycasts show that this box lies on the dark `Cabin_Console` interior side wall, not the glass opening. `moving-source-accum` has zero spatial spreading yet raises that crop's brightness by +22.18%, while `moving-no-diffuse-ray-specular` drops it by -62.05%. Thus the brightness rise on `panel-cool` (+6.13%) and the dark `Cabin_Console` wall (+22.18%) comes from no longer clipping rare bright diffuse-ray hits, not from light leaking off the sloped panel.

## Exact limitations

- Partial visibility of the physical sheet: only 1.15 to 1.17 m^2 of the 4.52 m^2 3-triangle panel is visible from `cockpit-camera`. The rest sits behind `Cabin_Liner` or outside the frustum. The offline physical-sheet heat solve insulates at both the true windshield boundary and the visibility edge so light does not drain into unobserved zero cells.
- Finite reference convergence: 128 independent draws establish a reliable 8 cm wash reference (8.3% to 11.6% split-half agreement) but remain noisy at 2 cm single cells (71.5% to 82.1% split-half RMS).
- View-angle lag under motion: the converged rough specular reflection itself shifts by 18.16% wash RMS across the 96-frame sweep. A 64-frame running accumulation lags view-dependent changes more than a 32-frame window. This study does not test rapid camera turns, light motion or glossy low-roughness lag.
- Live spread mechanism: `moving-source-accum-spread` applies a finite radius-5 screen-space reciprocal connected spread before RGBA32F motion-reprojected accumulation rather than a live GPU physical-sheet no-cutoff heat solver. A true GPU surface-atlas or no-cutoff multi-grid heat pass would need a separate engine design.
- Scope and performance: all runs are Mac headless diagnostics at one cockpit scene and fixed exposure. This report makes no Linux Runpod frame-time claim, no full .NET test suite claim and no production fix claim.

## Verification and reproduction

All 39 research unit tests pass. `test_physical_sheet.py` failed before `physical_sheet.py` existed, then passed. The test suite kills all 35 scripted model and measurement mutations in `check_mutations.py`, with six new mutations for `physical_sheet.py` (area weighting, stopped heat flow, doubled heat amount, reset physical accumulation, ignored batch divergence and distorted hinge unfolding). Line coverage is 31/31 for `measure.py`, 43/43 for `spread_model.py`, 15/15 for `multiscale.py`, 27/28 for `connected_model.py`, 9/9 for `heat_reference.py` and 371/388 for `physical_sheet.py` (missed lines are multiline docstrings, function signatures and two defensive empty-candidate early-continue guards). The restored ordinary Release build of `src/Tertius.Demo` passes with an empty `git diff src/`.

```powershell
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_render.py moving-normal captures/issue5-motion/moving-normal --frames 96
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_render.py moving-source captures/issue5-motion/moving-source --frames 96
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_render.py moving-connected captures/issue5-motion/moving-connected --frames 96
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_render.py moving-source-accum captures/issue5-motion/moving-source-accum --frames 96 --accum-cap 64
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_render.py moving-source-accum-spread captures/issue5-motion/moving-source-accum-spread --frames 96 --accum-cap 64
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_render.py moving-no-diffuse-ray-specular captures/issue5-motion/moving-no-diffuse-ray-specular --frames 96
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_render.py fixed-ref captures/issue5-motion/fixed-ref-P0 --fixed-s 0 --extra-ticks 0 --warmup 159 --min-sample-draw 32
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_render.py fixed-ref captures/issue5-motion/fixed-ref-P1 --fixed-s 0.5052631578947369 --extra-ticks 48 --warmup 159 --min-sample-draw 32
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_render.py fixed-ref captures/issue5-motion/fixed-ref-P2 --fixed-s 1 --extra-ticks 95 --warmup 159 --min-sample-draw 32
nix develop --command python3 docs/research/issue-5-diffuse-grain/moving_analysis.py captures/issue5-motion captures/issue5-motion/analysis --grid-spacing 0.02
nix develop --command python3 -m unittest discover -s docs/research/issue-5-diffuse-grain -p 'test_*.py'
nix develop --command python3 docs/research/issue-5-diffuse-grain/check_mutations.py
```
