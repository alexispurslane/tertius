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

## Irregular reciprocal exchange

The matched irregular control changes only the neighborhood sampling and weights. It keeps the regular controls' source calculation, outgoing-RGB exchange, surface tests and connection tests. It uses eight one-pixel connections, plus 24 candidate wide connections at 12 different directions and lengths. Their radii range from 3.16 to 12 render pixels. The one-pixel connections join different coordinate phases, including across the ray reuse and reconstruction blocks. No filter boundary follows an 8-by-8 block or a crop boundary.

Each local connection exchanges 1/16. Each accepted wide connection exchanges 1/48. A wide connection opens when its own deterministic blue-noise byte is below 192, about 75% of edges. The blue-noise address uses the same endpoint anchor in both directions, a different spatial offset for each direction, and fixed mask frames 32 through 43. Consequently both endpoints make the same decision and exchange the same RGB share. Local shares sum to at most 1/2 and wide shares to at most 1/2. Rejected shares stay at the center. These bounds are for exact arithmetic; normal floating-point roundoff still applies.

The blue-noise pattern stays fixed between frames. It does not rotate a single lattice per receiver or per frame. It is anchored in screen space and repeats with the existing mask's 128-cell period. Motion across that screen-space pattern remains a limitation. This is an interim screen-space neighborhood, not an isotropic disk projected from a geometric tangent plane. The geometry test remains unchanged so the experiment isolates the sampling change.

| Control, 32 measured frames | Panel brightness change | Spatial grain change | Temporal grain change | Warm-patch wash error | Cool-patch wash error |
|---|---:|---:|---:|---:|---:|
| Regular wide spread | +9.53% | -78.19% | -24.81% | 1.05% to 5.09% | 11.73% to 24.91% |
| Irregular reciprocal spread | +7.03% | -66.50% | -25.39% | 0.85% to 3.61% | 11.20% to 17.54% |

The irregular control removes the visible regular rows and leaves uneven grain. It removes less fine-grain RMS than the regular wide control. It also changes the local wash less. It still brightens the panel and changes its cool wash, so it is not a validated fix.

The grid check directly reuses `luma`, `corr` and `ac` from `docs/research/cut-grid-probe/tools/roi.py`, the functions used by that experiment's production record. It subtracts a Gaussian blur of sigma three output pixels from encoded Rec. 601 luma, then averages correlations along the two image axes. These dimensionless grid scores use the original grid method, separately from the display-linear grain and RGB-wash measures. The table shows the median over frames 16 through 47.

| Control | Correlation at 9 output pixels | Correlation at 18 output pixels |
|---|---:|---:|
| Normal | 0.01744 | 0.00491 |
| Regular wide spread | 0.47376 | 0.35301 |
| Irregular reciprocal spread | -0.00675 | 0.00359 |

Nine output pixels correspond to the wide control's six-render-pixel stride. The irregular control removes this lattice peak. `evidence/grid-stats.json` retains other lags and all per-frame values.

The window edge remains sharp in the matched crops. This statement concerns geometry, not unchanged lighting on either side. Window-interior brightness rises 15.57% under irregular spreading, with paired display-linear luminance RMS difference 0.000756. The dark interior therefore requires further review despite the sharp edge. The helmet-detail mean changes by less than 0.001%, its spatial detail RMS by +0.12%, and its temporal measure by +4.05%. This retains the dedicated glossy reflection detail in this crop; it does not establish preservation of all rough reflected detail.

The three-frame repeat is byte-identical to the first three frames of the 48-frame irregular run. The larger run is not repeated. `evidence/irregular-repeatability.json` records the exact scope. All candidate offsets and endpoint rules are in `irregular.glsl`; `evidence/irregular-experiment.patch` retains the actual live shader.

The irregular shader adds at most 32 neighbor source evaluations and three intermediate-cell checks per accepted candidate, rather than the regular shader's 24 neighbors. Geometry rejection and blue-noise gating reduce the actual count. It recomputes the source from immutable ray records, so it is a diagnostic implementation with extra work. No matched Linux Runpod cost is available. Mac run logs do not establish a shipping performance budget.

## Reciprocal tangent-plane footprint

The `tangent` control keeps the irregular control's candidate pixels, fixed blue-noise decisions, source evaluation and exchange shares. It adds one symmetric distance test after the existing compatibility and connection tests. Both endpoints must accept the pair inside a circular footprint in their geometric tangent planes. A tangent plane is the plane through a surface point, perpendicular to its geometric normal. Shading normals still participate only in the unchanged compatibility test.

The footprint radius is 12 times the geometric mean of the horizontal and vertical front-facing render-pixel sizes at each endpoint's view depth. These sizes are in metres in camera-relative world space. They come from the camera's field of view and the actual 848-by-480 render size. This supplies a depth-dependent world-distance bound without expanding the circle to compensate for a surface viewed at a shallow angle. The pair uses the smaller endpoint radius and the larger endpoint tangent distance.

The actual added shader ends with:

```glsl
    float distanceSquared = max(dot(firstTangent, firstTangent), dot(secondTangent, secondTangent));
    float radius = min(probeSurfaceRadius(first), probeSurfaceRadius(second));
    return distanceSquared <= radius * radius;
```

where

- `firstTangent` is the vector between the pair's surface points projected into the first endpoint's geometric plane, in metres.
- `secondTangent` is that vector projected into the second endpoint's geometric plane, in metres.
- `dot` computes a vector dot product. A tangent vector dotted with itself gives its squared length in square metres.
- `max` selects the larger of the two squared lengths.
- `distanceSquared` is that larger squared length, in square metres.
- `first` and `second` are the two endpoints' existing material, geometry and view records.
- `probeSurfaceRadius` computes the footprint radius described above, in metres.
- `min` selects the smaller endpoint radius.
- `radius` is the radius that both endpoints must accept, in metres.

Swapping the endpoints leaves this decision unchanged. The original local and wide shares still sum to at most one. Rejected shares stay at the center. This retains the original exchange's outgoing-RGB conservation in exact arithmetic. It introduces no clipping, luminance limit or RGB-channel scaling.

The sampling footprint and noise anchor are separate. The footprint now rejects candidates by distance along the surface. The blue-noise anchor still uses the same screen-pixel endpoint and fixed mask frame. The candidates themselves also remain sparse screen-space offsets. This is a tangent-bounded subset of the irregular graph, not uniform sampling of a surface disk, a surface-anchored noise field, or a material grain direction. Rejection also reduces the amount of exchange. A result cannot isolate footprint shape from that reduced exchange strength.

The bounded comparison captures 48 displayed frames at the same 6.0043726-stop exposure, simulation ticks, warm-up rule, output size and local cooked assets as the saved irregular run. It measures frames 16 through 47. The 21 cooked asset hashes still match `assets.json`. The first three tangent frames repeat byte for byte. A separate three-frame normal replay also matches the saved baseline byte for byte after restoring and rebuilding the renderer. `evidence/tangent-verification.json` records both checks, all 48 tangent-frame hashes, the matched settings and the unchanged engine source tree. Neither full 48-frame sequence is repeated.

### Measured outcome

| Control, 32 measured frames | Panel brightness change | Panel spatial grain change | Panel temporal grain change | Warm-patch wash error | Cool-patch wash error |
|---|---:|---:|---:|---:|---:|
| Irregular screen-space exchange | +7.03% | -66.50% | -25.39% | 0.85% to 3.61% | 11.20% to 17.54% |
| Tangent-bounded exchange | +5.42% | -50.77% | -21.60% | 0.60% to 3.28% | 10.00% to 15.22% |

The tangent bound keeps the grid peak absent. Its median correlations at nine and 18 output pixels are -0.00762 and -0.00933. The irregular screen-space control's values are -0.00675 and +0.00359. The same original grid-check functions and the same frames produce both rows. `evidence/tangent-grid-stats.json` holds the full lag and per-frame records.

The tangent bound leaves more fine speckles than the irregular control. It changes the warm and cool wash less, with no channel clipping or source cap. Both warm and cool colors remain visible. The local RGB values below show that the cool light still increases. This is not preservation of the original local wash.

The broad panel's temporal measure improves over normal, but the cool crop behaves differently:

| Crop and control | Spatial grain change | Temporal grain change | Mean brightness change |
|---|---:|---:|---:|
| Cool crop, irregular | -55.91% | -17.95% | +12.41% |
| Cool crop, tangent bound | -24.69% | +34.22% | +10.00% |
| Window interior, irregular | -62.00% | -43.40% | +15.57% |
| Window interior, tangent bound | -46.65% | -39.78% | +11.87% |

Thus the tangent bound trades away grain removal and worsens the cool crop's frame-to-frame grain measure, even while the broad panel's measure falls. These measures include motion and the retained 32 warm-up draws between displayed frames. They do not establish perceived flicker during uninterrupted 60 Hz motion. `evidence/tangent.mp4` retains the displayed sequence at the replay's 60 frames per second.

The windshield boundary stays visually sharp at matched frames 9, 24 and 38. No broad halo appears in those inspected crops. The dark window interior still changes: its paired luminance RMS difference from normal is 0.000610 for the tangent bound, versus 0.000756 for irregular exchange. Sharp geometry does not establish unchanged lighting or prove that the approximate connection guard rejects every thin gap. The helmet crop changes in mean brightness by -0.00060%, in spatial detail RMS by +0.13%, and in temporal RMS by +4.02%. Its dedicated glossy detail remains visible in the inspected frames.

The result supplies no evidence that this tangent bound is a better fix. It limits exchange over foreshortened surfaces and modestly reduces wash changes, while leaving more grain and worsening the cool crop's temporal measure. The noise pattern remains anchored in screen space. Testing a surface-anchored pattern would be a separate experiment. No production filter is selected, and issue #5 remains open.

### Local color evidence

Each entry is the time-mean display-linear RGB vector of one local patch, with channel values from zero to one. Patch rows run top to bottom and columns left to right within each fixed crop. These values use the same 32 measured frames. Wash errors above also account for the spatial distribution of color, using the eight-output-pixel blur defined earlier.

| Crop patch | Normal RGB | Irregular RGB | Tangent-bound RGB |
|---|---|---|---|
| panel-warm row0-column0 | 0.117832 / 0.106232 / 0.086458 | 0.118828 / 0.107169 / 0.087325 | 0.118476 / 0.106885 / 0.087138 |
| panel-warm row0-column1 | 0.032648 / 0.030845 / 0.025485 | 0.033414 / 0.031548 / 0.026076 | 0.033196 / 0.031379 / 0.025952 |
| panel-warm row1-column0 | 0.072744 / 0.065513 / 0.052649 | 0.074077 / 0.066797 / 0.053669 | 0.073774 / 0.066457 / 0.053373 |
| panel-warm row1-column1 | 0.028188 / 0.026727 / 0.022404 | 0.029004 / 0.027762 / 0.023395 | 0.028814 / 0.027616 / 0.023329 |
| panel-cool row0-column0 | 0.036950 / 0.040837 / 0.036993 | 0.039013 / 0.045624 / 0.042501 | 0.038768 / 0.045256 / 0.042149 |
| panel-cool row0-column1 | 0.037178 / 0.044363 / 0.041856 | 0.040324 / 0.052369 / 0.051199 | 0.039686 / 0.050974 / 0.049650 |
| panel-cool row1-column0 | 0.032257 / 0.036598 / 0.033811 | 0.033766 / 0.040144 / 0.037956 | 0.033552 / 0.039519 / 0.037187 |
| panel-cool row1-column1 | 0.044317 / 0.053730 / 0.051038 | 0.047149 / 0.061171 / 0.059866 | 0.046334 / 0.058952 / 0.057246 |

`evidence/tangent/results.json` retains all eight crops, all local RGB vectors and the unrounded measures. `evidence/tangent-summary.json` retains derived percentages. The actual temporary shader patch, script, settings and run logs are alongside them.

## Repeat the experiments

Run one renderer experiment at a time in this checkout. Each output folder must be new. The runner restores all temporary engine edits in a `finally` block. After a diagnostic, build once more before using `--no-build` for unrelated rendering.

```powershell
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py normal captures/issue5-repeat/normal --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py raw-source captures/issue5-repeat/raw-source --frames 48 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py cap-diffuse-ray-specular captures/issue5-repeat/cap-diffuse-ray-specular --frames 48 --limit 10.944249153137207 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py spread-step6 captures/issue5-repeat/spread-step6 --frames 48 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py irregular captures/issue5-repeat/irregular --frames 48 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py tangent captures/issue5-repeat/tangent --frames 48 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/analyze.py captures/issue5-repeat captures/issue5-repeat/analysis --frames 48 --variants normal irregular tangent --crops docs/research/issue-5-diffuse-grain/irregular-crops.json
nix develop --command python3 docs/research/issue-5-diffuse-grain/grid_stats.py captures/issue5-repeat captures/issue5-repeat/grid.json --variants normal irregular tangent
nix develop --command python3 -m unittest discover -s docs/research/issue-5-diffuse-grain -p 'test_*.py'
```

`render.py` also supplies the zero-diffuse, closed-diffuse, zero-specular and no-diffuse-ray-specular controls. `raw_stats.py` reduces raw readbacks. `analyze.py` retains matched frames, crops, local wash images and metrics. `evidence/` holds the clips, actual patches, settings and reduced results. The complete original frame sequences, raw full-screen buffers and build logs remain under `captures/issue-5-diffuse-grain/` in this checkout.

## Verification and limits

The 18 measurement and exchange-model tests pass. The measurement tests first failed on missing behavior, then passed. The exchange model's impulse, boundaries and constant-wash tests did the same. One surviving normalization mutation led to a behavior test that requires equal distribution to the center and its neighbors. All 18 listed model and measurement mutations die, including loss of reciprocity, disconnected coordinate phases, an unstable exchange weight and loss of either endpoint's tangent bound. The tangent behavior tests first fail on unrestricted exchange and then pass with the added bound. These CPU model mutations do not validate the live shader itself. Line coverage is 31/31 for `measure.py` and 43/43 for `spread_model.py`; the standard-library tracer does not measure branch coverage. The existing renderer-survey suite passes all 96 tests.

Every experimental Release build and real renderer run completed. The normal engine build also passes after restoring the patches. The final engine source has no diff. No full .NET suite, Linux GPU validation, performance benchmark or production hardening is claimed. Mac timing logs are retained only as run records. Renderer performance decisions require matched Linux Runpod measurements.

This one cockpit view does not cover the berth floor, independent camera and light angles, uniform tangent-plane sampling, surface-anchored noise, or normal uninterrupted motion. A production fix still needs matched moving shots and independent camera and light controls before it could close issue #5.
