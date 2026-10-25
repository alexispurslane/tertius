// The exposure of the frame and pre-exposure (docs/plans/auto-exposure.md, "Pre-exposure").
#ifndef TERTIUS_EXPOSURE_GLSL
#define TERTIUS_EXPOSURE_GLSL

#include "frame.glsl"

// The largest finite half float: the HDR target and the emissive target store no more (docs/plans/pbr-brdf.md, "The clamp").
const float MaxRadiance = 65504.0;

// The factor by which this frame's passes multiply radiance: 2 to the power of the exposure in stops.
float exposureScale()
{
    return exp2(exposureStops);
}

// Radiance times the exposure, clamped to what a half float holds.
vec3 preExposed(vec3 radiance)
{
    return min(radiance * exposureScale(), vec3(MaxRadiance));
}

#endif
