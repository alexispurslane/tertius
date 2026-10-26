// The depth pyramid (docs/plans/screen-space-reflections.md, "The depth pyramid"). The bounds of a cell are the largest
// and the smallest inverse view depth of its surface pixels, in 1/m: (w_near, w_far), or the empty bounds (0, EmptyFar)
// for a cell with no surface. Level 0 is the distance target; mip m of the pyramid image holds level m + 1. DepthPyramid
// and CellBounds are the C# copy. Change both together.
#ifndef TERTIUS_DEPTH_PYRAMID_GLSL
#define TERTIUS_DEPTH_PYRAMID_GLSL

#include "gbuffer_inputs.glsl"
#include "screen.glsl"

// Far above any surface's inverse depth, which is at most 1 / 0.05 m. Not infinity: fast math may assume none.
const float EmptyFar = 1e30;

vec2 pixelBounds(ivec2 pixel)
{
    float distanceToEye = texelFetch(gbufferDistance, pixel, 0).r;
    if (distanceToEye <= 0.0)
        return vec2(0.0, EmptyFar);
    return vec2(length(viewRayThrough(vec2(pixel) + 0.5)) / distanceToEye);
}

vec2 mergedBounds(vec2 a, vec2 b)
{
    return vec2(max(a.x, b.x), min(a.y, b.y));
}

// A cell outside its level holds no surface: no surface lies beyond the screen's edges. The test also keeps every
// texelFetch inside its image, because the Vulkan spec leaves a fetch out of range undefined without the robustness
// features.
vec2 cellBounds(texture2D pyramid, uint level, ivec2 cell)
{
    ivec2 size = level == 0u ? textureSize(gbufferDistance, 0) : textureSize(pyramid, int(level) - 1);
    if (any(lessThan(cell, ivec2(0))) || any(greaterThanEqual(cell, size)))
        return vec2(0.0, EmptyFar);
    return level == 0u ? pixelBounds(cell) : texelFetch(pyramid, cell, int(level) - 1).rg;
}

#endif
