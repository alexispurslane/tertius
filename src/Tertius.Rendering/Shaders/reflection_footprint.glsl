// The reflection's footprint f of a forward pass, from the screen derivatives: the transparent pass and the impostor
// capture. The deferred surfaces' closed form, the screen tracer's misses and its hits' closed form take f from the
// G-buffer through withAmbientFootprint (gbuffer_surface.glsl) where the environment's specular term reads f, and p
// elsewhere, which gives the same light (docs/plans/environment-map.md, "Where it runs").
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
