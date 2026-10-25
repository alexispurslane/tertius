---
id: ter-63wr
status: closed
deps: [ter-ptft]
links: []
created: 2026-10-11T20:44:45Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, long-term]
---
# Add a switchable bloom pass, so that the sun and other very bright things glow

The user saw that the sun looks far too small in the orbit scene's free play. A measurement on 2026-10-11 showed that the renderer draws it at its true size: a disc 6 px across at 1280 x 720 with the 60 degree vertical view, where the true 0.53 degree disc gives 6.4 px. It looks too small because the renderer has no glare. In eyes and lenses, the sun's light scatters into a halo many times the disc's size. Here, the sun's emission (60, 55, 45) saturates and the tone mapping clips it to a crisp white dot.

The user chose bloom over a bigger drawn disc, because a bigger disc would disagree with the eclipse math, which uses the sun's true angular radius.

Design to decide (two designs, then the user chooses): where bloom runs in the frame graph (on the HDR target before tone mapping), its threshold and its shape (for example a mip chain of downsamples and blurs), how the sun's emission level and SunIntensity feed it, and its cost per frame at 1080p. Per the project rules, measure its cost and make it switchable in RendererOptions, and keep it off for deterministic snapshot tests.

## Acceptance Criteria

With bloom on, the sun in the orbit scene shows a glow several times its disc, and other emissive things glow by their brightness. The disc itself keeps its true size. Bloom is a RendererOptions switch, its cost is measured and recorded, and the snapshot tests run with it off unless a test turns it on.


## Notes

**2026-10-22T00:09:45Z**

After the lights design (ter-bpvl), the HDR image holds pre-exposed radiance in SI units (radiance times the exposure of the previous frame, clamped to 65,504). Bloom works on those values.

## Design decisions (user, 2026-10-25)

Two designs were shown in the chat: A, lens scatter (all light, mixed in), and B, bright glow (a threshold, added on top). The user chose:

- Only bright light glows: a soft-knee threshold at white after the exposure, so that nothing pops on or off. The glow is added on top of the image by a strength setting. (User: dark areas stay clean, which keeps the contrast that the user prefers.)
- The smallest bright points glow faithfully: the first downsample has no Karis weighting, so a star, a glint or the 6 px sun glows by its full brightness. A glint that jumps between pixels can make its glow flicker until anti-aliasing calms it. (User)

Shared by both designs, and settled with them:

- Bloom reads the HDR image after every pass that writes it, and builds its own chain of images, each half the size of the one before, six levels by default. Down with Jimenez's 13-tap filter (Call of Duty: Advanced Warfare, SIGGRAPH 2014), up with a 3x3 tent filter, summing the levels.
- The glow lives in its own images. The tone map pass adds it, then applies the tone curve. The exposure meter measures the scene, not the glare.
- Nothing treats the sun as special. Its disc keeps its true size, and its glow comes from its brightness.
- Fragment passes, not compute passes, so bloom does not wait for the compute support of ter-zzyr.
- The setting is a union, NoBloom or Bloom(Threshold, Knee, Strength, Levels), in RendererOptions, on by default. Tests turn it off unless they test bloom.
- Side-by-side captures of the sun in orbit and the cabin lamps, with bloom off and on, go to the user before the default strength is final.
- Cost measured with the GPU timers. ARCHITECTURE.md updated.
