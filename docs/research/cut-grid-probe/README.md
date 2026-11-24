# The grid of square groups after a camera cut (t1-3er4), stage 1 research

## Result

The regular grid on the sunlit ceiling after a camera cut comes from step 3 of `accumulate_spatial.comp`, the short-history fix, in the specular signal. In draw d0 after the cut, the fix takes the mean of a 3 × 3 square of taps 4 render pixels apart. A pixel and its neighbor 4 render pixels away then share 6 of their 9 taps. At the render scale 2/3, 4 render pixels are 6 output pixels. So the image correlates with itself at a lag of 6 output pixels by 6/9 = 0.667, and at a lag of 12 by 3/9 = 0.333. The measured values are 0.637 and 0.396.

Candidate B removes the grid. It turns the 3 × 3 square per pixel to one of eight orientations, chosen by the existing blue-noise mask. It reads the same 9 taps per pixel at the same radius, within 2 %. On the cut onto the nose, the autocorrelation at lag 6 falls from 0.637 to −0.014 at d0, and from 0.402 to −0.007 at d1. The brightness, the specks, the shimmer, the glossy floor's grain and the lamp edges stay the same or improve. Two runs give the same frames, byte for byte.

Recommendation: build candidate B into production, at spacings 4 and 2. Details are in "Recommendation".

## What the record measures

The probe build is main at `e9e0dc043` plus [probe.patch](probe.patch). The patch adds one push constant, the fix's stencil mode, which the variable `TERTIUS_PROBE_FIX_STENCIL` selects:

| Mode | Value | What step 3 does |
|---|---|---|
| Baseline | unset or `baseline` | Today's axis-aligned 3 × 3 square, spacing 4, 2 and 1 render pixels in draws d0, d1 and d2. |
| Off | `off` | No fix. Diagnostic only. |
| Candidate A | `a` | One of four orientations of the square, the same for all pixels of a block of 8 × 8 render pixels. |
| Candidate B | `b` | One of eight orientations at spacing 4, or four at spacing 2, per pixel, from the blue-noise mask. |

`TERTIUS_PROBE_FIX_LIGHT` limits the mode to one signal: `all` (the default), `diffuse` or `specular`. The baseline mode reads the same taps in the same order as main. Its 126 frames of the nose cut are the same as main's, byte for byte (`tables/determinism.json`).

Every take used the normal entry point: `Tertius.Demo --script <script> --headless --size 1920x1080 --render-scale 0.6667 --exposure <stops> --video film.mkv --fps 60`, under `tools/gpu-lock.sh --shared`. The scripts are ter-fe63's committed cut scripts, [real-cut-nose.json](../reflection-mixture-adaptive/production/scripts/real-cut-nose.json) at −1.7507 stops and [real-cut-lamps.json](../reflection-mixture-adaptive/production/scripts/real-cut-lamps.json) at 3.83 stops. Frame 61 is d0, the first draw after the cut, and frame 125 is d64. The rays, the ray plan, the guided turn, the exposure and FSR are the same in all modes. Only step 3 changes.

The grid measure (`tools/roi.py`) works on the nose crop, output pixels 750..1169 × 0..329. It takes the crop's 601 luma, subtracts its Gaussian blur of σ = 3 output pixels, and keeps the region of interest (ROI): x = 12..159 and y = 20..239 of the crop, 148 × 220 output pixels. The number is the correlation of the ROI with itself moved by a lag along x, and along y, averaged.

## Phase 1: where the grid comes from

### The measure matches the parent's anchor

On ter-fe63's committed strip [real-cut-nose-strip-1x.png](../reflection-mixture-adaptive/production/pics/real-cut-nose-strip-1x.png), the measure with σ = 3 gives these values at lag 6 (`tables/anchor-roi-production-strip.txt`):

| Row of the strip | d0 | d1 | d2 | d5 |
|---|---:|---:|---:|---:|
| main before ter-fe63 | 0.652 | 0.419 | 0.074 | −0.008 |
| production (ter-fe63) | 0.638 | 0.403 | 0.113 | 0.047 |

This matches the parent's anchor of about 0.65 and 0.63 at d0. The production row falls to 0.113 at d2. The other high passes in the table (σ = 1 to 4, boxes of 7 and 13) give the same picture.

### A bypass of step 3 removes the lag-6 grid

| Take, nose cut | d0 lag 3 | d0 lag 6 | d0 lag 12 | d1 lag 6 | d2 lag 6 | d5 lag 6 |
|---|---:|---:|---:|---:|---:|---:|
| main `e9e0dc043` | −0.131 | 0.637 | 0.396 | 0.402 | 0.112 | 0.047 |
| probe, baseline | same frames as main | | | | | |
| probe, step 3 off in both signals | −0.114 | 0.012 | 0.166 | 0.010 | 0.006 | 0.004 |
| probe, step 3 off in the specular signal | −0.114 | 0.013 | 0.166 | 0.010 | 0.005 | 0.004 |
| probe, step 3 off in the diffuse signal | −0.131 | 0.637 | 0.395 | 0.403 | 0.110 | 0.047 |

The lag-6 grid comes only from step 3 of the specular signal. Step 3 of the diffuse signal adds nothing to it. So the diagnostic gate passed, and phase 2 ran.

A second, weaker structure remains without step 3: a correlation of 0.166 at lag 12 output pixels at d0, which falls to 0.07 by d5. In the zoom [pics/nose-zoom-2x.png](pics/nose-zoom-2x.png), row "off", it shows as vertical streaks about 12 output pixels (8 render pixels) apart. Its period is the size of a block of 8 × 8 render pixels. This record did not find its source. It is outside step 3 and outside this ticket.

### One bright sample through step 3

`CutGridProbeTests` (in the patch) runs step 3 on the GPU, through the real accumulator. A wall fills a view of 256 × 128 render pixels. All samples are 0, except 50 single samples of light 81, one in each cell of 24 × 24 pixels. Each sample sits at a different place relative to the 8 × 8 blocks. For stage n, the test draws n frames of 0 and then the frame with the bright samples. So the fix runs at spacing 4, 2 and 1. The energy of a sample is the light in its cell after the fix, divided by the light that the sample's own pixel held before the fix.

| Stage, spacing | Mode | Energy, mean ± std | Energy, min..max | Receivers, min..max | RMS radius, render px |
|---|---|---:|---:|---:|---:|
| 0, spacing 4 | baseline | 1.000 ± 0 | 1.000..1.000 | 9..9 | 4.62 |
| 0, spacing 4 | A | 1.006 ± 0.099 | 0.889..1.222 | 8..11 | 4.71 |
| 0, spacing 4 | B | 0.939 ± 0.291 | 0.333..1.667 | 3..15 | 4.65 |
| 1, spacing 2 | baseline | 1.000 ± 0 | 1.000..1.000 | 9..9 | 2.31 |
| 1, spacing 2 | A | 0.996 ± 0.089 | 0.778..1.222 | 7..11 | 2.41 |
| 1, spacing 2 | B | 0.982 ± 0.287 | 0.444..1.889 | 4..17 | 2.41 |
| 2, spacing 1 | all three | 1.000 ± 0 | 1.000..1.000 | 9..9 | 1.15 |

The sheet [pics/impulse-sheet-8x.png](pics/impulse-sheet-8x.png) shows four of the bright samples per mode and stage, at 8 ×, with white at 1/9 of the sample's light:

- Baseline spreads each sample into a regular square of 9 dots, 4 pixels apart at stage 0 and 2 pixels apart at stage 1. Many such squares side by side form the grid.
- A spreads it into a turned square. A sample next to a block border falls into two blocks with different orientations and gives parts of two squares.
- B spreads it into an irregular scatter. The number of dots changes from sample to sample.
- At stage 2 (spacing 1), all three give the same solid 3 × 3 patch.

[pics/impulse-evolution-8x.png](pics/impulse-evolution-8x.png) follows one sample from d0 to d5. Baseline's squares compound into a plaid lattice by d2. A gives a round textured blob, and B an irregular blob. All modes keep the same share of the light at each draw. At d4 the clamp of step 4 starts and removes most of what is left, in all modes the same way (baseline 0.013, A 0.011, B 0.011 of the light).

A per-pixel stencil is a gather with a changing shape. So one source does not always reach 9 receivers, and its energy varies. The mean stays 1. A CPU copy of step 3 (`tools/impulse_model.py`, all 65,536 sources of a periodic 256 × 256 domain, with uniform random bytes in place of the mask) gives these values at spacing 4:

| Mode | Energy std | Energy min..max | Sources below 0.5 | Sources above 1.5 | Noise variance after the fix | White-noise correlation, lag 4 render px | Lag 8 |
|---|---:|---:|---:|---:|---:|---:|---:|
| baseline | 0 | 1..1 | 0 % | 0 % | 0.113 | 0.667 | 0.331 |
| A | 0.113 | 0.56..1.33 | 0 % | 0 % | 0.112 | 0.161 | 0.039 |
| B | 0.294 | 0.11..2.56 | 3.4 % | 5.1 % | 0.111 | 0.047 | 0.027 |

All three reduce independent noise by the same factor, about 1/9.

## The two candidates

Each orientation is the 3 × 3 square turned by θ: u = spacing × (cos θ, sin θ), v = u turned by 90 degrees, and the taps ±u, ±v, ±(u + v) and ±(u − v), each rounded to whole pixels. The table is constant in the shader. No orientation is computed at run time. Each orientation is 4 symmetric pairs, so the first moment is 0 and the second moment is the same along x and y.

| Spacing | θ, degrees | u, v, u + v, u − v | RMS radius against the square |
|---|---|---|---:|
| 4 | 0 | (4,0) (0,4) (4,4) (4,−4) | 1.000 |
| 4 | 11.25 | (4,1) (−1,4) (3,5) (5,−3) | 1.031 |
| 4 | 22.5 | (4,2) (−2,4) (2,5) (5,−2) | 1.010 |
| 4 | 33.75 | (3,2) (−2,3) (1,6) (6,−1) | 1.021 |
| 4 | 45 | (3,3) (−3,3) (0,6) (6,0) | 1.061 |
| 4 | 56.25 | (2,3) (−3,2) (−1,6) (6,1) | 1.021 |
| 4 | 67.5 | (2,4) (−4,2) (−2,5) (5,2) | 1.010 |
| 4 | 78.75 | (1,4) (−4,1) (−3,5) (5,3) | 1.031 |
| 2 | 0 | (2,0) (0,2) (2,2) (2,−2) | 1.000 |
| 2 | 26.57 | (2,1) (−1,2) (1,3) (3,−1) | 1.118 |
| 2 | 45 | (1,1) (−1,1) (0,3) (3,0) | 0.957 |
| 2 | −26.57 | (2,−1) (1,2) (3,1) (1,−3) | 1.118 |

Spacing 1 keeps the square in both candidates. An exact match of the second moments allows almost no integer stencils (`tools/stencil_search.py` finds 3 at each spacing). So the table rounds the turned squares. Over the whole palette, the RMS radius is 2 % above the square's at spacing 4 and 5 % above it at spacing 2.

- Candidate A takes the orientations 0, 22.5, 45 and 67.5 degrees at spacing 4, and all four at spacing 2. One block of 8 × 8 render pixels, the workgroup's block, takes one orientation, from `pcg3d(block, draw)`.
- Candidate B takes one of the eight orientations at spacing 4, or one of the four at spacing 2, per pixel. The choice is the radial byte of the existing blue-noise mask (`blue_noise.glsl`, `blueNoiseBytesOf(pixel, draw)`), shifted to 3 or 2 bits. The mask already sits in the frame set at binding 20, which the accumulator's layout includes.

Both read exactly 9 taps per pixel, with the same weights (each tap's history length) and the same surface test (`sameSurface`). They trace no new rays and use no sorting and no trigonometry. They use only features that every desktop Vulkan driver has.

## Phase 2: the candidates on the real cuts

### The cut onto the nose, −1.7507 stops

| Draw | Baseline lag 3 / 6 / 12 | A lag 3 / 6 / 12 | B lag 3 / 6 / 12 |
|---|---|---|---|
| d0 | −0.131 / **0.637** / 0.396 | −0.114 / 0.093 / 0.043 | −0.064 / **−0.014** / 0.026 |
| d1 | 0.160 / **0.402** / 0.187 | 0.002 / 0.060 / 0.049 | 0.002 / **−0.007** / 0.040 |
| d2 | 0.191 / 0.112 / 0.063 | 0.152 / 0.047 / 0.048 | 0.158 / 0.003 / 0.058 |
| d3 | 0.175 / 0.085 / 0.066 | 0.147 / 0.032 / 0.053 | 0.151 / −0.005 / 0.059 |
| d5 | 0.103 / 0.047 / 0.070 | 0.087 / 0.025 / 0.063 | 0.092 / 0.008 / 0.063 |
| d10 | 0.014 / 0.032 / 0.052 | 0.012 / 0.024 / 0.050 | 0.017 / 0.022 / 0.055 |
| d29 | 0.057 / 0.024 / 0.048 | 0.056 / 0.022 / 0.051 | 0.057 / 0.024 / 0.050 |

The lag-3 values of 0.15 to 0.19 at d2 and d3 are the same in all modes. They come from the fix at spacing 1, a plain blur of neighbors, which no candidate changes.

| Nose cut | Baseline | A | B | Off |
|---|---:|---:|---:|---:|
| ROI linear luminance, d0 | 0.03475 | 0.03532 (+1.6 %) | 0.03557 (+2.4 %) | 0.02927 (−15.8 %) |
| ROI linear luminance, d1 | 0.04144 | 0.04168 (+0.6 %) | 0.04268 (+3.0 %) | 0.03213 |
| ROI linear luminance, d5 | 0.06698 | 0.06716 (+0.3 %) | 0.06722 (+0.4 %) | 0.05818 |
| ROI linear luminance, d10 | 0.08836 | 0.08835 | 0.08836 | 0.08458 |
| Cone box linear luminance, d0 | 0.08645 | 0.08694 (+0.6 %) | 0.08740 (+1.1 %) | 0.06941 |
| Specks in the cone box, d0 | 18,716 | 17,613 | 16,974 | 20,284 |
| Specks, median of d0..d29 | 4,193 | 4,153 | 4,170 | 6,401 |
| Specks, median of d30..d64 | 858 | 852 | 862 | 855 |
| Shimmer of the nose box, d0..d14 | 7.863 | 7.757 | 7.873 | 10.494 |
| Shimmer, d15..d29 | 2.080 | 2.082 | 2.087 | 2.326 |
| Shimmer, d30..d64 | 1.422 | 1.422 | 1.425 | 1.433 |

The linear luminance is that of the output's sRGB values, after the tone map. No candidate loses light: both make the ROI slightly brighter in the first draws, and the difference is under 0.5 % from d5. All modes use the same rays and the same tap weights, so the cause is where the light lands under the nonlinear tone map. The record did not isolate it further. A speck is a pixel more than 12 luma above the median of its 7 × 7 neighbors, outside the lamps (ter-fe63's measure, here on the cone box, output 640..1279 × 0..359). Shimmer is the frame-to-frame RMS of the nose box's luma.

Visual reading of [pics/nose-strip-1x.png](pics/nose-strip-1x.png) and [pics/nose-zoom-2x.png](pics/nose-zoom-2x.png):

- Baseline d0 shows a dense lattice of dots in squares, with horizontal and vertical lines. d1 shows a finer lattice in places.
- A d0 shows a crosshatch of turned squares, with some diamond shapes and faint vertical lines. Patches of square lattice remain at d1.
- B d0 shows irregular grain with small ring-like clusters and no lattice. d1 is irregular.
- From d2, the three look alike: soft blotches. Small comb-like patches at d1 to d5 appear in all three modes, so step 3 at spacings 4 and 2 does not make them.
- Off shows isolated bright dots and the vertical streaks of the lag-12 structure.

The film [pics/nose-film-baseline-a-b.mp4](pics/nose-film-baseline-a-b.mp4) shows the cone box from d0 to d64 side by side: baseline, A, B and off. It is H.264, CRF 12, 60 fps, 2584 × 382 pixels and 65 frames, as `ffprobe` reports.

### The cut onto rect-lamps' glossy floor, 3.83 stops

| Lamps cut, floor box | Baseline | A | B |
|---|---:|---:|---:|
| Grain d0 | 0.02221 | 0.02064 | 0.02002 |
| Grain d1 | 0.01387 | 0.01349 | 0.01351 |
| Grain d2 | 0.01091 | 0.01081 | 0.01095 |
| Grain d5 | 0.01446 | 0.01448 | 0.01453 |
| Grain d29 | 0.01091 | 0.01091 | 0.01092 |
| Grain d64 | 0.00968 | 0.00969 | 0.00969 |
| Linear luminance d0 | 0.16608 | 0.16607 | 0.16609 |
| Shimmer d0..d14 | 0.876 | 0.872 | 0.877 |
| Shimmer d15..d29 | 0.767 | 0.767 | 0.767 |
| Shimmer d30..d64 | 0.751 | 0.751 | 0.751 |

Grain is ter-fe63's measure: the RMS of the floor box's linear luminance less its Gaussian blur of σ = 2, over its mean. The floor box is output 600..1319 × 620..999. Both candidates lower the transient grain at d0 by 7 to 10 %. They change nothing else on the floor. In [pics/lamps-strip-1x.png](pics/lamps-strip-1x.png), baseline d0 shows a faint fine grid, softer in A and B. No stripes or seams are visible in any mode. The film is [pics/lamps-film-baseline-a-b.mp4](pics/lamps-film-baseline-a-b.mp4) (H.264, CRF 12, 60 fps, 2176 × 402, 65 frames).

### Block borders and stripes

`tools/seams.py` measures the mean absolute luma step into each output column and row of the cone box, over d0 to d3, by its phase modulo 12 output pixels (one block of 8 render pixels). The resampling of 2 render pixels into 3 output pixels gives every mode a period of 3. So the block border's phase is compared with the other phases of its resampling phase. A seam at A's block borders would give a ratio above 1.

| Mode | Column border ratio | Row border ratio |
|---|---:|---:|
| baseline | 1.014 | 0.989 |
| A | 0.991 | 0.959 |
| B | 1.016 | 0.992 |
| off | 0.997 | 1.014 |

A shows no step at its block borders, and no mode shows stripes in the strips or the films.

### Edges

`tools/edges.py` finds the ceiling lamp in the nose crop: the largest group of pixels in the top 120 rows with luma above 150 and a color that moves by at most 2 from d0 to d29. It compares the ceiling in a band 1 to 6 output pixels from the lamp with a band 7 to 18 pixels away.

| d0 | Baseline | A | B | Off |
|---|---:|---:|---:|---:|
| Band 1..6 px, mean luma | 112.47 | 112.45 | 112.38 | 111.78 |
| Band 7..18 px, mean luma | 111.60 | 111.48 | 111.82 | 110.08 |
| Band 1..6 px, pixels above 150 | 912 | 912 | 912 | 912 |

The near band is not brighter in A or B than in the baseline, so no lamp light leaks across the edge. [pics/edge-lamp-3x.png](pics/edge-lamp-3x.png) shows the lamp's corner at 3 × at d0 and d1. The edge is sharp in all modes, with no halo. The lamp's own face shows a faint square dot pattern in the baseline, faint irregular dots in A and B, and stronger dots without the fix. The candidates change only the offsets. So `sameSurface` still rejects every tap on another surface, as the code shows.

### Settled frames

The fix runs only while a pixel's history is shorter than 4 draws. The candidates change only the code inside the fix. So a frame whose histories never passed through the fix is the same in every mode. The frames after a cut do not meet that condition: the slow history keeps a share of d0 to d3 until its cap of 32 draws has long passed. So d29 and d64 differ from the baseline by small amounts, which shrink with the slow blend:

| Mean absolute luma difference from the baseline (max) | d4 | d10 | d29 | d64 |
|---|---|---|---|---|
| Nose box, A | 2.10 (51) | 1.26 (45) | 0.61 (30) | 0.36 (19) |
| Nose box, B | 2.95 (66) | 1.71 (51) | 0.72 (39) | 0.39 (20) |
| Nose box, off | 18.20 (178) | 7.88 (141) | 1.99 (60) | 0.70 (31) |
| Floor box, A | 0.31 (5) | 0.29 (4) | 0.14 (2) | 0.09 (2) |
| Floor box, B | 0.35 (5) | 0.30 (4) | 0.14 (3) | 0.09 (2) |

The statistics of the settled frames match: lag 6 at d29 is 0.024, 0.022 and 0.024, the speck median of d30..d64 is 858, 852 and 862, and the shimmer of d30..d64 is 1.422, 1.422 and 1.425.

### Repeatability

| Pair of runs | Frames the same, byte for byte |
|---|---|
| main and the probe's baseline, nose cut | 126 of 126 |
| A, two runs, nose cut | 126 of 126 |
| B, two runs, nose cut | 126 of 126 |

`tables/determinism.json` holds the SHA-256 of the decoded frames d0, d29 and d64 of each take.

## Recommendation

Build candidate B into production:

1. In step 3 of `accumulate_spatial.comp`, take the offsets from the table above, chosen per pixel by the blue-noise mask's radial byte at the pixel and the draw. Keep the square at spacing 1. Keep the loop order of today's 3 × 3 loop, so that row 0 stays bit-identical to the square.
2. Declare the frame set's mask in the accumulator's spatial shader and include `blue_noise.glsl`.
3. Give the accumulator the draw's index. The push constants hold only `RegionSlot`, the index modulo 36, today.
4. Turn the probe's impulse test into behavior tests: a short history spreads one bright sample with no regular lattice, the mean of the bright samples' energy stays 1, and a constant sample stays constant.

Reasons:

- B removes the lag-6 and lag-12 grid at d0 and d1. A leaves 0.093 and 0.060 at lag 6 and a visible crosshatch.
- B keeps the rays, the 9 taps, the weights, the surface test, the noise reduction and the radius within 2 % at spacing 4.
- B changes no settled statistic, no edge and nothing on the glossy floor except a 10 % lower transient grain at d0.

The cost of B is one texel fetch of the mask per pixel in the three draws after a cut, and a varying energy per single bright sample (std 0.29, 3 to 15 receivers at spacing 4). The record did not time it. The fix runs only on histories shorter than 4 draws.

## Open points for the parent

1. Settled frames are not byte-identical after a cut, because the slow history keeps a share of d0 to d3. The differences shrink with the slow blend, and the settled statistics match. A frame whose histories never ran the fix is the same by construction.
2. The lag-12 streaks without the fix (0.166 at d0, vertical, 8 render pixels apart) have a source outside step 3. They are a candidate for a new ticket.
3. B spreads one bright sample's energy unevenly (0.33 to 1.67 in the GPU test, mean 1). A keeps it within 0.89 to 1.22 but leaves a crosshatch. The real cut shows fewer specks with B (16,974 against 18,716 at d0) and the same shimmer.
4. At spacing 2 the turned squares are 12 % wider or 4 % narrower than the square, because few integer offsets exist so close to the center. A production change could keep the square at spacing 2. The record did not test that split, so its effect on the grid at d1 is not known.

## Stage 2: production

The parent approved candidate B with two changes: only the specular signal turns its squares, and the mask frame is half a period ahead of the draw, `(drawIndex + 32) mod 64`. The diffuse signal and spacing 1 keep the axis-aligned square. [production/README.md](production/README.md) holds the full record. In short:

- The grid: on the nose cut, the autocorrelation at lag 6 falls from 0.637 to 0.001 at d0. It falls from 0.402 to 0.009 at d1 and from 0.112 to 0.014 at d2.
- The image: the linear luminance, the specks, the shimmer, the lamp edge, the block seams and the glossy floor's grain stay as on main. The floor's grain at d0 falls from 0.02221 to 0.01998.
- Repeatability: two production runs give the same 126 frames, byte for byte.
- GPU cost: a cut draw costs 0.081 to 0.096 ms more, near main's own run-to-run spread of 0.092 ms. The first production shader read the turns from constant arrays with a run-time index. On Metal it cost about 14 ms more per cut draw and 7 ms more per steady draw. The final shader packs the turns into `uvec4` words, and its frames are byte-identical to the first shader's.
- Tests: 11 behavior tests on the real accumulator and one push-constant test. Mutation testing killed 16 of 18 mutants. G11 is an equivalent mutant, and G17 (no bounds check) is a judgment call.

One fresh-context reviewer (Claude Opus 5.5, high effort) read `e9e0dc043..dfe58f7c8` and the record, and ran nothing. It passed correctness, ticket fulfillment and code quality. It decoded the 12 packed words by computation and found 9 distinct taps in 4 symmetric pairs for each turn, within the footprint limits. It checked the push-constant layout (12 fields, 48 bytes), the frame set's binding 20 for compute, and the draw index's reset on a cut. Its four findings were text only: a stale comment on the mask's binding, a wrong sample count in a test comment, a claim that every timed cut draw runs the fix, and the radius below. A later commit corrects all four.

A correction to stage 1: at spacing 4, the RMS radius of the turned squares is 2.3 % above the square's on average, not within 2 %. One turn, 45°, is 6.1 % above it. At spacing 2 the mean is 4.8 % above it.

Answers to the open points of stage 1:

1. Item 3: the uneven energy of one bright sample stays. The mean over the mask's places and phases is 1 within 0.04 in the GPU test.
2. Item 4: production turns the squares at spacing 2 too. The lag 6 at d1 falls from 0.402 to 0.009.

## Files

- [probe.patch](probe.patch): the probe over `e9e0dc043`. `git apply --check` passes on a clean checkout. It touches `FixStencilProbe.cs` (new), `GpuTypes.cs`, `accumulate.glsl`, `accumulate_spatial.comp` and `tests/Tertius.Rendering.Tests/CutGridProbeTests.cs` (new).
- `tools/`: `take.sh` and `batch.sh` (the takes under the shared GPU lock, with a disk floor), `extract.py` (crops and hashes of each lossless film, after which the film is deleted), `roi.py` (the grid measure), `roi_anchor.py` (the measure on ter-fe63's strip), `analyze.py` (tables, strips and films), `seams.py`, `edges.py`, `zoom.py`, `impulse_sheet.py` (reads the GPU test's dumps), `impulse_model.py` (the CPU copy of step 3) and `stencil_search.py`. The tools name their scratch folder `/private/tmp/t1-3er4`.
- `tables/`: `phase1-roi.json`, `anchor-roi-production-strip.txt`, `nose-summary.json`, `lamps-summary.json`, `impulse-gpu.json`, `impulse-model.json`, `seam-profile.json`, `edge-leak.json` and `determinism.json`.
- `pics/`: the strips, zooms, impulse sheets and films named above.
- `production/`: the stage 2 record of the production build against main.

To repeat a take: apply the patch, build `src/Tertius.Demo` in Release, and run `tools/batch.sh real-cut-nose -1.7507 "nose-b:probe-bin:TERTIUS_PROBE_FIX_STENCIL=b"` in the dev shell. To repeat the impulse test: `TERTIUS_PROBE_DUMP=<folder> dotnet test tests/Tertius.Rendering.Tests -c Checked --filter FullyQualifiedName~CutGridProbeTests`, then `tools/impulse_sheet.py <folder> <out>`.
