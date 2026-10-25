---
id: ter-v0yz
status: in_progress
deps: []
links: []
created: 2026-10-25T19:42:22Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering]
---
# A surface without a normal map tilts by 0.32 degrees: the flat normal decodes to (0.0039, 0.0039, 1)

Found by the environment map's Part 5 builder on 2026-10-25. TextureStore.FlatNormal is [128, 128, 255] in an 8-bit UNORM texture, and material.glsl decodes a normal map texel as xyz * 2 - 1, so the flat normal becomes (128/255 * 2 - 1, same, 1) = (0.0039, 0.0039, 1). Every surface without a normal map is tilted by about 0.32 degrees, and a mirror's reflection moves by about 0.64 degrees, about 11 px in a 10-degree view. Decide the encoding that makes the flat texel exact (for example an SNORM normal format, or a decode that maps 128 to 0), with the normal maps of the asset pipeline in mind, and pin it with a test that a flat-normal surface's shading normal equals its vertex normal.

