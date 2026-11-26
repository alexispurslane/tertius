# Whole visible crop with no distance cutoff

This is the primary offline reference for the latest model in issue #5: spread reflected light carried by diffuse rays over the connected receiving domain, with a smooth center peak and no hard distance cutoff. Earlier adaptive-radius controls remain recorded in [CLUMPS.md](CLUMPS.md). They are superseded as the desired model.

The reference satisfies that mathematical model only on the saved visible rectangular crop. It does not establish a whole physical sheet, true surface boundaries, physical area weights or anchoring under motion. None of those data is present in this archive. No similarity test on normals is called shared topology.

## Domain and mechanism

The domain is all 9,620 cells of the 74-by-130 render-pixel crop, box [584,28,714,102], inspected within the cockpit's visible panel. The input is outgoing diffuse-ray-sourced specular RGB from the first fixed pose's distinct draw phases 8 through 32. The cells have equal screen area. Crop edges are artificial insulating boundaries, meaning no energy leaves the crop. Hidden parts of the panel and connections to other visible regions are unavailable. So are mesh connectivity, physical surface-cell areas and a surface motion map.

The reference solves continuous spatial heat flow on the grid's four-neighbor graph. A cell exchanges with its adjacent cells, and each exchange acts equally in both directions. The finite diffusion operator includes paths of every length. Its kernel is positive across the connected domain in exact arithmetic, peaks at the impulse's source, and has no imposed distance cutoff. Insulating edges return the flow to the domain. The method needs a rectangular, uniform-cell domain; that is the situation of this archive. It would need a different spatial operator for an irregular physical surface.

A cosine transform diagonalizes this rectangle's insulating discrete Laplacian. The implementation multiplies every spatial frequency by its heat-decay factor, then transforms back. That is an exact discrete spatial solve up to floating-point error, rather than a finite number of neighbor-exchange passes with a bounded path length. For 9,620 cells it needs two transforms per RGB sample and diffusion amount. It does not construct a 9,620-by-9,620 dense matrix. This offline CPU calculation establishes no renderer performance budget.

Diffusion amount is a spatial parameter in square render cells, with coefficient one. It is unrelated to elapsed scene time or the number of accumulated frames. In the interior, a unit impulse's RMS radial width is the square root of four times this amount. Widths 3, 5.5 and 11 render cells use amounts 2.25, 7.5625 and 30.25. All three have whole-domain support. Width is not cutoff. Boundary impulses have different measured widths because the insulating boundary returns energy.

Every source draw is filtered separately. There is no temporal accumulation, future-sample classifier, channel clipping or output normalization to a target mean. The cosine transform's constant mode preserves the input sum of each RGB channel. This conserves source RGB for equal cells. It is not area-aware conservation on a foreshortened physical sheet.

## Impulse and boundary verification

The tests verify a positive tail 15 cells from an interior impulse, beyond the earlier 11-cell cutoff, and an interior RMS radial width of 5.5 cells. A boundary impulse retains all three RGB sums and spreads inward without losing light. A constant RGB wash stays constant at edges. Applying two diffusion amounts equals applying their sum. A rectangular-domain test verifies equal diffusion distances along both axes.

Tiny negative tails at approximately floating-point roundoff can occur in the transform. The numerical tests allow -1e-14. The calculation does not clamp those tails, since that would change its channel sums. Only the diagnostic image encoding clamps colors for PNG display. `impulse-boundaries.png` shows center and corner impulses with one common diagnostic encoding.

## Source color and clumps

These are pre-filter versus post-filter source measurements. They are outgoing pre-exposed RGB, not final displayed RGB or scene radiance. Whole-domain mean RGB before filtering is 0.0079670973 / 0.0154102459 / 0.0177582606. Every width preserves that vector to floating-point precision. Maximum per-frame channel-sum drift is below 1.1e-12.

The local wash error compares four local patches after blurring each time-mean source RGB image by eight render cells. It detects color moved to the wrong place even when the total energy is correct. Spatial RGB fluctuation uses Gaussian bands at s and 2s, in render cells. A fixed 11-cell compact kernel is included at a similar RMS width of 5.5003 cells.

| Offline source control | RMS width, render cells | Distance cutoff | RGB fluctuation at s=1 | RGB fluctuation at s=4 | Local source wash error |
|---|---:|---|---:|---:|---:|
| Earlier compact kernel | 5.5003 | 11 cells | 0.0052539 | 0.0060961 | 1.393% to 1.710% |
| heat-width3 | 3 | None | 0.0074874 | 0.0088273 | 0.454% to 0.592% |
| heat-width5.5 | 5.5 | None | 0.0018831 | 0.0060191 | 1.372% to 1.843% |
| heat-width11 | 11 | None | 0.0002794 | 0.0023266 | 3.770% to 5.672% |

At the matched 5.5-cell width, removing the cutoff lowers the fine band compared with the compact kernel, while its scale-4 fluctuation is nearly unchanged. Both retain visible round pools in individual source draws. At width 11, the pools overlap into broader color patches. Their higher local wash error measures the movement of color despite full-energy preservation. Whole-domain support by itself does not remove Monte Carlo variance. It redistributes each sampled hit.

`source-comparison-016.png` and `whole-crop-heat-moving.mp4` show the same source draw and consecutive source draws for the compact kernel and all three heat widths. The clip runs at five draw phases per second. All tiles use the same logarithmic diagnostic encoding, with no per-tile contrast adjustment. These are source visualizations, not rendered game frames. The camera remains fixed. A small tail may be invisible in the PNG even though the numerical kernel has no cutoff.

`results.json` retains global and local RGB vectors, wash errors and persistent/temporal spatial-band splits. `whole-crop-heat.npz` retains every filtered source draw. The main study's live matched crops retain the physically useful orange and cyan colors for the earlier controls. The heat reference does not cover those complete warm/cool crops or the windshield boundary. It has not passed through renderer history, reconstruction or tone mapping. Thus there is no final heat-filter color or clump result, no true-boundary validation and no evidence of anchoring as the camera or surface moves.

## What the result supports

The no-cutoff model is coherent and preserves all source RGB on this limited domain. At a practical width it still turns bright sampled hits into pools. Increasing the width trades local color placement for smoother fluctuations. The saved source captures cannot identify a physical whole-sheet target or distinguish every legitimate dark region from missed samples.

This favors continued investigation of source variance and correct reflected-light sampling before calling any spread a fix. The current independent-source controls are also rejected because they change useful color. A production evaluation of the latest model would need actual receiving-surface connectivity, physical areas and surface motion, plus a matched live reconstruction and color comparison. Those are missing evidence, not features implemented by this reference. PR #14 stays draft and issue #5 stays open.

```powershell
nix develop --command python3 docs/research/issue-5-diffuse-grain/heat_analysis.py docs/research/issue-5-diffuse-grain/evidence/source-offline/source-and-offline-controls.npz captures/issue5-heat-repeat
nix develop --command python3 -m unittest discover -s docs/research/issue-5-diffuse-grain -p 'test_*.py'
```
