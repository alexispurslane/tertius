---
id: ter-nl5u
status: in_progress
deps: [ter-zzyr]
links: []
created: 2026-10-25T13:40:27Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering]
---
# Light glossy surfaces with a prefiltered environment map of the sky backdrop

Today the ambient specular light is the hemisphere of two colors (World.Ambient, lighting.glsl ambientLight). So chrome and polished steel reflect a gradient between a sky color and a ground color, and look like gray plastic wherever no light makes a highlight. SSR (ter-jxv9) adds what is on screen, and it falls back to the same hemisphere where its ray leaves the screen. Glass reflects only the hemisphere and the lights.

An environment map is a cube map of what surrounds the camera: the star field, the far planets and moons, and later the atmosphere color (ter-6vhj). Prefiltered by roughness into its mip levels, with the GGX lobe of the split-sum method whose DFG table the renderer already has, it gives the ambient specular light of every glossy surface, and SSR's fallback. Found on 2026-10-25, when the user asked for glass, steel and chrome that look beautiful on spaceships.

Design to decide (two designs, then the user chooses): what the map holds (the backdrop only, or also far geometry), where it is captured and when it updates, its size and the prefilter (on the GPU, compute), whether the diffuse ambient light also comes from it or stays the hemisphere, local probes inside ships against one map, and how it replaces or feeds the per-frame sky and ground colors (ter-hro7) and the atmosphere color (ter-6vhj).

Per the project rules: a RendererOptions switch, its cost measured at 1080p, GPU pixel tests, and captures for the user.

## Acceptance Criteria

Chrome and polished steel reflect the star field and the far bodies in every direction, sharp when smooth and blurred when rough. SSR falls back to the map. Its cost is measured, and captures show it.


## Notes

**2026-10-25T13:46:18Z**

Design decisions (user, 2026-10-25): design A, the scene map. The map holds the whole scene seen from the camera: all three depth ranges, lit by the sun, and the star field, at 128 px per face. It updates one face per frame, so all six every six frames, and a headless still refreshes all six first. The diffuse ambient light also comes from the map, and the hemisphere goes away while the map is on. Settled with both offered designs: the map sits at the camera; a compute pass prefilters it into its mip levels with the GGX lobe of the split-sum method whose DFG table exists, so the build waits for the compute pipelines of ter-zzyr; it replaces the hemisphere as the ambient specular light and as SSR's fallback, and glass reflects it; a union switch NoEnvironmentMap or SceneEnvironmentMap(FaceSize, FacesPerFrame) in RendererOptions, on by default, off in the test fixtures, the hemisphere when off; GPU pixel tests (a mirror sphere reflects a bright object behind the camera, a rough sphere shows it blurred); cost measured at 1080p; captures for the user. The per-frame sky and ground colors (ter-hro7) are no longer needed: the user chose this knowing it.
