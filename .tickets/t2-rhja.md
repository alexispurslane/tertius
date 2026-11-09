---
id: t2-rhja
status: open
deps: []
links: []
created: 2026-11-09T01:15:12Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# Make the rectangle light cheaper per pixel

The rectangle light (ter-e436) adds about 3.0 to 3.6 ms to the frame span of the orbit cabin's view along its five ceiling panels on the M1 Max at 1280 x 720 (median of 241 frames, two runs each: 15.8 ms against 12.1 to 12.7 ms with the bulbs), and 3.3 ms to the demo surface's bay view. Per pixel and light it takes the solid angle (two arctangents) and two cut edge sums (up to five arctangents each) and a table sample.

Candidates, each to check against RectLightTests' tolerances and the GPU tests:
- Heitz's rational fit of theta / sin(theta) in the edge integral (the LTC code of Heitz and Hill 2017), which needs one normalization per corner and no arctangent. It is not exact; measure its error against the integral before adopting it.
- Leave the solid angle out on surfaces, where lightIrradiance multiplies by it and rectResponse divides by it. Only lit puffs and fibers need it. This changes what LightPath.irradiance means for a rectangle, so every reader of it must agree.
- Measure each with a GPU trace (tools/README.md, "GPU trace"), not the per-pass JSON, which MoltenVK smears across neighboring passes.

