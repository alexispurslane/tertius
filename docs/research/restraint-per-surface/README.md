# A restraint for each surface: three prototypes (#42)

RESULTS

## Terms

- **Cell face:** one of the six sides of one grid cell. The directional light volume keeps one record for each cell face, in three grids with cell widths of 0.25, 0.5 and 1 m.
- **Reflectance:** the diffuse color of a surface, three numbers from 0 to 1.
- **Direct light:** the light that the scene's lamps send to a cell face. The shading pass computes it once for a lighting group of up to 4 × 4 × 4 cells.
- **Saturation of a color:** (largest channel − smallest channel) / largest channel. Gray has 0, pure red has 1.
- **Gray level of a color:** its smallest channel. **Colored part:** the color minus its gray level in every channel.
- **Neutral share:** the fraction of neutral bounce light that the volume keeps, 0.02 (`NeutralSourceShare`).
- **Knee:** a weight of the saturation: 0 under 0.58, 1 above 0.78, and a smooth rise between them (`saturationKnee`).
- **Color gain:** a factor on the part of the first bounce that variants 2 and 3 keep in full (`COLOR_GAIN`, 1 unless a picture says otherwise).
- **Glow:** the light that a self-lit surface emits, as the raster captures it.
- **Today:** the code at `eff66e481` (the #22 branch), which is variant 0 of the switch.

## The variants

The user decided on 2026-11-28 ([#42](https://github.com/alexispurslane/tertius/issues/42#issuecomment-7242969945)): a self-lit surface sends exactly the light that it emits, and only light scattered off surfaces is held back, per surface, gray paint times gray light alone. So Design 1 is dropped, and every variant that stays sends glow in full. Open are pure Design 2 against the hybrid, and the two amounts (the neutral share and the color gain).

Commit `bd76475b8` puts the rule behind preprocessor constants in `src/Tertius.Rendering/Shaders/volume.glsl`. `RESTRAINT_VARIANT` selects the rule, `RESTRAINT_RAMP` the ramp of variant 3, `RESTRAINT_LAMPS` whether variant 3 judges the summed direct light (0) or each lamp alone (1), and `COLOR_GAIN` the gain. The defaults (0, 0, 0, 1.0) are today's rule. Stage 2 keeps the chosen rule and removes the switches. The render tool names a variant 0, 2, 4 or 3 with its ramp letter, such as 3A, and an L for each lamp alone, such as 3AL.

The three ramps of a saturation s:

| Ramp | ramp(s) |
|---|---|
| A | smoothstep(0.58, 0.78, s), today's knee |
| B | (s / 0.78)³, at most 1 |
| C | smoothstep(0, 0.78, s) |
| D | ((s − 0.1) / 0.44)^1.8, limited to 0 and 1. With the weighted split it matches the product rule with ramp B on the two beige walls under the warm-white lamp, within 0.8% in red and 2% in green, and only there. It passes saturated paint in full from 0.54 upward. |

| Variant | What the capture adds to the cell face, per unit of area | What the first bounce sends out, times area | Glow |
|---|---|---|---|
| 0, today | reflectance | restraint(sum × light / area) × area: the knee reads the whole cell face's sum | restrained for each surface |
| 2, Design 2: only gray on gray is held back | colored part in `color.rgb`, gray level in `color.w` | gain × C × L + G × (gain × λ + share × m) | in full |
| 3, the weighted split (ramp A, B or C) | w_p × colored part in `color.rgb`, gray level in `color.w`, with w_p = mix(share, 1, ramp(saturation of the reflectance)) | gain × C × L + G × (gain × w_L × λ + share × m), with w_L = mix(share, 1, ramp(saturation of L)) | in full |
| 4, today with unrestrained glow | reflectance | as today | in full |
| 5, the product rule on the mean paint (ramp B or C) | reflectance, as today | for each lamp: area × keep(mean paint × that lamp's light), where keep(P) = gain × w(P) × (P − g) + share × g, g the smallest channel of P and w(P) = mix(share, 1, ramp(saturation of P)) | in full |

Number 1 held Design 1 and now stops the shader build.

### Names of the variants in one place

| Rule | `render-variant.sh` name | Constants in `volume.glsl` (edit the `#define` lines) |
|---|---|---|
| today | `0` | `RESTRAINT_VARIANT 0` |
| today with glow in full | `4` | `RESTRAINT_VARIANT 4` |
| Design 2 | `2` | `RESTRAINT_VARIANT 2` |
| ramp B, summed direct light | `3B` | `RESTRAINT_VARIANT 3`, `RESTRAINT_RAMP 1`, `RESTRAINT_LAMPS 0` |
| ramp B, each lamp judged alone | `3BL` | `RESTRAINT_VARIANT 3`, `RESTRAINT_RAMP 1`, `RESTRAINT_LAMPS 1` |
| ramp C, summed direct light | `3C` | `RESTRAINT_VARIANT 3`, `RESTRAINT_RAMP 2`, `RESTRAINT_LAMPS 0` |
| ramp C, each lamp judged alone | `3CL` | `RESTRAINT_VARIANT 3`, `RESTRAINT_RAMP 2`, `RESTRAINT_LAMPS 1` |
| ramp A (today's knee, out) | `3A`, `3AL` | `RESTRAINT_VARIANT 3`, `RESTRAINT_RAMP 0` |
| ramp D, summed direct light / each lamp alone | `3D` / `3DL` | `RESTRAINT_VARIANT 3`, `RESTRAINT_RAMP 3`, `RESTRAINT_LAMPS` 0 / 1 |
| product rule on the mean paint, ramp B | `5B` | `RESTRAINT_VARIANT 5`, `RESTRAINT_RAMP 1` |
| product rule on the mean paint, ramp C | `5C` | `RESTRAINT_VARIANT 5`, `RESTRAINT_RAMP 2` |

The gain is the second argument of `render-variant.sh`, or `COLOR_GAIN` with a decimal point. Example: ramp C, each lamp judged alone, gain 4 is `render-variant.sh 3CL 4.0 <set>...`, or `RESTRAINT_VARIANT 3`, `RESTRAINT_RAMP 2`, `RESTRAINT_LAMPS 1`, `COLOR_GAIN 4.0`. Glow is in full in every variant but 0.

where

- C and G are the cell face's summed `color.rgb` and `color.w`.
- L is the direct light of the lighting group, m its gray level and λ its colored part.
- Nothing moves from a colored part into a gray level, so a faint tint gives a dim light of the same hue. With both weights at the share, the bounce is share × reflectance × light, today's value with its natural tint. With both weights at 1 it is Design 2.
- The second bounce uses the same function with the light that reaches the cell face from the volume in place of L, and with no gain.
- "Glow in full" keeps the once-only rule of `glowSent`: an entity that holds a light sends no glow, so its power counts once.

After the capture, variants 2 and 3 only add sums and multiply them by the light. The restriction pass of #22 adds a quarter of each child face's four color words to its parent face. So the light of one surface cannot depend on the grid that captures it or on the other surfaces in its cell.

The second bounce (`volume_source.comp`) uses the same rule with the light that reaches the cell face from the volume, and with no gain. Small choices of the prototype, settled by the design's own rules and confirmed by the line orchestrator:

- Variants 2 and 3 store the color words with 64 steps per unit. One capture fragment of 0.65 gray at the neutral share adds only about 3 units at one step per unit, so rounding would bias it by up to 15%.
- With `RESTRAINT_LAMPS` 1, `volume_shade.frag` defines `VOLUME_JUDGES_LAMPS`. Then its copy of the light loop in `lighting.glsl` (`shapeLight`) also sums what gray paint keeps of each lamp's light alone, in the specular words that the volume shade does not read. No other shader changes. Stage 2 gives that sum a field of its own.
- `whiteLightAt` skips a cell face when all four color words are 0, not only `color.rgb`, because Designs 2 and 3 hold a gray surface in `color.w` alone. With today's rule `color.w` is always 0. The #20 lead, who owns the lighting points, agreed.

## Set 1: the cost of glow in full on the deck

The full-art hangar deck (`tools/deck-still.json`: `demo-exit.json` at its frame 500, the deck camera) at 1280 × 720, the headless render scale of 2/3 (848 × 477 rendered) and the automatic exposure. Each run takes three captures a, b and c of a frozen world. The exposure did not move between them: −3.1500 stops today and −3.1507 stops with glow in full (`evidence/exposure/`). The picture is c. Variant 0 (today) against variant 4 (today's restraint of scattered light, glow in full), both at commit `7f7a3979e`.

| Today | Glow in full |
|---|---|
| ![today](evidence/deck-today.png) | ![glow in full](evidence/deck-glow-in-full.png) |

[`evidence/deck-glow-difference-x16.png`](evidence/deck-glow-difference-x16.png) is the absolute difference of the two display images, times 16. It shows only thin lines on edges, the noise of the antialiasing. No surface is brighter as a whole.

In the scene-linear frame (`tools/deck-shift.py`):

- The mean RGB rises from (3.3839, 3.4839, 2.9210) to (3.3841, 3.4842, 2.9212) W/(m² sr), by 0.01%.
- The largest change in a tile of an 8 × 4 split of the frame is 0.2%, on the floor at the bottom middle. All four largest tiles are floor tiles, and all four rise.

The volume holds much more light (`tools/volume-totals.py`). The 0.5 m grid's summed outgoing light rises from (11.25, 12.38, 8.25) to (585.56, 600.69, 421.69) cell faces × W/(m² sr), and the 1 m grid's from (22.93, 23.68, 16.99) to (1154.57, 1184.73, 832.44): 50 times more. The added light is (6.4, 6.6, 4.6) W/(m² sr) on 96 faces of the 0.5 m grid and 271 faces of the 1 m grid. That is the floor's `glow0` of the station art (6.54, 6.71, 4.71), saturation 0.30, which today keeps 2% ([list of the #20 lead](https://github.com/alexispurslane/tertius/blob/t3/restore-panel-orange-light/docs/research/cockpit-helmet-light/self-lit-surfaces.md)). The strips lie 3.9 to 7.7 m from the camera and behind it, so this view sees little of their light. The bay lamps hold lights and send no glow, so they do not change.

So at this pose the gray self-lit panels do not lift the deck visibly. A pose that faces the floor's glow strips can show more. This set does not measure that.

## Set 1b: the worst deck pose for glow in full

Set 1 found the floor's warm gray glow (`glow0` of the station art, (6.54, 6.71, 4.71) W/(m² sr), saturation 0.30, today kept 2%) in a band of the deck floor from about 5 m behind the deck camera, and in a band on top of the upper ring, facing up. Three views stand beside the floor band (`tools/prepare-render.py`, offsets from the deck camera's eye in the station frame, +X up):

| View | Eye offset (m) | Looks at | Exposure today | Exposure, glow in full | Frame mean change | Largest tile change |
|---|---|---|---:|---:|---:|---:|
| glow-back | (−1.0, 0, 3.0) | back over the floor band | −5.2960 | −5.3066 | +0.82% | +1.5% |
| glow-up | (−1.6, 0, 5.5) | up at the ring above the band | −2.8914 | −2.8914 (also fixed) | +2.4% | +419% |
| glow-along | (−1.0, 0, 5.5) | along the band | −3.7357 | −3.7514 | +0.64% | +12% |

Each run took three captures a, b and c of a frozen world, and the exposure did not move between them. The pictures are c. glow-up is the worst pose. Its glow-in-full render ran again at the fixed exposure −2.8914 stops, today's settled value (`evidence/exposure/`), so the pair below is comparable. The automatic exposure gave the same value.

![glow-up: today left, glow in full right, fixed at −2.8914 stops](evidence/glow-up-pair.png)

[`evidence/glow-up-pair-x8.png`](evidence/glow-up-pair-x8.png) is the same pair times 8, and [`evidence/glow-up-difference-x16.png`](evidence/glow-up-difference-x16.png) the absolute difference times 16.

What it shows:

- The upper 60% of the frame is the underside of the upper ring, a neutral surface 5.6 to 6.4 m above the deck. Today it is near black: a mean of (1.2, 1.2, 0.6) in 8-bit display levels. With glow in full it is a very dark warm gray: (6.3, 6.6, 4.0). In scene-linear light it rises 2.7 times, from (0.039, 0.040, 0.028) to (0.105, 0.108, 0.076) W/(m² sr), saturation 0.30, the hue of the floor glow. This is visible beside today, and it lifts a neutral surface.
- The lower part (the ring's lit balcony) does not change.
- At the lower edge of the lifted area the difference image shows wavy bands. They have the look of the volume's discrete transport directions over a long distance.

Where the added light comes from, as far as this pose can tell (the question of #47):

- The depth readback places the lifted pixels 4.6 to 5.4 m above the camera, from 0.2 m in front to 3.8 m behind it along the deck. The floor band lies 0.9 m below the camera, from 0.8 m behind to 3.2 m in front of it, and faces up. The ring's own glow band lies on top of the same slab, about 4.9 to 5.4 m above the camera, from 1.5 to 4.0 m behind it, and faces up, away from the underside.
- The added light is largest where the underside lies above the floor band (+0.105 at 0.2 m in front) and falls to +0.015 at 3.8 m behind, under the ring's own glow. A leak through the slab from the ring's top glow would peak under that glow. So most of the added light follows the floor band, which faces the underside. I do not know of a wall between them, but this pose does not prove that the path is clear.
- A take that answers it: the same pose with the ring's top glow removed (or the floor band removed), or the #47 diagnosis of the #20 lead (light that crosses solid geometry in the 1 m grid) run on this pose.

The research counters (`tools/counters.py`, today's rule, one frame each): 37,632 to 45,043 capture fragments before the axis test and 20,186 to 23,642 at the cell-face write; 170 to 181 lighting points with 3.8 to 4.2 visible lamps each, and at most 8 or 9 at one point.

## The mixed-paint crossing: the weighted split against the product rule on the mean paint

The crossing of #22: the red slab beside the white slab, the camera from x = 0 to 0.24 m and back, EV 0, 388 frames at 60 frames per second. Only the 0.25 m grid steps on the way, between camera positions 50 and 51 (x = 0.125 to 0.1275 m): the red slab moves from the 0.5 m grid's capture, where it shares a cell face with the white slab, into the 0.25 m grid's capture, where it is alone. Gain 1. The weighted split judges each lamp alone (3BL, 3CL); the scene has one lamp. Ramps B and C give the same weighted split here, because the red slab has saturation 1 and the white slab 0.

[`evidence/crossing-mixed-paint.mp4`](evidence/crossing-mixed-paint.mp4): top left today, top right the weighted split, bottom left the product rule on the mean paint with ramp B, bottom right with ramp C. [`evidence/crossing-mixed-paint-sheet.png`](evidence/crossing-mixed-paint-sheet.png) holds frames 96 to 107 around the step.

The largest one-frame change of the region's mean, in 8-bit display levels (`tools/crossing-steps.py`; "receiver" is the panel left of the slabs, "top" the band at its top where main showed a red patch). All largest changes fall at the grid's step, frames 101 to 102 on the way out or 285 to 286 on the way back. Two frames at one camera position differ by at most 0.01 levels.

| Rule | Receiver: largest step | Receiver: red after / before | Top band: largest step | Top band: red after / before |
|---|---:|---:|---:|---:|
| today | 95.5 | 7.94 | 86.6 | 8.51 |
| weighted split, ramp B or C | 1.6 | 0.992 | 4.6 | 1.049 |
| product rule on the mean paint, ramp B | 48.6 | 0.555 (on the way back) | 48.2 | 1.97 |
| product rule on the mean paint, ramp C | 12.6 | 1.13 | 16.9 | 1.21 |

- Today steps from dim pink to strong red, as in #22.
- The weighted split shows no visible step in the video. The receiver's mean changes by 0.8% in red at the step, and the top band by 4.9%. The red patch at the top of the receiver that main shows at the step is gone: the top band is red before and after.
- The product rule on the mean paint steps by 44% with ramp B and 13% with ramp C, both visible. In the 0.5 m cell face that holds both slabs, the mean paint (0.95, 0.5, 0.5) is not saturated enough for the ramp, so the red slab's light falls until the fine grid separates it.

By the line orchestrator's rule (a step above 10%), the weighted split stays the rule.

## The second-bounce crossing at gain 1

The scene restraint-second-bounce (see `tools/prepare-render.py`): a red floor slab lit from above by a white star, a gray ceiling slab above it whose underside only the floor's wash reaches, and the gray receiver behind both. Both slabs span the place where the 0.25 m grid's edge moves at x = 0.125 m. The camera path is the crossing of #22. These films were rendered at gain 1 with each lamp judged alone. The crossings at gain 4 that the line orchestrator ordered still wait for the slot.

[`evidence/second-bounce-frame-100.png`](evidence/second-bounce-frame-100.png): frame 100 (just before the step) of today, the weighted split with ramp B, and the product rule on the mean paint with ramp B, from top to bottom.

The largest one-frame change of each region's mean, in 8-bit display levels (`tools/crossing-steps.py --second`):

| Region | today | weighted split, ramp B, per lamp | product rule on the mean paint, ramp B |
|---|---:|---:|---:|
| ceiling underside | 2.92 (91 to 94, x1.03) | 2.92 | 2.92 |
| receiver, red-lit middle | 0.01 | 0.00 | 0.00 |
| receiver, gray-lit top | 0.08 | 0.08 | 0.08 |

- No rule makes a step on the receiver. The red floor shares no cell face with gray paint, so today's restraint already passes its light in full.
- The ceiling's step at the grid's step is the same 2.92 levels (3%) in all three rules, so the restraint does not cause it. It belongs to the transport.
- The ceiling's underside shows gray (91, 90, 90), not red. Gray light from elsewhere in the scene outweighs the floor's red wash on it, so this scene at gain 1 tests the second bounce of colored light only weakly.

## The ramp sheet: a faint tint gives a dim tinted wash

The user's choice of the ramp. The warm-white lamp (1, 0.9, 0.8) of set 2 lights a beige wall in the top row, reflectance (0.7, 0.6, 0.5), and a fainter beige in the bottom row, reflectance (0.62, 0.6, 0.56), closer to hull paint. The receiver gets only the wall's bounce light. Every picture has the same fixed exposure, EV 0, the "outside" pose, gain 1 and commit `bd76475b8`. Glow plays no part: no surface emits. Each label gives the receiver's mean scene-linear RGB in W/(m² sr) and its saturation.

![today, ramp A, ramp B, ramp C, Design 2; top: beige, bottom: faint beige](evidence/ramps-warm-white-lamp.png)

| Rule | Beige: receiver RGB | Saturation | Red against today | Faint beige: receiver RGB | Saturation | Red against today |
|---|---|---:|---:|---|---:|---:|
| today | (0.0193, 0.0149, 0.0111) | 0.43 | 1 | (0.0172, 0.0149, 0.0123) | 0.28 | 1 |
| ramp A | (0.0193, 0.0149, 0.0110) | 0.43 | 1.00 | (0.0171, 0.0149, 0.0124) | 0.28 | 1.00 |
| ramp B | (0.0349, 0.0221, 0.0110) | 0.68 | 1.81 | (0.0198, 0.0163, 0.0124) | 0.37 | 1.15 |
| ramp C | (0.1237, 0.0630, 0.0110) | 0.91 | 6.40 | (0.0453, 0.0293, 0.0124) | 0.73 | 2.64 |
| Design 2 | (0.4252, 0.2043, 0.0110) | 0.97 | 22.0 | (0.2498, 0.1394, 0.0124) | 0.95 | 14.6 |

Each render matches the arithmetic of the rule within 1% in its ratio to today (beige within 0.2%, faint beige within 0.9%). The arithmetic for the beige wall: w_p and w_L are the weights of the paint (saturation 0.29) and of the light (saturation 0.20).

| Ramp | w_p | w_L | Bounce of the wall per unit light | Red against today |
|---|---:|---:|---|---:|
| A | 0.020 | 0.020 | (0.0140, 0.0108, 0.0080), today's value | 1.00 |
| B | 0.068 | 0.037 | (0.0253, 0.0160, 0.0080) | 1.81 |
| C | 0.318 | 0.180 | (0.0897, 0.0456, 0.0080) | 6.40 |

What the pictures show:

- Ramp A is today on these two walls. Both tints lie under the knee of 0.58, so both weights stay at the share. The hue stays today's faint warm tint.
- Ramp B gives a dim warm wash: 1.8 times today's red on beige and 1.15 times on the faint beige. The faint beige looks almost like today.
- Ramp C gives a clear warm wash at a fraction of Design 2's strength: 6.4 times today's red on beige (Design 2: 22 times) and 2.6 times on the faint beige (Design 2: 14.6 times).
- No rule turns the tint gray. The blue channel, the gray level, is the same in every rule.

## Set 2 (record): Design 2 and the rejected hybrid

The user rejected the hybrid of this set on 2026-11-28, because it turned faint tints gray ([#42](https://github.com/alexispurslane/tertius/issues/42#issuecomment-7243692495)). Its pictures stay here as a record only. They came from commit `7f7a3979e`, whose variant 3 was the hybrid, and its renders are in `captures/issue42/rejected-hybrid-g1.0/`.

Both scenes keep the layout of the #22 probe (`tools/prepare-render.py`). A gray receiver panel (reflectance 0.65) stands 3 m ahead of the camera. A wall 0.48 m wide stands 1.9 m ahead, where the probe's slabs stand, outside the picture on the right. One star lights the wall's face that looks at the receiver, and its light does not reach the receiver's front. So the receiver shows only the wall's bounce light. The exposure is fixed at EV 0, with no environment map, no screen tracer and no upscaling, at the "outside" camera pose. Glow plays no part: no surface emits. Every variant at commit `7f7a3979e` and gain 1.

**Warm-white lamp (1, 0.9, 0.8) on a beige wall (0.7, 0.6, 0.5):**

![warm-white lamp: today, Design 2, rejected hybrid](evidence/warm-white-lamp.png)

**Orange lamp (1, 0.4, 0) on a gray wall (0.8, 0.8, 0.8):**

![orange lamp: today, Design 2, rejected hybrid](evidence/orange-lamp.png)

The receiver's mean scene-linear RGB in W/(m² sr) (`tools/stills.py`; the region is the panel from x = 1/16 to 5/8 and y = 1/9 to 5/6 of the frame), at the two camera poses:

| Scene | Variant | Receiver RGB, outside | Saturation | Receiver RGB, across | Saturation |
|---|---|---|---:|---|---:|
| warm-white on beige | today | (0.0193, 0.0149, 0.0111) | 0.43 | (0.0182, 0.0141, 0.0104) | 0.43 |
| warm-white on beige | Design 2 | (0.4252, 0.2043, 0.0110) | 0.97 | (0.4015, 0.1930, 0.0104) | 0.97 |
| warm-white on beige | rejected hybrid | (0.0155, 0.0155, 0.0155) | 0.00 | (0.0146, 0.0146, 0.0146) | 0.00 |
| orange on gray | today | (1.1064, 0.4426, 0) | 1.00 | (1.0441, 0.4177, 0) | 1.00 |
| orange on gray | Design 2 | (1.1043, 0.4417, 0) | 1.00 | (1.0428, 0.4171, 0) | 1.00 |
| orange on gray | rejected hybrid | (1.1043, 0.4417, 0) | 1.00 | (1.0428, 0.4171, 0) | 1.00 |

What the pictures show:

- Warm-white on beige decides between the two. Design 2 keeps the whole colored part of the beige paint under the warm light. The receiver gets 22 times today's red and turns strong orange at saturation 0.97. The hybrid's knees drop the faint tint of the paint (saturation 0.29) and of the light (saturation 0.2) into the gray level. So the hybrid holds the scene back like today, at about the same luminance, but neutral gray instead of today's faint warm tint.
- Orange on gray is the same in all three. Today keeps this light already, because the lamp's light is saturated, and both new rules keep it in full too. The 0.2% difference between today and the new rules comes from the second bounce.
- The two poses differ by 5 to 6% in every row and every variant alike. The receiver region shows a different part of the panel when the camera moves 0.15 m, so this difference is the view and not the restraint.

## Reproduce

`tools/render-variant.sh` applies the probe of #22 (`docs/research/fine-grid-outgoing-light/tools/prepare-render.py`) and adds the two lamp scenes (`tools/prepare-render.py`), sets the two constants, builds the Demo and renders the sets that it names. It restores the checkout on exit.

```powershell
nix develop --command bash docs/research/restraint-per-surface/tools/render-variant.sh 0 1.0 probe lamps crossing deck
nix develop --command bash docs/research/restraint-per-surface/tools/render-variant.sh 3 4.0 probe orange crossing
nix develop --command bash docs/research/restraint-per-surface/tools/compose.sh captures/issue42/v0-g1.0/crossing.mkv 'today' captures/issue42/v3-g1.0/crossing.mkv 'hybrid' docs/research/restraint-per-surface/evidence/crossing-hybrid.mp4
nix develop --command python3 -I docs/research/restraint-per-surface/tools/crossing-steps.py captures/issue42/v3-g1.0/crossing.mkv
nix develop --command python3 -I docs/research/restraint-per-surface/tools/stills.py captures/issue42/v3-g1.0/outside
nix develop --command python3 -I docs/research/fine-grid-outgoing-light/tools/measure-volume.py captures/issue42/v3-g1.0/outside/volume.bin --camera-x 0.05
```

| Tool | Use |
|---|---|
| `tools/prepare-render.py` | The #22 probe, plus the scenes `restraint-orange-lamp` and `restraint-warm-white` and their scripts. |
| `tools/render-variant.sh` | Renders the sets `probe`, `lamps`, `orange`, `crossing` and `deck` for one variant and gain into `captures/issue42/v<variant>-g<gain>/`. |
| `tools/deck-still.json` | The full-art hangar deck: `demo-exit.json` cut at its frame 500 (from `docs/research/t1-af5w-shadow-softness/tools/views/exit-deck.json`), then three captures a, b and c of a frozen world. |
| `tools/compose.sh` | Two crossing films side by side, and a sheet of the frames around the grid's step. |
| `tools/crossing-steps.py` | The steps between neighboring frames of a crossing film, in 8-bit display levels. |
| `tools/stills.py` | The whole frame's and the receiver's mean scene-linear RGB, with saturation. |
| `tools/deck-shift.py` | The color shift of the deck between two variants, by region, and the display difference times 16. |
| `tools/volume-totals.py` | The summed outgoing light that each grid holds. |
| `tools/sheet.sh` | One labeled row of stills of one capture from several variants. |
| `tools/counters.py` | The research counters of one frame (`COUNTERS=1`): capture fragments and visible lamps per lighting point. |
| `tools/compose-grid.sh` | Four crossing films in a 2 × 2 grid. |
| `tools/take.sh` | A list of `render-variant.sh` calls in one take, with logs in `captures/issue42/logs/`. |
