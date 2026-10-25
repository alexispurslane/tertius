---
id: ter-b3h5
status: in_progress
deps: [ter-ptft, ter-zzyr]
links: []
created: 2026-10-11T20:51:00Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, long-term]
---
# Add screen-space ambient occlusion (SSAO) to the ambient light

A long-term render feature the user has always wanted. Today the ambient light is the hemisphere mix of AmbientLight.SkyColor and GroundColor by the surface normal (lighting.glsl), and nothing darkens creases, corners and contact points. SSAO estimates, for each pixel, how much of the hemisphere above the surface nearby geometry hides, from the depth and normals on screen, and scales the ambient light by it.

What the renderer has to build on: a deferred G-buffer with albedo, normal, specular and gloss, emission, and an R32F target with each pixel's Euclidean distance from the camera (0 for sky), written by gbuffer.frag. Lighting is a fullscreen pass, lighting.frag.

Design to decide (two designs, then the user chooses): the method (for example classic hemisphere sampling, horizon-based HBAO, or ground-truth GTAO, which matches a cosine-weighted visibility integral); full or half resolution; the noise pattern and the denoise (a bilateral blur that respects depth and normals); how it works across the renderer's three depth ranges (near 0.05 m to 2 km, middle to 2,000 km, far beyond), where a planet and a ship lie in different ranges; its radius in meters and how it fades with distance; and whether it also darkens local lights or only the ambient term.

Per the project rules: a RendererOptions switch, its cost measured at 1080p, fixed seeds and no temporal accumulation under the deterministic switch, and snapshot tests whose references are reviewed by eye.

## Acceptance Criteria

With SSAO on, creases, inner corners and the ground under objects get darker ambient light, and open surfaces keep theirs. The effect has no halos at depth edges, it is stable from frame to frame, it is switchable, its cost is measured, and snapshot tests cover it.


## Notes

**2026-10-22T00:22:07Z**

2026-10-21: the user put the snapshot tests epic (ter-rtxu) after the features that change the look. This ticket reviews its look by eye in headless screenshots, and ter-rtxu turns them into reference images later. The acceptance line about snapshot tests moves to ter-rtxu.

**2026-10-25T13:13:15Z**

Design decisions (user, 2026-10-25): GTAO (Jimenez 2016), the cosine-weighted visibility integral, at half resolution with an edge-aware upsample to full resolution. Compute passes, so the build waits for the compute pipelines of ter-zzyr. The reference is Intel's XeGTAO (MIT), whose download the user allowed. Shared by both offered designs and settled: it scales only the ambient light, the diffuse by the occlusion and the ambient specular by a specular occlusion derived from it (Lagarde 2014); it reads the distance target and the normals, so one pass serves all three depth ranges, and a radius of about 1 m fades out where it covers less than a pixel; fixed spatial noise and an edge-aware blur, no accumulation over frames; no multi-bounce brightening, because SSGI adds real bounced light; a union switch NoAmbientOcclusion or GroundTruthOcclusion(RadiusMeters, Slices, StepsPerSide) in RendererOptions, on by default, off in the test fixtures; GPU pixel tests (an inner corner darker than an open floor, an open floor unchanged, no dark halo beside a near silhouette); cost measured at 1080p; the default radius chosen from captures that the user sees.
