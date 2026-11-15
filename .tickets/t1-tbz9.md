---
id: t1-tbz9
status: open
deps: []
links: [ter-ukw6, t1-3j2j]
created: 2026-11-15T13:18:54Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, lighting]
---
# Rooms get Earth's light only through one environment capture at the eye: a path trace is 16 to 58 times brighter in the cabin and in rect-lamps

Found by the reference experiment of the back-face layer (docs/research/back-face-depth-layer/stage-2/reference.md, part 1, 'The control without the sun'). A Cycles path trace of the engine's own exported frame is the reference. Under full lights, the reference's neutral floor region is 16.1 times as bright as the engine's in the cabin of demo-exit: 3.161 against 0.1961 W/(m² sr). In rect-lamps it is 58 times as bright: 1.19 against 0.02036. The reference's light there is blue. Without the sun, the factors fall to 2.5 and 2.0, so Earth's reflection of the sun is the missing light. The lead's reading: Earth's sunlit day side lights both rooms through the station's open hub and the side windows. The engine gets Earth's light only through its environment map, one capture at the eye, and the walls hide Earth from that point. Not checked: a black-Earth control for the cabin, and Earth's real texture (the reference uses Earth's mean color). Pictures: stage-2/cabin-full.png and stage-2/rect-lamps-window-full.png, last panel each. This is a design question for the lighting, outside the epic ter-kh4g.


## The two crops (added 2026-11-15)

Each image has three panels of one crop under full lights. The left panel is the engine's still under the 10 % rule. The middle panel is the path trace at the engine's exposure. The right panel is the path trace at the exposure that gives its neutral floor region the engine's luminance. `docs/research/back-face-depth-layer/stage-2/tools/earth_light.py` made both from the kept data of the reference experiment.

| Image | Crop | Engine's exposure | The right panel's exposure | Neutral floor: engine / path trace, W/(m² sr) |
|---|---|---:|---:|---|
| `docs/research/back-face-depth-layer/stage-2/earth-light-rect-lamps.png` | The side window of `rect-lamps`, x 1700 to 1920, y 440 to 880 | 2.787 stops | 5.87 stops less | 0.0204 / 1.190 |
| `docs/research/back-face-depth-layer/stage-2/earth-light-cabin.png` | The cabin's left wall, x 540 to 680, y 340 to 720 | −0.390 stops | 4.01 stops less | 0.196 / 3.160 |

What the images show: at the engine's exposure, the path trace of both crops is almost white. At the matched exposure, it is an even blue.

## How sure is the path trace's Earth

What supports its radiance:

- The path trace takes the sun and Earth's material from the engine's own export. The sun gives 1,361 W/m² in the cabin and 1,372 W/m² in `rect-lamps`.
- A calibration render gave 0.31830 W/(m² sr) for a white plate under 1 W/m², where the exact value is 1/π = 0.31831.
- Earth emits its own Lambert reflection of the sun: albedo × irradiance × cos / π. The albedo is the area-weighted mean of the engine's `EarthGrid` texture in linear light, (0.0840, 0.1655, 0.4655). At the point below the sun, that is about 74 W/(m² sr) of luminance.
- The emitter form agrees with a path-traced Earth: the cabin's floor is 3.29 W/(m² sr) against 3.26.

What is not established:

- No directly seen Earth pixel was compared between the engine and the path trace. The 8-bit stills clip there.
- The two crops raise a doubt. In `rect-lamps`, the engine shows black through the window's pane, and the path trace shows blue. In the cabin's top right corner, the engine shows bright blue, and the path trace shows dark blue. So the two renderers do not agree on what the camera sees of Earth.
- Until that is explained, the factors 16 and 58 are measurements of this path trace, not yet of the truth. The control without the sun does not depend on Earth, so the factors 2.0 and 2.5 of ticket `t1-mybi` stand.

Part 4 of ticket `t1-3j2j` does that comparison: Earth's place in the engine's frame against the export, and the radiance of a directly seen Earth pixel.
