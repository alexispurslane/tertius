// The exposure of the frame and pre-exposure (https://github.com/alexispurslane/tertius/wiki/auto-exposure, "Pre-exposure").
#ifndef TERTIUS_EXPOSURE_GLSL
#define TERTIUS_EXPOSURE_GLSL

#include "frame.glsl"

// The retained half-float indirect-estimator budget per channel. Original scene radiance uses RGBA32F,
// without this cap. ShaderInput.MaxRadiance is the CPU copy for the retained estimator helpers.
const float MaxRadiance = 32256.0;

// Raw source bound, above the supported light-buffer sum (storage.md). No exposure enters this bound.
const float SceneRadianceLimit = 1329227995784915872903807060280344576.0; // 2^120
// Downstream only: the meter and bloom keep the original scene source.
const float SceneDisplayLimit = 281474976710656.0; // 2^48

// R_cut = 2^−13: a pre-exposed radiance that both tone curves show below half an 8-bit step, so a light below it changes no
// pixel. LightUnits.CutoffRadiance is the C# copy.
const float CutoffRadiance = 1.0 / 8192.0;

// The factor by which this frame's passes multiply radiance: 2 to the power of the exposure in stops.
float exposureScale()
{
    return exp2(exposureStops);
}

// A finite nonnegative value times a finite nonnegative scale, bounded before any overflowing product or quotient.
// Scale at most 1 permits the product first; a larger scale permits cap / scale first. Shared by display exposure,
// bounded estimators, light falloff and bloom strength (ARCHITECTURE.md, "Portable Vulkan").
vec3 exposed(vec3 value, float scale, float cap)
{
    if (scale <= 1.0)
        return min(value * scale, vec3(cap));
    return min(value, vec3(cap / scale)) * scale;
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

// Apply display exposure after metering, bounding the operand before its potentially overflowing product.
vec3 displayExposed(vec3 sceneRadiance, float stops)
{
    return exposed(sceneRadiance, exp2(stops), SceneDisplayLimit);
}

#endif
