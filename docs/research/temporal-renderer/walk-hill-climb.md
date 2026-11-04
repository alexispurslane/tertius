# The V6 walk hill-climb (ter-dzxf)

Date: 2026-11-04. This is a fixed-input performance spike, not an engine integration. The original review base is `3c9fda2a35bb3f3154751a46efd09e2448acd205`. No renderer file changes in this ticket.

The harness, the native experiment and the generated shaders stay on branch `ter-dzxf-walk-hill-climb`; main holds this report and its records.

## Scope and reading

The user's starting point is V6 for both kinds of ray, with 256 reflection steps and 62 diffuse steps. The report also measures 64 diffuse steps. A step is one call of the original two-depth traversal. A hit is the integer render-pixel texel reached by a ray. The main gate requires the same hit texel, or the same miss, in the same ray kind for every active ray. It does not require the same step count. Approximations are separate options, never exact winners.

The allocation is 2.94–3.76 ms for the two kinds' tracing under the user's 12 ms deck frame. The obsolete 1.1 ms allocation and the earlier 35–55% target are not inputs. `ter-gayt` starts with one ray per 2×2 block and 10% own rays per kind. The 25% cases are stress cases, not a change to that decision.

Inputs read: the complete ticket and its goal notes; the 2026-11-04 budget and user decisions in `ter-gayt`; [profiling-spike.md](profiling-spike.md); [lean-trace-spike.md](lean-trace-spike.md); the FMA and clock sections of `gpu-calibration.md` from the read-only main Git object; sections 6 and 12 of [the design](../../plans/temporal-renderer.md); and the completed 420-line survey at `/private/tmp/dzxf-survey.md`. The previous run's `pilot`, `climb1`, `climb2`, and interrupted `climb3` were inspected before changing the harness. The checkout initially contained only that uncommitted tool directory, at the expected base. The harness checkpoint is commit `01540e26a830d495ed9e4f475d4341d23ac8a556`.

## Harness and gate

`tools/walk-hill-climb/generate.py` on branch `ter-dzxf-walk-hill-climb` extracts `twoDepthStep`, its structures, and its actual dependencies from the checked-out GLSL. It records source hashes. It does not substitute a synthetic marcher. The baseline uses integer-vector storage and reads the five needed 16-byte fields of each 96-byte dumped ray before the loop. The unused field holds surface parameters that this extracted walk does not consume. It uses the original normal-facing test, reciprocal values, thickness, cap, initial level, and 8×8 block mapping. Own rays retain the captured append order. Removing repeated record loads was already present at the start; it is not a new saving claimed by this ticket.

The four V6 dumps are frame 10 at 1280×720:

| Dump | Active rays, both kinds |
|---|---:|
| deck-10 | 606,080 |
| deck-25 | 866,587 |
| floor-10 | 644,318 |
| floor-25 | 921,446 |
| Total | 3,038,431 |

These are all four saved V6 ray frames in `top/dumps`. Their `frames/` directories contain timing and exposure metadata, not more saved ray records. V1 dumps are not V6 references. Captures were read in place at `/Users/alexispurslane/.t3/worktrees/tertius/ter-sftw-profiling-spike/captures/sftw/top/`; no bulk capture was copied.

The pilot baseline passes the saved trace outcomes on every active ray. State 0 slots have no ray and are ignored. State 2 rays must remain misses. Missing records or outcomes and nonfinite hit coordinates are errors. The gate floors the hit position and applies the renderer's upper viewport clamp before comparing texels. The ray kind and block/own partition are held by the four separate input/output pairs. Here, exact means that hit identity contract: it does not mean bit-identical subtexel coordinates or entry shares.

The inherited gate also required identical step counts. That could reject an exact schedule and was removed before the new climb. It could not let a wrong hit pass. The saved reference has a diffuse cap of 64. For cap 62, a verified baseline-64 hit after step 62 becomes a reference miss; a hit on step 62 stays a hit. A saved miss never becomes a hit. This follows from V6's identical step prefix, not from an experimental variant's count. The baseline-64 outcomes in `pilot/<view>/baseline` supply those counts. New red tests first failed on these rules, then passed. The gate's unit tests also cover changed hit texels, hit/miss changes, inactive slots, finite coordinates, and length mismatches.

### Timing contract

One sample is the GPU timestamp time of the four dispatches—reflection blocks, reflection own rays, diffuse blocks, diffuse own rays—repeated 20 times and divided by 20. A run normally has 60 samples; its median is one frame's walk time. The host warms the GPU for 2 seconds. GPU clocks are sampled beside the host, and only samples between `timed-start` and `timed-end` define the recorded span clock. A few cold samples can remain at the start of that span; its mean, 10th and 90th percentiles, and sample count are retained.

Every timed host process uses `/Users/alexispurslane/Development/tertius/tools/gpu-lock.sh` and `gpu-clock.sh`, with the main checkout's `gpu_clock.py`. The helper owns and releases the lock, including on failure. No lock directory was manually removed. The lock covers one measurement process, including setup and warm-up, not CPU gate checks, source reading, documentation, or the whole sweep. Configuration order reverses in alternate rounds.

`tools/walk-hill-climb/table.py` on branch `ter-dzxf-walk-hill-climb` rejects a failed gate or an unknown clock, requires a span clock within 3% of 1296 MHz, then applies `tools/sftw/kept.py` on that branch: retain runs within 2% of the fastest eligible median. Comparisons use the kept range, not a single fastest sample. Outliers remain in the raw data. The final table states kept/all counts; a pilot with one run is not a replicated win. These are busy shared-machine measurements, not isolated hardware-counter captures.

### Harness-to-trace check

The extracted baseline is faster than the profiling trace, outside the runs' spread. This gap is reported, not calibrated away. The source step is real, but the harness has a smaller caller, fixed inputs, no ray setup or hit shading, and no surrounding frame traffic. The spike's top-clock table is a whole trace over a film; the dumped frame is one fixed frame. Its split-pass walk table was recorded at another clock. There is no directly measured top-clock, same-fixed-frame split-walk reference in those artifacts.

The `trace-caller` experiment adds the real `RayJob`/`walkRay` caller. It is exact but slower than the extracted baseline. Freezing its specialization mode and removing the unused vote/V1 choices did not fix that regression. This establishes caller sensitivity, not the source of every part of the real-trace gap. No harness saving below is called a measured whole-renderer saving. `ter-gayt` must measure the real frame after taking the portable changes.

The cap-64 comparison below uses the `final1` harness batch and the profiling spike's `top/top-table.md`. Times are median [kept minimum, kept maximum], in milliseconds per frame. Both clock ranges are within 3% of 1296 MHz. The difference is too large to call a timing match.

| View | Extracted V6 | Harness / trace span-clock ranges, MHz | Profiling whole trace | Harness below trace |
|---|---:|---:|---:|---:|
| deck-10 | 3.443 [3.442, 3.450] | 1279.1–1292.7 / 1286–1291 | 3.925 [3.920, 3.928] | 12.3% |
| deck-25 | 4.803 [4.803, 4.803] | 1288.8 / 1289–1292 | 5.384 [5.372, 5.409] | 10.8% |
| floor-10 | 3.071 [3.069, 3.073] | 1277.7–1290.5 / 1288–1291 | 3.498 [3.487, 3.519] | 12.2% |
| floor-25 | 4.247 [4.244, 4.248] | 1283.1–1293.2 / 1290–1292 | 4.767 [4.759, 4.830] | 10.9% |

Only one of three deck-25 baseline rounds was kept in `final1`. The independent `final2` baseline is 4.808 [4.797, 4.818] ms at 1283.9–1292.9 MHz, with all three rounds kept. It confirms the gap, not a single-run speed claim.

## Exact candidates

The full generated attempt table and raw per-run samples accompany this report. They include old rounds, new rounds, failed gates, clocks, source hashes, kept labels, and retained ranges. A failed candidate never sets a baseline.

The final record has 224 completed runs in 100 stage/view/variant groups, covering 55 generated shader forms. That includes the two deliberately separate red/green cap-probe runs. Every current form compiles with `glslc --target-env=vulkan1.3 -O -Werror` and passes `spirv-val --target-env vulkan1.3`. Recompiled portable binaries match the measured SPIR-V hashes. The historical pre-fix cap-probe binary remains separately recorded, not overwritten or relabeled as passing.

### Results at both diffuse caps

The fastest measured ordinary GLSL form is `one-image+bounds-predicate+loop-predicate+facing`, with `+cap62` for the user's cap. The native form is `one-image+facing+metal`, also with `+cap62` when selected. All three columns below pass the gate on every active ray, in all four dumps, at both caps. Reflection cap stays 256. These are `final1` medians, followed by kept ranges, in milliseconds per frame. Each bracket in the clock column gives the baseline / GLSL / native span-clock range, in MHz.

| View | Diffuse cap | V6 baseline, ms | Fastest GLSL, ms | Native experiment, ms | Span clocks, MHz |
|---|---:|---:|---:|---:|---|
| deck-10 | 62 | 3.422 [3.418, 3.445] | 2.534 [2.533, 2.570] | 2.481 [2.480, 2.489] | [1280.8–1287.8] / [1279.4–1286.0] / [1275.1–1284.2] |
| deck-10 | 64 | 3.443 [3.442, 3.450] | 2.553 [2.553, 2.554] | 2.499 [2.499, 2.499] | [1279.1–1292.7] / [1283.7–1290.1] / [1282.7–1287.1] |
| deck-25 | 62 | 4.756 [4.752, 4.757] | 3.450 [3.447, 3.452] | 3.418 [3.417, 3.423] | [1288.2–1293.9] / [1279.4–1283.8] / [1285.4–1287.0] |
| deck-25 | 64 | 4.803 [4.803, 4.803] | 3.478 [3.475, 3.482] | 3.447 [3.445, 3.449] | [1288.8] / [1285.8–1292.1] / [1280.7–1289.2] |
| floor-10 | 62 | 3.042 [3.039, 3.044] | 2.262 [2.259, 2.295] | 2.206 [2.203, 2.210] | [1276.2–1285.3] / [1267.3–1289.1] / [1268.1–1290.2] |
| floor-10 | 64 | 3.071 [3.069, 3.073] | 2.278 [2.275, 2.316] | 2.223 [2.219, 2.246] | [1277.7–1290.5] / [1266.4–1284.2] / [1274.7–1290.1] |
| floor-25 | 62 | 4.206 [4.206, 4.253] | 3.062 [3.058, 3.063] | 2.999 [2.999, 3.000] | [1289.2–1296.0] / [1277.2–1284.9] / [1283.4–1284.6] |
| floor-25 | 64 | 4.247 [4.244, 4.248] | 3.084 [3.084, 3.087] | 3.024 [3.021, 3.026] | [1283.1–1293.2] / [1277.1–1288.2] / [1290.5–1291.1] |

At cap 62, ordinary GLSL cuts the extracted time by 25.6–27.5%. Native source saves a further 0.032–0.062 ms, or 0.9–2.5%, against that GLSL. The native and GLSL kept ranges do not overlap. Rounded equal endpoints above do not mean identical raw times. [The timing table](walk-hill-climb/timings.md) gives six decimals and kept/all counts for every batch. [The attempt log](walk-hill-climb/attempts.md) gives every decision, including failures and regressions; [measurements.json](walk-hill-climb/measurements.json) retains all samples and rejected rounds.

The two-image combination `bounds-predicate+loop-predicate+facing` keeps the original depth layout and allocation. It is the conservative portable handoff. `final2` independently alternates it with the corresponding baseline. All kept ranges are disjoint from their baseline ranges; both caps pass all four dumps. These times include no one-image allocation change.

| View | Cap | Two-image baseline, ms | Two-image combination, ms | Baseline / combination span clocks, MHz |
|---|---:|---:|---:|---|
| deck-10 | 62 | 3.429 [3.416, 3.431] | 2.674 [2.671, 2.679] | 1275.2–1288.2 / 1276.5–1285.8 |
| deck-10 | 64 | 3.449 [3.448, 3.453] | 2.694 [2.690, 2.704] | 1280.7–1295.5 / 1272.1–1283.0 |
| deck-25 | 62 | 4.764 [4.758, 4.820] | 3.642 [3.637, 3.698] | 1290.3–1291.9 / 1285.7–1287.9 |
| deck-25 | 64 | 4.808 [4.797, 4.818] | 3.671 [3.666, 3.673] | 1283.9–1292.9 / 1279.0–1290.5 |
| floor-10 | 62 | 3.062 [3.041, 3.076] | 2.395 [2.391, 2.398] | 1276.4–1290.1 / 1269.6–1292.8 |
| floor-10 | 64 | 3.076 [3.070, 3.082] | 2.414 [2.414, 2.420] | 1288.8–1295.3 / 1284.0–1290.6 |
| floor-25 | 62 | 4.206 [4.203, 4.227] | 3.233 [3.231, 3.238] | 1282.1–1291.5 / 1275.4–1291.1 |
| floor-25 | 64 | 4.246 [4.245, 4.246] | 3.266 [3.265, 3.267] | 1281.1–1284.8 / 1277.2–1285.7 |

This conservative combination cuts cap-62 time by 21.8–23.6%. The extra one-image gain is real on this Mac, but has a bandwidth tradeoff described below. Neither result proves the integrated renderer's frame budget.

### What the small portable changes do

The candidate named `one-image` changes the original image read to:

```glsl
        cellBounds = texelFetch(bounds, ivec2(cell), walk.level).rg;
```

where

- `cellBounds` is the pair of nearest and farthest inverse depths for this cell.
- `bounds` is one RG32F image containing every V6 level.
- `cell` is the two-dimensional integer-valued cell coordinate at this level.
- `walk.level` is the current pyramid level, with level zero at one render pixel.
- `texelFetch` reads one texel at an explicit integer mip level; `ivec2` converts the coordinate to signed integers; `.rg` selects its two channels.

Mip zero duplicates the exact center inverse depth into both channels. Higher mips are the original two-depth bounds without recomputation or rounding. The host pads mip zero to 2048×1024 for these captures, so the existing 1024×512 bounds image becomes mip one. Padding is not read by active rays. This removes a per-lane choice between two image reads; it does not add a second depth fetch. It needs about 21.33 MiB for the full RG32F chain instead of about 8.85 MiB for the separate finest and bounds images: about 12.48 MiB more resident allocation for this fixed layout. It also doubles level-zero's texel payload from R32F to RG32F. Do not silently replace the engine's pyramid or change its other readers' mip meanings.

`bounds-predicate` uses this original arithmetic with a vector Boolean combination:

```glsl
    return any(bvec2(any(lessThan(cell, vec2(0.0))), any(greaterThanEqual(cell * side, frame.viewport.xy))));
```

where

- `cell` is the current cell coordinate; `side` is its width in render pixels.
- `frame.viewport.xy` is the active render width and height in pixels.
- `lessThan` and `greaterThanEqual` compare the two axes, `bvec2` forms a Boolean pair, and `any` reports whether at least one component is true.

`loop-predicate` similarly combines the original loop conditions without short-circuit control flow:

```glsl
    while (all(bvec3(walks, result == WalkOn, walk.steps < push.maxSteps)))
```

where

- `walks` says whether the captured ray state requests a walk.
- `result` is the previous step's result; `WalkOn` means neither hit nor terminal miss.
- `walk.steps` counts completed original steps; `push.maxSteps` is the dispatch's limit.
- `bvec3` forms a Boolean triple and `all` requires all three conditions. The cap-62 form substitutes the local `maximumSteps` without rounding the cap upward.

`facing` changes the final line of octahedral normal decoding to:

```glsl
    return n;
```

where `n` is the decoded, unnormalized three-dimensional surface normal. Its only consumer here tests the sign of the dot product with the ray direction. Positive normalization is mathematically unnecessary for that sign. Floating-point rounding near zero is not an identity proof; acceptance is the measured all-ray gate, not that algebraic argument. This change is local to the facing test, not to shading normals elsewhere.

### Portability and expected costs

| Change | Vulkan requirement and NVIDIA/RADV/ANV reasoning |
|---|---|
| Vector Boolean bounds and loop predicates | Core Boolean/vector instructions, unchanged operands, no optional subgroup feature: their expected cost is neutral or lower on NVIDIA, RADV and ANV because they remove short-circuit branches without extra depth reads. Compiler-specific code-shape regressions still require measurement. |
| Unnormalized sign-only facing test | Core FP32 arithmetic with fewer operations and no new storage or reads: there is no identified execution-cost increase on NVIDIA, RADV or ANV. The fixed-frame gate remains required for boundary rounding. |
| One RG32F pyramid | Core sampled RG32F already used by V6, one fetch instead of a divergent resource choice: instruction cost should not rise on NVIDIA, RADV or ANV. The larger level-zero payload and allocation are a plausible cache/bandwidth cost, so absence of Linux performance regression is not established by this Mac spike. Preserve the two-image result as the conservative comparison. |

No portable winner requires FP16 arithmetic, subgroup-size control, subgroup vote, quad operations, vendor assembly, or hardware ray queries. The report does not turn a portability argument into a claim that NVIDIA, RADV, ANV or lavapipe was physically timed here.

### Scheduling bounds and independent work

`tools/walk-hill-climb/order.py` on branch `ter-dzxf-walk-hill-climb` keeps every 96-byte record with its own saved outcome and writes an invertible original-slot permutation. Depth and normal images are symlinks to the original captures. The oracle sorts by verified baseline step count; the cheap key uses projected ray length from the captured ray, with no future count. Both are offline experiments. Their GPU time excludes key construction, sorting, scatter, and queue/dispatch costs. They are not deployable exact winners. A real implementation must retain original sample identity and scatter hits back before shading.

The `pair` candidate explicitly carries two independent ray states per thread and interleaves their original steps. It uses linear slot mapping and safely handles an odd last slot. It also leaves half the original dispatch groups empty rather than changing the host dispatch. Its comparison must include that overhead and the changed grouping. This differs from expanding two dependent steps of one ray.

The ordering runs below use deck-10 at cap 64 and the fastest ordinary GLSL body. Each passes all 606,080 active rays; all three rounds are kept. Times exclude ordering and result scatter.

| Offline key | Launch mapping | Median [range], ms | Span clocks, MHz |
|---|---|---:|---:|
| Actual baseline step count, an oracle | Original 8×8 blocks | 2.539481 [2.524771, 2.547461] | 1271.3–1288.6 |
| Actual baseline step count, an oracle | Linear slots | 2.362531 [2.360599, 2.366235] | 1279.4–1286.0 |
| Projected ray length | Original 8×8 blocks | 3.136029 [3.120650, 3.136592] | 1283.6–1289.9 |
| Projected ray length | Linear slots | 3.119794 [3.116680, 3.123534] | 1276.5–1290.7 |

Even perfect future-length information buys only about 0.19 ms against the unsorted 2.553 ms result, before every ordering cost. A cheap available key loses time. This does not rule out every persistent scheduler, but it does not justify adopting a global sorting pass. The two-ray `pair` form takes about 5.300 ms in its diagnostic run and is dropped; its exact clock and range are in the attempt log.

### What the negative results establish

Explicit 2× and 4× step expansion, masked expansion, subgroup-coherent termination, constant cap branches, level tables, fixed initial state, integer bounds, flat block order, folded slab work, moved live state, alternate caller structure, and arithmetic rewrites were measured rather than assumed beneficial. The attempt table distinguishes exact regressions from changed-hit failures. In particular, explicit FMA changes hit texels and is not an exact optimization. No FMA approximation is offered without image measurements.

Cap-62 confirmation also rejects explicit expansion: 2× takes 4.549594 ms at 1288.7 MHz and 4× takes 4.804476 ms at 1291.7 MHz. Both pass the gate. Each is one diagnostic round, so its kept range has one point, not evidence of repeatability. The regressions are much larger than the confirmed rolled-loop spread.

A final review found that combining `split-cap` with `cap62` tested the already reduced cap against 64, selecting the 256-step branch for diffuse rays. The existing every-ray integration test first fails on 18,699 diffuse outcomes. The repair compares against the chosen diffuse cap; the same test then passes all 606,080 rays. Its 3.826614 ms at 1284.3 MHz is still slower, so it remains dropped. The failing pre-fix batch is retained separately as `cap62-split-red`, with its source hash; it is not a failed winner or a quality option. This is a repair to an experimental probe, not a change to any measured winning source.

The pinned SPIRV-Cross revision's MSL block-hint emitter is empty. An unroll hint is not an actual expanded loop here. Explicit expansion did survive compilation, but made the texture-dependent walk slower. The arithmetic-only calibration's 0.41→0.77→0.93→0.97 rates for 1/2/4/8 FMA steps do not predict this traversal's speedup.

## Approximation track

The measured option is original V6 with a diffuse cap of 54, against the user's cap of 62. Reflection cap stays 256. This is not in the main line: some diffuse hits reached after step 54 are removed. The image comparison reuses the spike's 1024-draw converged deck and floor captures without rendering different random samples. Both tile bias and FLIP are measured against V0 and V1. Tile bias is the signed difference of mean 16×16-pixel tile luminance divided by V0's mean; the accompanying absolute difference is the mean absolute tile difference on the same scale. FLIP is the perceptual error on the saved display-mapped RGB PNGs, not a proof about HDR lighting energy.

The cap-54 GPU times are the profiling spike's replicated, GPU-locked top-clock trace measurements, not optimized-harness estimates. Its `top-table.md` retains clocks and spreads. The comparison should not add the cap saving to the new shader saving as though they were independently measured in the engine.

| View | Cap-54 whole trace, median [range], ms | Span clock, median [range], MHz | Kept/all |
|---|---:|---:|---:|
| deck-10 | 3.779 [3.775, 3.786] | 1289 [1289, 1290] | 6/8 |
| deck-25 | 5.173 [5.169, 5.180] | 1292 [1291, 1292] | 5/8 |
| floor-10 | 3.346 [3.335, 3.361] | 1289 [1285, 1291] | 7/8 |
| floor-25 | 4.538 [4.529, 4.542] | 1290 [1286, 1290] | 6/8 |

The image comparisons below are for the diffuse image. Each bias pair is signed / absolute, in percent of V0 mean luminance. Even the difference against V1 uses V0's mean as its denominator. Each FLIP pair is mean / maximum, on its 0–1 scale; lower is closer. Cap 62 and 64 are controls, not approximation recommendations. All 24 reflection/diffuse FLIP comparisons are retained in [approximation-flip.json](walk-hill-climb/approximation-flip.json). The original bias output is [approximation-bias.txt](walk-hill-climb/approximation-bias.txt).

| View | Diffuse cap | Tile bias vs V0, % | Tile bias vs V1, % | FLIP vs V0, mean / maximum | FLIP vs V1, mean / maximum |
|---|---:|---:|---:|---:|---:|
| deck | 54 | −9.5 / 9.6 | +0.1 / 1.7 | 0.05074 / 0.98254 | 0.01932 / 0.53089 |
| deck | 62 | −7.5 / 7.7 | +2.0 / 2.4 | 0.03959 / 0.98247 | 0.01775 / 0.78947 |
| deck | 64 | −7.1 / 7.3 | +2.4 / 2.7 | 0.03767 / 0.98246 | 0.01783 / 0.81228 |
| floor | 54 | +1.4 / 1.5 | +0.8 / 0.9 | 0.02313 / 0.27441 | 0.01763 / 0.21300 |
| floor | 62 | +0.8 / 0.8 | +0.2 / 0.3 | 0.01556 / 0.27437 | 0.00841 / 0.15981 |
| floor | 64 | +0.7 / 0.7 | +0.1 / 0.2 | 0.01422 / 0.27439 | 0.00643 / 0.16072 |

The reflection cap does not change, and its image measures are identical across these diffuse caps. Deck reflection bias is −6.2 / 7.3% against V0 and +0.3 / 0.3% against V1; FLIP is 0.03168 / 0.72447 against V0 and 0.00470 / 0.40659 against V1. Floor reflection bias is +1.6 / 1.8% against V0 and −0.1 / 0.1% against V1; FLIP is 0.02235 / 0.39093 against V0 and 0.00233 / 0.20499 against V1.

Visual review opened V1, cap 54 and its FLIP heat map for both views, plus deck V0. Deck cap 54 has a localized bright patch in the right cabin bay against V1, despite similar mean brightness. V0 is broadly brighter there, so difference from V1 alone does not identify physical error. Cap 54 loses more light against V0 than cap 62. Floor cap 54 looks close, but changes smooth wall/floor regions and edges; its mean FLIP against V1 is about twice cap 62's. The large deck maximum against V0 is shared across caps and cannot be attributed solely to cap 54. No reference image was approved or replaced.

Cap 54 remains an optional quality/time tradeoff, not the user's selected cap and not a main-line optimization. The accidental changed-hit FMA candidate is a failed exact experiment, not a second quality recommendation: it is slower, changes two reflection rays, and has no measured image cost. No unmeasured coarser/thicker variant is offered as an option.

## Mac-only Metal experiment

On 2026-11-04 the user chose portable only, so no Mac-only Metal path is adopted; the experiment stays on branch `ter-dzxf-walk-hill-climb`.

The host still creates a Vulkan shader module, descriptors, pipeline, command buffer and timestamps. There is no second renderer. `SolShaderModule.CodeForPlatform` is the single platform-selection point: an explicitly supplied native source can replace SPIR-V only on macOS; every other case returns the original SPIR-V. Unit tests cover both platform outcomes, source packaging, NUL termination and uint alignment. GPU gates cover the actual native dispatch, not just that wrapper.

MoltenVK 1.4.2's private format accepts a four-byte header `0x19960412` followed by NUL-terminated UTF-8 Metal source, or `0x19981215` followed by a compiled Metal library. The implementation really loads these payloads. Both the header and source state that direct Metal is not officially supported. For compute, the deprecated `vkSetWorkgroupSizeMVK` is needed because the module has no SPIR-V workgroup metadata. That API explicitly warns that the loader/layers framework does not support it.

This experiment loads the already selected ICD's library from `VK_ICD_FILENAMES`, obtains the setter export, and calls it on the non-dispatchable module handle. That empirically works with this pinned loader/ICD and validation disabled. It does not make the undocumented integration supported. Normal Vulkan validation may reject the non-SPIR-V payload. It must never be sent to NVIDIA, RADV, ANV or lavapipe.

`tools/walk-hill-climb/native_walk.metal` on branch `ter-dzxf-walk-hill-climb` is a hand-written scalar V6 walk with the captured block mapping, two-depth cell tests, caps, facing test and entry share. Its argument-buffer IDs and push-constant buffer index match the generated MSL interface of this exact pipeline. They are not a stable Vulkan ABI. The native-facing variant removes only the normal normalization, as in the portable candidate. Source hashes record the actual payload.

The direct path adds 134 lines of shader plus a 51-line payload/ICD helper and 39 lines of packaging tests, as well as the small host/generator changes. Keeping it requires a second shader kept in semantic agreement, descriptor-layout and MoltenVK-version checks, platform packaging, and the same GPU reference tests after changes. A compiled library would mainly remove startup compilation for the same shader, not improve its GPU execution. Offline Metal compilation was attempted but unavailable: `xcrun metal` reports a missing Metal Toolchain. No global toolchain was installed and no large component was downloaded. Runtime source compilation succeeded. Shipping this path is explicitly a user decision, not part of this spike's engine changes.

## Prediction and limits

The machine is a 24-core M1 Max, with 3072 FP32 lanes, not the 32-core/4096-lane model used in one profiling derivation. That mistaken derivation scales lane-cycle figures by 4/3; it does not change GPU timestamps. The fixed-frame model counts the longest baseline ray per 32-lane M1 subgroup, including the actual 8×4 portion of each 8×8 block group. It clamps diffuse step counts to the selected cap. It does not infer a new instruction count from elapsed time.

For comparison with the calibration's rough model, use 40 lane-operations per step at 0.41 lane-operations per lane-cycle, 3072 lanes and 1296 MHz. This is the calibration's convention, not 40 independently issued real instructions measured from the shader. Texture dependencies, branches, occupancy, integer work, normal reads and dispatch gaps are not separately predicted. If the 40 is instead interpreted as individual FLOPs at two per FMA, the arithmetic floor halves. Neither estimate proves attainable runtime.

| View | Model at cap 62, ms | Model at cap 64, ms | Best GLSL / model at cap 62 |
|---|---:|---:|---:|
| deck-10 | 1.333051 | 1.344495 | 1.90× |
| deck-25 | 1.935307 | 1.951981 | 1.78× |
| floor-10 | 1.168371 | 1.180962 | 1.94× |
| floor-25 | 1.700486 | 1.718981 | 1.80× |

[fixed-frame-model.json](walk-hill-climb/fixed-frame-model.json) records the subgroup counts and formula inputs. At deck-10 cap 62, baseline is 2.57× the model, the conservative two-image combination is about 2.01×, and native is 1.86×. The measurements reduce the gap but do not reach an arithmetic bound.

An additive budget projection, not an engine measurement, is 3.90 − (3.421760 − 2.533515) = 3.012 ms for deck-10 cap 62. It fits 3.76 ms, but not the strict 2.94 ms end of the estimated allocation. Applying the same projection to the conservative two-image pair gives about 3.144 ms. Caller and surrounding-frame effects can change both savings. The 12 ms frame target is not proven by this spike.

The remaining gap to that estimate is not all recoverable compiler overhead. This report does not claim a global optimum. Published ray scheduling, conservative lookahead and wavefront methods remain research leads, not measured wins in this shader. A first-hit-preserving replacement must also preserve the finite-cap prefix or pass the fixed-ray gate; full-length visibility equivalence alone is insufficient.

## Research disposition

### Check the source's situation before adopting its method

The original Aila–Laine 2009 paper was downloaded from NVIDIA and read directly, including its test setup and persistent-thread sections. It used an NVIDIA GTX285, CUDA 2.1, 192-thread blocks, 80K–282K triangle scenes, a 3D bounding-volume hierarchy, and 1024×768 output. It traced 32 AO or diffuse rays per pixel—about 25.2 million rays for each secondary-ray test. Diffuse rays were very long closest-intersection queries; its tracing-only comparison did not depend on a denoiser. Its persistent threads targeted that generation's CUDA work distributor.

Ours is a 24-core M1 Max, about 0.61–0.92 million active rays of both kinds together, a 2D depth hierarchy, and short finite caps whose exact hit identity must survive. We cannot import the paper's hardware-distribution diagnosis or speedup. The applicable question is only whether grouping different walk lengths can reduce inactive-lane time. That is why the next experiment is an offline ordering bound, not adoption of its persistent scheduler.

The constant cost matters at this size. A GPU sort requires key generation, histogram/prefix work, several memory passes, original-ray identities, and extra dispatches. A materialized scatter of the deck-10 harness's roughly 0.63 million 96-byte slots alone reads and writes about 122 MB; an index-only design avoids that copy but adds indirect record reads. A comparison sort is O(N log N); a radix sort is O(PN) for P digit passes, not zero-cost O(N). Any saved walk time must exceed all that work and the cost of splitting the engine's fused setup/walk/shading. An offline oracle time is not a net frame result. No paper-based scheduler is adopted here.

The survey's Pikulsky 2025 proposal has a different valid-query/quality contract and an RTX 2080 Super timing, not this exact M1 fixed-ray workload. A fresh attempt to retrieve that publisher's page and PDF hit redirect loops, so this run does not claim a new reproduction of that paper. Its multi-sample idea remains an unimplemented lead. The unavailable source detail and the finite-cap mismatch rule out adopting it merely from its published time. The direct Metal experiment is different: it uses the exact same local V6 inputs and measures its constant ABI/upkeep cost explicitly; it is not a borrowed traversal algorithm.

| Family | Evidence and disposition |
|---|---|
| Perspective-correct DDA and Hi-Z traversal | [McGuire–Mara 2014](https://jcgt.org/published/0003/04/04/) and [AMD SSSR](https://gpuopen.com/manuals/fidelityfx_sdk/techniques/stochastic-screen-space-reflections/) explain traversal and empty-cell skipping. V6 already has hierarchical two-depth skipping; replacing its traversal order can change finite-cap hits. |
| Ray ordering and wavefront queues | [Aila–Laine 2009](https://research.nvidia.com/publication/2009-08_understanding-efficiency-ray-traversal-gpus), [Laine–Karras–Aila 2013](https://research.nvidia.com/publication/2013-07_megakernels-considered-harmful-wavefront-path-tracing-gpus), and [Meister et al. 2020](https://doi.org/10.1145/3384382.3384534) motivate the ordering bound. The earlier persistent-lane spike's bad partition does not disprove the family. Net queue cost must be included before adoption. |
| Multi-sample lookahead and gather | [Pikulsky 2025](https://visnyk.vntu.edu.ua/index.php/visnyk/en/article/view/3227) samples several current-mip cells. V6 can descend or hit at the first cell, so speculative reads must commit in original order. GLSL gather returns a single component over a 2×2 footprint, not arbitrary cells at different mips; it is not a drop-in four-step fetch. No speculative-packet gain is claimed here. |
| FP16 and coarser/slab exits | FP16 arithmetic is optional, stored half depths can change the gate, and coarser exits change hits. The existing R16F probe was neutral. No such approximation is put in the exact winner. |
| Spatial/quad sharing and mip queues | Rays' cell and level diverge. Sharing needs matching addresses and a fallback; optional quad/subgroup features cannot become portable requirements. Global queues add traffic and passes. These remain unmeasured, not silently rejected as impossible. |
| Other visibility models | Visibility bitmasks, horizon AO, screen probes, planar reflection and Newton refraction solve different queries. They are not the same per-ray first-hit result for GI and reflections. The survey's newer papers do not prove an exact V6 speedup. |
| Native input and compiler shape | [MoltenVK's private header](https://github.com/KhronosGroup/MoltenVK/blob/v1.4.2/MoltenVK/MoltenVK/API/mvk_private_api.h), [shader-module source](https://github.com/KhronosGroup/MoltenVK/blob/v1.4.2/MoltenVK/MoltenVK/GPUObjects/MVKShaderModule.mm), [deprecated setter](https://github.com/KhronosGroup/MoltenVK/blob/v1.4.2/MoltenVK/MoltenVK/API/mvk_deprecated_api.h), and [pinned SPIRV-Cross](https://github.com/KhronosGroup/SPIRV-Cross/blob/6c09849fe88c48eaed08413aa022aaa136a3a057/spirv_msl.cpp) support the exact mechanism and its limitations. No supported raw Apple-ISA deployment route was found. |

Downloaded sources stayed inert in new folders under `/private/tmp`. No downloaded program was run. The survey is a map of candidates, not evidence that every candidate was implemented or measured.

## Reproduction and handoff

Use `nix develop --command bash -c 'COMMAND'` for builds and tests. On branch `ter-dzxf-walk-hill-climb`, build `tests/Tertius.Rendering.Tests` in Release, then use `tools/walk-hill-climb/README.md` on that branch. The clock executable directory is `/private/tmp/ter-dzxf.vv9W25/bin`, not its `gpu-clock` executable. The absolute capture root is given above. Generated shaders, full outcomes, native payloads, clock logs, red/green logs and heat maps stay in `/private/tmp/ter-dzxf.vv9W25`. The committed measurement records make the report's tables reproducible without another GPU run.

`ter-gayt` takes the original V6 two-depth walk for both kinds, reflection cap 256 and diffuse cap 62, and specifically the two-image `bounds-predicate+loop-predicate+facing` combination. These are the vector Boolean bounds test, vector Boolean loop condition and sign-only facing normal shown above. Keep the sign-only change local, preserve exact arithmetic order, and preserve the verified integer-words input form where a split replay reads records. The fastest ordinary GLSL one-image form remains a measured follow-up: adoption must preserve mip-to-cell meaning and exact center depth, measure its construction cost and Linux bandwidth cost, and audit other pyramid readers. It is not an unconditional cross-driver performance claim. Do not copy the fixed-dump harness, its hard-coded state probes, offline oracle order, or a failed arithmetic rewrite into the engine. Keep the portable shader as the reference. Measure the actual busy frame at the top clock before claiming that the 12 ms target is met.

The unresolved fork is whether to adopt the Mac-only private-API native path. Keeping SPIR-V alone avoids a second shader and unsupported ABI dependency. Adding native source buys the measured extra 0.032–0.062 ms per captured frame, with the 224 source/test lines and upkeep described above. The recommendation is to take the portable change first. This spike measures both choices but implements neither choice in the engine; the user owns adoption. The stage therefore reports `done=false` with that fork, even after its experiments and checks finish.

On 2026-11-04 the user chose portable only, so no Mac-only Metal path is adopted; the experiment stays on branch `ter-dzxf-walk-hill-climb`.

## Final checks and retained evidence

Every owned measurement, build and test process finished before the stage result. The full Checked solution ran first, followed by the full Release solution, on the final implementation. Only Markdown documentation changed afterward.

| Check | Result |
|---|---|
| `python3 -m unittest discover -s tools/walk-hill-climb -p "test_*.py"` in the dev shell, on branch `ter-dzxf-walk-hill-climb` | 23 passed. Red/green evidence is committed in `walk-hill-climb/red-green.txt`. |
| All 55 current generated GLSL forms | Compile with the engine's Vulkan 1.3 flags and pass `spirv-val`; portable measured hashes match. The four final GLSL forms require only core `Shader` capability. |
| Portable and native GPU outcomes, caps 62 and 64 | All 3,038,431 active rays of the four saved V6 frames pass, in each of three final alternating rounds. |
| Native packaging and platform choice | Four tests pass, including non-Mac SPIR-V retention. The full suites exercise these tests too. |
| Approximation image measures | All 24 FLIP comparisons complete; tile bias completes for both views and both kinds. |
| `dotnet test --solution Tertius.slnx -c Checked` in the dev shell | Passed: 10,160 succeeded, 18 skipped, zero failures; 4m 36s. |
| `dotnet test --solution Tertius.slnx -c Release` in the dev shell | Failed: 10,146 succeeded, 18 skipped, one failure; 4m 53s. The sole failure is the unchanged empty-pass GPU timing test below. |
| Focused Release `GpuTimingTests`, under the main GPU-lock helper | 12 passed, one skipped, the same one failed; 5s. |
| Offline `xcrun metal` | Unavailable: the Metal Toolchain is missing. Runtime Metal source compilation and dispatch succeed. |
| Linux runtime/performance | Unavailable on this Mac. Core SPIR-V validation and platform-fallback tests do not replace driver testing. |

The Release failure is `GpuTimingTests.APassThatRecordsNothingAddsNearlyNoTimeBetweenTheEndOfThePassBeforeItAndTheEndOfThePassAfterIt`, at `tests/Tertius.Rendering.Tests/GpuTimingTests.cs:302` on branch `ter-dzxf-walk-hill-climb`. It finds an extra 0.078917 ms against the 0.05 ms bound. The focused GPU-locked retry finds 0.075667 ms against the same bound. This is the existing open `ter-abun` issue, also recorded in the profiling ticket, not a changed walk test or snapshot failure. It recurs even under the lock, so this report does not attribute it solely to concurrent measurements. No engine file or this test changed; the bound was not weakened. The failed Release run is not described as a passing suite.

Full logs are `/private/tmp/ter-dzxf.vv9W25/final-checked.log`, `final-release.log`, `release-timing-retry.log` and `final-python.log`. Red/green cap-probe logs, all 224 raw runs, generated shaders, GPU clocks, per-ray outcomes and approximation heat maps remain in the same scratch folder. Duplicate current-summary/table and survey-read files were removed. The retained scratch is about 2.6 GiB, plus 1.8 MiB of inert primary-paper files in `/private/tmp/ter-dzxf-reading.7YZF56`. The committed measurement records occupy about 820 KiB. About 19 GiB of disk space remains free. Original capture images were never copied or changed, and the researcher's survey was left intact.

Coverage and mutation testing are deliberately left to the orchestrator. No ticket is closed and no merge or PR is made here.
