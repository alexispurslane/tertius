---
id: t2-supl
status: open
deps: []
links: []
created: 2026-11-12T20:23:21Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, exposure]
---
# The automatic exposure has several stable values in a scene with a large even surface: 0.037 stops apart in rigs

Found by the frame-time sweep on 2026-11-12 (docs/research/frame-time-sweep-2026-11-11/addendum.md, section A13).

The symptom: a headless still of scripts/rigs.json settles at an exposure of -9.3555 stops or of -9.3184 stops. Which one it takes depends on details that do not change the scene's light. A reflection step cap of 104 in place of 128 flips it at 1280 x 720. At 1920 x 1080 the still takes the second value at every cap from 104 to 256. The still's mean then differs by 3.8 %. In scripts/doorway.json the values lie 0.037 and 0.073 stops apart.

The cause, from the code: the meter counts the pre-exposed luminance in bins of 0.125 stops (meter.glsl, BinsPerStop = 8). The adapt step takes the trimmed mean of the bin centers (exposure_adapt.comp, binCenterStops). In rigs the floor is one even surface, and one band of 8 levels holds 43.5 % of the still's pixels. When that surface crosses a bin's edge, the mean steps by up to 0.125 stops times the surface's share. The metered luminance depends on the exposure itself, so the loop has several fixed points.

Why it matters: a comparison of two stills with the automatic exposure mixes the tested change with this step. It gave two wrong readings in the sweep: the doorway floor at a reflection cap of 64 (addendum A10) and rigs at a cap of 96. In a window the adapt step spreads the step over time, so no visible fault is known.

A possible repair, for the owner of the exposure design: let each bin give its mean luminance in place of its center, for example with a second buffer that holds a fixed-point sum per bin. The trimmed mean then moves without steps. Until then, compare stills at a fixed exposure.

To see it: render scripts/rigs.json headless at its own size of 1280 x 720, then with the script's width and height set to 1920 and 1080. Read exposure.json of both runs.

