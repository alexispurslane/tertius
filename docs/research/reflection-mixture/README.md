# The guided mixture for rough reflection rays, stage 1 (ter-fe63), with the thin box (t1-cyhy) and the bay's history lengths (t1-nyjl)

This record holds stage 1 of ter-fe63: probe takes of a guided mixture for the reflection kind's rays, before any engine
build. It also holds the parts of t1-nyjl and t1-cyhy that ran on the same probe: the one-bounce picture, the history
lengths of the bay, the lifetime of the blobs, and the thin target box. It changes no engine code. The tickets'
notes of 2026-11-23 hold the orchestrator's decisions. This record holds the measurements and the tools.

## Terms

- The **lobe draw** is today's draw of a reflection ray from the GGX lobe, with the density `q_l` per steradian.
- The **guided draw** aims a ray at a target that the light pyramid picks by its light, as the diffuse kind's guide
  does, with the density `q_g` (`guidedDensityOf`). For a hit that does not count for the target, `q_g` is 0.
- The **share** `s` is the probability of a guided draw. The **mixture density** is `q_s = (1 - s) q_l + s q_g`. The
  resolve weighs every reflection ray by the mixture density of the pixel that weighs it.
- The **ramp** sets the share per pixel: the full share times `clamp((sqrt(alpha) - 0.1) / (0.38 - 0.1), 0, 1)`, with
  alpha the floored roughness. Every pixel of the views here has `sqrt(alpha)` = 0.380, so it takes the full share.
- The **coin** decides each draw by a random number of its own.
- The **exact count** gives each ray cell exactly one guided draw in each window of `P = max(4, round(1 / s))` draws.
  The cell's slot in the window is `(o(cell) * P / 4 + hash(cell / 2, window)) mod P`, with `o` = (0, 2, 3, 1) by the
  cell's parity. The hash of the window breaks the lock with the block's chosen pixel, whose turn has a period of 4.
- **Today's arrival**: a guided draw counts only when its walk hits the target's depth box, 0.05 of the target's view
  depth. The **thin box** (flag 4) is a scratch setting with 0.0125. It is fair for pictures, and it is not a model
  (t1-cyhy).
- **Grain** and **blob** are the two bands of a still's residual against its build's own reference (the mean of
  captures 30 to 99 of a 100-capture take). Grain holds scales of 1 to 3 output pixels. Blob holds about 6 to 70
  output pixels. Both are RMS values in units of main's full-picture mean of the region (`guide/parts.py`).
- **w** is a reflection sample's luminance as the mixture weighs it, before the caps (layer 8's z).
- The **cause record** (probe layer 25) tells for each reflection ray whether it was guided, how its walk ended
  (hit, below the start pixel's plane, out of steps, off the screen), whether the hit counts, and the hit's depth
  over the target's.

## The probe

`probe.patch` is the probe over the surface share's head e11b3548b. It contains the ray census's probe (ter-8pco,
ter-pdw8), the guide census's extension (docs/research/guide-census), and this stage's switches. It is a scratch
change and never goes into `src`. Its switches are environment variables:

| Variable | Effect |
|---|---|
| `TERTIUS_PROBE_REFLECTION_SHARE` | The reflection kind's full share. |
| `TERTIUS_PROBE_RAMP=start,end` | The ramp's ends in `sqrt(alpha)`. The takes use 0.1,0.38. |
| `TERTIUS_PROBE_EXACT_REFLECTION=1` | The exact count (flag 8). Without it, the coin. |
| `TERTIUS_PROBE_THIN_BOX=1` | The thin box (flag 4). |
| `TERTIUS_PROBE_ONE_BOUNCE=1` | A diffuse ray's hit reads only the sender's fresh light (flag 2). |
| `TERTIUS_PROBE_WRITERS_OFF=bits` | Turns off a writer of the diffuse history's length: 1 region evidence, 2 the anti-lag, 4 light evidence, 8 the clamps and every evidence cut. |
| `TERTIUS_PROBE_DIFFUSE_CAP` | The diffuse history's cap. |
| `TERTIUS_PROBE_SIGNAL` | 2 the diffuse signal alone, 3 the specular signal alone. |
| `TERTIUS_PROBE_TRACE_DUMP=first-end` | Dumps the specular accumulation trace's layers 3 and 6. |
| `TERTIUS_PROBE_CENSUS_KEEP_REFLECTION` | 1: 145-byte keep records with the cause record. 0: 65 bytes. |

## The takes

All takes ran on the Mac M1 Max under the shared GPU lock, at 1920 x 1080 with a render scale of 2/3 (1280 x 720),
each at one fixed exposure, with the clamps on unless a row says otherwise. The cases and their regions (output pixels):

| Case | Exposure | Regions |
|---|---|---|
| bay-sunbeam (frozen bay) | -4.089 stops | sunlit patch 100,40,420,360 (the wall beside the sunlit stripe); back wall below the slot 560,420,980,560; floor 600,650,1200,950; right wall 1300,100,1800,500 |
| sunlit-nose | -1.7507 stops | the ceiling box 750,0,1170,330 |
| rect-lamps-f62 | 3.83 stops | the glossy floor 600,620,1320,1000 |
| mirror-plate | 0.73 stops | the plate 800,350,1150,700 |

A still is 20 captures, 33 draws apart. The tables use capture 19. `tools/` holds every script with its plan file
(`part1.txt` to `part4.txt`, `hold3.sh`, `hold5.sh`, `hold5b.sh`).

## 1. The nose: the carrier and the cure

On the nose's ceiling box, the brightest samples are lobe draws of rough reflection rays that hit a sunlit floor strip
(guide census, part 2). The mixture aims there.

| Build | Capture specks, median (largest) | Box mean, 20 captures |
|---|---|---|
| Main | 3,719.5 (13,095) | 0.3012 |
| Coin, 0.25 | 128 (2,142) | 0.3231 |
| Exact count, 0.25 | 101 | |
| Exact count, 0.125 | 257.5 (2,666) | 0.3197 |

- With the clamps off, main gives 0.3156 and the mixture 0.3161. So the mixture is unbiased, and main's clamp loses
  4.6 % of the box's light.
- The film (frames 120 to 359) gives a temporal deviation per pixel of 4.01 (main), 2.26 (coin) and 2.16 (exact). The
  exact count shows no beat at a period of 4.
- At draw 130 of rect-bay's left wall, the capture specks are 4,208 (main), 688 (coin) and 726 (exact). The diffuse
  samples are the same on all three builds.

`pics/nose-box-1x-125.png`: capture 19 at 1:1, main, coin 0.25, exact 0.25 and exact 0.125.

## 2. The bay, rect-lamps' floor and the plate: the cost

Grain / blob against each build's own reference:

| Region | Main | Coin 0.25 | Exact 0.25 | Exact 0.125 | Thin box, coin 0.25 |
|---|---|---|---|---|---|
| Bay sunlit patch | 0.0096 / 0.0072 | 0.0133 / 0.0090 | 0.0125 / 0.0084 | 0.0108 / 0.0077 | 0.0127 / 0.0084 |
| Bay back wall | 0.0055 / 0.0095 | 0.0059 / 0.0094 | 0.0055 / 0.0095 | 0.0054 / 0.0095 | 0.0058 / 0.0101 |
| Bay floor | 0.0099 / 0.0114 | 0.0102 / 0.0114 | 0.0099 / 0.0112 | 0.0097 / 0.0113 | 0.0091 / 0.0099 |
| Bay right wall | 0.0331 / 0.0212 | 0.0308 / 0.0175 | 0.0303 / 0.0175 | 0.0316 / 0.0187 | 0.0220 / 0.0121 |
| rect-lamps floor | 0.0055 / 0.0026 | 0.0087 / 0.0052 | 0.0068 / 0.0037 | 0.0061 / 0.0031 | |

- The thin box has a dark bias of 1.1 % on the patch against a cosine-only reference. Today's box is within 0.3 %
  (`tables/guide/box-bias.json`).
- The 20-capture means against main (`means-bay-*.json`, `means-lamps*.json`): the right wall is 1.5 % brighter at
  0.25 (coin and exact) and 2.6 % with the thin box, and 1.0 % at 0.125. Every other region stays within 0.5 %. In the
  specular signal alone, the means rise up to 9 % (coin, exact) and 18 % (thin box) on the right wall and the floor.
  With the clamps off, every build agrees with main within the noise (hold 3, `noclamp-bay.json`). Main's specular
  clamp cuts 19 % there, the mixture's 12 to 14 % and the thin box's 6 to 8 %. So the rise is the clamp's loss
  shrinking, not a bias of the mixture.
- The plate's pixels have `sqrt(alpha)` = 0.05, so the ramp gives them a share of 0. The plate is the same as main's
  to the byte.
- The specular signal alone (part 2, `parts-bay-spec.json`) shows the same pattern: the patch's grain rises 52 % (coin)
  and 33 % (exact). The right wall improves only with the thin box.
- The rect-lamps floor's film (frames 120 to 359) gives a temporal deviation per pixel of 0.384 (main), 0.643 (coin)
  and 0.499 (exact), with no beat at a period of 4 (`flicker-lamps.json`).

Pictures at 1:1: `pics/bay-125-sunlit-patch.png`, `pics/bay-125-right-wall.png`, `pics/lamps-floor-125.png`.
Films of frames 120 to 359, main / coin / exact at 0.25: `pics/nose-film-main-coin-exact.mp4`,
`pics/lamps-floor-film-main-coin-exact.mp4`.

## 3. Why the patch and the floor lose: the causes of the guided draws

The guided draws of the reflection kind by how they end, in % of the guided draws (`t5-causes-*.json`, exact count; the
shares 0.25 and 0.125 give the same classes within 0.5 points):

| Region | Own plane | Box miss | Blocked | Arrives and counts | Arriving draws' share of the second moment, 0.125 / 0.25 |
|---|---|---|---|---|---|
| Bay sunlit patch | 78.6 % | 19.7 % | 0.2 % | 1.5 % | 5.6 % / 10.1 % |
| Bay right wall | 0.8 % | 67.0 % | 0.002 % | 32.2 % | 26 % / 42 % |
| Bay floor | 1.3 % | 85.9 % | 0.04 % | 12.8 % | 19 % / 25 % |
| Nose box | 1.0 % | 10.8 % | 0.7 % | 87.5 % | 45 % / 50 % |
| rect-lamps floor | 82.9 % | 12.6 % | 0.2 % | 4.3 % | 0.5 % / 0.9 % |

- "Own plane": the walk ends below the start pixel's plane, because the guide's target is the lit spot on the same
  surface.
- "Blocked": the hit does not count and lies nearer than the target.
- "Box miss": every other draw that does not count.

On the patch and the lamps floor, the share takes lobe draws away and brings almost nothing back. On the right wall
and the bay floor, most guided draws miss today's depth box (t1-cyhy).

## 4. The replay and the slope of the variance in the share

`tools/ramp.py` replays a take's samples at other shares: the second moment under the share `s` is the mean of
`X^2 q_t / q_s`, where `q_t` is the take's own density. The relative variance is the pooled variance over the mean of
the squared pixel means. The rows come from the share-0 lobe takes of hold 3 (40 draws). The bay floor has none, so
its rows come from the mixture takes of hold 5b (20 draws).

| Region | 0 | 1/32 | 1/16 | 1/8 | 1/4 | 1/2 | Least |
|---|---|---|---|---|---|---|---|
| Nose box | 7.15 | 3.97 | 3.03 | 2.13 | 1.41 | 1.14 | 1/2 |
| Bay right wall | 5.76 | 5.19 | 4.83 | 4.31 | 3.66 | 2.92 | 1/2 (0.75: 2.53) |
| Bay sunlit patch | 3.67 | 3.75 | 3.84 | 4.04 | 4.52 | 6.05 | 0 |
| Bay floor, from the 0.25 take | 4.70 | 4.67 | 4.66 | 4.66 | 4.77 | 5.54 | 1/8, flat from 0 |
| Bay floor, from the 0.125 take | 5.29 | 5.15 | 5.05 | 4.93 | 4.91 | 5.59 | 1/4, flat from 1/8 |
| rect-lamps floor | 0.391 | 0.434 | 0.480 | 0.581 | 0.834 | 1.71 | 0 |
| Plate (at its own roughness) | 0.084 | 0.119 | 0.156 | 0.239 | 0.445 | 1.17 | 0 |

The replay is exact only where the recorded densities are the densities that drew the samples. Away from a take's
own share, the takes disagree in the heavy-tailed regions. At share 0, the nose box gives 7.15 (lobe take), 2.32
(0.25 take) and 3.46 (0.125 take). The right wall gives 5.76, 4.12 and 4.64. The patch and the lamps floor agree
within 12 % and 4 %. Every take gives the same least share in every region. A guided draw that misses its box records
`q_g = 0` although the guide drew it. That can break the weights; it is not checked.

The mean of w² over each kind of draw, over the region's pooled mean squared (`t5-wslope-*.json`):

| Region | Lobe draws, 0.25 / 0.125 | Guided draws, 0.25 / 0.125 | dM/ds at 0.25: density estimate / lobe minus guided |
|---|---|---|---|
| Nose box | 2.58 / 3.11 | 7.74 / 18.16 | -1.77 / -5.16 |
| Bay sunlit patch | 11.80 / 9.71 | 3.94 / 4.04 | +7.32 / +7.86 |
| Bay right wall | 6.36 / 7.89 | 13.56 / 19.46 | -4.21 / -7.20 |
| Bay floor | 9.03 / 8.55 | 8.82 / 14.34 | +2.40 / +0.21 |
| rect-lamps floor | 2.44 / 1.81 | 0.069 / 0.065 | +2.37 / +2.37 |

`M(s)`, the second moment, has the slope `dM/ds = E_l[w^2] - E_g[w^2]`, with each expectation under its own kind's
density. "Lobe minus guided" estimates it from the two kinds' draws. The density estimate
`mean(X^2 (q_l - q_g) / q_t)` (`tools/slopecheck.py`) uses every sample and has less noise. It lies between the
replay's secants on both sides of 0.25 in every region; the split does not. Use the density column.

## 5. The diffuse history: its lengths, the lever and the lifetime of the blobs (t1-nyjl)

The mean diffuse history length and the share of pixels under 3/4 of the cap, in the frozen bay
(`hist-c*.json`):

| Cap | Patch | Back wall | Floor | Right wall |
|---|---|---|---|---|
| 32 | 31.1, 7 % | 30.6, 14 % | 31.0, 0 % | 19.7, 69 % |
| 64 | 55.1, 28 % | 53.3, 28 % | 56.1, 22 % | 26.6, 78 % |
| 128 | 96.3, 35 % | 76.4, 50 % | 99.6, 23 % | 33.6, 91 % |

Region evidence is the only writer that shortens these histories. With it off, every short pixel reaches the cap.
The anti-lag and light evidence shorten none here. At a cap of 128 with region evidence off (the **lever**), the
diffuse blobs fall 1.9 to 3.4 times (`parts-d128.json`), and the blobs live as long as a plain history does
(`life-d128-*.json`).

Part 4: the lever beside the mixture, the full picture against each build's own reference (`parts-lever.json`):

| Region | Main | Lever | Lever + exact 0.25 | Lever + exact 0.125 |
|---|---|---|---|---|
| Sunlit patch | 0.0096 / 0.0072 | 0.0089 / 0.0054 | 0.0119 / 0.0070 | 0.0101 / 0.0062 |
| Back wall | 0.0055 / 0.0095 | 0.0049 / 0.0040 | 0.0049 / 0.0039 | 0.0049 / 0.0040 |
| Floor | 0.0099 / 0.0114 | 0.0098 / 0.0056 | 0.0094 / 0.0057 | 0.0093 / 0.0056 |
| Right wall | 0.0331 / 0.0212 | 0.0329 / 0.0203 | 0.0301 / 0.0168 | 0.0313 / 0.0179 |

The lever's 20-capture means against main: patch 0.997, back wall 0.974, floor 0.980, right wall 0.991
(`means-lever.json`). So the lever darkens the back wall by 2.6 % and the floor by 2.0 %. No clamp-off take of the
lever exists, so the cause is not measured. Pictures at 1:1, main / lever / lever + exact 0.25 / lever + exact 0.125:
`pics/lever-bay-back-wall.png`, `pics/lever-bay-floor.png`.

**A correction.** An earlier note (t1-nyjl, 2026-11-23) read the back wall's blobs as the 8-bit rounding of the
output, from a correlation of 0.95 between captures and of 0.965 with the rounding band. That was a fault of the
lead's tools. `lifetime.py` and `quant.py` blurred a relative residual over the whole image, and near-black pixels
leaked into the regions. On the absolute residual (`tables/guide/lifetime-abs-main.json`, `quant-abs.json`), the back
wall's blob band correlates 0.32 after one capture and 0.10 after two, like the other regions, and its correlation
with the rounding band is 0.0004. So the back wall's blobs are ray noise through the history, as on the other regions.

## 6. The one-bounce picture (t1-nyjl)

With flag 2, a diffuse ray's hit reads only the sender's fresh light of this draw (`tables/guide/`):

- The one bounce darkens the bay: the 20-capture means against main are patch 0.863, back wall 0.381, floor 0.477,
  right wall 0.761 (`means-ob.json`).
- It removes no blobs. In main's units, grain / blob of the full picture: patch 0.0093 / 0.0074, back wall
  0.0046 / 0.0105, floor 0.0066 / 0.0086, right wall 0.0333 / 0.0212, against main's in section 2.
- The blobs live as long as main's (`lifetime-abs-ob.json`).

## 7. The thin box (t1-cyhy)

The share of guided diffuse draws that arrive, thin box against today's box (`cyhy-bay-thin.json`): sunlit patch
5.5 % against 1.4 %, back wall 57.2 % against 14.8 %, floor 44.1 % against 11.6 %, right wall 98.0 % against 32.4 %.
The thin box's dark bias on the patch is 1.1 % (section 2).

## Deleted raw takes

The raw takes were deleted once their tables existed, 19.2 GB in all (`tables/deletions.log` lists the last round):

- 2.8 GB of the first census captures.
- 9.2 GB of census sums and slow images, among them main's still captures 0 to 18 of the bay, the nose and the
  lamps. So the 0.125 means are taken against main's recorded 20-capture means.
- 7.24 GB after hold 5: the four replay logs of hold 3 (2.76 GB, after their tables, which are in `tables/keeps/`),
  the nose films (2.80 GB) and the lamps floor films (1.69 GB), after their videos and flicker tables.
- The trace dumps of the fast histories (11 MB), after `fasttrace.json`.

The probe's build and every reference mean stay for the next probe. The other crops stay in the lead's evidence folder (`t3-2b720d96-b40221aeff8f27f7-evidence/fe63/`).

## Files

- `probe.patch`: the probe over e11b3548b.
- `tools/`: the take scripts and the analysis tools of this stage. `tools/guide/` holds the shared still tools
  (`parts.py`, `means.py`, `reference.py`, the lifetime and the band tools, and the arrival tools of t1-cyhy). The
  scripts name the lead's scratch paths, `/private/tmp/t1-wl7c/...`, where they ran.
- `tables/`: every table named above, as JSON. `tables/keeps/` holds the replay tables of the share-0 takes, and
  `tables/guide/` the guide census's, the one bounce's and t1-cyhy's tables.
- `pics/`: the 1:1 crops named above.
