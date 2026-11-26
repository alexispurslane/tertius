# Mainstream specular accumulator control and sender-side attribution, issue #5

Two questions follow from the continuous-motion cockpit study (`MOTION_AND_SHEET.md`):

1. Can the existing shared specular accumulator remove rough-receiver grain under motion without a second RGB history buffer or spatial spreading?
2. Where do the rare bright diffuse-ray-sourced specular spikes come from on the sender side? Does the existing light pyramid see them, and can the same one-ray budget distribute their energy at creation?

Every engine edit in this report is a temporary diagnostic patch restored after each run. Production defaults stay unchanged, `git diff src/` is empty, PR #14 stays draft, and issue #5 stays open.

## Setup

Every run reuses the 1:41 continuous-motion cockpit setup from `MOTION_AND_SHEET.md`. Output size is 1280 by 720 pixels, internal FSR render size is 848 by 477 pixels, and exposure stays fixed at 6.0043726 stops. Each run uses `every = 1` (60 Hz) with 32 warm-up draws on frame 0 and 96 moving frames (`sampleDraw` 32 through 127) with zero resets after draw 0. Metrics cover frames 16 through 95.

## Part 1: Existing shared specular accumulator diagnostic

### AccumulationTrace separates reprojection match from Step 4 history shortening

Production already runs `SpecularHistoryMode.PreciseUncapped` (RGBA32F slow history with uncapped length). It already reprojects moving surfaces through `historyPlaceOf` in `accumulate_temporal.comp`. To separate surface-match failure from later history shortening, we ran unmodified production with `TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace` (`moving-control-trace`). We read back `AccumulationTrace(0)` layers 0, 2, 4, 5, 6 and 8 plus G-buffer roughness and distance on `sampleDraw` 32, 48, 64, 80, 96, 112 and 127. All 96 rendered PNGs match `moving-normal` byte for byte.

| Cockpit crop (pooled `sampleDraw` >= 48) | Roughness min / median / max | Pixels >= 0.40 | Surface reprojection holds (`TracedSurfaceHolds`) | Mean bilinear weight sum | Reflected place tried / won | History length before Step 4 (mean / p50 / p90) | History length after Step 4 (mean / p50 / p90) | Step 4 clamp ran | Step 4 shortened length |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `sloped-panel` | 0.478 / 0.490 / 0.510 | 100.0% | 100.0% | 1.000 | 0.0% / 0.0% | 14.0 / 12.9 / 22.9 | 13.0 / 11.9 / 22.0 | 100.0% | 22.7% |
| `panel-warm` | 0.486 / 0.494 / 0.529 | 100.0% | 100.0% | 1.000 | 0.0% / 0.0% | 14.0 / 13.1 / 22.4 | 12.9 / 12.0 / 21.8 | 100.0% | 23.9% |
| `panel-cool` | 0.494 / 0.514 / 0.780 | 100.0% | 100.0% | 0.998 | 0.0% / 0.0% | 13.0 / 11.8 / 22.1 | 11.9 / 10.8 / 21.3 | 100.0% | 27.2% |
| `windshield-edge` | 0.455 / 0.561 / 0.898 | 100.0% | 100.0% | 0.986 | 0.0% / 0.0% | 14.8 / 13.7 / 24.8 | 13.6 / 12.5 / 24.2 | 100.0% | 24.4% |
| `helmet-detail` | 0.369 / 0.569 / 0.957 | 99.9% | 99.9% | 0.895 | 0.1% / 0.0% | 21.7 / 19.4 / 39.0 | 20.6 / 18.3 / 38.6 | 99.6% | 17.1% |
| `glossy-trim` | 0.369 / 0.420 / 0.957 | 61.1% | 99.3% | 0.960 | 38.9% / 12.3% | 22.5 / 20.6 / 39.1 | 21.5 / 19.6 / 38.7 | 99.2% | 14.9% |

The trace answers the reprojection question directly:

1. Surface reprojection does not fail or reset as the panel moves across pixels. On `sloped-panel` and `panel-warm`, `TracedSurfaceHolds` is 100.0% with mean bilinear tap weight sum 1.000. On `panel-cool`, `TracedSurfaceHolds` is 100.0% with mean weight sum 0.998.
2. Step 4 of `accumulate_spatial.comp` stalls the history length. Even under `PreciseUncapped` (`cap = +inf`) after 48 to 127 draws, the 1.5-sigma box (`retainedSigmas = 0`) and `antiLagShare = 0.25` cut length on 22.7% to 27.2% of panel pixels every draw. That repeated cut traps median length at 10.8 to 12.0 draws. Step 4 also clamps `slow.rgb` toward the noisy 4-frame `fastHistory` box on every draw.
3. Roughness on `sloped-panel` spans 0.478 to 0.510. Every pixel lies at or above `reflectedPlaceBelow = 0.40`, and only 4.7% lie at or above `MaxRoughness = 0.50`. Thus 95.3% of `sloped-panel` sits in the 0.40 to 0.50 hand-over band where diffuse rays supply about 90% of specular and dedicated reflection rays supply about 10%. On `panel-cool`, 94.5% of pixels lie at or above 0.50 (100% diffuse-ray-sourced).

### Bounded rough-receiver policy controls in the existing accumulator

Next we tested if the existing shared specular accumulator works without an extra RGB history buffer or spatial spread. We kept `historyPlaceOf` and all glossy behavior (`roughness < reflectedPlaceBelow = 0.40`) intact. We isolated only Step 4's clamp and history-shortening policy on rough specular receivers in `accumulate_spatial.comp`:

```glsl
    bool skipRoughSpecularClamp = parameters.light == SpecularLight
        && parameters.reflectedPlaceBelow > 0.0
        && roughnessAt(pixel) >= parameters.reflectedPlaceBelow;
    if (historyLength > fastCap && !skipRoughSpecularClamp)
    {
        ...
    }
```

where

- `parameters.light` identifies the accumulated signal (`SpecularLight` or `DiffuseLight`).
- `parameters.reflectedPlaceBelow` is `ScreenTrace.HandOverStart` (0.40) for specular and 0 for diffuse.
- `roughnessAt(pixel)` is the pixel's floored G-buffer roughness in `[0.045, 1.0]`.
- `historyLength` is the reprojected, blended sample count of the slow history in draws.
- `fastCap` is the fast history's maximum length (4 draws).
- `skipRoughSpecularClamp` bypasses Step 4's narrow-box clamp, anti-lag cut and light-evidence cap only on rough specular pixels (`roughness >= 0.40`). Glossy specular pixels (`roughness < 0.40`) and all diffuse pixels stay untouched.

We ran two matched variants with `control-trace` active:

- `moving-mainstream-rough-cap64`: skips Step 4 on `roughness >= 0.40` and caps rough specular `capAt(roughness)` at 64.0 draws (matching the prototype's 64-draw window).
- `moving-mainstream-rough-uncapped`: skips Step 4 on `roughness >= 0.40` and keeps production's uncapped length (`cap = +inf`).

`AccumulationTrace` confirms full history growth on the moving panel with zero Step 4 cuts. `sloped-panel` median length rises from 11.9 draws in production to 64.0 draws in `moving-mainstream-rough-cap64` and 89.0 draws (p90 = 128.0) in `moving-mainstream-rough-uncapped`. On `glossy-trim`, Step 4 still runs on all 38.9% of pixels below 0.40 roughness (`reflectedTried = 38.9%`, `reflectedWon = 12.3%`, the same as production).

### Quantitative comparison: mainstream accumulator vs isolated-source prototypes

Display-linear metrics over frames 16 through 95 on the 1:41 moving cockpit:

| Variant | Sloped-panel mean / spatial / temporal | Panel-warm wash error | Panel-cool mean / spatial / temporal | Panel-cool wash error | Windshield glass mean change | Helmet mean / spatial change | Glossy-trim mean / spatial change |
|---|---:|---:|---:|---:|---:|---:|---:|
| `moving-normal` | 0.00% / 0.00% / 0.00% | 0.00% | 0.00% / 0.00% / 0.00% | 0.00% | 0.00% | 0.00% / 0.00% | 0.00% / 0.00% |
| `moving-mainstream-rough-cap64` | +7.47% / -64.93% / -56.15% | 2.68% to 4.65% | +9.58% / -72.92% / -78.51% | 6.78% to 13.50% | +0.03% | +0.13% / -0.12% | +0.00% / -0.00% |
| `moving-mainstream-rough-uncapped` | +7.47% / -67.33% / -57.07% | 2.71% to 4.65% | +9.09% / -75.71% / -80.78% | 6.24% to 13.56% | +0.02% | +0.13% / -0.11% | +0.00% / +0.01% |
| `moving-source-accum` (separated, cap 64, no spread) | +7.05% / -75.59% / -58.86% | 2.18% to 4.44% | +6.13% / -84.09% / -86.11% | 4.21% to 10.27% | +0.00% | -0.13% / +0.01% | +0.00% / +0.00% |
| `moving-source-accum-spread` (separated + radius-5 spread) | +7.41% / -84.18% / -59.06% | 2.65% to 5.30% | +9.45% / -92.66% / -86.89% | 6.06% to 13.86% | +0.01% | -0.12% / +0.02% | +0.00% / -0.01% |

Multiscale display-linear RGB fluctuation on edge-free `sloped-panel` pixels (total RMS, temporal RMS in parentheses):

| Variant | s=1 px | s=2 px | s=4 px | s=8 px | s=16 px |
|---|---:|---:|---:|---:|---:|
| `moving-normal` | 0.00197 (0.00185) | 0.00170 (0.00157) | 0.00106 (0.00097) | 0.00056 (0.00050) | 0.00056 (0.00029) |
| `moving-mainstream-rough-cap64` | 0.00069 (0.00060) | 0.00078 (0.00063) | 0.00063 (0.00047) | 0.00041 (0.00025) | 0.00063 (0.00020) |
| `moving-mainstream-rough-uncapped` | 0.00064 (0.00056) | 0.00074 (0.00060) | 0.00062 (0.00045) | 0.00041 (0.00024) | 0.00063 (0.00019) |
| `moving-source-accum` | 0.00048 (0.00040) | 0.00067 (0.00052) | 0.00065 (0.00044) | 0.00045 (0.00025) | 0.00063 (0.00018) |
| `moving-source-accum-spread` | 0.00020 (0.00017) | 0.00037 (0.00028) | 0.00050 (0.00033) | 0.00041 (0.00023) | 0.00062 (0.00019) |

Motion lag and directional-streak check on `sloped-panel` (early frames 16..40 vs late frames 70..95, plus 2-px horizontal vs vertical correlation):

- `moving-normal`: `late/early = 0.9511`, frame-to-frame RMS `0.00253`, `corr_x2 = 0.557` vs `corr_y2 = 0.564`.
- `moving-mainstream-rough-cap64`: `late/early = 0.9415`, frame-to-frame RMS `0.00093` (-63%), `corr_x2 = 0.879` vs `corr_y2 = 0.877` (isotropic, no motion streaks).
- `moving-mainstream-rough-uncapped`: `late/early = 0.9414`, frame-to-frame RMS `0.00090`, `corr_x2 = 0.890` vs `corr_y2 = 0.889`.
- `moving-source-accum`: `late/early = 0.9694`, frame-to-frame RMS `0.00084`, `corr_x2 = 0.922` vs `corr_y2 = 0.923`.
- `moving-source-accum-spread`: `late/early = 0.9548`, frame-to-frame RMS `0.00083`, `corr_x2 = 0.974` vs `corr_y2 = 0.954`.

### Verdict on the simpler existing-history policy

The existing shared specular accumulator captures almost the entire temporal-accumulation gain with zero new buffers and zero spatial blur. On `sloped-panel`, `moving-mainstream-rough-cap64` matches `moving-source-accum` on broad bands (`s4 = 0.00063` vs `0.00065`, `s8 = 0.00041` vs `0.00045`). It keeps warm and cool wash steady (`panel-warm` 2.68% to 4.65%, `panel-cool` 6.78% to 13.50%). It clears pixel confetti with no motion streaks and keeps `glossy-trim` (`0.00%`), `helmet-detail` (`+0.13%`) and `windshield-glass` (`+0.03%`) sharp.

Two limits remain without signal separation or spread:

1. Fine residual grain (`s1 = 0.00069`, `s2 = 0.00078`) stays above `moving-source-accum` (`0.00048`, `0.00067`) and `moving-source-accum-spread` (`0.00020`, `0.00037`). In `moving-source-accum`, the isolated diffuse-ray term accumulates for 64 draws before the shared specular accumulator runs a second temporal filter on the combined signal.
2. A full Step 4 bypass on `roughness >= 0.40` also bypasses `LightEvidence` cuts. It puts the 0.40 to 0.50 hand-over band's dedicated reflection ray in the same unclamped history. Under fast light changes, a production policy must keep `LightEvidence` active (it tracks deterministic direct light) while it widens or drops only the noisy 4-frame narrow-box clamp.

Visual contact sheets and slow-motion clips sit in `evidence/mainstream-accum/` (`mainstream-vs-prototype-contact.png`, `mainstream-helmet-glass-zoom.png`, `mainstream-vs-prototype-slow.mp4`).

## Part 2: Sender-side upper-tail and light-pyramid attribution

Next we checked if directing more of the same one-ray budget at known bright sources distributes energy at creation instead of turning bright output pixels into blobs. We captured `sender-baseline` on the 1:41 moving cockpit across 16 settled draws (`sampleDraw` 48, 52, ..., 108). Temporary readback arrays logged every unique diffuse ray's hit pixel, guided flag, guided target, 3-by-3 acceptance (`counts`), `edgeFade`, `lobeDensity`, `guidedDensity`, `missLight` luminance and `leaving` luminance. They also logged every receiver pixel's outgoing diffuse-ray-sourced specular RGB, incoming luminance, capped BRDF weight (`MostRayWeight = 4.0`), uncapped BRDF weight and `PlanOwnDiffuse` bit, plus `lightFinest` and `hitLight`.

The extra readback stores shifted dithered 8-bit PNG codes by at most 1 to 2 codes (a 1-ULP GPU FMA order shift in `screen_trace_resolve.comp`). Scene setup, ray budget and light distributions stayed unchanged.

### Unique rays vs 2-by-2 receiver records, on-screen vs offscreen, guided vs ordinary

| Panel crop (16 moving draws) | Subset | Receiver records | Unique diffuse rays (rec/ray) | Share of crop outgoing lum | On-screen hit / offscreen env lum share | Guided / ordinary draw lum share | Incoming lum p50 / p90 | Capped BRDF weight p50 / p90 (`>4.0` cap hit rate) |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| `sloped-panel` | All records | 179,872 | 52,759 (3.41) | 100.00% | 97.12% / 2.88% | 77.00% / 23.00% | 0.002 / 0.117 | 0.247 / 3.253 (8.62%) |
| `sloped-panel` | Top 5% | 8,994 | 2,652 (3.39) | 92.22% | 99.75% / 0.25% | 81.61% / 18.39% | 0.766 / 9.674 | 2.573 / 4.000 (39.26%) |
| `sloped-panel` | Top 1% | 1,799 | 547 (3.29) | 60.44% | 99.99% / 0.01% | 93.19% / 6.81% | 9.653 / 9.708 | 1.560 / 2.821 (3.00%) |
| `panel-cool` | All records | 64,320 | 20,632 (3.12) | 100.00% | 98.59% / 1.41% | 75.05% / 24.95% | 0.002 / 0.112 | 0.170 / 2.681 (7.34%) |
| `panel-cool` | Top 1% | 644 | 200 (3.22) | 77.01% | 99.98% / 0.02% | 86.42% / 13.58% | 9.646 / 9.708 | 4.000 / 4.000 (53.73%) |
| `panel-warm` | All records | 76,608 | 22,571 (3.39) | 100.00% | 92.90% / 7.10% | 56.78% / 43.22% | 0.002 / 0.117 | 0.203 / 2.275 (6.75%) |
| `panel-warm` | Top 1% | 767 | 229 (3.35) | 54.08% | 99.94% / 0.06% | 75.68% / 24.32% | 9.591 / 9.702 | 0.901 / 4.000 (27.38%) |

### Dominant on-screen senders and why the light pyramid still produces rare spikes

Four sender-side facts explain the spikes on the 1:41 cockpit panel:

1. Tiny on-screen senders supply 99.94% to 99.99% of top-1% energy, not offscreen environment fallback. On `sloped-panel` top 1%, `console-cyan-emissive-screens` (71.40%, 392 unique rays) and the adjacent `console-housing` screen border (27.03%, 144 unique rays) supply 98.43% of upper-tail luminance. On `panel-cool` top 1%, those two supply 99.99%. On `panel-warm` top 1%, `pilot-suit-and-helmet` supplies 49.74% (74 unique rays), `console-cyan-emissive-screens` supplies 25.91% (77 unique rays), and `console-housing` supplies 14.97% (51 unique rays).
2. The existing light pyramid already sees these dominant sources with 100.0% coverage. At draw 80 (`guideTotal = 90,602.0`), `console-cyan-emissive-screens` fills only 1,621 render pixels (0.4% of screen) yet holds 17.24% of `guideTotal()` (`pyrMed = 9.65`). `cabin-floor` holds 35.66%, `console-housing` 8.83%, and `pilot-suit-and-helmet` 6.22%. Every top-1% hit on the panel has `guidedDensity > 0`.
3. Guided draws already produce 93.19% of `sloped-panel` top-1% luminance (86.42% on `panel-cool`, 75.68% on `panel-warm`), yet only about 1% of unique panel rays land a counted hit on the cyan screens. Three factors starve the hit rate:
   - Only 25% of diffuse rays take a guided draw (`GuidedShares.Diffuse = 0.25`).
   - Only 11.9% to 12.7% of guided draws from the panel pick a pixel on `console-cyan-emissive-screens` because `cabin-floor` takes 35% of guided targets.
   - Even when a guided ray aims directly at `console-cyan-emissive-screens`, only 25.9% to 38.3% count (`counted3x3`). On draw 80 (`sloped-panel`), 109 unique guided block rays aimed at the cyan screens. Of those 109 rays, 103 (94.5%) hit the cyan screen surface and only 3 hit an occluder (`cheb > 5`). Yet `if (guided && !counts)` zeroed out 69 of 109 (63.3%). `TargetDepthShare = 0.05` aims up to 5% behind the screen depth, so oblique rays intersect the screen 2 to 4 pixels ahead of `target`. Sixty-six of the 69 rejected hits landed at Chebyshev distance 2 to 5 pixels, outside `guidedDensityOf`'s 3-by-3 window.
4. Incoming brightness and BRDF weight amplify the rare survivors. Incoming luminance jumps 4,800x from the panel median (0.002) to the top-1% median (9.65). On `panel-cool` top 1%, 53.73% of records hit `MostRayWeight = 4.0`. Each rare unique ray stamps 3.12 to 3.41 receiver pixels through 2-by-2 block reuse.

### Fixed-budget probability-weighted guiding controls

Attribution identified two sender-side levers that keep the exact one-ray budget, keep the production temporal accumulator unchanged, and add zero spatial blur:

1. `guided-diffuse-075`: raises `GuidedShares.Diffuse` from `0.25f` to `0.75f`. Every diffuse ray keeps the exact mixture density `(1 - share) * lobeDensity + share * guidedDensity`.
2. `guided-diffuse-075-tight`: keeps `GuidedShares.Diffuse = 0.75f`, lowers `TargetDepthShare` in `screen_trace.glsl` from `0.05` to `0.01`, and expands the matching MIS counting box in `guidedDensityOf` from 3-by-3 (`[-1, 1]`) to 5-by-5 (`[-2, 2]`):

```glsl
const float TargetDepthShare = 0.01;
...
float guidedDensityOf(vec3 origin, vec3 direction, ivec2 hit, ivec2 target, out bool counts)
{
    float density = 0.0;
    counts = false;
    for (int y = -2; y <= 2; y++)
        for (int x = -2; x <= 2; x++)
        {
            ivec2 q = hit + ivec2(x, y);
            float pixel = pixelDensityOf(origin, direction, q);
            density += pixel;
            counts = counts || all(equal(q, target)) && pixel > 0.0;
        }
    return density / guideTotal();
}
```

where

- `TargetDepthShare` is the depth thickness of the target pixel's aim box as a fraction of the target pixel's view depth. A 0.01 depth box reduces oblique parallax overshoot on tilted console screens.
- `guidedDensityOf` sums `pixelDensityOf` over every pixel `q` inside the 5-by-5 acceptance box around `hit`, then divides by `guideTotal()`, the sum of the light pyramid's finest level.
- `counts` is true when `target` lies inside that 5-by-5 box and `pixelDensityOf` at `target` is positive, so the acceptance set matches the summed MIS density.

Because `screen_trace_resolve.comp` caps every ray's specular weight at `MostRayWeight = 4.0`, neither control stays unbiased under the cap. In `moving-normal`, 53.73% of `panel-cool` top-1% records hit the `4.0` cap, which biases the baseline downward. Higher guided hit density lowers per-ray weights so fewer rays hit the `4.0` cap and fewer spikes trigger Step 4's narrow-box clamp. Both effects recover more cyan wash brightness.

Display-linear metrics on the 1:41 moving cockpit (frames 16 through 95):

| Variant (same 1-ray budget) | Sloped-panel mean / spatial / temporal | Panel-warm wash error | Panel-cool mean / spatial / temporal | Panel-cool wash error | Cabin-floor diffuse mean / spatial | Windshield glass mean | Helmet mean / spatial | Glossy-trim mean |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `moving-normal` (`diffuseShare = 0.25`) | 0.00% / 0.00% / 0.00% | 0.00% | 0.00% / 0.00% / 0.00% | 0.00% | 0.00% / 0.00% | 0.00% | 0.00% / 0.00% | 0.00% |
| `guided-diffuse-075` | +4.12% / -41.41% / -35.93% | 1.43% to 2.39% | +13.50% / -15.29% / -17.03% | 10.90% to 21.24% | -0.14% / -0.01% | -0.02% | -0.33% / +0.33% | +0.00% |
| `guided-diffuse-075-tight` | +12.34% / -63.10% / -49.21% | 6.21% to 8.80% | +25.37% / -62.33% / -63.30% | 20.06% to 36.26% | -0.11% / -0.02% | -0.01% | -0.34% / +0.33% | +0.00% |
| `moving-mainstream-rough-cap64` | +7.47% / -64.93% / -56.15% | 2.68% to 4.65% | +9.59% / -72.92% / -78.51% | 6.78% to 13.50% | +0.05% / -0.03% | +0.03% | +0.13% / -0.12% | +0.00% |
| `moving-source-accum-spread` | +7.41% / -84.18% / -59.06% | 2.65% to 5.30% | +9.45% / -92.66% / -86.89% | 6.06% to 13.86% | +0.03% / -0.01% | +0.01% | -0.12% / +0.02% | +0.00% |

Multiscale display-linear RGB fluctuation on edge-free `sloped-panel` pixels (total RMS, temporal RMS in parentheses):

| Variant | s=1 px | s=2 px | s=4 px | s=8 px | s=16 px |
|---|---:|---:|---:|---:|---:|
| `moving-normal` | 0.00197 (0.00185) | 0.00170 (0.00157) | 0.00106 (0.00097) | 0.00058 (0.00052) | 0.00055 (0.00030) |
| `guided-diffuse-075` | 0.00121 (0.00113) | 0.00113 (0.00103) | 0.00070 (0.00062) | 0.00040 (0.00033) | 0.00056 (0.00021) |
| `guided-diffuse-075-tight` | 0.00076 (0.00070) | 0.00073 (0.00067) | 0.00046 (0.00040) | 0.00032 (0.00022) | 0.00067 (0.00017) |
| `moving-mainstream-rough-cap64` | 0.00069 (0.00060) | 0.00078 (0.00063) | 0.00063 (0.00047) | 0.00041 (0.00026) | 0.00061 (0.00020) |
| `moving-source-accum-spread` | 0.00020 (0.00017) | 0.00037 (0.00028) | 0.00050 (0.00033) | 0.00040 (0.00023) | 0.00060 (0.00019) |

### What this proves about sender-side sampling versus receiver-side spreading

1. A fixed one-ray budget aimed at known bright sources distributes energy at creation without turning bright pixels into blobs. `guided-diffuse-075-tight` beats both `moving-mainstream-rough-cap64` and `moving-source-accum-spread` on the medium and large clump bands (`s4 = 0.00046` vs `0.00063` and `0.00050`, and `s8 = 0.00032` vs `0.00041` and `0.00040`). It keeps production's unchanged temporal accumulator and adds zero spatial blur.
2. The oblique `!counts` rejection rate (`TargetDepthShare = 0.05` + 3-by-3 box) limits guided sampling more than `diffuseShare = 0.25` alone. A higher `diffuseShare = 0.75` alone cuts `panel-cool` spatial grain by 15.29% because 63.3% of oblique cyan-screen hits still fail `counts`. A tighter `TargetDepthShare = 0.01` with a matching 5-by-5 MIS box cuts `panel-cool` spatial grain by 62.33% and temporal grain by 63.30%.
3. Both controls leave `cabin-floor-diffuse` (-0.11%), `windshield-glass` (-0.01%), `glossy-trim` (+0.00%) and `helmet-detail` (-0.34%) nearly unchanged in this cockpit view. But `diffuseShare = 0.75` leaves only `0.25` for ordinary cosine draws, which can raise variance on surfaces lit mainly by offscreen sky or environment light. A 5-by-5 `pixelDensityOf` loop also increases per-hit GPU work relative to 3-by-3 and requires Linux Runpod timing before any production change.

Evidence files sit under `evidence/sender-attribution/` (`sender-results.json`, `sender-attribution-map.png`, `guided-results.json`, `guided-controls-contact.png`, `guided-controls-slow.mp4`, patches, settings and draw logs).

## Verification and limits

- `test_cross_and_mainstream.py` tests `AccumulationTrace` decoding, output-to-render coordinate mapping, unique-ray vs 2-by-2 receiver deduplication, and `edgeFade` splitting. All 44 research unit tests pass and all 35 scripted model mutations die.
- Restored `src/` has an empty diff (`git diff src/`). The clean Release build of `src/Tertius.Demo` succeeds with zero warnings and zero errors (`evidence/restored-build-cross-mainstream-sender.log`).
- Scope limits: all runs here test one Mac headless cockpit view at fixed exposure. We did not yet test `guided-diffuse-075-tight` on open-sky shots (`0:13` doorway or `7:27` berth) or combine it with a relaxed rough-specular accumulator clamp. This report makes no Linux Runpod frame-time claim.
