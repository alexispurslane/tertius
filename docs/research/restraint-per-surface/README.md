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

Commit `1ae3e6170` puts the rule behind two preprocessor constants in `src/Tertius.Rendering/Shaders/volume.glsl`. `RESTRAINT_VARIANT` selects the rule and `COLOR_GAIN` the gain. The defaults, 0 and 1.0, are today's rule. Stage 2 keeps the chosen rule and removes both switches. Every picture in `evidence/` comes from this commit or from a commit with the same meaning of its variant.

| Variant | What the capture adds to the cell face, per unit of area | What the first bounce sends out, times area | Glow |
|---|---|---|---|
| 0, today | reflectance | restraint(sum × light / area) × area: the knee reads the whole cell face's sum | restrained for each surface |
| 2, Design 2: only gray on gray is held back | colored part in `color.rgb`, gray level in `color.w` | gain × C × L + G × (gain × λ + share × m) | in full |
| 3, hybrid | w × colored part in `color.rgb`; gray level + luminance((1 − w) × colored part) in `color.w`, with w = knee(reflectance) | gain × C × L + G × (gain × u × λ + share × (m + luminance((1 − u) × λ))), with u = knee(light) | in full |
| 4, today with unrestrained glow | reflectance | as today | in full |

Number 1 held Design 1 and now stops the shader build.

where

- C and G are the cell face's summed `color.rgb` and `color.w`.
- L is the direct light of the lighting group, m its gray level and λ its colored part.
- luminance(x) = 0.2126 x.r + 0.7152 x.g + 0.0722 x.b. A gray color (y, y, y) has luminance y, so a knee moves its dropped colored part into the gray level without a change of luminance.
- "Glow in full" keeps the once-only rule of `glowSent`: an entity that holds a light sends no glow, so its power counts once.

After the capture, variants 2 and 3 only add sums and multiply them by the light. The restriction pass of #22 adds a quarter of each child face's four color words to its parent face. So the light of one surface cannot depend on the grid that captures it or on the other surfaces in its cell.

The second bounce (`volume_source.comp`) uses the same rule with the light that reaches the cell face from the volume, and with no gain. Small choices of the prototype, settled by the design's own rules and confirmed by the line orchestrator:

- Variants 2 and 3 store the color words with 64 steps per unit. One capture fragment of 0.65 gray at the neutral share adds only about 3 units at one step per unit, so rounding would bias it by up to 15%.
- The hybrid multiplies the colored paint by the light as it is. The light's knee acts only on the light that falls on gray paint.
- `whiteLightAt` skips a cell face when all four color words are 0, not only `color.rgb`, because Designs 2 and 3 hold a gray surface in `color.w` alone. With today's rule `color.w` is always 0. The #20 lead, who owns the lighting points, agreed.

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
| `tools/deck-shift.py` | The color shift of the deck between two variants, by region. |
