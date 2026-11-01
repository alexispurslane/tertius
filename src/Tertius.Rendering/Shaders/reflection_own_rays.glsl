// The own-ray list of the half-resolution reflections (docs/plans/screen-space-reflections.md, "Rays at outlines"): the
// pixels whose texels' rays disagree, which reflection_upsample.comp appends and reflection_own_rays.comp traces in an
// indirect dispatch of OwnRayGroupSize invocations per workgroup. OwnRayList is the C# side of this layout, and
// OwnRayDispatch clears the header to OwnRayList.EmptyHeader before the upsample.
#ifndef TERTIUS_REFLECTION_OWN_RAYS_GLSL
#define TERTIUS_REFLECTION_OWN_RAYS_GLSL

#include "reflection_ray.glsl"

const uint OwnRayGroupSize = 64u;
// The most workgroups of the own-ray dispatch: Vulkan's least maxComputeWorkGroupCount[0], the C# ComputeLimits.
// MaxGroupsPerDimension. Each invocation strides over
// the list by that many workgroups, so a screen of more than 65,535 · 64 listed pixels still traces each one once.
const uint MostOwnRayGroups = 65535u;

layout(set = 3, binding = 6, std430) buffer OwnRays
{
    uvec3 groups;    // the dispatch's VkDispatchIndirectCommand: x the workgroups, y and z 1
    uint count;      // the listed pixels
    uint pixels[];   // x in the low 16 bits, y in the high 16 bits
} ownRays;

// Lists the pixel for its own ray, and counts a workgroup for each OwnRayGroupSize-th entry, up to MostOwnRayGroups, so
// that the dispatch's x is ⌈count / OwnRayGroupSize⌉ capped at MostOwnRayGroups. The order of the entries depends on the
// GPU's scheduling, but each listed pixel's result does not.
void appendOwnRay(ivec2 pixel)
{
    uint entry = atomicAdd(ownRays.count, 1u);
    if (entry % OwnRayGroupSize == 0u && entry / OwnRayGroupSize < MostOwnRayGroups)
        atomicAdd(ownRays.groups.x, 1u);
    ownRays.pixels[entry] = uint(pixel.x) | uint(pixel.y) << 16;
}

ivec2 ownRayPixel(uint entry)
{
    uint packed = ownRays.pixels[entry];
    return ivec2(packed & 0xffffu, packed >> 16);
}

#endif
