# Anti-aliasing

Ticket: `ter-gcm3`. Its tests use the [renderer snapshot tests](renderer-snapshot-tests.md).

## Goal

Add SMAA 1x (subpixel morphological anti-aliasing, single sample) as the default anti-aliasing. The user chose it over FXAA, provided that its cost stays low.

## Decisions

- SMAA 1x is on by default. A switch in `RendererOptions` turns it off. (User)
- The deterministic-rendering switch from the renderer snapshot tests work turns it off, so the existing snapshots stay valid. Separate snapshot scenes test SMAA itself.
- Measure the cost with GPU timestamps and report it. (User: the engine must be fast.)
- Toksvig roughness widening is optional. It reduces specular sparkle on bumpy, smooth surfaces. It widens the roughness, not a gloss. If the snapshots show sparkle, add it as a switchable feature.

## Design

SMAA runs after tone mapping, as three full-screen passes: luma edge detection, blending-weight calculation with the precomputed area and search textures, and neighborhood blending. The reference implementation is iryoku/smaa on GitHub. Check its license before you copy its shaders.

## Steps

1. Write CPU tests for the switch and the pass setup, and snapshot scenes with hard diagonal edges.
2. Port the three passes to GLSL 4.60 and add the area and search textures.
3. Measure the GPU time at 1920 × 1080, 2560 × 1440 and 3840 × 2160. Report the numbers.
4. Approve the new reference images after a real review.
