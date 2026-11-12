// The wavelet dispatch of a step of the diffuse filter (diffuse_filter.glsl), for a kernel of (2 KernelRadius + 1)²
// taps: the thin kernel's 3 × 3 (diffuse_filter_wavelet.comp) or the wide kernel's 5 × 5
// (diffuse_filter_wavelet_wide.comp), each the B-spline of its width. The paper takes each pixel's weighted mean
// over its taps, divided by its own sum of weights. That gives a quiet pixel, whose own neighbors weigh little, nearly
// the light of a noisy neighbor, which loses almost nothing: the filter makes light. This dispatch moves light instead:
// pixel p takes the share w(p, q) of the difference from each tap q, where w is the tap's weight, the same in both
// directions, so what p takes, q gives. The kernel's weights sum to 1 and each stop is at most 1, so the shares of a pixel
// sum to less than 1 and its light stays a weighted mean: the weight that the stops remove stays with the pixel. Where
// no stop acts, as on an even surface, it is the paper's mean. The variance of the luminance follows the same shares. In
// the feedback step it also writes the light into the slow history, and keeps the history's length.
#ifndef TERTIUS_DIFFUSE_FILTER_WAVELET_GLSL
#define TERTIUS_DIFFUSE_FILTER_WAVELET_GLSL

#extension GL_GOOGLE_include_directive : require
#include "diffuse_filter.glsl"

#if KernelRadius == 1
// The 3 × 3 kernel's weight at a tap 0 or 1 taps from the center along one axis: (1, 2, 1) / 4.
const float KernelWeights[2] = float[2](0.5, 0.25);
#elif KernelRadius == 2
// The 5 × 5 kernel's weight at a tap 0, 1 or 2 taps from the center along one axis: (1, 4, 6, 4, 1) / 16.
const float KernelWeights[3] = float[3](0.375, 0.25, 0.0625);
#else
#error "KernelRadius must be 1 or 2"
#endif

layout(set = 3, binding = 0, rgba16f) uniform readonly image2D filterInput;    // rgb the light, a the deviation
layout(set = 3, binding = 1, rgba16f) uniform writeonly image2D filterOutput;
layout(set = 3, binding = 2, rgba16f) uniform image2D slowHistory;             // rgb the light, a the length

void main()
{
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (!insideFilter(pixel))
        return;
    vec4 centerLight = imageLoad(filterInput, pixel);
    if (!filteredAt(pixel))
    {
        imageStore(filterOutput, pixel, centerLight);
        return;
    }

    FilterTap center = filterTapAt(pixel, centerLight);
    vec3 moved = vec3(0.0);
    float kept = 1.0;
    float varianceSum = 0.0;
    for (int y = -KernelRadius; y <= KernelRadius; y++)
        for (int x = -KernelRadius; x <= KernelRadius; x++)
        {
            ivec2 neighbor = tapPixel(pixel, x, y);
            if (neighbor.x < 0 || x == 0 && y == 0)
                continue;
            vec4 light = imageLoad(filterInput, neighbor);
            float share = KernelWeights[abs(x)] * KernelWeights[abs(y)] * edgeStops(center, filterTapAt(neighbor, light));
            moved += share * (light.rgb - centerLight.rgb);
            kept -= share;
            varianceSum += share * share * light.a * light.a;
        }
    vec3 filtered = centerLight.rgb + moved;
    float deviation = min(sqrt(kept * kept * center.variance + varianceSum), MaxRadiance);
    imageStore(filterOutput, pixel, vec4(filtered, deviation));
    if (parameters.feedback != 0u)
        imageStore(slowHistory, pixel, vec4(filtered, imageLoad(slowHistory, pixel).a));
}

#endif
