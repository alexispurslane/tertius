---
id: ter-uhls
status: closed
deps: [ter-nl5u]
links: []
created: 2026-10-26T00:20:49Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, linux]
---
# The environment map's shader sites in the portable site table

The portable float audit (ter-lozc) listed every shader site that can leave a float's domain in ARCHITECTURE.md, "Portable Vulkan", but it ran on main before the environment map (ter-nl5u) merged. The map's own shaders were reviewed only against the map design's "Portable Vulkan" section: environment_face.frag, environment_sky.frag, environment_downsample.comp, environment_prefilter.comp, environment_irradiance.comp, cube_faces.glsl, spherical_harmonics.glsl, environment.glsl, sky.glsl (reflectedStars, starWidth, starKeptShare, starMeanRadiance) and reflection_footprint.glsl. The merge of main into ter-nl5u moved the face to normalizedOr and MaxRadiance. Go through the remaining sites by the audit's rules and add a row for each to the site table, fixed or safe with the reason.

## Acceptance Criteria

Every site of the map's shaders has a row in the site table of ARCHITECTURE.md, "Portable Vulkan", and each fix has a test.

