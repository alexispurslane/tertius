// The exposure of the frame and pre-exposure (docs/plans/auto-exposure.md, "Pre-exposure").
#ifndef TERTIUS_EXPOSURE_GLSL
#define TERTIUS_EXPOSURE_GLSL

#include "frame.glsl"

// The most that a pass writes into the HDR target, the emissive target or the impostor atlas, per channel
// (docs/plans/pbr-brdf.md, "The clamp"; ARCHITECTURE.md, "Portable Vulkan"): the largest finite half float, 65,504, less
// 1/64 of it, the room that the rounding of the glass blend and of the summed bloom levels needs below it. Past 65,504
// a GPU that is not Metal stores infinity. ShaderInput.MaxRadiance is the C# copy.
const float MaxRadiance = 64512.0;

// The factor by which this frame's passes multiply radiance: 2 to the power of the exposure in stops.
float exposureScale()
{
    return exp2(exposureStops);
}

// Radiance times `exposure`, at most MaxRadiance per channel. The cap applies before the product: the brightest radiance
// that the renderer reads, 2^64, times the largest exposure, 2^64, is past the largest single float, and a driver may
// assume that no operand of min is an infinity. MaxRadiance / exposure lies between 2^-48 and 2^80.
vec3 exposed(vec3 radiance, float exposure)
{
    return min(radiance, vec3(MaxRadiance / exposure)) * exposure;
}

// Radiance times this frame's exposure, at most MaxRadiance per channel.
vec3 preExposed(vec3 radiance)
{
    return exposed(radiance, exposureScale());
}

#endif
