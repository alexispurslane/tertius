// The scene environment map's light, for the ambient source of lighting.glsl and the lit puffs
// (docs/plans/environment-map.md, "Reading the map").
#ifndef TERTIUS_ENVIRONMENT_GLSL
#define TERTIUS_ENVIRONMENT_GLSL

// ShaderConstants.EnvironmentMap: true while the renderer's setting is SceneEnvironmentMap, fixed for its life.
layout(constant_id = 0) const bool environmentMapOn = false;

#endif
