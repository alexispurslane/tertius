---
id: ter-ipc1
status: in_progress
deps: []
links: []
created: 2026-11-02T16:37:04Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, bounce-light]
---
# One rule for where a depth sample's plane holds, for the reflections' pixels and the bounce light's near steps

Found in ter-eqtb's review (finding 3). Two rules now decide where the plane of a depth sample, through its point along its geometric normal, holds. The reflections' pixel surface (pixelPlane and pixelSurface in depth_pyramid.glsl, PixelPlane and PixelSurface in DepthPyramid.cs) extends a full-resolution pixel's plane toward an edge neighbor only where each plane holds the other's center within 1 % in inverse depth, and otherwise stops it at the center line; toward the sky it always stops. The bounce light's near steps (stepSurfacePoint and depthsAround in sectors.glsl, SectorSearch.SurfacePoint and DepthsAround) extend a half-resolution texel's plane to the step's exact place where the plane's depth lies between the depths of the texel and its two neighbors toward the place, widened by CenterBias, with sky at SkyDepth, so the plane reaches up to half a texel toward the sky. Both share kappa (ThicknessShare). A later change to one rule, such as ter-y1s7's impostor normal, a new tolerance or a new resolution, must find both. The readers differ: a pixel must bound pyramid cells over its whole square, and a near step reads one sub-texel place.

## Design

Design it twice: (1) the continuation test at the step's texel, so a near step extends the plane toward a neighbor only where the two planes continue each other; (2) the depths-around rule for a pixel's edges, so a pixel's plane reaches an edge where the plane's depth there lies between the depths around. Report the image changes of each on the step view, the bounce light's corner tests and demo-exit's deck, and the cost.

## Acceptance Criteria

One function per language decides where a depth sample's plane holds, and both readers call it; the docs (screen-space-reflections.md 'The pixel's surface', ARCHITECTURE.md) name one rule; the user chose between the two designs with images.


## Notes

**2026-11-02T18:02:21Z**

2026-11-02: launched as workflow run bebaa068-a164-4d94-a183-f53f31faa869, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-ipc1-one-plane-rule, base 6c71a2943 (brief /tmp/briefs/ipc1.txt): a probe of the ticket's two designs that ends at a fork for the user.
