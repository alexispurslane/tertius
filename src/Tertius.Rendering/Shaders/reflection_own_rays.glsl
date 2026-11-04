// The own-ray list of the half-resolution reflections (docs/plans/screen-space-reflections.md, "Rays at outlines"): the
// pixels whose texels' rays disagree, which reflection_upsample.comp appends and reflection_own_rays.comp traces. The
// list's layout and its append are own_rays.glsl's.
#ifndef TERTIUS_REFLECTION_OWN_RAYS_GLSL
#define TERTIUS_REFLECTION_OWN_RAYS_GLSL

#include "reflection_ray.glsl"
#include "own_rays.glsl"

OWN_RAY_LIST(3, 6, ownRays);

void appendOwnRay(ivec2 pixel)
APPEND_OWN_RAY(ownRays, pixel)

ivec2 ownRayPixel(uint entry)
{
    return unpackedPixel(ownRays.pixels[entry]);
}

#endif
