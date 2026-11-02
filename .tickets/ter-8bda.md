---
id: ter-8bda
status: open
deps: [ter-b2da]
links: []
created: 2026-11-02T01:13:12Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, reflections, performance]
---
# SSR's roughness limit from 0.6 to 0.37, after epic ter-b2da

The user's decision of 2026-11-01: after epic ter-b2da, which keeps the deck floor's SSR on as its test case, the default ConeTracedReflections.MaxRoughness goes from 0.6 (the user's fork-2 choice of 2026-10-25) to 0.37. The roughness fade clamp(2 - 2 r / MaxRoughness, 0, 1) then gives full SSR up to roughness 0.185 and none from 0.37, so the demo's paint (roughness 0.38) shows only the environment map's prefiltered sheen. Why: ter-y8z0's normalized 64-ray reference shows that at roughness 0.38 the screen adds almost nothing over the environment map on the deck (+0.1 levels of 255 in the blocky box), and ter-y8z0 measured the saving of 0.37: 2.10 ms on the deck window and 1.38 ms on the floor window, mostly the trace and the upsample, while the depth and radiance pyramids (about 1.25 ms) still run. This ticket also asks whether the pyramids can skip a frame or a region with no reflecting pixel, and measures it. Snapshot references of the review scenes with rough materials change, so each re-approval opens the reference, the actual image and the FLIP heat map. Updates docs/plans/screen-space-reflections.md (the fork-2 decision) and ARCHITECTURE.md, and sends the user screenshots of the deck, the floor film and pbr-spheres before and after.

