// The reflection's footprint f of a forward pass, from the screen derivatives: the transparent pass and the impostor
// capture. The pass that adds the reflections' delta (the full-resolution trace, or the upsample at half resolution)
// takes f from the G-buffer in surfaceAt (gbuffer_surface.glsl). The ambient pass takes the same f in ambientSurfaceAt
// where its light reads f, and p elsewhere, which gives the same light, so the delta takes away the very term that the
// ambient pass added (docs/plans/environment-map.md, "Where it runs").
#ifndef TERTIUS_REFLECTION_FOOTPRINT_GLSL
#define TERTIUS_REFLECTION_FOOTPRINT_GLSL

#include "frame.glsl"

// f, in rad: how far the unit dominant direction d moves to the next pixel, at least p, the main view's pixel angle.
// Derivatives are defined only in uniform control flow, so the caller calls this at the top of main.
float reflectionFootprint(vec3 d)
{
    return max(frame.params.z, max(length(dFdx(d)), length(dFdy(d))));
}

#endif
