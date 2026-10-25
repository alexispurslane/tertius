---
id: ter-h7v9
status: open
deps: []
links: []
created: 2026-10-25T04:18:20Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering]
---
# The default flat normal map tilts every surface without a normal map by about 0.3 degrees

TextureStore.FlatNormal is the texel (128, 128, 255), and material.glsl decodes a normal map as rgb * 2 - 1. 128/255 is not exactly 0.5, so a material with no normal map gets a shading normal tilted by 1/255 along the tangent u and v, about 0.0055 rad. The fixer of ter-ptft found it on 2026-10-25: a roughness-0 metal, head on to the camera and the sun, shows its highlight about 2.5 px off center at 640x360. Under GGX at the roughness floor the error is visible on mirror-like surfaces. The fix is a design choice: for example a texel format or an encoding that stores 0 exactly, or a material that skips normal mapping when it has no map.

## Acceptance Criteria

A material with no normal map shades with its vertex normal exactly: a GPU test of a roughness-0 metal finds its highlight at the pixel that the geometry predicts.

