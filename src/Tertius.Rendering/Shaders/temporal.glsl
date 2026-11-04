// What the temporal signals share (docs/plans/temporal-renderer.md, sections 1 and 8): the material factors, by which a
// producer divides its light and the composite multiplies it back in (decision 18), and the test of a neighbor's surface,
// which the accumulator's fix and clamp read.
#ifndef TERTIUS_TEMPORAL_GLSL
#define TERTIUS_TEMPORAL_GLSL

#include "frame.glsl"
#include "gbuffer_inputs.glsl"
#include "lighting.glsl"
#include "octahedral.glsl"
#include "screen.glsl"
#include "exposure.glsl"

// The least material factor, 2^-14: a factor below it divides as this, so that the quotient stays finite and a black
// material's history holds its light at a scale that the composite gives back.
const float LeastFactor = 0.00006103515625;

// The factors of a surface's two parts: the diffuse color and the compensated specular albedo W (specularWeight). A hair
// fiber's specular lobes hold their own color and weight, so its specular factor is 1. Its diffuse color is its base color,
// because hair has no metal.
struct MaterialFactors
{
    vec3 diffuse;
    vec3 specular;
};

MaterialFactors materialFactors(ShadingInput s, SurfaceLobes lobes)
{
    MaterialFactors f;
    f.diffuse = lobes.diffuseColor;
    f.specular = s.model == ShadingHair ? vec3(1.0) : specularWeight(lobes);
    return f;
}

// Radiance divided by its factor and pre-exposed by `k`, at most MaxRadiance per channel: a producer's sample.
vec3 demodulated(vec3 radiance, vec3 factor, float k)
{
    return exposed(radiance / max(factor, vec3(LeastFactor)), k);
}

// The bounds of sameSurface: a view depth within 10 % and normals within 25°.
const float SameDepthShare = 0.1;
const float SameNormalCosine = 0.906307787;

// What sameSurface compares at a pixel: its view depth in m, 0 for sky, its geometric normal and its shading model.
struct SurfaceKey
{
    float depth;
    vec3 normal;
    uint model;
};

// The key of a pixel inside the G-buffer.
SurfaceKey surfaceKeyAt(ivec2 pixel)
{
    SurfaceKey key;
    // The view ray's component along forward is 1, so the view depth is the distance over its length.
    key.depth = texelFetch(gbufferDistance, pixel, 0).r / length(viewRayThrough(vec2(pixel) + 0.5));
    key.normal = octDecode(texelFetch(gbufferNormal, pixel, 0).ba);
    key.model = modelOfWord(texelFetch(gbufferShading, pixel, 0).a);
    return key;
}

// Whether a neighbor shows the same surface as a pixel (section 8, "The fix"): it holds a surface, its view depth lies
// within 10 % of the pixel's, its normal within 25° and its model is the pixel's.
bool sameSurface(SurfaceKey pixel, SurfaceKey neighbor)
{
    return neighbor.depth > 0.0 && abs(neighbor.depth - pixel.depth) <= SameDepthShare * pixel.depth
        && dot(neighbor.normal, pixel.normal) >= SameNormalCosine && neighbor.model == pixel.model;
}

// Whether every channel of a sample is finite: isnan and isinf test it, because a clamp or a min of NaN has no defined
// result in Vulkan.
bool finiteSample(vec3 v)
{
    return !any(isnan(v)) && !any(isinf(v));
}

#endif
