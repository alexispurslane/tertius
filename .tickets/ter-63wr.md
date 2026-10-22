---
id: ter-63wr
status: open
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
