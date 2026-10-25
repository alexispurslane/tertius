# Anti-aliasing

Ticket: `ter-gcm3`, a member of the rendering upgrade epic ([rendering-upgrade.md](rendering-upgrade.md)).

## Goal

Add SMAA 1x (subpixel morphological anti-aliasing, single sample) as the default anti-aliasing. The user chose it over FXAA, provided that its cost stays low.

## Decisions

- SMAA 1x is on by default. A switch in `RendererOptions` turns it off. (User)
- Measure the cost with GPU timestamps and report it. (User: the engine must be fast.)
- The user allowed the download of iryoku/smaa on 2026-10-25, without a new design pass. (User)
- The tests are GPU pixel tests, not reference images, because no member of the epic adds reference images. The snapshot tests epic adds them later.
- The deterministic switch of the snapshot tests does not exist yet. Until it exists, the test fixtures turn SMAA off through its own switch, as they do for bloom. So the existing pixel tests keep their values.
- The switch is `RendererOptions.AntiAliasing`, an enum `AntiAliasing { None, Smaa }` with the default `Smaa`. It matches `ToneCurve`, the other choice without parameters. A headless script run takes `--anti-aliasing off|smaa`, as it takes `--tone-curve`.
- The quality preset is `SMAA_PRESET_HIGH`, fixed. The reference calls it 95 percent of the quality of the highest preset. A choice of preset waits until a user needs it.
- The edge detection is luma edge detection, with no predication. Predication needs a depth input, and the reference adds it only as an option.
- A stencil mask that skips pixels with no edge in the second pass is left out at first. The measured cost decides whether it comes in.
- Toksvig roughness widening is left out. If the captures show specular sparkle on bumpy, smooth surfaces, a ticket of its own adds it.

## Design

SMAA runs after tone mapping and before the overlay, as three full-screen passes:

1. Luma edge detection reads the tone-mapped image and writes an edge image.
2. Blending-weight calculation reads the edge image and the two precomputed tables, the area table and the search table, and writes a weight image.
3. Neighborhood blending reads the tone-mapped image and the weight image, and writes the output.

When SMAA is on, the tone map pass writes an intermediate image, and the neighborhood blending writes the output. When it is off, the tone map pass writes the output as before, and the three passes are not in the frame graph. The overlay draws after SMAA, so the frame outlines stay sharp.

The reference implementation is iryoku/smaa, commit `71c806a838bdd7d517df19192a20f0c61b3ca29d` (2013-11-06), under the MIT license. Its license does not require the notice in binary distributions. The shader header `SMAA.hlsl` compiles as GLSL 4 with `SMAA_GLSL_4`. The area table has 160 × 560 texels with two 8-bit channels. The search table has 64 × 16 texels with one 8-bit channel. Both come from the reference headers `AreaTex.h` and `SearchTex.h`, converted to raw bytes. The folder `src/Tertius.Rendering/ThirdParty/Smaa` holds these files, the license and their provenance.

## Steps

1. Write the tests first: the switch and its default, the pass setup with SMAA on and off, the script option, and GPU pixel tests on a hard diagonal edge.
2. Port the three passes to GLSL 4.60 with the reference header, and upload the two tables.
3. Measure the GPU time of the three passes at 1920 × 1080, 2560 × 1440 and 3840 × 2160. Report the numbers here and in the ticket.
4. Take headless captures with SMAA off and on, and review them by eye.
