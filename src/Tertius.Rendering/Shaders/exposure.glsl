// The exposure of the frame and pre-exposure (docs/plans/auto-exposure.md, "Pre-exposure").
#ifndef TERTIUS_EXPOSURE_GLSL
#define TERTIUS_EXPOSURE_GLSL

#include "frame.glsl"

// The most light per channel that the surfaces write into a pixel of the HDR target, the emissive target or the impostor
// atlas (ARCHITECTURE.md, "Portable Vulkan"). The puffs over a pixel add at most half as much, ShaderInput.PuffBudget,
// which a rounded blend at most doubles. So a pixel stays at or below 64,512, below the largest finite half float,
// 65,504, past which a GPU that is not Metal stores infinity. ShaderInput.MaxRadiance is the C# copy.
const float MaxRadiance = 32256.0;

// R_cut = 2^−13: a pre-exposed radiance that both tone curves show below half an 8-bit step, so a light below it changes no
// pixel. LightUnits.CutoffRadiance is the C# copy.
const float CutoffRadiance = 1.0 / 8192.0;

// The factor by which this frame's passes multiply radiance: 2 to the power of the exposure in stops.
float exposureScale()
{
    return exp2(exposureStops);
}

// Radiance times `exposure`, at most `cap` per channel. The cap applies before the product: the brightest radiance that
// the renderer reads, 2^64, times the largest exposure, 2^64, is past the largest single float, and a driver may assume
// that no operand of min is an infinity. The caps lie from the puffs' budget, 16,128, over 2^31 puffs to MaxRadiance, so
// cap / exposure lies between 2^-82 and 2^79, a normal single float.
vec3 exposed(vec3 radiance, float exposure, float cap)
{
    return min(radiance, vec3(cap / exposure)) * exposure;
}

// Radiance times `exposure`, at most MaxRadiance per channel.
vec3 exposed(vec3 radiance, float exposure)
{
    return exposed(radiance, exposure, MaxRadiance);
}

// Radiance times this frame's exposure, at most MaxRadiance per channel.
vec3 preExposed(vec3 radiance)
{
    return exposed(radiance, exposureScale());
}

#endif
