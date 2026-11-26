# Visible clumps and source sampling, issue #5

All spread controls fail the visual requirement. Fine-grain RMS alone hid that failure. The dense connected control turns bright hits into broad smooth blobs and changes the cyan wash. Independent source directions reduce the panel's middle-scale color fluctuation, but change useful color and cost extra rays. No production fix is selected.

The [whole-crop heat-flow reference](WHOLE_SHEET.md) is now the primary offline comparison. It replaces adaptive radius as the desired model. Earlier controls below remain as evidence.

## What the displayed clip measures

All live controls use the saved 48-frame Blue Danube cockpit replay, the same 1280-by-720 output, 848-by-480 internal size, 6.0043726-stop exposure and local cooked assets. Measurements use frames 16 through 47. The 48 displayed frames cover the original film's 100.00 through 100.78 seconds. The simulation advances 120 ticks between displayed frames. The cockpit camera follows the ship, so panel motion in screen space is small.

A newly verified limit matters: each accelerated displayed frame is a still. It resets every history, runs sampling draw indices 0 through 31 as warm-up, and captures draw index 32. The global readback indices 33, 66 and 99 are not three independent sampling phases. `source-draws-indices.jsonl` records that reset directly. The displayed temporal measures mostly describe scene motion through a repeated sampling phase. These clips cannot prove normal uninterrupted-motion flicker performance.

`clump-moving-crops.mp4` shows the six controls at the original 60 displayed frames per second. `clump-moving-crops-slow.mp4` shows the same frames ten times slower, with no interpolation. Each tile keeps the original RGB pixels, cropped to output box [730,20,1130,430]. This includes orange light, cyan light and the windshield boundary. The PNG contact sheets retain matched frames 9, 24 and 38. Labels identify each control.

## Live controls and sample cost

| Control | Changed reflected contribution | Diffuse lighting | Existing dedicated reflection term | Extra source work per eligible render pixel per draw |
|---|---|---|---|---|
| Normal | Diffuse-ray estimate reused for rough reflection | Active | Active at its original share | None |
| Regular wide | Symmetric stride-six exchange | Active | Active | Up to 24 neighbor source evaluations |
| Irregular | Sparse reciprocal exchange with fixed screen-space blue-noise gates | Active | Active | Up to 32 neighbor source evaluations |
| Connected disk | Dense compact exchange with a checked contiguous path | Active | Active | Up to 372 neighbor source evaluations and their path checks |
| Independent GGX, one sample | New per-pixel direction replaces only the diffuse-ray-sourced specular estimate | Active | Active at its original share | One extra screen walk |
| Independent GGX, four samples | Mean of four new per-pixel directions replaces that same estimate | Active | Active at its original share | Four extra screen walks |

GGX is the engine's existing distribution of rough reflection directions. The new samples use its existing visible-normal sampler and matching density. They retain the original source path's diffuse-kind hit light, diffuse-kind environment miss light and 62-step walk budget. Their direction stream is distinct from the diffuse lighting stream. They use no guiding mixture, so the weight's sampling density is the GGX density alone. Directions and densities use the engine's existing record encoding. The existing specular weight cap and light cap remain active. Consequently neither estimator is an uncapped, unbiased reference.

The original diffuse lighting ray and its final contribution remain intact. The existing dedicated reflection term also remains active; it can respond to changed specular history at later bounces. This is not an ablation that removes specular light. Earlier zero-indirect-specular and zero-diffuse-ray-sourced-specular controls remain preserved in the main study.

At the displayed draw after the shared-ray budget settles, the usual budget is roughly one diffuse ray per 2-by-2 block and, where eligible, one dedicated reflection ray per block, with extra own rays at rejected shares. One added source ray per eligible pixel is four extra rays per full block; four added samples are 16 extra rays per block. Across all 33 draws of each displayed frame, these controls add up to 33 or 132 source walks per eligible pixel. The four-sample row is a higher-cost comparison, not an equal-budget improvement or a converged target. No Mac timing is used to choose a production performance budget.

The one-sample control's actual resolve includes:

```glsl
            specularLight += handOver * probeRecord.light.rgb
                * specularWeightOf(p, lobes, factors, probeRay) / float(1u);
```

where

- `specularLight` is the demodulated, pre-exposed specular RGB before reconstruction.
- `handOver` is the original fraction of specular light assigned to the diffuse-ray source, from zero to one.
- `probeRecord.light.rgb` is incoming RGB from the newly drawn ray, using the original source's hit and miss light.
- `specularWeightOf` is the engine's capped specular response divided by that ray's sampling density and the receiver's material factor.
- `p` is the receiving surface's geometry, material and view record.
- `lobes` contains the receiver's material lobes.
- `factors` contains the receiver's removed diffuse and specular material factors.
- `probeRay` contains the new direction, its GGX sampling density and its validity.
- `float(1u)` converts the one-sample count to a float. The four-sample control divides each contribution by four.

## Connected support and effective width

The connected control gives every integer offset strictly inside an 11-render-pixel disk a positive weight. Its radial weight is the square of one minus squared distance divided by 121. The full-disk normalization includes the center. It checks every cell on an 8-connected raster path between endpoints against both endpoints' surface compatibility tests. Reversing the pair visits the same cells. A hole on that path rejects the pair. The existing material, geometric-normal, shading-normal and plane-separation tests stay active.

Each accepted pair exchanges the same share of outgoing RGB in both directions. Rejected shares stay at the center, and the off-center shares sum to less than one. This preserves each RGB channel in exact arithmetic and avoids receiver normalization. The path guard is still an approximation to visible geometry. It cannot certify subpixel gaps or separate coplanar surfaces with indistinguishable G-buffer properties.

The measured impulse RMS radius is 5.5003 render pixels. The irregular graph's expected RMS radius with 75% wide gates open is 5.5255 render pixels. Their widths are thus close by that measure. The fixed control is a single dense kernel, not repeated diffusion. No claim confuses repeated diffusion's maximum reach with its width. Its full-support impulse is a round blob. `impulse-support.png` compares numerical impulse responses before any history, using the same diagnostic encoding. The irregular model uses a seeded synthetic gate field, not the live blue-noise texture.

## Local color and fine grain

All changes below are relative to the same normal 32-frame measurement. Wash error uses the original four local patches per crop and an eight-output-pixel blur in display-linear RGB. Positive brightness change means added displayed luminance.

| Control | Panel brightness change | Fine spatial grain change | Warm-patch wash error | Cool-patch wash error |
|---|---:|---:|---:|---:|
| spread-step6 | +9.53% | -78.19% | 1.05% to 5.09% | 11.73% to 24.91% |
| irregular | +7.03% | -66.50% | 0.85% to 3.61% | 11.20% to 17.54% |
| connected | +12.05% | -84.88% | 1.93% to 9.48% | 21.55% to 32.00% |
| independent1 | +7.76% | -61.07% | 5.67% to 11.86% | 23.61% to 57.71% |
| independent4 | +16.41% | -72.19% | 7.45% to 15.72% | 42.24% to 81.75% |

The connected control retains a sharp windshield outline in the inspected crops, while its window-interior brightness rises 24.09%. The one-sample source control changes that interior by -5.60%; the four-sample control changes it by +4.24%. An intact outline therefore does not establish preserved light on either side.

The four-sample source control reduces more fine grain than the one-sample control, but increases the cool crop's mean brightness by 54.40%, versus 35.12% with one sample. Its local cool wash error reaches 81.75%. This higher-cost comparison does not establish the correct wash. The distribution, retained caps, per-pixel sampling budget and response of history all differ from the shared cosine estimate. The experiment does not isolate their individual effects. Correct density accounting alone does not validate the displayed result.

`clumps/results.json` retains every local time-mean RGB vector, wash image and unrounded measure for all eight crops. The earlier tangent experiment remains a control. Its radius is not tuned further.

## Multiscale color fluctuation

The new measure subtracts a Gaussian blur at standard deviation 2s from one at s, for s = 1, 2, 4, 8 and 16 output pixels. This difference rejects constant and linear wash away from boundaries. A zero-indirect-specular control identifies material and geometry edges: its time-mean luminance gradient must stay below 0.001 per output pixel, with all pixels within 6s of a stronger gradient excluded. Every variant uses the same mask at each scale. The calculation reads 128 pixels of image context around each crop.

The spatial RGB RMS includes all frames in that band. The persistent RGB RMS measures the band's temporal mean, so a stationary clump remains visible to the measure. Temporal RGB RMS measures the band after subtracting that temporal mean. RGB catches color fluctuation even when luminance stays constant. The tests include that case, a persistent clump, a moving clump and a smooth linear wash.

The panel has 23,000 measured pixels at scales 1, 2 and 4, 21,000 at scale 8, and 9,142 at scale 16. Other crops lose more pixels near real edges. The warm and cool crops have no valid scale-16 region. The cool crop has only 52 pixels at scale 8, so that number has a narrow spatial scope. Masks differ between scales; compare variants within a column. Real rough reflected detail and curved wash can remain in these bands. There is no noise-only ground truth or motion registration.

The panel's spatial RGB RMS, in display-linear channel units from zero to one, is:

| Control | s=1 | s=2 | s=4 | s=8 | s=16 |
|---|---:|---:|---:|---:|---:|
| normal | 0.0030272 | 0.0022750 | 0.0012938 | 0.0006254 | 0.0006173 |
| spread-step6 | 0.0007211 | 0.0005579 | 0.0003084 | 0.0003301 | 0.0005602 |
| irregular | 0.0012809 | 0.0013134 | 0.0009032 | 0.0005538 | 0.0006679 |
| tangent | 0.0017810 | 0.0016907 | 0.0011011 | 0.0006081 | 0.0006962 |
| connected | 0.0004272 | 0.0005690 | 0.0008840 | 0.0010255 | 0.0011053 |
| independent1 | 0.0010640 | 0.0008026 | 0.0005205 | 0.0003884 | 0.0006174 |
| independent4 | 0.0008038 | 0.0006319 | 0.0003982 | 0.0003371 | 0.0006542 |

The original grid check also passes for these new controls: the connected disk's correlations at nine and 18 output pixels are 0.01992 and -0.01274, versus the regular wide control's 0.47376 and 0.35301. The independent one-sample values are -0.00206 and 0.01451, and four-sample values are 0.00275 and -0.00093. `clumps-grid-stats.json` retains the complete check. The absence of that grid peak does not establish the absence of clumps.

The connected control suppresses the fine band but raises the scale-8 band 63.99% and the scale-16 band 79.06% above normal. Its visible blobs therefore have measurable broad structure. The irregular result's color fluctuation peaks at scale 2 rather than scale 1. The source controls reduce the scale-4 and scale-8 bands relative to irregular exchange, but their wash changes reject them as fixes. The displayed bands are almost entirely persistent under the repeated sampling phase. `clumps-multiscale.json` retains the persistent and temporal split at every scale and crop.

## Where the bright groups start

The outgoing source probe writes only diffuse-ray-sourced specular RGB before reconstruction, already multiplied by the receiving material's specular factor. Alpha identifies the immutable diffuse-ray record used by that receiver: a positive block record number or a negative own-pixel record number. A shared block number identifies one shared direction, density and hit-light record. Different receiver weights can still produce different outgoing RGB.

The 48-frame record probe leaves every displayed PNG byte-identical to normal. The separate consecutive-draw probe retains all 99 draws of three displayed frames and logs each reset. Its three displayed PNGs also match normal. The independent-direction analysis uses only draw phases 8 through 32 of the first fixed pose: 25 distinct pseudo-random draw phases. These samples have no camera motion. Their diffuse incoming-light history is still warming, which limits claims of a stationary distribution. They are not diffuse-output variance measurements.

Within the complete 2-by-2 blocks inside render box [584, 28, 714, 102], 99.98586% of pixel samples use shared records. Among the brightest 1% of outgoing source samples, 99.95842% use shared records, and 99.87526% belong to shared records with multiple bright receivers. 99.95010% of complete blocks use one shared record at all four receivers. Thus nearby bright pixels already share a bright source before spreading or history.

Spreading then broadens that source. The dense impulse produces a round blob without history, and the live connected result retains broad blobs. These observations do not isolate how much the existing history and reconstruction add. They establish both the shared source grouping and the spread's ability to create larger structure.

## Two bounded offline adaptive controls

These controls use the outgoing source archive from the 25 distinct draw phases. They are applied before any history, exposure reconstruction or tone mapping. They are offline experiments that use future samples, not live classifiers. Their units are outgoing pre-exposed RGB, and their band scales are render pixels. Their numbers cannot be compared directly with the displayed-image table above. Their raw-source wash measure uses a blur of eight render pixels, with four patches of this smaller source crop.

Both controls choose a per-pixel reach before exchange, cap it at 11 render pixels and use the smaller endpoint reach for each pair. They use the fixed disk's global normalizer and a smooth squared radial taper. Reducing a reach can only reduce pair weights, so the transfer stays symmetric and bounded. Rejected shares stay at the center. The maximum per-frame, per-channel source-sum drift across these runs is below 1.2e-12 in double precision.

| Control | How it chooses reach | What it protects | Result |
|---|---|---|---|
| Energy adaptive | Use temporal source coefficient of variation, dominance of one shared donor in a 5-by-5 donor-block neighborhood, and the gradient of the time-mean reflected source | Zero reach where fewer than three independent draw phases support bright reflected light; restrict persistent source gradients | Less local wash movement than the fixed disk, but most source fluctuation remains |
| Meeting adaptive | Form recurring reflected-source clusters, estimate the distance to another cluster, then smoothly blend capped cluster reaches over overlapping distance fields | The same uncertainty guard plus persistent gradients; no hard nearest-seed or Voronoi partition | Almost no wash change, but almost all clumps remain |

A supported cluster requires source luminance above the archive's 90th percentile in at least three distinct draw phases, time-mean luminance above the 75th percentile and at least four connected pixels. An 8-connected component inside one reused 2-by-2 donor cannot become multiple separate adjacent seeds. Distances are to other supported components. A cluster without a neighbor uses the capped 11-pixel fallback. Other cluster reaches use 0.75 times the edge-to-edge distance plus two pixels, bounded to [3,11], so opposite reaches can overlap. Gaussian distance weights blend the reaches continuously. There are no hard nearest-cluster boundaries. The later uncertainty and gradient guards do introduce hard zero-reach boundaries. Those guards use the whole fixed-pose batch and remain fixed across it, so this experiment cannot establish their stability under camera motion.

The energy control measures one complete shared donor block once. A negligible set of blocks has multiple own records; donor dominance there is only an approximation. It favors large reach where temporal coefficient of variation exceeds one, mean donor dominance exceeds one half, and normalized source gradient is below 0.15. Its reach field is smoothed before the conservative uncertainty guard.

The archive leaves 53.73% of pixels uncertain under the three-draw support rule. These data cannot distinguish legitimate dark gaps from unsampled light. Both controls assign zero reach there. This protects darkness conservatively and also blocks much useful spreading. The median reach is zero in both controls. The 75th-percentile reaches are 10.95 render pixels for energy adaptation and 4.31 for meeting adaptation.

| Offline control | Source RGB RMS at s=1 render pixel | Source RGB RMS at s=4 render pixels | Local raw-source wash error |
|---|---:|---:|---:|
| unspread | 0.0359083 | 0.0108650 | 0.000% to 0.000% |
| fixed | 0.0052539 | 0.0060961 | 1.393% to 1.710% |
| energy-adaptive | 0.0281357 | 0.0096778 | 0.376% to 0.688% |
| meeting-adaptive | 0.0346960 | 0.0108091 | 0.027% to 0.047% |

The adaptive controls do not match the fixed disk's wash movement or smoothing strength. Their smaller wash errors accompany much less exchange. They supply no fair evidence of better clump removal at equal local wash. `source-offline-moving.mp4` shows their consecutive source draw phases, slowed to five frames per second, using a common logarithmic diagnostic encoding. `source-offline/results.json` retains thresholds, cluster distances, reach distributions, local RGB values, persistent/temporal bands and conservation errors. The NPZ retains the source, record identifiers and all three offline outputs.

The offline domain is the inspected continuous panel crop. It lacks a full geometry buffer, so its crop boundary rejects exchange and its guards protect uncertain dark features. It cannot certify boundaries outside that crop or prove that a dark feature is geometric. The live fixed control supplies the actual geometry guard. None of these offline results proves boundary stability during camera motion; the source-draw sequence holds the camera fixed.

## Judgment and verification

The evidence favors investigating the source estimator and sampling budget before more radius tuning. It does not select the current independent-GGX control: both one and four samples change useful color too much. The connected control demonstrates that connected support alone turns outliers into blobs. The adaptive controls expose a missing fact: sparse reflected samples do not reliably identify dark gaps versus unsampled light.

All 30 measurement/model tests and 96 renderer-survey tests pass. All 29 scripted model/measurement mutants are killed; [MUTATIONS.md](MUTATIONS.md) retains one result per mutant. The core numerical line coverage is 31/31 for `measure.py`, 43/43 for `spread_model.py`, 15/15 for `multiscale.py` and 27/28 for `connected_model.py` and 9/9 for `heat_reference.py`. Its uncovered line skips offsets larger than a small image; the measured crop and test fixtures exceed the radius. These checks do not test the live shader's numerical correctness or validate the heuristic classifiers. Every live Release build and renderer run completed. The restored ordinary Release build passes, and the final engine source has no diff.

`clumps-verification.json` records the matched live scripts, exposure, all frame hashes, probe identity and restored source tree. `source-records-identity.json` records the byte identity of all 48 record-probe frames. Build logs and actual temporary shader patches are retained beside the evidence.

Issue #5 remains open and PR #14 stays draft. There is no production change, Linux validation, performance decision, converged reference, ordinary-motion flicker claim or successful fix.


## Reproduce the additional controls

Use new output folders. These commands assume the saved baseline and earlier controls remain under `captures/issue-5-diffuse-grain`. The runner restores each temporary patch before the next experiment. Run only one live renderer experiment at a time.

```powershell
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py connected captures/issue5-extra/connected --frames 48 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py independent1 captures/issue5-extra/independent1 --frames 48 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py independent4 captures/issue5-extra/independent4 --frames 48 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/render.py source-draws captures/issue5-extra/source-draws --frames 3 --exposure 6.0043726 --content-root '/Users/alexispurslane/Development/tertius/content'
nix develop --command python3 docs/research/issue-5-diffuse-grain/source_stats.py captures/issue5-extra/source-draws/raw captures/issue5-extra/offline --draws
nix develop --command python3 docs/research/issue-5-diffuse-grain/clump_analysis.py captures/issue-5-diffuse-grain captures/issue5-extra/clumps.json --variants normal spread-step6 irregular connected independent1 independent4
nix develop --command python3 -m unittest discover -s docs/research/issue-5-diffuse-grain -p 'test_*.py'
nix develop --command dotnet build src/Tertius.Demo -c Release
```

`clump_analysis.py` reads one capture root with one subfolder per variant. To analyze fresh controls alongside the saved baseline, place or link their folders under one fresh capture root first. Its zero-specular folder supplies the common edge mask. `check_coverage.py` and `check_mutations.py` reproduce the numerical checks.
