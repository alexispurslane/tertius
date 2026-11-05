// An own-ray list (docs/plans/temporal-renderer.md, section 6, "The ray budget"): the pixels that trace a ray of their
// own, which a dispatch appends and an indirect dispatch of OwnRayGroupSize invocations per workgroup traces. OwnRayList
// is the C# side of this layout, and OwnRayDispatch clears the header to OwnRayList.EmptyHeader before the appends. The
// screen tracer keeps one list per ray kind, and no other reflection list exists. GLSL
// passes no buffer to a function, so the declaration and the append are macros over the list's name.
#ifndef TERTIUS_OWN_RAYS_GLSL
#define TERTIUS_OWN_RAYS_GLSL

const uint OwnRayGroupSize = 64u;
// The most workgroups of an own-ray dispatch: Vulkan's least maxComputeWorkGroupCount[0], the C# ComputeLimits.
// MaxGroupsPerDimension. Each invocation strides over the list by that many workgroups, so a screen of more than
// 65,535 · 64 listed pixels still traces each one once.
const uint MostOwnRayGroups = 65535u;

// Declares the list `name` at set `setIndex` and binding `bindingIndex`: the dispatch's VkDispatchIndirectCommand (x the
// workgroups, y and z 1), the count of listed pixels, then each pixel, x in the low 16 bits and y in the high 16 bits.
#define OWN_RAY_LIST(setIndex, bindingIndex, name) \
    layout(set = setIndex, binding = bindingIndex, std430) buffer name##Block \
    { \
        uvec3 groups; \
        uint count; \
        uint pixels[]; \
    } name

uint packedPixel(ivec2 pixel)
{
    return uint(pixel.x) | uint(pixel.y) << 16;
}

ivec2 unpackedPixel(uint packed)
{
    return ivec2(packed & 0xffffu, packed >> 16);
}

// Lists `pixel` in `list`, and counts a workgroup for each OwnRayGroupSize-th entry, up to MostOwnRayGroups, so that the
// dispatch's x is ⌈count / OwnRayGroupSize⌉ capped at MostOwnRayGroups. The order of the entries depends on the GPU's
// scheduling, but each listed pixel's result does not.
#define APPEND_OWN_RAY(list, pixel) \
    { \
        uint entry_ = atomicAdd(list.count, 1u); \
        if (entry_ % OwnRayGroupSize == 0u && entry_ / OwnRayGroupSize < MostOwnRayGroups) \
            atomicAdd(list.groups.x, 1u); \
        list.pixels[entry_] = packedPixel(pixel); \
    }

#endif
