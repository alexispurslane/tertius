---
id: ter-v0yz
status: closed
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


## Notes

**2026-10-25T21:25:57Z**

Merged into main on 2026-10-25 (3d9de0c8). Coverage of the changed C# lines: 1 of 1; the rest is GLSL and tests. ShadingNormalTests pins the exact vertex normal (22.7 px off before the fix) and glTF's decode for authored maps. The six reference images pass unchanged. No screenshots: the change moves a reflection by a third of a degree. The authored maps' half step is a note on the asset pipeline epic (ter-kge6).
