---
id: ter-80o1
status: open
deps: [ter-vcfa, ter-s7c6, ter-odli]
links: []
created: 2026-11-03T00:07:59Z
type: epic
priority: 2
assignee: Alexis Purslane
tags: [rendering, lights, reflections]
---
# Panel lights: a lamp's light takes the shape of its glowing surface, so reflections show lamps at their true size and brightness

The user's choice (2026-11-02): panel lights, after FSR (ter-htg7) and the performance epic (ter-pq5y). Rejected: keep sphere lights and make each lamp's glow a globe the size of its light (no engine cost, but no lamp can be a flat panel); and running this before the performance work (then the budget work would measure the final lights). The design doc goes in docs/plans/panel-lights.md, written by the first child ticket.

Why. Every reflection of a lamp comes from its light, and the eye sees its glowing surface, a second store that the demo sets by hand (src/Tertius.Demo/DemoScenes.cs): a cabin lamp's light is a sphere of LampRadius = 0.05 m that sends its 10 W in all directions (LightUnits.PointIntensity = P / 4π, radiance about 101 W/(m²·sr)), while its panel is a 1.2 m square that glows with LampColor · P / (π A), about 2.2 W/(m²·sr), downward only. So a mirror floor shows a disc 10 cm wide, 46 times brighter than the panel, on a dark square (docs/plans/screen-space-reflections.md, 'A lamp's glow', the cost), and a rough floor straight below gets 0.8 W/sr where a true panel sends 3.2 W/sr, 4 times more. pbr-spheres' glow lamps already match, because their glow is their light's own sphere.

The model to design: a light takes the shape of its glowing surface (a rectangle for a panel, with the sphere of ter-gf0t kept for bulbs). Its specular and diffuse parts come from linearly transformed cosines (Heitz, Dupuy, Hill, Neubelt 2016) or a design that the design ticket compares with it. The glow is derived from the light, not set by hand, so the two stores cannot disagree: one store per emitter. An entity still sends its power once, so ter-b1j3's rule stays: SSR, the environment map and the bounce source read the glow dark, and the light's lobe shows the panel. Every reader of a light follows the same shape: the direct pass, the transparent pass, the environment faces, the impostor capture and the screen-space light. Shadows can stay at the light's center at first; the design says so and what it costs. Portable Vulkan only (the LTC tables as sampled images with required formats).

To measure: the direct pass's cost per panel light against a sphere light on demo-exit, and the frame's span against ter-pq5y's 12 ms budget after that epic. The user sees the cabin lamps reflect at their true size and brightness in a mirror floor and in the demo's paint.


## Notes

**2026-11-03T14:54:04Z**

2026-11-03: its dependencies on the closed ter-pq5y and ter-htg7 moved to ter-s7c6 (FSR in the frame) and ter-odli (the budget), by section 13 of docs/plans/temporal-renderer.md. Section 7, 'Panel lights', fits the LTC formula into ReSTIR DI.
