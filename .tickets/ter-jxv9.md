---
id: ter-jxv9
status: in_progress
deps: [ter-ptft, ter-zzyr]
links: [ter-a24f]
created: 2026-10-22T00:22:07Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, long-term]
---
# Add screen-space reflections (SSR) to glossy surfaces

Today a surface reflects only the lights: the ambient term is diffuse only (ambientLight in lighting.glsl), so a glossy floor, a canopy or a hull in shadow shows no reflection of its surroundings. SSR traces, for each glossy pixel, the mirror ray through the depth on screen, and where the ray hits something on screen, it adds the radiance of that pixel, weighted by the Fresnel term of the material.

What the renderer has to build on: a deferred G-buffer with albedo, normal, specular and gloss, emission, and an R32F target with each pixel's Euclidean distance from the camera (0 for sky). The lighting pass writes the HDR target. Materials are energy-normalized Blinn-Phong with Schlick Fresnel, F0 = 0.16 * specular^2, so F0 is at most 0.16: reflections show most at grazing angles, on floors and canopies. A move to metallic materials would make them show more.

Design to decide (two designs, then the user chooses): the tracing (a linear march in view space, or a hierarchical depth buffer, Hi-Z, which SSGI could share); which image the rays sample (the HDR target of this frame before the transparent pass, or the previous frame reprojected); glossy reflections (a cone over a mip chain of the HDR image, sized by the gloss exponent, or stochastic rays with a denoise); what a ray that leaves the screen or passes behind an object falls back to (a fade to the ambient term, until reflection probes exist); how it works across the three depth ranges; and its resolution.

Per the project rules: a RendererOptions switch, its cost measured at 1080p, and no temporal accumulation under the deterministic switch. Review it by eye in headless screenshots. The snapshot tests epic (ter-rtxu) comes after it and locks in its look.

## Acceptance Criteria

With SSR on, a glossy floor reflects the objects on screen above it, the reflection blurs as the gloss falls, and it fades out where its ray leaves the screen, with no hard edge. It is switchable, its cost is measured, and screenshots show it.


## Notes

**2026-10-22T00:25:39Z**

2026-10-21: this ticket now waits for the PBR BRDF (ter-ptft). Its note on Blinn-Phong and the F0 cap of 0.16 describes the renderer before that ticket. After it, reflections weight by the Fresnel term of the PBR material and blur by its roughness, and metals reflect with their color.

**2026-10-25T13:28:35Z**

Design decisions (user, 2026-10-25): Hi-Z tracing over a min-max pyramid of depth, which SSGI can reuse; the rays read this frame's lit HDR image, before the transparent pass, so glass and puffs do not show in reflections; rough surfaces blur by a cone over a mip chain of that image, sized by the roughness, with no noise; tracing at full resolution. Settled with both offered designs: where a ray hits, its radiance replaces the hemisphere ambient specular term, with the same weight from the BRDF tables and the same energy compensation, and where it leaves the screen, hits nothing or reaches the sky, it fades back to the hemisphere term with no hard edge; above a maximum roughness SSR fades out; rays read the distance target, so one pass serves all three depth ranges, and the assumed thickness grows with distance; compute passes, so the build waits for the compute pipelines of ter-zzyr; a union switch NoReflections or ConeTracedReflections(MaxRoughness, MaxSteps) in RendererOptions, on by default, off in the test fixtures; GPU pixel tests (a bright object above a mirror floor shows in the floor, a rougher floor blurs it, the reflection fades at the screen edge); cost measured at 1080p; captures for the user before the merge.

**2026-10-25T14:21:50Z**

The design doc is on branch ter-jxv9-ssr (32a3b989). Its open forks, settled by the orchestrator on 2026-10-25: (1) the cone takes one trilinear read of the radiance pyramid at the hit, option A, by the design preference for a simple, honest mechanism; Uludag's march of inscribed circles with a visibility pyramid can follow in a ticket of its own if the captures show front and back surfaces blending across depth edges. (2) The default MaxRoughness comes from the Part 6 captures and cost runs at 0.6 and 0.3, which the user sees. (3) The setting takes the form of STYLE.md's table for struct cases: a [GeneratedUnion] of positional record structs with a valid zero case NoReflections and a Problem check, as the exposure and SSAO settings have. Also: occludedAmbient returns the occluded AmbientTerms, not their sum, so SSR can replace the occluded specular term (told to the SSAO builders).
