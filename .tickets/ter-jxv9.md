---
id: ter-jxv9
status: open
deps: [ter-ptft]
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
