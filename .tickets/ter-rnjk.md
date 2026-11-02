---
id: ter-rnjk
status: closed
deps: [ter-f3g2]
links: [ter-f3g2]
created: 2026-11-02T01:00:44Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections]
---
# Frame-pure stochastic SSR: rays drawn from the GGX lobe by a fixed noise, a resolve that reuses neighbors' hits, and a blur

The user's choice of 2026-11-01 (ter-f3g2): fix the blocky reflections on rough surfaces (ter-y8z0's artifacts 2, 3 and 5) with design 2. Each pixel's ray takes a direction drawn from the GGX lobe by a fixed noise per pixel, the same every frame, as the screen-space light draws its noise. A resolve reuses the hits of neighboring pixels, weighted by this pixel's BRDF over the neighbor's pdf and normalized (Stachowiak 2015), and a depth- and normal-aware blur removes the noise. Nothing is kept between frames (decision 8 of docs/plans/rendering-performance.md). Mirrors stay mirrors: at roughness 0 the lobe is one direction. Starts from ter-f3g2's prototype, its settings, its measures against the normalized lobe reference and its cost. Updates docs/plans/screen-space-reflections.md (decisions 3 and 12 reopened by the user, the new passes) and ARCHITECTURE.md. Acceptance: the artifact views of ter-y8z0 lie close to the reference by ter-f3g2's measure, mirrors keep their image, the cost is measured with interleaved runs and two full traces, and the user sees screenshots.


## Notes

**2026-11-02T02:28:35Z**

The user's decision of 2026-11-01, after ter-f3g2's probe: SSR's roughness limit goes to 0.37 now, inside epic ter-b2da; below it, today's method (one ray with a cone) stays; stochastic SSR (ter-rnjk) is not built; ter-gf0t moves into the epic to give back part of the lamp glow on rough surfaces that lose SSR. Reason: the probe showed that stochastic SSR helps only between roughness 0.37 and 0.6, which the 0.37 limit removes from SSR anyway, at +3 to +6 ms measured and with grain that never settles, while the limit saves 1.65 to 2.0 ms and matches the 64-ray reference on the deck paint. This ticket closes without a build.
