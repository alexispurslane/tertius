// The diffuse filter (ticket ter-9371, DiffuseFilter.cs): a variance-guided à-trous wavelet filter of the indirect diffuse
// light after its accumulation, after Schied et al. 2017, "Spatiotemporal Variance-Guided Filtering". The variance dispatch
// (diffuse_filter_variance.comp) pairs the slow history's light with the standard deviation of the samples' luminance.
// Each step of the filter, a wavelet dispatch (diffuse_filter_wavelet.comp), takes a 5 × 5 B-spline kernel at taps 2^i
// pixels apart, stopped by the planes, the normals and the luminance, and moves light between the taps. Set 3 is the
// filter set (DescriptorLayouts.FilterSet): every image holds the render size, RGBA16F, and stays in the General layout.
#ifndef TERTIUS_DIFFUSE_FILTER_GLSL
#define TERTIUS_DIFFUSE_FILTER_GLSL

#extension GL_GOOGLE_include_directive : require
#include "temporal.glsl"
#include "meter.glsl"
#include "signals.glsl"

layout(local_size_x = 8, local_size_y = 8) in;

// DiffuseFilterPushConstants.
layout(push_constant, std430) uniform FilterPush
{
    int step;       // the distance between the kernel's taps, in pixels: 2^i in step i
    uint feedback;  // 1 in the step whose light the slow history takes, else 0
} parameters;

// The 5 × 5 B-spline kernel's weight at a tap 0, 1 or 2 taps from the center along one axis: (1, 4, 6, 4, 1) / 16.
const float KernelWeights[3] = float[3](0.375, 0.25, 0.0625);

// The normal stop's power of the cosine between the normals: the paper's 128.
const float NormalPower = 128.0;

// The luminance stop's bound: a difference of luminance of this many deviations of the difference weighs 1 / e, the
// paper's 4.
const float LuminanceDeviations = 4.0;

// The least deviation of the luminance stop, so that two equal luminances without noise weigh 1, and two that differ
// weigh 0.
const float LeastDeviation = 1e-10;

// Whether a pixel lies inside the images.
bool insideFilter(ivec2 pixel)
{
    return all(greaterThanEqual(pixel, ivec2(0))) && all(lessThan(pixel, ivec2(frame.viewport.xy)));
}

// Whether a pixel shows a lit surface, whose diffuse light the signal holds: not the sky, and not an unlit surface such as
// an impostor's, whose light is its emission.
bool filteredAt(ivec2 pixel)
{
    return texelFetch(gbufferDistance, pixel, 0).r > 0.0 && texelFetch(gbufferBaseColor, pixel, 0).a > 0.0;
}

// What the edge stops compare at a pixel: its point relative to the eye in m, its view depth, its geometric normal, its
// luminance and the variance of its luminance.
struct FilterTap
{
    vec3 point;
    float depth;
    vec3 normal;
    float luminance;
    float variance;
};

// The tap of a pixel whose input is `light`: rgb the light, a the deviation of its luminance.
FilterTap filterTapAt(ivec2 pixel, vec4 light)
{
    FilterTap tap;
    vec3 ray = viewRayThrough(vec2(pixel) + 0.5);
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    tap.point = normalize(ray) * distanceToEye;
    // The view ray's component along forward is 1, so the view depth is the distance over its length.
    tap.depth = distanceToEye / length(ray);
    tap.normal = octDecode(texelFetch(gbufferNormal, pixel, 0).ba);
    tap.luminance = luminance(light.rgb);
    tap.variance = light.a * light.a;
    return tap;
}

// The weight between two pixels at kernel offsets (x, y) apart: the kernel's weight times the three edge stops. Each stop
// is symmetric in the two pixels. The plane stop takes the larger distance of each point from the other's plane, the
// normal stop the cosine between the two normals, and the luminance stop the deviation of the difference of two
// independent luminances, the root of the sum of their variances.
float tapWeight(FilterTap a, FilterTap b, int x, int y)
{
    vec3 apart = b.point - a.point;
    float planeDistance = max(abs(dot(apart, a.normal)), abs(dot(apart, b.normal)));
    // The weight falls to 0 at the surface test's bound, PlaneShare of the nearer view depth.
    float plane = max(1.0 - planeDistance / (PlaneShare * min(a.depth, b.depth)), 0.0);
    float normal = pow(max(dot(a.normal, b.normal), 0.0), NormalPower);
    float deviation = LuminanceDeviations * sqrt(a.variance + b.variance) + LeastDeviation;
    float light = exp(-abs(a.luminance - b.luminance) / deviation);
    return KernelWeights[abs(x)] * KernelWeights[abs(y)] * plane * normal * light;
}

// The pixel of tap (x, y) of `pixel`, `step` pixels apart, or (-1, -1) where the tap lies outside the images or shows no
// lit surface.
ivec2 tapPixel(ivec2 pixel, int x, int y)
{
    ivec2 neighbor = pixel + parameters.step * ivec2(x, y);
    return insideFilter(neighbor) && filteredAt(neighbor) ? neighbor : ivec2(-1);
}

#endif
