---
id: ter-442t
status: open
deps: []
links: [ter-89xq, ter-jnyk]
created: 2026-11-03T17:14:10Z
type: feature
priority: 4
assignee: Alexis Purslane
tags: [rendering, temporal, testing, later]
---
# Cycles ground-truth references for the renderer

Much later work (user, 2026-11-03). Nothing waits for this ticket, and it does not gate the screen tracer (ter-gayt) or any other ticket of the temporal renderer epic (ter-89xq).

Goal: measure how far the engine's image is from physics. Today every FLIP comparison uses the engine's own output as its reference, for example today's screen trace. That reference shows the error that a simplification adds, but not the error of the screen-space model itself. A Blender Cycles render of the same scene gives that absolute error.

How the two references divide the work:

- The engine's own reference (for example today's trace) isolates the error of one simplification. Keep it for spikes and simplification work.
- The Cycles reference measures the whole model's error: off-screen light, the thickness guess, the parallax of the environment maps, the simplifications, and any mismatch of materials or light units between the engine and Cycles.
- A margin can then come from the model's own distance to Cycles. A variant passes when its error against Cycles stays within a chosen margin of the baseline's error against Cycles.

The user's choice of scenes: purpose-built reference scenes, not the demo's own views. Example scenes are a lamp-lit white room for bounce light, the glossy floor with boxes for reflections, and the PBR spheres. Their materials are ones that both renderers share exactly. A later exporter of the demo's render snapshot stays out of scope, unless these scenes miss something that the demo shows.

What the work needs:

- The same scene in both renderers: geometry, transforms, camera, lights and environment.
- The same light units. The engine's lights are in SI watts. Blender's watt convention differs, for example a Blender spot light does not concentrate its power into its cone. The scene setup converts.
- The same materials. Use only materials that map exactly between the engine's GGX and Lambert model and Cycles' Principled BSDF.
- A comparison of linear HDR images before the tone map, with FLIP's HDR mode.
- CPU Cycles renders, reviewed once like a snapshot reference and then stored. GPU Cycles can abort in its kernel compile on this Mac (ter-jnyk).
- Tools in tools/blender exist already (bridge.sh, build.py, tertius_blender).

