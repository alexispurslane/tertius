# What a surface reads from the light volume at a cell face with surface (#49)

Issue #49: the final filter pass of the directional light volume gives a thin wall's unlit face the light of its lit face. The #42 research switch (commit 3aa8099ab) makes a cell face with surface read the one diagonal upwind corner, as the bounce pass does. This folder holds the test with a known answer that the brief asked for before any fix, and a third read that came out of it.

## The three reads

The final pass (`volume_filter.comp`, `finalPass == 1`) makes, for each cell and each of the six signed axis sides, the value that a surface on screen reads. For each of the 32 directions it needs the light that arrives at the cell.

- The old read (main): `incomingOf(volume.field[index])`, the sweep's blend of four upwind cells. For a direction such as (3, 1, −1), 6/9 of the blend comes from cells in the cell's own z layer.
- The upwind read (the #42 switch, the brief's settled fix): at a cell face whose bucket holds surface, `incidentAtSurface`, the outgoing light of the one upwind corner that lies across all three cell faces, `c − sign(step)`. Other cell faces keep the old read.
- The across-face read (a probe, [tools/probe-across-face.patch](tools/probe-across-face.patch)): at a cell face whose bucket holds surface, only the taps of the sweep's own stencil that lie across that face's axis (`q[axis] != c[axis]`), with the sweep's bilinear weights renormalized. For a direction whose major axis is the face's axis, all four taps cross, so it equals the old read. For (3, 1, −1) at a +z face, it keeps (x−1, y, z+1) and (x−1, y−1, z+1) with the weights 2/3 and 1/3.

## The scene with a known answer

All lengths are in cells of width c. The camera is at the origin and looks along −z. The scene has no lights, no ambient light and no environment.

- The emitter is a one-sided square of side 1c, radiance 8 in red, facing −z, at z = +3.68c.
- The receiver is a white box (base color 1, roughness 1, specular reflectance 0), 8c × 8c × 0.16c. Its front face is at z = −4.32c, so the distance between the planes is h = 8c.
- The irradiance from arithmetic at the plane point P that a pixel sees is E(P) = L ∫ h² / r⁴ dA over the emitter, by 64 × 64 midpoints. Both planes are parallel, so cos θ = h / r on both.
- The measured irradiance is π times the HDR red value: the receiver's outgoing radiance is ρ E / π with ρ = 1, in scene units at fixed exposure 0.
- At c = 0.25, 0.5 and 1 m the receiver reads only that grid. The numbers are identical at the three sizes, because the scene scales with the cell. The exception is the 120° view at 1 m, whose edge lies in the outer grid's fade.
- The large emitter is the same scene with a 16c × 16c emitter at z = +0.32c, h = 4.64c, and at h = 2c (0.25 m only, since the camera stands between the two planes).

| Scene | Exact | Old | Upwind | Across-face |
|---|---|---|---|---|
| small emitter, 60° view (±2.5c), mean E | 0.1103 | 0.1207 (+9.5%) | 0.0988 (−10.4%) | 0.1207 (+9.5%) |
| small emitter, center pixel, measured / exact | 0.1243 | 0.469 | 0.365 | 0.469 |
| small emitter, 120° view (±7c), mean (at 1 m) | 0.0624 | −1.5% (−4.2%) | −1.7% (−4.6%) | −1.5% (−4.2%) |
| large emitter 16c × 16c, h = 4.64c, mean | 19.37 | −4.9% | −4.4% | −4.4% |
| large emitter, h = 4.64c, central ±1c | 19.68 | −6.6% | −6.1% | −6.1% |
| large emitter, h = 2c (0.25 m only), mean | 23.90 | −11.4% | −8.7% | not run |

The thin wall (0.25 m grid): a 1 m × 1 m × 0.04 m gray wall (0.8) at z −1.12 to −1.08 m, lit on its far face by an orange star (1, 0.4, 0) of 3 W/m². The lit face's radiance is 0.8 · 3 / π = 0.764 in red. The camera looks at the unlit face from 1.08 m.

| Unlit face, red | Old | Upwind | Across-face |
|---|---|---|---|
| mean over the face (|x|, |y| < 0.48 m) | 0.0128 (1.67% of the lit face) | 0.00066 | 0.00066 |
| middle (|x|, |y| < 0.25 m) | 0.0164 | 0.0000 | 0.0000 |
| edge band (|x| > 0.40 m, |y| < 0.25 m), mean | 0.0109 | 0.0021 | 0.0021 |
| edge band, largest pixel | 0.0121 | 0.0032 | 0.0032 |

The upwind and across-face reads remove the light from the unlit face's middle. Over the whole face 5.2% of the old value stays, all of it in the edge bands. The #42 wall kept 3% because it is wider, so its bands weigh less. In the bands the surface's trilinear read blends in the cells beside the wall, which hold no surface and keep the old read.

## What causes the differences

- The upwind read takes the last hop from the diagonal corner, 1 cell sideways along each minor axis, instead of the 1/3 cell of the sweep's own slope. So each beam of a small source lands 2/3 cell too far along its travel. The receiver's middle, which lies between the beams, loses light.
- With a large emitter the field is smooth, so that shift does not matter. There the old read loses light, because 6/9 of its taps for the grazing directions pass through the receiver's own slab, which blocks part of them.
- The across-face read keeps the sweep's lateral position and drops only the taps in the cell's own layer. So it is as close as the better of the two other reads in every scene above.

## Files

- [tools/VolumeSurfaceReadMeasurement.cs.txt](tools/VolumeSurfaceReadMeasurement.cs.txt): the measurement test (`KnownAnswer`, `Wider`, `ThinWall`). Copy it into `tests/Tertius.Rendering.Tests/` as a `.cs` file and run it with `--filter-class "*.VolumeSurfaceReadTests"` and `ISSUE49_LOG=<file>`. It prints the numbers and asserts nothing.
- [tools/probe-across-face.patch](tools/probe-across-face.patch): the across-face read in the final pass, against main.
- `evidence/old*.log` main, `evidence/new*.log` the upwind read, `evidence/face.log` the across-face read. `old.log` and `new.log` are the first runs: in `old.log` the chip lines still count the chip's own pixels. The chip lines in all logs show no step, because the chip (0.05c) missed every raster sample of the volume, so they measure nothing.
