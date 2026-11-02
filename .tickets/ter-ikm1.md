---
id: ter-ikm1
status: open
deps: [ter-cvyx, ter-pywg, ter-nw4a, ter-s6zr]
links: []
created: 2026-11-02T21:32:41Z
type: chore
priority: 4
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, bounce-light]
---
# Rename PixelPlane and PixelSurface to SamplePlane and SampleSurface, after ter-cvyx merges

Found in ter-ipc1's review (finding 2). ter-ipc1 made one rule for where a depth sample's plane holds, for the reflections' pixels and the bounce light's half-resolution texels. The shared functions say sample (samplePlane, sampleSurface, PixelPlane.OfSample), but the types still say pixel: PixelPlane and PixelSurface in depth_pyramid.glsl and DepthPyramid.cs. So texelSurface returns a PixelSurface, SectorSearch.TexelPlane returns a PixelPlane, and every interface comment explains that the pixel can be a texel. Rename the types to SamplePlane and SampleSurface in both languages, with their uses in reflection.glsl, ScreenRay.cs, texel_surface.glsl, sectors.glsl, SectorSearch.cs, the tests and the docs. Wait until ter-cvyx merges, because it edits reflection.glsl and ScreenRay.cs. A pure rename: every image stays byte for byte.

## Acceptance Criteria

No type named PixelPlane or PixelSurface remains in src, tests or docs. The Checked and Release tests pass. No image changes.

