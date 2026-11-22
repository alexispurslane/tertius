# E2: the lit block's sender sum

2026-11-22. Ticket `ter-9t54`, experiment E2 of the lighting review.
This experiment uses Python and NumPy on the CPU. It builds no engine code.

## Result

The block's diffuse senders supply 76.56% of the reference's one-bounce light
at the back wall and 86.26% at the floor gap. Segment visibility changes neither
sum in this scene. Increasing the sender grid from 16 × 16 to 32 × 32 changes
either sum by less than 0.025%.

The second closed room receives false light when segment visibility is absent.
Its floor receives 4.753% of the lit room's rear-half floor-center irradiance.
Its far wall receives 12.599% of the lit room's back-wall-center irradiance.
Both values become exactly zero with segment visibility.

This supports the presence of useful light from the hidden face. It does not
establish agreement with the path trace. The sums remain 23.44% and 13.74% lower,
respectively. The second room also exceeds the review's few-percent leak criterion.

## Scene and sources

All coordinates below use the scene's world axes, in meters. Positive Y points up.
The camera looks mainly toward negative Z.

| Quantity | Value | Source |
|---|---|---|
| Room interior | X from −6 to 2, Y from 0 to 4, Z from −4 to 6 | Archived probe patch, checked against exported meshes |
| Slab thickness | 0.2 m | Same sources |
| Block bounds | X from −1.2 to 1.2, Y from 0 to 1.6, Z from −0.75 to 0.75 | Same sources |
| Emitting block face | Z = −0.75, normal (0, 0, −1) | Same sources |
| Surface diffuse albedo | Linear RGB (0.7, 0.7, 0.7), metallic 0 | Exported materials |
| Surface roughness | 0.9, except positive-X wall at 0.2 | Exported materials |
| Surface reflectance | 0.5, hence dielectric normal-incidence reflectivity 0.04 in the reference model | Export and archived `cycles_scene.py` |
| Key lamp position and axis | (0, 0.8, −2.15), toward positive Z | Export, shifted from camera-relative coordinates |
| Key lamp power and color | 10 W, linear RGB (1, 0.9, 0.75) | Probe patch and `DemoScenes.cs` |
| Key lamp RGB intensity | (11.879486, 10.691537, 8.909615) W/sr | Export |
| Key lamp cone | Inner half-angle 25°, outer half-angle 30° | Export and probe patch |
| Key lamp source radius | 0.05 m | Export |
| Neutral floor lamp | (−3.5, 3.9, 1.5), straight down, 10 W, same color and radius | Export and probe patch |
| Floor lamp cone and RGB intensity | Inner 18°, outer 25°; (16.986998, 15.288298, 12.740249) W/sr | Export |
| Camera position and target | (1, 1.5, 4.5), toward (−0.8, 0.7, −1) | Probe patch, consistent with exported camera axes |
| Camera vertical field of view | 60° | Export |
| Reference image size | 1920 × 1080 pixels | Archived Cycles script and original arrays |
| Sky and sun | Black sky, no sun | Export and archived Cycles script |

The scene has **two** spot lamps. The key lamp lights the hidden block face.
The floor lamp supplies the neutral floor patch used for normalization.
The sender sum includes only the key-lit block face, as E2 requires.
The path trace includes both lamps.

The repository sources are:

- `../../back-face-depth-layer/stage-2/probe-lit-block.patch`, the scene definition.
- `../../back-face-depth-layer/stage-2/fade-lit-front.md`, the measurement record.
- `../../back-face-depth-layer/stage-2/tools/lit-block/views-lit-block.json`, the exact region definitions and reference paths.
- `../../back-face-depth-layer/stage-2/tools/lit-block/regions.py`, the exact mask and one-pixel edge erosion.
- `../../back-face-depth-layer/stage-2/tools/lit-block/lit_ratios.py`, the reference normalization.
- `../../back-face-depth-layer/stage-2/tools/cycles_scene.py`, the reference materials, lamps, camera, and bounce limit.
- `src/Tertius.Rendering/Shaders/lighting.glsl`, the engine's cosine-domain smoothstep cone.

The original export and arrays still exist. They were read, not regenerated:

```text
/Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/
  export/back/scene.json
  export/back/mesh-<id>.bin
  ref/geo-normal.npy
  ref/geo-position.npy
  ref/front-b1-s1.npy
  ref/front-b1-s2.npy
  ref/back-b1-s1.npy
  ref/back-b1-s2.npy
```

The SHA-256 of `export/back/scene.json` is
`bddd5ca22631eae7427a06e5e4552182f8b7409ff677d65b1a19a31399f27967`.
`results.json` preserves its camera axes, lamp parameters, and exact mesh bounds.
The bounds retain the export's small floating-point errors.

The original runner `/tmp/t1-r9vj/seeds1b.sh` sets `REF_MAX_BOUNCES=1` and requests
2048 samples per pixel for seeds 1 and 2. The archived Cycles script applies this
limit to total, diffuse, and glossy bounces. No new Blender run was needed.

## Units and regions

The ticket's 0.085 and 0.064 are **dimensionless ratios**, not irradiances.
Each value is a region's mean scene-linear RGB-weighted radiance divided by the
neutral floor patch's mean radiance in `front-b1`. The weighting is
0.2126 red + 0.7152 green + 0.0722 blue. The archived tools label this quantity
linear luminance, with radiometric units W/(m² sr). It is not photometric cd/m².
The record prints these ratios as percentages: 8.49% and 6.44%.

The two neutral seeds average to **0.2220718073 W/(m² sr)**.
The two back-wall seeds average to **0.01885688028 W/(m² sr)**.
The two floor-gap seeds average to **0.01430630332 W/(m² sr)**.
The exact ratios are therefore 0.08491344 and 0.06442197.

| Region | Output pixel box, upper bounds excluded | Additional camera-relative filters | Pixels after erosion | World-coordinate extent of retained pixels, m |
|---|---|---|---:|---|
| Back wall above the block | X [760, 1390), Y [60, 545) | Normal (0, 0, 1), Z between −8.6 and −8.4 m | 174418 | X [−3.89837, 1.99232], Y [0.37309, 3.99233], Z = −4 |
| Floor behind, right gap | X [1300, 1420), Y [560, 820) | Normal (0, 1, 0), Z between −8.5 and −5.3 m | 9762 | X [1.29143, 1.99324], Y = 0, Z [−3.95098, −0.81973] |

Normal components must be within 0.05 of the requested normal. Position bounds
are strict. The masks lose a one-pixel border as in the original measurement.
The world-coordinate extents describe the mask, not rectangular integration areas.
The experiment averages output pixels with equal weight, not square meters of surface.

## Sender calculation

Each grid covers the cone's footprint bounding rectangle on the emitting face.
Its X bounds are approximately ±0.80829 m. Its Y bounds are 0 and 1.6 m.
Patch centers outside the cone get zero flux. No surviving patch is renormalized.

The key's exported intensity supplies inverse-square illumination, the face's
receiving cosine, and the engine's smoothstep attenuation between the two cone
cosines. Every sender is Lambert diffuse. These are the script's two flux steps:

```python
    radiance_area = ALBEDO * incident * area / math.pi
```

```python
    contribution = radiance_area[None, :] * emission_cosine * receiving_cosine / distance_squared
```

where

- `ALBEDO` = the block's diffuse reflectance, 0.7, without units.
- `incident` = the RGB-weighted irradiance at each patch center, in W/m².
- `area` = the area of one equal-size patch, in m².
- `math.pi` = π, the Lambert hemisphere normalization.
- `radiance_area` = each patch's outgoing diffuse radiance times its area, in W/sr.
- `emission_cosine` = the clamped cosine between the face normal and the direction to a receiver, from 0 to 1.
- `receiving_cosine` = the clamped cosine between the receiver normal and the direction to a sender, from 0 to 1.
- `distance_squared` = squared sender-center to receiver distance, in m².
- `contribution` = irradiance from each sender at each receiver, in W/m². Rows are receivers and columns are senders.

The script sums those contributions. It also multiplies the receiver irradiance
by 0.7/π to compare outgoing diffuse radiance with the reference image.
All scalar light values retain the reference's RGB weighting.

Visibility uses analytic segment–axis-aligned-box intersection against the six
room slabs and the block. Open segment endpoints do not count as blockers.
The implementation excludes 0.000001 of the segment parameter at either end to
avoid self-intersection from exported coordinates. Parallel tests use numerical
tolerances. This is geometric visibility, not a depth-buffer approximation.
The lamp fixtures do not cast shadows in the export, so they are not blockers.

## Comparison with the original one-bounce trace

The sender grid is 32 × 32. Each region uses 2048 receiver samples. A receiver is
the midpoint pixel of an equal-count stratum of the mask's row-major pixel list.
All columns below use the ticket's dimensionless normalization.
The final column divides the visibility-tested sum by the path trace.

| Region | Sum with visibility | Sum without visibility | Path trace, one bounce | Sum / path trace |
|---|---:|---:|---:|---:|
| Back wall above the block | 0.06501255 | 0.06501255 | 0.08491344 | 0.765633 |
| Floor behind, right gap | 0.05556854 | 0.05556854 | 0.06442197 | 0.862571 |

The visibility-tested irradiances are 0.06479514 W/m² at the wall and
0.05538272 W/m² at the floor gap. Their outgoing diffuse radiances are
0.01443745 and 0.01234021 W/(m² sr).

## Convergence and the largest sender

The receiver samples stay fixed while the sender grid changes. The patch share
of the region mean is the largest sender's mean contribution divided by the
region's mean total. The largest point share takes the largest such fraction at
any sampled receiver. Both visibility choices give the same values here.

| Grid | Active patches | Reflected flux, RGB-weighted W | Wall sum | Change from preceding grid | Floor-gap sum | Change from preceding grid |
|---|---:|---:|---:|---:|---:|---:|
| 8 × 8 | 52 of 64 | 5.426942 | 0.06516069 | — | 0.05570149 | — |
| 16 × 16 | 208 of 256 | 5.415673 | 0.06502817 | −0.20338% | 0.05558198 | −0.21457% |
| 32 × 32 | 816 of 1024 | 5.414390 | 0.06501255 | −0.02401% | 0.05556854 | −0.02416% |

| Grid | Wall: largest patch share of mean | Wall: largest point share | Floor: largest patch share of mean | Floor: largest point share |
|---|---:|---:|---:|---:|
| 8 × 8 | 3.01860% | 3.23268% | 4.86261% | 7.68028% |
| 16 × 16 | 0.76142% | 0.81005% | 1.39338% | 2.47084% |
| 32 × 32 | 0.19061% | 0.20256% | 0.34230% | 0.64361% |

At 32 × 32, increasing receivers from 2048 to 8192 changes the wall sum by
−0.06695% and the floor sum by +0.19513%. This check is separate from sender-grid
convergence. It is not a rigorous error bound. No patch dominates either region
at 32 × 32. These samples do not test an arbitrarily near receiver.

## Leakage into a second closed room

The second room is an empty copy translated by (0, 0, −10.2) m. Its interior is
X [−6, 2], Y [0, 4], Z [−14.2, −4.2] m. It shares the first room's negative-Z
wall slab. There is no doorway. Its far wall faces positive Z, toward the senders.

The first room's literal floor center is (−2, 0, 1). It receives **zero** from this
face because it lies behind the emitting plane. A leak divided by that zero is
undefined. The floor reference below is therefore the center of the first room's
floor **behind the block**, (−2, 0, −2.375). This choice is explicit, not a hidden
replacement of the requested center. The wall reference is the first room's
back-wall center, (−2, 2, −4).

| Receiver | Position, m | No-visibility irradiance, W/m² | Visibility-tested irradiance, W/m² | Lit-room reference irradiance, W/m² | Leak / lit-room reference |
|---|---|---:|---:|---:|---:|
| Second-room floor center | (−2, 0, −9.2) | 0.001990585 | 0 | 0.04187892, rear-half floor center | 4.75319% |
| Second-room far-wall center | (−2, 2, −14.2) | 0.008956583 | 0 | 0.07109039, back-wall center | 12.59887% |

The second-room outgoing diffuse radiances are 0.0004435361 and
0.001995678 W/(m² sr), respectively, without visibility. Both are zero with it.

## Leakage on the camera's side of the block

The camera-side floor probe at (0, 0, 2), with upward normal, gets exactly
0 W/m² with either visibility choice. All senders face negative Z. Every point
with Z greater than or equal to −0.75 is outside their emitting hemisphere.

No point in the original room that these senders cannot see receives positive
light in this box model. A point in the emitting hemisphere lies behind the
block's back plane. Its segment to that plane cannot pass through the block.
The room interior is convex, so that segment cannot cross a room wall either.
This also explains the identical two-region sums. It does not justify omitting
visibility in scenes with another blocker or another room.

## Assumptions and limits

- The exported geometry, lamp intensity, material values, camera, and masks were checked. No required scene number was missing.
- The sender model replaces the key's 0.05 m emitter with a point source. It does not integrate the finite emitter's shape.
- The sender model uses the engine's cosine-domain smoothstep. The reference exporter sets Blender's outer angle to 60° full width and `spot_blend` to approximately 1/6. Equal angular falloff between the two implementations was not established.
- Both sending and receiving surfaces use only the 0.7 Lambert diffuse component. The reference adds a GGX reflection component at both surfaces.
- The sender model omits the floor lamp's once-bounced light from the floor and every sender other than the key-lit block face. The original path trace includes those paths.
- The 23.44% and 13.74% deficits were measured. Their split between omitted paths, reflection components, cone falloff, and finite-source effects was not measured. No fitted scale factor was used.
- Receiver quadrature approximates the original pixel means. The 8192-receiver check is reported above. The path trace uses all retained pixels of both seeds.
- The second room, its placement, its comparison points, and its diffuse material are experiment choices, not part of the original scene.
- A list of point-like diffuse senders does not bound near-field blotches in arbitrary scenes. Only the specified regions were checked.
- No engine behavior, GPU cost, shadow-tile sampling, temporal change, or image quality was tested. No engine test, build, or GPU run was performed.

## Reproduction and checks

From the repository root, in PowerShell 7:

```powershell
nix develop --offline --command python3 -I docs/research/lighting-system-review-2026-11-22/e2-sender-sum/test_sender_sum.py
nix develop --offline --command bash -c 'OPENBLAS_NUM_THREADS=1 python3 -I docs/research/lighting-system-review-2026-11-22/e2-sender-sum/sender_sum.py'
```

`--evidence <folder>` can relocate the original evidence directory. The script
requires its original scene, meshes, geometry arrays, and four radiance arrays.
Those external arrays are not copied into this commit. If they are removed, the
experiment cannot reproduce the exact region comparison from the repository alone.
It fails on a missing input rather than substituting guessed scene values.

`results.json` holds the measured numbers before rounding. The kept run took
5.01 seconds for the full experiment, including the receiver-convergence check.
Seven CPU unit checks cover cone rejection, irradiance falloff, both angular
cosines, reflected flux, half-space emission, and segment visibility.
They use the standard-library unittest runner. They are not engine tests.
