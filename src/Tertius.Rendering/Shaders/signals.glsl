// The reprojection of the temporal signals (docs/plans/temporal-renderer.md, section 8, "The surface test"): a point's
// place in the last drawn frame's image, the surface test of the 2 × 2 texels of the last surface history around a
// place, and the bilinear weights of those taps, by which every reader of a history reprojects it. Needs frame.glsl and
// octahedral.glsl.
#ifndef TERTIUS_SIGNALS_GLSL
#define TERTIUS_SIGNALS_GLSL

#extension GL_EXT_samplerless_texture_functions : require

// The plane test's bound: a tap's distance from the pixel's plane, at most this share of the pixel's last view depth.
const float PlaneShare = 0.01;

// The normal test's bound: cos 25°, the least cosine between a tap's normal and the pixel's normal.
const float NormalCosine = 0.906307787;

// The specular test's bound: the most that a tap's roughness differs from the pixel's.
const float RoughnessStep = 0.1;

// SurfaceTestPass.DeviceDepthNear: the near plane of FSR's reversed and infinite depth, in m (DepthRanges.NearPlane). The
// device depth is this over the view depth, 0 for sky.
const float DeviceDepthNear = 0.05;

// The last frame's view ray through a place in its image, in units of its width and height from the top left: forward +
// x right tan h − y up tan v, in world axes. Its component along the last frame's forward is 1.
vec3 lastViewRayThrough(vec2 place)
{
    vec2 ndc = (place + frame.previousJitter.xy * frame.previousViewport.zw) * 2.0 - 1.0;
    return frame.previousCameraForward.xyz + ndc.x * frame.previousCameraRight.xyz - ndc.y * frame.previousCameraUp.xyz;
}

// The pass bits of the 2 × 2 texels of the last surface history around a place in the last frame's image (section 8,
// "The surface test"): bit i for tap i of the surface test, bit 4 + i of the specular test. Tap 0 is the texel at the
// top left of the place, tap 1 the one right of it, taps 2 and 3 the row below. lastPoint is the pixel's point in the last
// frame, relative to the last frame's eye, normal its geometric normal and roughness its roughness. A tap outside the
// last frame's image, or one that held no surface, fails both tests.
uint surfaceTaps(texture2D lastSurfaceHistory, texture2D lastDeviceDepth, vec2 place, vec3 lastPoint, vec3 normal,
    float roughness)
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
        float lastDevice = texelFetch(lastDeviceDepth, texel, 0).r;
        if (lastDevice <= 0.0)
            continue;
        float lastDepth = DeviceDepthNear / lastDevice;
        vec4 last = texelFetch(lastSurfaceHistory, texel, 0);
        vec3 point = lastViewRayThrough((vec2(texel) + 0.5) * frame.previousViewport.zw) * lastDepth;
        bool surface = abs(dot(point - lastPoint, normal)) <= bound && dot(octDecode(last.yz), normal) >= NormalCosine;
        if (!surface)
            continue;
        bits |= 1u << tap;
        if (abs(last.w - roughness) <= RoughnessStep)
            bits |= 1u << (tap + 4u);
    }
    return bits;
}

// The least sum of the passing taps' bilinear weights that holds a history (section 8, "The surface test").
const float LeastHistoryWeight = 0.05;

// The taps of a place in the last frame's image: the first of its 2 × 2 texels, at the top left of the place, and the
// bilinear weight of each tap, in the order of surfaceTaps, times its pass bit. A failing tap holds no weight.
struct Reprojection
{
    ivec2 first;
    vec4 weights;
};

// The reprojection of a place through the pass bits of its taps: bit i for tap i.
Reprojection reprojectionOf(vec2 place, uint bits)
{
    vec2 texelPlace = place * frame.previousViewport.xy - 0.5;
    Reprojection r;
    r.first = ivec2(floor(texelPlace));
    vec2 f = texelPlace - vec2(r.first);
    vec4 passes = vec4(uvec4(bits, bits >> 1, bits >> 2, bits >> 3) & 1u);
    r.weights = vec4((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y) * passes;
    return r;
}

// Whether a reprojection holds a history: the sum of its weights reaches LeastHistoryWeight.
bool holdsHistory(Reprojection r)
{
    return dot(r.weights, vec4(1.0)) >= LeastHistoryWeight;
}

// The texel of tap `tap` of a reprojection.
ivec2 tapTexel(Reprojection r, uint tap)
{
    return r.first + ivec2(tap & 1u, tap >> 1);
}

// The history length at a place, from the last slow history's lengths at its four taps: their weighted mean, or 0 where
// the reprojection holds no history. The ray plan reads a signal's length through it, as the accumulation does.
float reprojectedLength(Reprojection r, vec4 tapLengths)
{
    return holdsHistory(r) ? dot(r.weights, tapLengths) / dot(r.weights, vec4(1.0)) : 0.0;
}

// The pass bits of the taps around a place (surfaceTaps), or none for a place outside the last frame's image.
uint surfaceTapsInside(texture2D lastSurfaceHistory, texture2D lastDeviceDepth, vec2 place, vec3 lastPoint, vec3 normal,
    float roughness)
{
    bool inside = all(greaterThanEqual(place, vec2(0.0))) && all(lessThanEqual(place, vec2(1.0)));
    return inside ? surfaceTaps(lastSurfaceHistory, lastDeviceDepth, place, lastPoint, normal, roughness) : 0u;
}

// A place whose history a point reads, the pass bits of its taps, and whether it is the pixel place.
struct HistoryTaps
{
    vec2 place;
    uint bits;
    bool pixel;
};

// The place whose history a point reads under the pixel rule (screen.glsl), of its sample place and its pixel place: the
// pixel place, unless its taps that pass the surface test hold less than LeastHistoryWeight, as at a silhouette whose
// own texel showed another surface; then the sample place. Both test their taps against the point's exact last point.
HistoryTaps historyTaps(texture2D lastSurfaceHistory, texture2D lastDeviceDepth, vec2 samplePlace, vec2 pixelPlace,
    vec3 lastPoint, vec3 normal, float roughness)
{
    uint bits = surfaceTapsInside(lastSurfaceHistory, lastDeviceDepth, pixelPlace, lastPoint, normal, roughness);
    if (holdsHistory(reprojectionOf(pixelPlace, bits & 0xFu)))
        return HistoryTaps(pixelPlace, bits, true);
    return HistoryTaps(samplePlace,
        surfaceTapsInside(lastSurfaceHistory, lastDeviceDepth, samplePlace, lastPoint, normal, roughness), false);
}

#define HISTORY_READER(name, source) \
vec4 name(Reprojection r) \
{ \
    vec4 value = vec4(0.0); \
    for (uint tap = 0u; tap < 4u; tap++) \
        if (r.weights[tap] > 0.0) \
            value += r.weights[tap] * imageLoad(source, tapTexel(r, tap)); \
    return holdsHistory(r) ? value / dot(r.weights, vec4(1.0)) : vec4(0.0); \
}

float historyRatio(float currentStops, float lastStops)
{
    return exp2(clamp(currentStops - lastStops, -64.0, 64.0));
}

#endif
