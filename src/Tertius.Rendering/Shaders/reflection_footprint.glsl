// The reflection's footprint f of a forward pass, from the screen derivatives: the transparent pass and the impostor
// capture. The lighting pass and the pass that adds the reflections' delta (the full-resolution trace, or the upsample
// at half resolution) take f from the G-buffer in surfaceAt (gbuffer_surface.glsl), so that both see the same f
// (docs/plans/environment-map.md, "Where it runs").
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
