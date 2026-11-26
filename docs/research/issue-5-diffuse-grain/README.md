# Cockpit grain, issue #5

The 1:41 cockpit panel's fine speckles travel through indirect specular light. Most of that light comes from diffuse rays reused for rough reflections. Removing the final indirect diffuse term leaves most speckles and loses half the panel's light.

These are diagnostic experiments. Every engine edit is a temporary patch. The committed engine source stays unchanged. None of the controls is a validated fix for [issue #5](https://github.com/alexispurslane/tertius/issues/5).

## Matched replay

- Base: `860685c71`. Scene: `demo-surface`, with the locally cooked Blue Danube art. `assets.json` records its file hashes.
- Film time: 100.00 to 101.98 seconds, including the five-second opening card. This is `night` frames 300 through 419. The short script advances the original posts, camera choices and simulation ticks, replacing earlier films with runs.
- The segment starts after 87,581 simulation ticks. Each displayed frame advances 120 ticks. The cockpit camera follows the ship; the panel moves little in screen space. This does not test a walking camera.
- Output: 1280 by 720 pixels. Actual internal rendering: 848 by 480 pixels, with the default FSR upscale. Exposure stays at 6.0043726 stops in every comparison.
- Each displayed frame has 32 unrecorded warm-up draws before its captured draw. This follows the original accelerated shot's still rule. It limits claims about ordinary, uninterrupted 60 Hz motion and flicker. `evidence/raw-source/draws.json` retains the draw numbers.
- The checked-in `docs/media/blue-danube-demo.mp4` shows an older gray-box scene at 1:41. It is not the visual reference for the current cooked art. The scene and simulation sequence replay successfully.

The long controls have 120 displayed frames. Measurements omit frames 0 through 15, leaving 104 frames. The source, cap and spread experiments have 48 displayed frames. Their measurements use frames 16 through 47, leaving 32 frames. Compare numbers only within their table.

## What the measures mean

The PNGs become display-linear RGB by undoing sRGB encoding. Luminance uses Rec. 709 weights and ranges from zero to one. This is the displayed image after exposure, reconstruction, bloom and tone mapping. It is not scene radiance.

Spatial grain is the root mean square (RMS) of luminance minus its Gaussian blur at a standard deviation of 1.5 output pixels. Temporal grain is the RMS change of that residual between consecutive displayed frames. Material detail and motion can contribute to these measures. They are controlled comparisons, not a noise-only ground truth.

The warm and cool panel crops each split into four local patches. Each patch retains its time-mean linear RGB vector. Wash error is the RMS difference of the time-mean RGB images after an eight-output-pixel Gaussian blur, divided by the baseline wash's RMS. It includes lost or added light and changed color. Whole-panel brightness cannot replace these local checks. Crop boxes are in `crops.json` and `detail-crops.json`.

## Final light terms, 104 measured frames

All rows retain direct light. The diffuse controls retain the indirect specular path.

| Control | Panel mean luminance | Spatial grain RMS | Temporal grain RMS | Mean change | Spatial change |
|---|---:|---:|---:|---:|---:|
| Normal | 0.02634373 | 0.00571264 | 0.00036760 | 0% | 0% |
| Zero indirect diffuse | 0.01291350 | 0.00491405 | 0.00020542 | -50.98% | -13.98% |
| Environment estimate for indirect diffuse | 0.01397667 | 0.00499579 | 0.00023533 | -46.94% | -12.55% |
| Zero indirect specular | 0.01994621 | 0.00041572 | 0.00023472 | -24.28% | -92.72% |

The diffuse controls leave the colored speckles visible. Their lower absolute grain partly comes from darkening the panel. Relative to mean brightness, their grain increases. The zero-specular control clears the speckles but removes useful reflected color. Its cool-patch wash error is 25.60% to 34.21%. It is not a proposed fix.

`evidence/sloped-panel-matched.png` shows frames 24, 60 and 96 in rows, with normal, zero diffuse, environment diffuse and zero specular in columns. The original PNGs and four clips are beside it. `evidence/results.json` holds all local RGB values.

## Which rays supply the specular light

The measured panel roughness ranges from 0.478431 to 0.505882, with median 0.490196. The actual shader's diffuse-ray share of specular ranges from 0.784314 to 1, with median 0.901961. Only 5.04% of measured panel pixel-frames take their entire specular estimate from diffuse rays. Dedicated reflection rays still contribute elsewhere in the panel.

The source ablation replaces this exact statement in `screen_trace_resolve.comp`:

```glsl
        specularLight = handOver * diffuse.light.rgb * specularWeightOf(p, lobes, factors, diffuseRay);
```

where

- `specularLight` is the RGB specular estimate before reconstruction, with its material factor removed and exposure applied.
- `handOver` is the fraction, from zero to one, that diffuse rays supply to this specular estimate.
- `diffuse.light.rgb` is the incoming RGB light in the selected diffuse ray's record.
- `specularWeightOf` evaluates that ray for the specular lobe and caps its weight.
- `p` is the receiving pixel's material, geometry and viewing direction.
- `lobes` holds the receiving material's diffuse and specular lobes.
- `factors` holds the material factors removed from each stored lighting term.
- `diffuseRay` holds the selected diffuse ray's direction, sampling density and validity.

It replaces the statement with zero, while leaving final diffuse light, direct light and the dedicated reflection-ray term intact. Over the matched 32 frames, spatial grain falls 89.45%, temporal grain falls 36.90%, and panel mean brightness falls 20.19%. This identifies the main source path. It does not distinguish ray direction sampling, 2-by-2 ray reuse, reweighting, or reconstruction as the underlying cause.

The independent raw probe writes this source contribution into the renderer's existing RGBA32 diagnostic image. It retains the shader's hand-over value in alpha. The usual diagnostic writer is disabled for this image only. All 48 probe PNGs are byte-identical to normal. `evidence/probe-identity.json` records that check. The raw source samples and material bytes are in `evidence/raw-source/panel-raw-samples.npz`.

## Bright samples and the cap control

The source distribution contains rare bright samples. In 316,800 panel pixel-frames, its median luminance is 0.0002574, its 99th percentile is 2.7251, its 99.5th percentile is 10.944249, and its maximum is 33.50362. The brightest 1% carries 71.51% of this raw source term's luminance.

These units are demodulated, pre-exposed luminance before reconstruction. Exposure multiplies by `2^6.0043726`. They are not outgoing scene radiance or display brightness.

The cap control uses the observed 99.5th percentile, 10.944249, as a fixed limit. It multiplies all three source RGB channels by one common factor before adding the dedicated reflection contribution. It keeps source hue, final diffuse light and dedicated reflection light. The raw probe observes the uncapped source in that same run.

The cap affects 0.49558% of panel pixel-frames. Those samples lose 29.34% of the raw source term's luminance after limiting. The baseline and cap runs' uncapped raw source archives are byte-identical. `evidence/raw-cap/shared-data.json` links the retained copy.

The displayed panel loses 4.32% of mean brightness, spatial grain falls 45.32%, and temporal grain falls 19.83%. The warm-patch wash error is only 0.51% to 1.66%, but the cool-patch wash error reaches 11.21% to 19.43%. The cap therefore removes useful colored light despite a small whole-panel brightness loss. It remains a diagnostic control.

## Live spatial spreading before reconstruction

The existing `diffuse_filter_wavelet.glsl` already exchanges a symmetric share of each neighboring pair's difference. Rejected weights stay at the center. Its fixed symmetric weights preserve RGB energy before later nonlinear operations. The older `diffuse_spatial_prototype.glsl` instead normalizes each receiver's accepted taps; that does not supply the same conservation property. The experiments adapt the former exchange rule.

`spread.glsl` recomputes each neighbor's diffuse-ray-sourced specular contribution from the same immutable ray records. It multiplies by that neighbor's specular material factor, exchanges outgoing RGB, then removes the receiver's material factor again. This happens in the live resolve shader, before adding the dedicated reflection-ray contribution and before temporal reconstruction. There is no offline image filtering.

Both regular controls use a uniform 5-by-5 stencil. Each accepted neighbor exchanges 1/25 of its RGB difference. At most 24 neighbors exchange, so at least 1/25 remains at the center. The geometry test is symmetric. It checks the receiver card, shading model, metallic value, reflectance, roughness, base color, geometric and shading normals, and plane separation. Three intermediate raster cells must agree with both endpoints. This is an approximate connection check, not a proof for holes thinner than the checked cells.

| Control | Tap spacing | Maximum axis reach | Maximum diagonal reach | Panel brightness change | Spatial grain change | Temporal grain change |
|---|---:|---:|---:|---:|---:|---:|
| Small regular spread | 2 render pixels | 4 render pixels | 5.66 render pixels | +11.96% | -80.38% | -21.78% |
| Wide regular spread | 6 render pixels | 12 render pixels | 16.97 render pixels | +9.53% | -78.19% | -24.81% |

At this render size, one render pixel spans about 1.51 output pixels horizontally and 1.50 vertically. These are screen-space stencils, not tangent-plane disks. Surface normals define a plane for the boundary tests. They do not define a material grain direction.

The small spread turns speckles into broad patches. The wide spread retains an obvious regular grid. Its stride-six offsets do not connect different integer-coordinate residues modulo six. Lower fine-grain RMS does not establish a clean image.

RGB energy conservation at this intermediate stage does not preserve a local displayed wash after temporal reconstruction and tone mapping. Cool-patch wash error is 22.56% to 33.27% for the small spread, and 11.73% to 24.91% for the wide spread. The helmet detail's mean brightness changes by less than 0.007%. The window edge remains geometrically sharp in the retained crops, with light changing on its surfaces. The sparse-grid artifacts and wash changes prevent treating either regular control as a finished fix.

An irregular reciprocal exchange is the next bounded comparison. Its record will follow in this draft research branch.

## Repeat the experiments

Run one renderer experiment at a time in this checkout. Each output folder must be new. The runner restores all temporary engine edits in a `finally` block. After a diagnostic, build once more before using `--no-build` for unrelated rendering.

```powershell
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py normal captures/issue5-normal --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py raw-source captures/issue5-source --frames 48 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py cap-diffuse-ray-specular captures/issue5-cap --frames 48 --limit 10.944249153137207 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py spread-step6 captures/issue5-wide --frames 48 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/analyze.py captures/issue-5-diffuse-grain captures/issue5-analysis --frames 48 --variants normal cap-diffuse-ray-specular spread-step2 spread-step6 --crops docs/research/issue-5-diffuse-grain/crops.json
nix develop --command python3 -m unittest discover -s docs/research/issue-5-diffuse-grain -p 'test_*.py'
```

`render.py` also supplies the zero-diffuse, closed-diffuse, zero-specular and no-diffuse-ray-specular controls. `raw_stats.py` reduces raw readbacks. `analyze.py` retains matched frames, crops, local wash images and metrics. `evidence/` holds the clips, actual patches, settings and reduced results. The complete original frame sequences, raw full-screen buffers and build logs remain under `captures/issue-5-diffuse-grain/` in this checkout.

## Verification and limits

The 11 measurement and exchange-model tests pass. The measurement tests first failed on missing behavior, then passed. The exchange model's impulse, boundaries and constant-wash tests did the same. One surviving normalization mutation led to a behavior test that requires equal distribution to the center and its neighbors. All ten listed mutations then die. Line coverage is 31/31 for `measure.py` and 14/14 for `spread_model.py`; the standard-library tracer does not measure branch coverage. The existing renderer-survey suite passes all 96 tests.

Every experimental Release build and real renderer run completed. The normal engine build also passes after restoring the patches. The final engine source has no diff. No full .NET suite, Linux GPU validation, performance benchmark or production hardening is claimed. Mac timing logs are retained only as run records. Renderer performance decisions require matched Linux Runpod measurements.

This one cockpit view does not cover the berth floor, independent camera and light angles, tangent-plane filtering, or normal uninterrupted motion. Those remain required before a production change could close issue #5.
