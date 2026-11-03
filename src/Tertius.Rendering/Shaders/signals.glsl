// The reprojection of the temporal signals (docs/plans/temporal-renderer.md, section 8, "The surface test"): a point's
// place in the last drawn frame's image, and the surface test of the 2 × 2 texels of the last surface history around a
// place. Needs frame.glsl and octahedral.glsl, and the last surface history bound as lastSurfaceHistory.
#ifndef TERTIUS_SIGNALS_GLSL
#define TERTIUS_SIGNALS_GLSL

#extension GL_EXT_samplerless_texture_functions : require

// The plane test's bound: a tap's distance from the pixel's plane, at most this share of the pixel's last view depth.
const float PlaneShare = 0.01;

// The normal test's bound: cos 25°, the least cosine between a tap's normal and the pixel's normal.
const float NormalCosine = 0.906307787;

// The specular test's bound: the most that a tap's roughness differs from the pixel's.
const float RoughnessStep = 0.1;

// The last frame's view ray through a place in its image, in units of its width and height from the top left: forward +
// x right tan h − y up tan v, in world axes. Its component along the last frame's forward is 1.
vec3 lastViewRayThrough(vec2 place)
{
    vec2 ndc = place * 2.0 - 1.0;
    return frame.previousCameraForward.xyz + ndc.x * frame.previousCameraRight.xyz - ndc.y * frame.previousCameraUp.xyz;
}

// The pass bits of the 2 × 2 texels of the last surface history around a place in the last frame's image (section 8,
// "The surface test"): bit i for tap i of the surface test, bit 4 + i of the specular test. Tap 0 is the texel at the
// top left of the place, tap 1 the one right of it, taps 2 and 3 the row below. lastPoint is the pixel's point in the last
// frame, relative to the last frame's eye, normal its geometric normal and roughness its roughness. A tap outside the
// last frame's image, or one that held no surface, fails both tests.
uint surfaceTaps(vec2 place, vec3 lastPoint, vec3 normal, float roughness)
{
    ivec2 size = ivec2(frame.previousViewport.xy);
    ivec2 first = ivec2(floor(place * frame.previousViewport.xy - 0.5));
    float bound = PlaneShare * dot(lastPoint, frame.previousCameraForward.xyz);
    uint bits = 0u;
    for (uint tap = 0u; tap < 4u; tap++)
    {
        ivec2 texel = first + ivec2(tap & 1u, tap >> 1);
        if (any(lessThan(texel, ivec2(0))) || any(greaterThanEqual(texel, size)))
            continue;
        vec4 last = texelFetch(lastSurfaceHistory, texel, 0);
        if (last.x <= 0.0)
            continue;
        vec3 point = lastViewRayThrough((vec2(texel) + 0.5) * frame.previousViewport.zw) * last.x;
        bool surface = abs(dot(point - lastPoint, normal)) <= bound && dot(octDecode(last.yz), normal) >= NormalCosine;
        if (!surface)
            continue;
        bits |= 1u << tap;
        if (abs(last.w - roughness) <= RoughnessStep)
            bits |= 1u << (tap + 4u);
    }
    return bits;
}

#endif
