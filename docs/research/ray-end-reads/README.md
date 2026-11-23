# What a ray's end reads: resolved, prefiltered and surface-shared hit light (ter-pdw8)

An experiment for the structural step ter-6fka, in the ray census's probe build
([../ray-census/README.md](../ray-census/README.md)). It changes no engine code on main.

**Base: every number in this record is on 3549d3283, before Design X.** That commit's engine code equals main 998165d42.
Design X changes what the diffuse accumulator does with the samples (the bounce light's evidence per 8 × 8 block, the
retained deviation, the region drift test, the 8-draw restart and the variance fix in the clamp). So the kept shares
and the picture measures below describe main's old accumulator. The probe build stays on this base.

## 1. The answer in short

- **The general mottle is not a fault of what a ray's end reads.** A temporal resolve at the hit pixel (stand-in (c) of
  V1) does not lower bay-sunbeam's far-hit variance: factor x1.00 to x1.02 at a fixed exposure. A prefilter (V2)
  changes it there by x0.41 to x3.07 only by mixing surfaces that the receiver cannot see, and it loses 29 % to 55 % of
  the bounce light on three of bay's four regions.
  So the general mottle needs a far-field store or better sampling (more guided rays). A resolved read is a remedy of
  the tail only: the specks.
- **The specks come from one surface thinner than a pixel.** In one jitter phase of 25 at 1072 × 603, render row 314
  shows the sill's sunlit face (direct light 162). In the other 24 phases it shows the sky. A ray that hits that pixel
  in that draw reads 162 in place of about 0.2.
- **The guide is part of the fault.** In V0, guided rays are 0.25 of all diffuse samples but 0.906 of the samples above
  5. The guide aims at the bright row, and the hit then reads the row's unfiltered light.
- **A per-pixel resolve of the hit light leaks.** V1, V2 and V3 put the sill's light on row 314's pixel at the depth of
  whatever the pixel shows in the other phases. Rays from the floor at the far wall's foot then read light that the
  sill cannot send them: pale flecks at 1:1. The upscaler's output also holds one value per pixel for two surfaces, so
  the real V1 would leak the same way.
- **The candidate is the surface share S2'.** The direct part of a pixel's hit light counts in full where the diffuse
  history's surface test holds a history at that pixel, and with 1 / P (P = the jitter's phase count) where it does not.
  The guide reads the same value. S2' needs no new image and no new state. It passes the orchestrator's rule on every
  still that it ran:
  - It removes the specks at both lamp hall sizes (capture specks: largest 4, against V0's 180).
  - It adds no leak or halo at 1:1 in any of its 19 crops.
  - It keeps every region's mean within 1 % of V0's: −0.6 % to 0.0 % in all 21 regions (the lamp hall at both sizes,
    bay at 2/3 and the two cabins).
  - It shows no fade or darkening in two films with motion: every region stays within 0.21 % of V0 in every frame
    (section 9.2).
- **The half-float trap.** A running mean in an rgba16f image with an update weight of 1/64 loses a small change below
  half a step of the format. The bounce loop compounds the loss: the old stand-in ran 7 % to 9 % dark. Any later mean in
  a half-float image needs a weight of about 1/8 or a float32 image.
- **Short diffuse histories in a still have a writer: the anti-lag** (ticket t1-g1jd). 8.1 % of the lamp hall's surface
  pixels hold a diffuse history shorter than 25 draws in a still. With the diffuse anti-lag off, 0.027 % do.

## 2. Terms

- **Hit light**: the light that leaves a hit pixel toward a ray of one kind (diffuse or reflection). The plan writes it
  per pixel (`leavingLightOf` in `screen_trace_plan.comp`), and a ray reads it at its hit pixel (`hitLightOf`). On this
  base it is this draw's direct part (the pixel's direct light times its material factors, plus its glow) plus the last
  draw's history part.
- **Direct part, history part**: the two terms of the hit light above.
- **Phases**: the jitter's phase count, `(int)(8 · (1920 / render width)²)`: 25 at 1072 × 603, 27 at 1040 × 585, and 18
  at 1280 × 720.
- **Diffuse sample X**: the luminance of one pixel's diffuse sample in one draw, after the caps, as the ray census
  defines it. Units: pre-exposed linear light.
- **Far hit**: a hit at 2 m or more (the census's classes: miss, near hit, far hit).
- **Var**: the mean over a region's pixels of each pixel's variance of X over the 400 counted draws.
- **Far var**: Var times the far hits' share of the variance (the sum of Cov(X_far, X) over the sum of Var(X)).
- **Far-hit variance factor** (the tables' "Factor vs V0"): V0's far var over the variant's far var, per region. x2.11
  means that the variant's far-hit variance is less than half of V0's. Above 1, the variant lowers it.
- **Rel factor**: V0's Var / mean² over the variant's. No exposure moves it.
- **Bias**: the region's mean of X over V0's mean, minus 1. **Radiance bias** undoes each draw's pre-exposure first. At
  a fixed exposure it equals the bias.
- **Check, or kept share**: the region's census mean of X over the mean of the slow diffuse history at the last draw.
  1.00 means that the accumulator keeps what it receives. Above 1, it keeps less (ter-18hc).
- **Keep rectangle**: a rectangle of output pixels whose samples the census writes one by one. In the lamp hall it is
  [643, 200, 1043, 400] around the near ceiling lamps, and [885, 577, 1040, 594] for the floor at the far wall's foot.
- **Mean specks**: pixels of the keep rectangle whose per-pixel mean of the samples stands more than 25 % above the
  median of its 7 × 7 neighbors. An accumulator cannot average such a speck away.
- **Capture specks**: the specks of the speck counter's rule (t7-3e22's `specks.py`) in each of the take's 20 shown
  captures. The tables give their median and largest count.
- **Grain, daub, boil, level** (the shown picture, `pics.py`): measured on capture c-s09 against the take's own
  reference (the mean of captures c-s08 to c-s19), in display-linear luminance.
  - grain = the RMS of the relative error finer than about 4 output pixels.
  - daub = the 90th percentile of the relative error between 3 px and 36 px blurs.
  - boil = how much the soft blobs move from capture to capture.
  - level = the variant's reference over V0's, minus 1.
- **holdsHistory**: the plan's existing surface test of the diffuse history at a pixel: the reprojected history is the
  same surface. It is false for a pixel that shows a new surface this draw.
- **Stand-in**: a probe-only substitute for a variant that the probe build cannot reach. Every table marks it.

## 3. Method

1. The probe build is a plain copy of 3549d3283 (git archive, not a checkout) with the census probe
   (`../ray-census/census-probe.patch`) and the steps `tools/apply-rayend.py` to `tools/apply-rayend6.py`, applied in
   order. `probe.patch` is the whole build copy's diff against 3549d3283. It never entered `src`.
2. Specialization constant 40 (`rayEndVariant`, from `TERTIUS_PROBE_RAY_END`) selects the variant as a bit mask. The
   variants of section 4 are its values.
3. A take is one census still (`tools/census.py`): 240 settling draws, then 400 counted draws (240 to 639), clamps on,
   20 continued captures of 33 draws. The census sums per pixel as in the ray census.
4. Exposure: the lamp hall at a fixed 4.08 stops (the speck films' exposure). bay-sunbeam first ran at the automatic
   exposure. Stand-in (c)'s mean is not rescaled when the exposure changes, so its automatic exposure swung by ±0.17
   stops there (V0: ±0.005). All later bay and cabin takes use a fixed exposure: V0's automatic mean (bay −4.089 at 2/3,
   cabin-light-cabin −7.812, exit-cabin −0.397).
5. Each take runs through `tools/queue-share.sh` (earlier `queue-cause.sh` and `queue-rest.sh`): the census, then
   `lamp.py`, `analyze.py`, `sill.py`, `premise.py` and `pics.py`, then the raw data is deleted before the next take.
6. `tools/table.py` writes the tables in `tables/`. `tools/sharemap.py` writes the share maps. `tools/walkcmp.py`
   compares two films' per-frame region means.
7. A take with `TERTIUS_PROBE_CENSUS_HITS=1` also records each kept sample's hit pixel (for the wall foot's hit rows) and
   V2's level (`tools/levels.py`).

The stills:

| Still | Render size | Phases | Exposure |
|---|---|---:|---|
| lamp hall (rect-lamps-f62), bursts | 1072 × 603 | 25 | 4.08 fixed |
| lamp hall, clean | 1040 × 585 | 27 | 4.08 fixed |
| bay-sunbeam | 1280 × 720 (2/3) and 1840 × 1035 (0.9584) | 18, 8 | automatic, then fixed |
| cabin-light-cabin, exit-cabin | 1280 × 720 | 18 | fixed |
| the walk (cabin-walk) | the survey's walk part (dynamic scale) | | 4.08 fixed |
| the exit-cabin film | the survey's motion film (dynamic scale) | | −0.3957 fixed (V0's film exposure) |

## 4. The variants and their cost

| Variant | Value | What the hit reads | New images | New passes and work per draw |
|---|---:|---|---|---|
| V0 | 0 | Today: this draw's direct part plus the last draw's history part, at the hit pixel. | none | none |
| V1, old stand-in | 4 | A coverage-weighted running mean of the whole hit light, weight 1/64 after 64 draws. | 2 rgba16f layers at render size | one read and one write per pixel in the plan |
| V1, stand-in (c) | 1 | A 64-draw mean of the direct part, plus this draw's history part. | 4 rgba16f layers (mean state and read value) | one read and one write per pixel in the plan |
| V2 | 2 | The hit light's mip level that matches the ray's footprint (section 5). | a mip atlas: the hit light image 1.5 × as wide and 16 rows taller | one pass of about 11 small dispatches (one per level), one load per hit |
| V2 covered-only | 10 | V2, with each 2 × 2 mean over the children that hold light only. | as V2 | as V2 |
| V3 | 3 | V1 (c) and V2 together: the mip chain of stand-in (c)'s read value. | both | both |
| guide only | 16 | V0's hit read; the guide aims by the direct part's mean (layer 14). | 1 layer | the guide's sums |
| V1 + G | 17 | V1 (c), and the guide aims by the same mean. | as V1 (c), plus 1 layer | as V1 (c) |
| S1 | 80 | The direct part times share1 (a two-surface state, below). The guide reads the same. | 1 rgba16f state layer | one state read and write per pixel |
| S2 | 144 | The direct part times min(1, (L + 1) / min(P, draws)), L = the diffuse history's length. | none | none beyond V0 |
| **S2'** | **272** | **The direct part times 1 where holdsHistory holds, else 1 / min(P, draws since the reset + 1).** | **none** | **none beyond V0** |

The real V1 reads the upscaler's output at the hit's reprojected place. It needs one more binding and a reprojection
per hit read, and no new image. The probe build cannot reach that image without engine work, so V1 is a stand-in
everywhere.

S2''s share, as the probe computes it in `screen_trace_plan.comp` (apply-rayend5.py and -6.py):

```glsl
    float phases = min(floor(8.0 * pow(1920.0 / frame.viewport.x, 2.0)), draws);
    ...
    float share3 = probeHolds > 0.5 ? 1.0 : 1.0 / phases; // S2' (bit 8): the surface test alone
    ...
    imageStore(hitLight, ivec3(pixel, KindReflection),
        vec4(max(probeReflection - (1.0 - share) * probeDirectReflection, vec3(0.0)), 1.0));
    imageStore(hitLight, ivec3(pixel, KindDiffuse),
        vec4(max(probeDiffuse - (1.0 - share) * probeDirectDiffuse, vec3(0.0)), 1.0));
    imageStore(hitLight, ivec3(pixel, 14), vec4(dot(share * probeDirectDiffuse, vec3(0.2126, 0.7152, 0.0722))));
```

where

- `draws` = the draw index since the history's reset, plus 1.
- `phases` = P, the jitter's phase count for the render width `frame.viewport.x`, at most `draws`.
- `probeHolds` = 1 where the plan's `holdsHistory` test of the diffuse history holds at this pixel, 0 where it fails.
  An impostor pixel gets 1.
- `share3` = S2''s share of the direct part, 1 or 1 / P.
- `share` = the share that the selected variant applies (S1's, S2''s or S2's).
- `probeReflection`, `probeDiffuse` = the hit light that `leavingLightOf` wrote for each ray kind (V0's value).
- `probeDirectReflection`, `probeDirectDiffuse` = the direct part of that hit light, computed as `leavingLightOf` adds it.
- layer 14 = level 0 of the light that the guide aims by. Under S1, S2 and S2' it holds the shared direct part, so the
  guide and the hit read the same value.

In an engine build, S2' is one multiply where `leavingLightOf` adds the direct part, by a value from a test that the
plan already makes. It keeps no state. The cost that it accepts in advance: a surface thinner than a pixel sends only
1/P of its direct light into the bounce in the draw that shows it.

S1's state: per pixel, a reference view distance and normal, and q = the running share (over up to 64 draws) of draws
whose surface differs from the reference by more than 5 % in distance or by the normal test. share1 = 1 − q for the
reference surface and q for the other.

## 5. V2's level and its histogram

```
level     = clamp(round(log2(footprint)), 0, top)
footprint = d_hit · sqrt(Ω) / (r_eye(hit) · p)        (render pixels)
Ω         = 1 / (32 · density)                           (steradians)
```

where

- `d_hit` = the ray's hit distance from the receiver, in meters.
- `r_eye(hit)` = the hit point's distance from the eye, in meters.
- `p` = the angle of one render pixel, in radians (`frame.params.z`).
- `density` = the ray's own probability density per steradian: the mixture density with its guided part for a diffuse
  ray, the GGX density for a rough reflection ray.
- 32 = one ray per pixel per draw times the history's cap of 32 draws.
- `top` = the atlas's top level.

The far diffuse hits read coarse levels. Share of the far diffuse hits by level (levels with 5 % or more), and the mean
reflection level:

| Still, region | Far diffuse mean level | Far diffuse levels | Reflection mean level |
|---|---:|---|---:|
| lamp hall 1072, ceiling near lamps | 6.64 | 6: 43 %, 7: 37 %, 8: 12 % | 4.63 |
| lamp hall 1072, floor middle | 7.61 | 7: 40 %, 8: 59 % | 5.57 |
| lamp hall 1072, left wall | 6.69 | 5: 7 %, 6: 31 %, 7: 46 %, 8: 14 % | 5.16 |
| lamp hall 1072, far end wall | 5.65 | 4: 14 %, 5: 33 %, 6: 32 %, 7: 15 %, 8: 5 % | 5.01 |
| lamp hall 1072, wall foot | 6.03 | 5: 23 %, 6: 51 %, 7: 19 % | 3.78 |
| bay 2/3, sunlit patch | 7.49 | 5: 6 %, 6: 5 %, 7: 22 %, 8: 64 % | 4.56 |
| bay 2/3, back wall below the slot | 6.46 | 5: 18 %, 6: 32 %, 7: 32 %, 8: 14 % | 4.01 |
| bay 2/3, floor | 7.20 | 6: 15 %, 7: 47 %, 8: 32 % | 5.47 |
| bay 2/3, right wall | 7.62 | 6: 12 %, 7: 21 %, 8: 60 %, 9: 7 % | 6.55 |

A level-7 cell is 128 render pixels wide. Such a cell mixes surfaces that the receiver cannot see, and it weighs its
pixels by screen area, not by their solid angle from the receiver. Files: `results/*.levels.json`.

## 6. The lamp hall: V0 to V3 and the two cause questions

All tables in this section: 1072 × 603, fixed exposure 4.08, **on 3549d3283, before Design X**. The full tables are
`tables/lamp1072.md`, `tables/c1072.md`, `tables/c1072-cause.md`, `tables/lamp1040.md` and `tables/c1040.md`.

### 6.1 The first variants

V1 here is the old stand-in.

| Region | V0b factor, bias | V1 old factor, bias | V2 factor, bias | V3 factor, bias | Check V0, V1, V2, V3 |
|---|---|---|---|---|---|
| ceiling near lamps | 1.00, 0.0 % | 1.81, −7.6 % | 1.95, −4.4 % | 3.02, −13.8 % | 0.993, 0.989, 0.993, 0.989 |
| floor middle | 1.00, 0.0 % | 1.26, −7.2 % | 1.21, −5.3 % | 1.59, −13.2 % | 1.030, 1.024, 1.027, 1.023 |
| left wall above windows | 1.00, 0.0 % | 1.21, −8.6 % | 1.30, −5.7 % | 1.84, −15.9 % | 0.996, 0.991, 0.994, 0.990 |
| far end wall | 1.00, 0.0 % | 1.19, −8.8 % | 0.97, +0.1 % | 1.21, −9.9 % | 0.991, 0.985, 0.993, 0.990 |

Far hits carry 0.99 to 1.01 of the variance in every region and variant. At 1040 × 585: V1 old x1.18 to x1.26 at −7 % to
−10 %, V2 x1.18 to x1.28 at −5.5 % to −7.9 %, V3 x1.44 to x1.77 at −14 % to −21 %.

Measures 2 and 5, ceiling keep rectangle, 1072:

| Variant | Largest diffuse, reflection sample | Diffuse samples above 5 | Mean specks diffuse, reflection | Capture specks median, largest | Guided share: all, brightest 0.1 %, above 5 |
|---|---|---:|---|---|---|
| V0 | 127, 272 | 466 | 67, 648 | 83, 180 | 0.250, 0.134, 0.906 |
| V0b | 127, 272 | 466 | 66, 648 | 83, 180 | 0.250, 0.134, 0.906 |
| V1 old | 10.1, 12.9 | 1,380 | 4, 52 | 0, 7 | 0.250, 0.036, 0.046 |
| V2 | 36.1, 45.7 | 598 | 10, 1,420 | 18, 228 | 0.250, 0.044, 0.035 |
| V3 | 0.447, 1.13 | 0 | 0, 0 | 0, 0 | 0.250, 0.005, none |

At 1040 every variant is clean: no sample above 5, capture specks 0 to 17.

Picture, ceiling (grain / boil): V0 0.0063 / 0.0068, V0's second picture 0.0109 / 0.0068, V1 old 0.0057 / 0.0082, V2
0.0057 / 0.0062, V3 0.0051 / 0.0062. Daub moves by less than 0.001 everywhere. V1 old's boil rises (far wall 0.0106
against 0.0065), because its 64-draw mean lags.

### 6.2 Question 1: why the resolved reads darken

| Test (lamp hall, 1072) | Bias against V0 in the four regions |
|---|---|
| old stand-in, counted draws 240 to 639 | −7.2 % to −8.8 % |
| old stand-in, settled 1,240 draws, counted 1,240 to 1,639 (test b) | −6.0 % to −7.0 % |
| old stand-in, mean weight 1/8 in place of 1/64 | −0.2 % to −0.6 % |
| stand-in (c): the direct part's mean only (test c) | −0.5 % to −0.8 % (1040: −0.6 % to −1.3 %) |
| V2 covered-only | −3.5 % to −5.1 % (V2: −4.4 % to −5.7 %) |

- The settling explains about 1.5 points of the old stand-in's darkening.
- The rest is the half float's rounding. A 1/64 update of a small change falls below half a step of rgba16f and is
  lost, and the bounce loop compounds the loss. With weight 1/8 the loss is gone. **The darkening is a fault of the old
  stand-in, not of a resolved read.** Test (a), a start from V0's light, did not run: (b), (c) and the weight test settle
  the question.
- V2's darkening is not the lamp's dilution in empty children: the covered-only mean removes about 1 point. The rest is
  the coarse levels of section 5.
- Even at those levels, V2 lowers the far hits' variance by only x1.0 to x1.95 in the lamp hall. Reading: most of that
  variance is in where a ray lands and its weight, not in the value that it reads.

### 6.3 Question 2: the flecks at the foot of the far wall

The flecks lie on the floor 1 to 15 output pixels below the far wall's base (output rows 577 to 594), in the region
"floor at the foot of the far wall" [885, 577, 1040, 594].

- **Kept share**: V0 0.988, V1 (c) 1.001, V2 1.068, V3 1.011. V0 does not lose that light in the accumulator, so the
  clamp is not the cause.
- **Hit surface**: in V1 (c), all 92 bright diffuse samples (above 10 times the mean) hit render row 314, the sill row,
  at 0.8 to 1.7 m. So do 99.9 % of the 1,314 bright reflection samples, which carry 17 % of that floor's reflection
  light. None of the bright samples is guided.
- **Per draw**: V0's rays from that floor hit rows 312 to 316 in 348 of 400 draws (diffuse) and in all 400
  (reflection). The mean value that they read there in any draw is at most 0.38 (diffuse) and 0.53 (reflection): the
  wall's light. V0 never reads the sill's 162 from this floor. V1 (c) reads up to 10 there.
- **The row's mixing**: in draw 631 at 1072, all 79 pixels of render row 314 that hold the sill's 162 (columns 496 to
  574) fail the surface test: holdsHistory 0 and history length 0. In draw 630, the same pixels show the sky: no light
  and no history (`results/sh1072-v0.premise.json`). So row 314 shows the sky in 24 phases of 25 and the sill's sunlit
  face in one.
- **Reading**: the floor's rays reach row 314's pixel, but the sill face faces away from the floor. A per-pixel mean over
  the phases puts the face's light on the pixel, and a ray reads it at the depth of the other surface. So the variants
  put light where the sill cannot deliver it. This is a leak through the pixel's mixing of two surfaces over the
  jitter. Any per-pixel resolve of the hit light does the same, the upscaler's output included. The added light is
  +1 % of the region's diffuse mean, and its flecks show at 1:1.
- **Picture** (grain / boil at the wall foot): V0 0.0067 / 0.0034, V1 (c) 0.0184 / 0.0069, V2 0.0292 / 0.0147, V3
  0.0171 / 0.0069.

### 6.4 The filtered guide

V1 + G: the guide aims by the same filtered direct light that V1 (c)'s hit reads.

- Ceiling far-hit variance factor x1.77 against V1 (c)'s x1.62. Left wall x1.14 against x1.05.
- Mean specks (diffuse, reflection): 0 and 35. Capture specks: median 0, largest 11. Samples above 5: 885 against V1
  (c)'s 1,388.
- The wall-foot flecks stay, a little fainter. Their bright samples are unguided, and the reflection rays take no guide.
- The guide alone, with V0's hit read: the ceiling's far-hit variance factor is x0.28, 3.6 times worse. The guide aims
  at the row's mean while the hit reads 0 or 162.

## 7. bay-sunbeam: the test of the far hits' variance

All far hits there land at 2.8 to 11 m on one sunlit patch. **On 3549d3283, before Design X.** Tables:
`tables/bay-sunbeam-067.md`, `tables/bay-sunbeam-096.md` (automatic exposure), `tables/bay-sunbeam-067-fx.md` and
`tables/bay-sunbeam-096-fx.md` (fixed exposure).

At a fixed exposure, 2/3:

| Region | V1 (c) factor, bias | S2' factor, bias | Check V0, V1 (c), S2' |
|---|---|---|---|
| sunlit patch at the upper left | 1.02, −0.1 % | 1.00, −0.1 % | 1.072, 1.074, 1.071 |
| back wall below the slot | 1.01, −0.3 % | 1.00, −0.2 % | 1.073, 1.076, 1.072 |
| floor | 1.01, −0.2 % | 1.00, −0.2 % | 1.089, 1.089, 1.090 |
| right wall | 1.01, −0.2 % | 1.01, −0.2 % | 1.061, 1.061, 1.061 |

At 0.9584 and a fixed exposure, V1 (c): factors 1.00 to 1.01, bias −0.1 % to −0.2 %.

- **V1 (c) does not lower bay's far-hit variance** at either size. The guided share above 5 stays at 0.35.
- **V2 changes it by mixing surfaces** (automatic exposure): x2.18 to x3.07 on the back wall, x1.66 to x2.06 on the
  floor, x1.10 to x1.69 on the right wall, and x0.41 to x0.65 on the sunlit patch, where it raises the variance. With
  the pre-exposure undone, it loses 29 % to 55 % of the bounce light on the back wall, the floor and the right wall, and
  the sunlit patch gains 12 % to 18 %. Its automatic exposure is 0.75 stops brighter. At 1:1 it washes the wall right of the sunlit strip: a halo. V3
  does the same.
- **The kept share in bay is 1.06 to 1.10 for every variant.** The accumulator keeps 0.91 to 0.94 of what it receives
  there, so no variant brings the check to 1.00 (ter-18hc). In the lamp hall the check is 0.985 to 1.03.
- Capture specks in bay (2/3, fixed exposure): median and largest: V0 177 and 273, V1 (c) 177 and 287,
  S2' 203 and 279. These are
  bay's own bright grain on the sunlit patch, not the lamp hall's sill specks. S2' leaves them as V0 has them.
- Picture, S2' at 2/3 against V0 (grain / daub / boil / level): equal to V0 within 0.0002 in every region, level −0.04 %
  to −0.29 %.

## 8. The surface share: S1, S2 and S2'

The lamp hall, fixed exposure 4.08. The guide reads the same value as the hit in all three. **On 3549d3283, before
Design X.** Tables: `tables/sh1072.md`, `tables/sh1040.md`.

### 8.1 The premise

In burst draw 631 at 1072, the 79 sill pixels of render row 314 all fail the surface test (holdsHistory 0, history
length 0). So:

| Variant | Share of the 79 pixels in draw 631 | Light that they send (162 direct) |
|---|---|---|
| S1 | 0.43 (median) | about 70 |
| S2 | 0.040 = 1/25 | 6.5 |
| S2' | 0.040 = 1/25 | 6.5 |

S1's share is 0.43 because row 314 shows a surface that is not sky in about 43 % of the draws, not only the sunlit face.
So a count of "the pixel showed a surface" is not a count of "the pixel showed this surface".

### 8.2 Measures

Bias against V0 and far-hit variance factor, diffuse samples:

| Region | S1 1072 | S2 1072 | S2' 1072 | S1 1040 | S2 1040 | S2' 1040 |
|---|---|---|---|---|---|---|
| ceiling near lamps | −0.5 %, x1.54 | −1.2 %, x2.13 | −0.6 %, x2.11 | −0.2 %, x1.00 | −0.8 %, x1.01 | 0.0 %, x1.00 |
| floor middle | −0.2 %, x1.01 | −0.8 %, x1.03 | −0.2 %, x1.01 | −0.1 %, x1.00 | −0.7 %, x1.02 | 0.0 %, x1.00 |
| left wall above windows | −0.4 %, x1.10 | −1.1 %, x1.19 | −0.5 %, x1.17 | −0.1 %, x1.00 | −0.9 %, x1.02 | 0.0 %, x1.00 |
| far end wall | −0.6 %, x1.01 | −1.1 %, x1.02 | −0.5 %, x1.01 | −0.2 %, x1.00 | −0.8 %, x1.02 | 0.0 %, x1.00 |
| wall foot | −1.0 %, x1.01 | −1.3 %, x1.02 | −0.6 %, x1.01 | −0.6 %, x1.00 | −1.3 %, x1.01 | 0.0 %, x1.00 |

The check is 0.988 to 1.030 in every variant, as V0's. So no variant adds an accumulator loss.

Ceiling keep rectangle, 1072:

| | V0 | S1 | S2 | S2' |
|---|---|---|---|---|
| Largest diffuse, reflection sample | 127, 272 | 69.7, 125 | 8.28, 10.9 | 9.89, 25.1 |
| Diffuse samples above 5 | 466 | 254 | 44 | 48 |
| Mean specks (diffuse, reflection) | 67, 648 | 31, 639 | 0, 0 | 0, 1 |
| Capture specks (median, largest) | 83, 180 | 30, 111 | 0, 0 | 0, 4 |
| Guided share above 5 | 0.906 | 0.827 | 0.455 | 0.500 |

At 1040 all four are clean: capture specks median 0, largest 17 (V0), 2 (S1), 1 (S2) and 18 (S2'). Those are single
counts in one capture each.

Wall foot at 1072: S1, S2 and S2' have no bright sample from the sill rows. The sill rows read at most 0.37 to 0.38 in
any draw, as in V0. Grain there: V0 0.0067, S1 0.0069, S2 0.0068, S2' 0.0066. At 1040: V0 0.0075, S2' 0.0075.

Picture (grain / boil / level), S2' against V0:

| Region | 1072 V0 | 1072 S2' | 1040 V0 | 1040 S2' |
|---|---|---|---|---|
| ceiling near lamps | 0.0063 / 0.0068 | 0.0049 / 0.0056 / −0.5 % | 0.0054 / 0.0062 | 0.0054 / 0.0062 / 0.0 % |
| floor middle | 0.0044 / 0.0030 | 0.0044 / 0.0030 / −0.1 % | 0.0043 / 0.0030 | 0.0043 / 0.0030 / 0.0 % |
| left wall | 0.0038 / 0.0053 | 0.0038 / 0.0051 / −0.5 % | 0.0038 / 0.0052 | 0.0038 / 0.0052 / 0.0 % |
| far end wall | 0.0043 / 0.0065 | 0.0044 / 0.0059 / −0.6 % | 0.0044 / 0.0060 | 0.0045 / 0.0060 / 0.0 % |
| wall foot | 0.0067 / 0.0034 | 0.0066 / 0.0034 / −0.3 % | 0.0075 / 0.0037 | 0.0075 / 0.0037 / 0.0 % |

At 1072, S2''s ceiling grain (0.0049) is inside V0's capture-to-capture spread (0.0063 to 0.0109). Its ceiling boil
(0.0056 against 0.0068) is beyond the spread of about 4 % between V0's two halves of captures: the specks no longer move.

### 8.3 Where the share falls below 1 (the share maps)

The diagnosis layer holds, per pixel, holdsHistory, the history length L, S1's share and S2's share. `sharemap.py` reads
it from one dump.

| Map | Pixels with a share below 1 | Light that they hold back, over the hit light | Where |
|---|---|---|---|
| S2, still at draw 630 | 51,304 of 634,438 (8.1 %) | 1.7 % (4 % on the middle floor) | scattered over every surface, densest on the near floor |
| S2, still, diffuse anti-lag off | 174 (0.027 %) | 0.008 % | window edges |
| S2', still at draw 630 | 74 (0.012 %) | 0.002 % | the windows' jambs and sills only |
| S2', walk draw 228 | 94 (0.010 %) | 0.002 % | window jambs and the far window's edge (section 9.2) |

- Of S2's 51,304 pixels, only 0.14 % (74) fail the surface test. The rest hold a history of 8.4 to 23 draws (p10 to p90,
  median 17.9) against the frame's median of 32. So the history's length is cut at random pixels of a still, and the
  length is not a witness of a new surface. The surface test is. That is why S2' replaced S2.
- The surface test fails at the same 74 pixels with the anti-lag on and off (0.14 % of 51,304, and 42.5 % of 174), so
  S2''s map is the same in both builds. The S2' picture (`pics/sh1072-share2-map-no-antilag-s2prime.png`) comes from the
  no-anti-lag build's dump.

### 8.4 The short histories: ticket t1-g1jd

One build copy skipped the diffuse anti-lag in `accumulate_spatial.comp`:

```glsl
    historyLength = max(fastCap, historyLength * (1.0 - min(1.0, lag / parameters.antiLagShare)));
```

where

- `historyLength` = the diffuse history's length at the pixel, in draws.
- `fastCap` = the fast history's cap, 4 draws.
- `lag` = `luminance(abs(slow - clamped)) / max(luminance(mean), 1e-4)`: the luminance of the clamp's move of the slow
  history, over the luminance of the mean of the 3 × 3 fast histories.
- `parameters.antiLagShare` = 0.5.

That line ran only for the specular signal. The pixels with S2's share below 1 then fell from 51,304 to 174, and on the
middle floor from 16 % to 0. So the anti-lag after the clamp shortens the diffuse histories of a still. A median length
of 18 against the cap of 32 fits a cut to the fast cap (4) followed by growth of one draw per draw. It is the same clamp
event as ter-18hc, seen in the length. The shader was then restored, and it is identical to 3549d3283.

- Not measured: the size of `lag` at the cut, and whether a far or guided sample moved the box. The accumulation trace
  exists only for the specular signal (`TemporalSignal.cs:106-107` gives every other signal
  `HistoryInstrumentation.None`).
- The picture grain of the no-anti-lag take: not measured. That take kept no pictures.
- The light-change age cut is compiled out of the diffuse accumulator on this base. Turning it off changed the share
  map by nothing (`results/sh1072-share2-map-no-age-cut.json`).
- Maps: `pics/sh1072-share2-map.png` (S2, anti-lag on) and `pics/sh1072-share2-map-no-antilag.png` (anti-lag off).
- Ticket t1-g1jd (linked to ter-18hc and ter-pdw8) holds the open question.

## 9. The cabins and the walk

### 9.1 cabin-light-cabin and exit-cabin at 2/3

Fixed exposure (V0's automatic mean). **On 3549d3283, before Design X.**

| Still, region | S2' factor | S2' bias | Check V0, S2' | Picture grain V0, S2' | Picture level |
|---|---:|---:|---|---|---:|
| cabin-light-cabin, floor with sun stripes | 1.00 | −0.2 % | 1.081, 1.080 | 0.0213, 0.0214 | −0.06 % |
| cabin-light-cabin, floor by the right wall | 1.00 | −0.1 % | 1.085, 1.084 | 0.0158, 0.0158 | −0.20 % |
| cabin-light-cabin, sunlit roof beams | 1.00 | −0.2 % | 1.115, 1.115 | 0.0161, 0.0157 | 0.00 % |
| exit-cabin, floor in the middle | 1.00 | −0.1 % | 1.092, 1.091 | 0.0120, 0.0120 | −0.03 % |
| exit-cabin, right wall below the windows | 1.00 | 0.0 % | 1.064, 1.060 | 0.0100, 0.0099 | −0.04 % |
| exit-cabin, ceiling between the beams | 1.00 | 0.0 % | 1.082, 1.081 | 0.0266, 0.0268 | −0.02 % |
| exit-cabin, side of the tilted blue crate | 1.00 | −0.1 % | 1.036, 1.035 | 0.0152, 0.0152 | −0.10 % |

Boil and daub move by 0.0006 or less in every region. Tables: `tables/cabin-light-cabin-067.md`,
`tables/exit-cabin-067.md`.

### 9.2 The walk

Two films with motion, V0 against S2', **on 3549d3283, before Design X**. A region mean here is the probe's per-frame
mean of the shown image's display luminance, over the whole image and over a 3 × 3 grid of the frame.

1. **cabin-walk**, the survey's walk part (`tools/renderer-survey.py run <out> --parts walk --only cabin-walk`): the
   orbit scene's hall, the person stands for 180 frames, walks forward for 120 frames and stands for 200 frames. The
   render scale is the dynamic one, and the exposure is fixed at 4.08 stops.
2. **exit-cabin**, the survey's motion film (`demo-exit.json` from frame 100, 180 frames, moving view), rendered by
   `tools/motionfilm.py` with only the per-frame region means. The exposure is fixed at V0's film exposure, −0.3957
   stops, for both variants.

S2''s region means over V0's, minus 1 (`tools/walkcmp.py`, `results/walk-cabin-walk-s3-vs-v0.json`,
`results/film-exit-cabin-s3-vs-v0.json`):

| Film | First 30 frames | Whole film | Lowest single frame | Settle draws after the stop, V0 and S2' |
|---|---|---|---|---|
| cabin-walk, whole image | −0.01 % | −0.01 % | −0.01 % | 0 and 0 |
| cabin-walk, the 9 grid regions | −0.01 % to −0.02 % | −0.01 % to −0.02 % | −0.01 % to −0.03 % | equal in every region (slowest 26 and 26) |
| exit-cabin, whole image | 0.00 % | 0.00 % | −0.01 % | |
| exit-cabin, the 9 grid regions | −0.19 % to +0.01 % | −0.18 % to +0.03 % | −0.21 % at most | |

A repeat of V0's exit-cabin film is bitwise equal to the first run (`results/film-exit-cabin-v0b-vs-v0.json`), so these
small differences are S2''s. No region of either film fades or darkens by more than 0.21 % in any frame. The largest
difference is in the exit-cabin film's lower middle grid region (the floor), −0.19 % over the first 30 frames.

**What a newly seen surface loses.** A pixel fails the surface test in the draw that first shows its surface. Its
history then starts, so the test holds in the next draw.

- Under S2', the surface sends 1/P of its direct part in that one draw. It loses 1 − 1/P of one draw's direct light:
  0.94 of a draw at P = 18.
- Under S2, the share ramps as (L + 1) / P over the first P draws. The surface loses the sum of 1 − (L + 1) / P for
  L = 0 to P − 1, that is (P − 1) / 2 draws of direct light: 8.5 draws at P = 18.
- So S2' loses about 1/9 of what S2 loses on a new surface. S2 did not run in the films. Its loss here is computed, not
  measured.
- In both films, S2''s loss stays below 0.21 % of any region's mean in any frame. The films therefore show no fade of
  newly seen surfaces under S2'.

**The share map of a walk draw** (`tools/walkmap.py`): the walk script cut after 20 walk frames, at a fixed render scale
of 2/3 and 4.08 stops, with one census dump at draw 228, the 16th frame of the walk (`results/walk-d228-share-map.json`):

| Map, walk draw 228 | Pixels with a share below 1 | Light that they hold back, over the hit light | Where |
|---|---|---|---|
| S2 | 14,573 of 909,244 (1.6 %) | 0.27 % | scattered, 4.0 % to 4.6 % of the pixels on the near floor (lower third), 0.02 % to 0.08 % on the ceiling |
| S2' | 94 (0.010 %) | 0.002 % | the windows' jambs and the far window's edge; 66 % of them have a neighbor more than 5 % off in view distance |

- Of S2's 14,573 pixels, 0.65 % fail the surface test. The rest hold histories of 8.4 to 16.5 draws (p10 to p90), the
  same short histories as in the still (section 8.4).
- `pics/walk-d228-share2-map.png` (S2): red specks spread over the near floor, a few on the walls and ceiling.
- `pics/walk-d228-share2-map-s2prime.png` (S2'): red only on thin lines along the window jambs and the far window's
  right edge. No open surface is red.
- The forward walk uncovers little: only the surfaces behind the window jambs. The exit-cabin film's moving view is the
  harder test of newly seen surfaces, and it shows the same result.
- The PNG that the map take kept (`g-000015.png`) is the probe's colour view, not the shown picture, so the record does
  not keep it.

## 10. Per-variant statements

| Variant | Removes the specks | Far-hit variance factor | Bias it adds | Leak or halo at 1:1 | Verdict |
|---|---|---|---|---|---|
| V1, old stand-in | yes at 1072 (largest 7) | x1.19 to x1.81 (lamp hall) | −7 % to −10 % (half-float rounding) | wall-foot flecks | fails: bias, leak |
| V1, stand-in (c) | yes at 1072 (largest 11) | x0.89 to x1.62 (lamp hall; x0.89 at the wall foot), x1.00 to x1.02 (bay) | −0.5 % to −1.3 % | wall-foot flecks | fails: leak; does not lower bay's variance |
| V2 | no (largest 228) | x0.13 to x1.95 (lamp hall; x0.13 at the wall foot), x0.41 to x3.07 (bay) | +0.1 % to −5.7 % and +7.4 % at the wall foot (lamp hall), −29 % to −55 % in radiance (bay) | flecks, halo in bay | fails |
| V3 | yes (0) | x1.03 to x3.02 | −0.1 % to −7.4 % over (c), −10 % to −21 % over the old stand-in | flecks, halo | fails |
| guide alone | no | x0.28 (ceiling) | 0 % | none | fails |
| V1 + G | yes (largest 11) | x0.93 to x1.77 | as V1 (c) | wall-foot flecks | fails: leak |
| S1 | no (largest 111) | x1.01 to x1.54 | −0.1 % to −1.0 % | none | fails |
| S2 | yes (0 at both sizes) | x1.01 to x2.13 | −0.7 % to −1.3 % | none | fails the 1 % rule narrowly |
| **S2'** | **yes (largest 4 at 1072)** | **x1.00 to x2.11** | **−0.6 % to 0.0 %** | **none in 19 crops** | **passes: stills and both films** |

Where S2' lowers the far-hit variance (the lamp hall's ceiling at 1072, x2.11), it lowers it by removing the specks'
samples. It does not change the general mottle: elsewhere and in bay its factor is x1.00 to x1.01.

## 11. The spread between two V0 runs

- V0b counts draws 247 to 646 in place of 240 to 639: a start 7 draws later, which is not a multiple of the 25 or 27
  phases. Every census measure differs from V0's by less than 0.2 % (lamp hall and bay). A plain repeat on this Mac is
  bitwise equal.
- The pictures: a second V0 run that shifts only the counted draws renders identical pictures. So the picture spread is
  "V0's second picture" (capture c-s10, 33 draws after c-s09) for grain and daub, and V0's two halves of captures for
  boil. On the lamp hall's ceiling at 1072: grain 0.0063 against 0.0109, and boil spread about 4 %.

## 12. What stand-in (c) cannot show

- The real V1 reads the output-size image. That image also holds the old eye's specular light and the upscaler's own
  clamp and lag (t3-5jws). The stand-in holds neither.
- The real hit light is rescaled when the exposure changes (`historyRatio`). The stand-in's mean is not. Under the
  automatic exposure this made bay's exposure swing by ±0.17 stops (V0: ±0.005). The fixed-exposure takes remove it.
- The stand-in resolves per render pixel. The upscaler resolves per output pixel. Both hold one value per pixel for two
  surfaces, so the wall-foot leak (section 6.3) applies to both.

## 13. The crops

Each crop is the shown still (capture c-s09) at 1:1, beside V0's reference (the mean of captures c-s08 to c-s19).
Columns left to right: V0's reference, V0, then the variants, unless the line says otherwise. I opened every crop.

The first variants (lamp hall, columns reference, V0, V1 old, V2, V3):

- `pics/lamp1072-crop-ceiling.png`: the ceiling around the near lamps. V0 has faint pale specks left and right of the
  lamp. V1 and V2 have fewer. V3 has none but is visibly darker.
- `pics/lamp1072-ceiling-lamp-2x.png`: the same lamp at 2x. V0's specks show around the lamp. V3 is clean.
- `pics/lamp1072-sill-row-2x.png`: the sill row below the window at 2x. Rows V0, V1, V2, V3, and columns captures c-s09,
  c-s15, c-s19. V0 is smooth. V1, V2 and V3 have light flecks along the sill's upper edge in every capture, V2's the
  brightest (flecks per capture, 15 % above a 7 px median: V0 0 to 5, V1 0 to 1, V2 0 to 42, V3 0 to 5).
- `pics/lamp1072-crop-window-sill.png`, `-dark-niche.png`, `-lamp.png` and `pics/lamp1040-crop-*.png`: no variant
  differs from V0 beyond the darkening that the tables give.

Stand-in (c) and the guide (lamp hall 1072):

- `pics/e1072-wall-foot-3x.png`: the floor at the far wall's foot at 3x. Rows reference, V0, V1 (c), V1 + G, V3. V0 is
  smooth. V1 (c), V1 + G and V3 have pale flecks along the floor's edge, a little fainter with the guide.
- `pics/e1072-lamp-3x.png`: a near ceiling lamp at 3x. Columns reference, V0, V1 (c), V1 + G, V3. V0 has two pale
  specks right of the lamp. The variants are clean, and V3 is darker.
- `pics/e1072-crop-*.png` and `pics/c1072-crop-*.png`: the same five boxes for those variants and for V2 and V3 over
  stand-in (c). They agree with the two pictures above.

bay-sunbeam (columns reference, V0, V1, V2, V3 at the automatic exposure, and reference, V0, V1 (c) at a fixed
exposure):

- `pics/bay-sunbeam-067-crop-sunbeam-edge.png`: the sunlit strip's edge. V2 and V3 wash the wall right of the strip
  bright and smooth: a halo at 1:1. The reference there is mid gray. V1 is as grainy as V0 and darker in this capture,
  because of its exposure swing.
- `pics/bay-sunbeam-067-fx-crop-*.png`, `pics/bay-sunbeam-096-fx-crop-*.png`: at a fixed exposure, V1 (c) is equal to V0
  in all three boxes at both sizes.
- `pics/old-standin/`: the same boxes with the old stand-in.

The surface share (lamp hall, columns reference, V0, variant):

- `pics/sh1072-lamp-3x.png`: a near ceiling lamp at 3x. Columns reference, V0, S1, S2. V0 has pale specks right of the
  lamp, S1 one fainter speck. S2 is clean, at the reference's brightness.
- `pics/sh1072-wall-foot-3x.png`: the floor at the far wall's foot at 3x. Rows reference, V0, S1, S2. All four are
  smooth, so S1 and S2 add no flecks.
- `pics/sh1072-s3-pv-crop-ceiling.png`: S2' on the ceiling. V0 has a few pale specks above and right of the lamp. S2'
  has none, at V0's brightness.
- `pics/sh1072-s3-pv-crop-window-sill.png`: the window and its sill. V0 and S2' are equal: no fleck on the sill's edge.
- `pics/sh1072-s3-pv-crop-wall-foot.png`, `-dark-niche.png`, `-lamp.png` and `pics/sh1040-s3-pv-crop-*.png`: equal to V0.
- `pics/sh1072-s1-*`, `pics/sh1072-s2-*`, `pics/sh1040-s1-*`, `pics/sh1040-s2-*`: S1 and S2 in the same boxes.

S2' in bay (fixed exposure, columns reference, V0, S2'):

- `pics/bay-sunbeam-067-fx-s3-pv-crop-sunbeam-edge.png`: the sunlit strip's edge. V0 and S2' have the same grain on the
  wall right of the strip, and no halo.
- `pics/bay-sunbeam-067-fx-s3-pv-crop-window-corner.png`: the dark window's lower edge. Both have the same mottled line
  along the edge.
- `pics/bay-sunbeam-067-fx-s3-pv-crop-floor-hole.png`: the hole in the floor. Equal.

S2' in the cabins (columns reference, V0, S2', three silhouettes per still, `tools/crops-*.json`):

- `pics/cabin-light-cabin-067-s3-pv-crop-pillar-on-floor.png`: the blue pillar and box before the sunlit floor stripes.
  The pillar's edges and the stripes are the same in V0 and S2'. No bright fringe, no dark rim.
- `pics/cabin-light-cabin-067-s3-pv-crop-right-window-jamb.png`: a bright jamb before the sky and the dark lintel.
  Equal.
- `pics/cabin-light-cabin-067-s3-pv-crop-left-door-frame.png`: a dark door frame before the outside floor. The same
  grain on the frame's lit side in V0 and S2', and no halo.
- `pics/exit-cabin-067-s3-pv-crop-tilted-pillar.png`: the tilted blue crate before the floor and the window frames.
  Equal.
- `pics/exit-cabin-067-s3-pv-crop-left-wall-lamp.png`: a bright wall lamp before the frames. The glow is the same.
- `pics/exit-cabin-067-s3-pv-crop-right-doorway.png`: the doorway's dark frame before the bright outside. Equal.

The share maps (frame at 2x, gray = hit light, red = share below 1):

- `pics/sh1072-share2-map.png`: S2. Red pixels scattered over all surfaces, densest on the near floor.
- `pics/sh1072-share2-map-no-age-cut.png`: S2 with the light-change age cut off. The same as the one above.
- `pics/sh1072-share2-map-no-antilag.png`: S2 with the diffuse anti-lag off. A few red pixels on window edges only.
- `pics/sh1072-share2-map-no-antilag-s2prime.png`: S2'. A few red pixels on the windows' jambs and sills, none on an
  open surface.

## 14. Files

- `probe.patch`: the probe build copy's diff against 3549d3283 (`src` only, because `tools` stays as it is). It applies to that
  commit with `git apply`. It never enters `src` on main.
- `tools/`: the steps `apply-rayend*.py`, the take tools (`census.py`, `queue-*.sh`, `walkmap.py`, `motionfilm.py`), the measures
  (`analyze.py`, `lamp.py`, `lampdump.py`, `sill.py`, `premise.py`, `levels.py`, `pics.py`, `sharemap.py`, `table.py`,
  `walkcmp.py`, `summarize.py`, `atlascheck.py`), the boxes (`regions.json`, `regions-walk.json`, `crops-*.json`) and V2's mip shader
  (`screen_trace_hit_mips.comp`). Each file's header says what it does and how to run it.
- `tables/`: the tables of measures 1 to 5 per still.
- `results/`: per take, `census-result.json`, `lamp-result.json`, `census.json` and the series. Beside them are the
  `*.lamp.txt`, `*.analyze.txt`, `*.sill.json`, `*.premise.json`, `*.pictures.txt`, `*.levels.json` and the share maps'
  JSON.
- `pics/`: the crops, the 2x and 3x pictures and the share maps.
