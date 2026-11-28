# Every source of light in the 1:41 cockpit (#20)

This list holds every lamp and every self-lit surface that can light the cockpit of the 1:41 shot. Positions are in the
shuttle's frame, in meters. The cockpit is the part of the cabin forward of z −8.7, with its floor at y −0.50.

## Lamps

The demo's lamps (`DemoScenes.Shuttle.cs`, `CeilingLamps`, `Lamps`, `SpawnLamps`) are rectangle lights with the color
(1, 0.9, 0.75). Each lamp is its own entity, holds its light, and draws a panel whose glow it does not send into the
volume. All nine lamps are inside the cabin.

| Lamp | Place (m) | Power (W) | Power per channel (W) | Face radiance (W/(m²·sr)) |
|---|---|---|---|---|
| `lamp-ceiling-aft` | (−0.65, 3.04, 2.30) to (0.65, 3.09, 3.30), faces down | 18 | (18, 16.2, 13.5) | (4.41, 3.97, 3.31) |
| `lamp-ceiling-mid-aft` | (−0.65, 3.04, −0.95) to (0.65, 3.09, 0.05), faces down | 18 | (18, 16.2, 13.5) | (4.41, 3.97, 3.31) |
| `lamp-ceiling-ramp` | (−0.60, 3.04, −3.55) to (0.60, 3.09, −2.55), faces down | 18 | (18, 16.2, 13.5) | (4.77, 4.30, 3.58) |
| `lamp-ceiling-fore` | (−0.52, 3.04, −6.05) to (0.52, 3.09, −5.15), faces down | 15 | (15, 13.5, 11.25) | (5.10, 4.59, 3.83) |
| `lamp-ceiling-cockpit` | (−0.32, 1.93, −10.95) to (0.32, 1.98, −10.35), faces down | 12 | (12, 10.8, 9) | (9.95, 8.95, 7.46) |
| `lamp-port-fore`, `lamp-starboard-fore` | side walls, y 1.25, z −5.0, 0.16 × 0.80 m, face inward | 4 each | (4, 3.6, 3) each | (9.95, 8.95, 7.46) |
| `lamp-port-aft`, `lamp-starboard-aft` | side walls, y 1.25, z +0.2, 0.16 × 0.80 m, face inward | 4 each | (4, 3.6, 3) each | (9.95, 8.95, 7.46) |
| All nine | | 97 | (97, 87.3, 72.75) | |

Only `lamp-ceiling-cockpit` hangs in the cockpit. The fore lamps are 5 m behind the pilot's seat, and the rest are
farther aft.

## Self-lit surfaces

Power is π × emission × area per channel, for one side. A double-sided material emits from both sides into the volume
(`GlbReader` gives it reversed copies of its triangles), so the volume receives twice the power of the table for those
rows.

| Entity | Material | Area (m²) | Emission (W/(m²·sr)) | Power per channel, one side (W) | Sides | Holds a light | Place |
|---|---|---|---|---|---|---|---|
| the nine lamp entities | `LampPanelMaterial` | as above | as above | as above | box faces | yes, sends no glow | inside |
| `shuttle-art` | `Cabin_Display` | 0.407 | (0.045, 0.155, 0.195) | (0.058, 0.198, 0.250) | one | no | inside, the console at z −12.0 to −12.2 |
| `shuttle-art` | `lunarshuttle_fuselage.glow0`, two ceiling patches | 0.046 | (4.45, 4.08, 2.47) | (0.64, 0.60, 0.36) | two | no | inside, at (±0.62, 2.03, −10.13) |
| `shuttle-art` | nose patches of `glow0` to `glow3`, eight | 1.37 | (4.45 to 51.65, ...) | (147.9, 141.3, 97.4) | two | no | outside, on the nose, z −14.15 to −14.94, y −1.42 to −2.18 |

The nose patches by material: `glow3` (0.780 m², (126.6, 121.3, 84.6) W), `glow2` (0.232 m², (13.0, 12.2, 8.0) W),
`glow1` (0.152 m², (5.4, 5.1, 3.2) W) and `glow0` (0.210 m², (2.9, 2.7, 1.6) W). `art-glow-vs-lamps.md` lists each
patch.

## Totals under variant 4

| Source | Red power, one side (W) | Red power that enters the volume (W) |
|---|---|---|
| The nine lamps | 97 | 97 (as direct light, then as its bounce) |
| The nose glow | 147.9 | 295.9 (both sides) |
| The ceiling glow0 patches | 0.64 | 1.28 (both sides) |
| `Cabin_Display` | 0.058 | 0.058 |

So the art's glow sends three times the red power of all nine lamps into the volume, and almost all of it comes from
the nose, outside the cabin.

## Shares of the light on the dark canopy and the panel

The shares are of the scene-linear light under variant 4, from the frame-60 HDR dumps of four takes with the normal
helmet (`tools/source_shares.py`): today, variant 4 (`glow`), N0 (the nose sends no glow) and N1 (the nose sends its
front side only). `nose-glow-cases.jpg` shows the four takes, and bug #47 explains the back-side path.

| Source | How the takes give it | Dark canopy, red | Dark canopy, luminance | Panel, red | Panel, luminance |
|---|---|---|---|---|---|
| The nine lamps, direct and bounced | today | 32.9% | 31.7% | 42.1% | 41.6% |
| The ceiling glow0 patches | N0 − today | 14.0% | 13.8% | 28.4% | 28.1% |
| The nose, front sides | N1 − N0 | 1.3% | 1.3% | 1.1% | 1.1% |
| The nose, back sides (bug #47) | variant 4 − N1 | 51.8% | 53.2% | 28.4% | 29.2% |
| The helmet's reflection (inside the lamps' share) | normal − black helmet | unmeasured | unmeasured | 1.2% | not computed |
| `Cabin_Display` (inside the lamps' share) | no take removes it | unmeasured | unmeasured | unmeasured | unmeasured |
| Each lamp alone | no take splits the lamps | unmeasured | unmeasured | unmeasured | unmeasured |

Notes on the table:

- Today's take keeps 2% of the gray glow, so the lamps' row also holds 2% of the glow rows. The lamp pilot (`lamp16`)
  and the restraint-off control used other settings, so they do not give a share under variant 4.
- The ceiling glow0 patches emit from both sides too, and the takes cannot split their two sides. So part of their
  share can be the same fault as #47.
- About half of the dark canopy's light and over a quarter of the panel's light under variant 4 come from the nose's
  back sides, which no real light path connects to the cabin.

## The ceiling patches against the panel

In N0 the two small ceiling glow0 patches raise the panel from 39.0 to 59.5 in 8-bit red. In the volume, the diffuse
light at the panel receiver goes from 0.00063 to 0.00153 in red, 2.4 times. This section checks that rise by
arithmetic (`tools/ceiling_patches.py`). The panel receiver is the one of `README.md`, "Direction count". It lies at
(1.23, 1.37, −10.19) in the shuttle frame, and its normal (−0.39, −0.84, +0.38) points down.

What each side of a patch faces:

- Each patch hangs 0.11 m under the fuselage skin, at (±0.66, 1.97, −10.07).
- The front side faces down and forward, (±0.21, −0.56, −0.80). It looks into the cockpit and meets the canopy frame
  (`Cabin_Frame`) 1.29 m ahead.
- The back side faces up and aft. Its ray crosses no cabin surface and meets the inside of the fuselage skin 2.52 m
  away. So the back side lights the shuttle's inside above and aft of the cockpit, not a closed cavity.

Direct light on the panel:

- The near patch (starboard, 0.84 m away) faces the panel's position with its front side (cosine 0.66). The panel's
  surface faces away from the patch (cosine −0.28). So neither side lights the panel directly.
- The far patch (port, 1.98 m) is occluded.

So the panel can get the patches' light only as a bounce, like the lamps' light. The panel faces away from the
12 W cockpit lamp too (cosine −0.12). Today the volume gives the panel 0.00063 red from all nine lamps (97 W). If all
of it came from the cockpit lamp alone, a bounce would give at most 0.00063 ÷ 12 W = 0.000053 per watt. The two
patches send 1.28 W of red from both sides, so a bounce would give at most about 0.000067. The volume gives 0.00090,
13 times that bound.

The receiver's lobes show where the excess comes from. `volumeLobes` in `volume_receive.glsl` weights the six axial
lobes by n × n:

| Lobe | Weight | Added red light (N0 − today) |
|---|---|---|
| −y, the light from below, which the panel really faces | 0.71 | 0.00000 |
| −x, the light from the side of the near patch | 0.15 | 0.00062 |
| +z, the light from aft | 0.14 | 0.00028 |

The −x lobe holds the light that arrives from the whole −x half of the sphere, also from above the panel's plane. The
near patch lies there, 0.65 m to the −x side and 0.6 m above. So 69% of the rise enters through a lobe that sees the
patch from behind the panel's surface. This is a different fault from #47: the receiver's basis, not the transport
through a wall.
