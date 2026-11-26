// The reprojection of the temporal signals (https://github.com/alexispurslane/tertius/wiki/temporal-renderer, section 8, "The surface test"): a point's
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

// The surface test image's motion facts above its pass bits, which the surface test pass writes beside them because it
// already holds each pixel's surface point and motion target (ticket t0-89zi). The ray plan's moved mark and the
// resolve's motion bit read them.
const uint SurfaceMoved = 0x100u;          // the pixel's surface moved since the last draw (motion.glsl, movedSurface)
const uint SurfaceMovesOnScreen = 0x200u;  // its point moves on screen by LeastMotion or more (movesOnScreen)

// The taps that pass the surface test, bits 0 to 3 of a surface test word: bit i for tap i.
uint surfacePassBits(uint word)
{
    return word & 0xFu;
}

// The taps that also pass the specular test, bits 4 to 7 of a surface test word: bit i for tap i.
uint specularPassBits(uint word)
{
    return (word >> 4) & 0xFu;
}

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

// The largest distance in texels, along an axis, between a place in the last frame's image and a texel's center at which
// the place counts as that center (ticket t0-y88b). A place is a share of the image, so a still pixel's place reaches the
// last image through a reciprocal of this draw's size and a product with the last draw's: the trip lands up to about
// 2.5e-4 texel off the center at a width of 3840 and about 1e-3 at 16,000. A motion whose step per draw lies within this
// bound of a whole number of texels, along an axis, reads whole texels, so the history of a slow pan trails it by up to
// this bound per draw of the history's length. The precise specular history has no age limit, so there only its clamp
// ends the trail.
const float StillPlaceTexels = 1.0 / 256.0;

// A place in the last frame's image, in texels of that image, such that the place of the texel at (i, j) is (i, j): its
// floor is the first of its 2 × 2 taps. Along each axis, a place within StillPlaceTexels of a texel's center is that
// center, so that a still pixel reads its own texel alone. Every reader that names the taps of a place takes them from
// here, so that the surface test's pass bits and the reprojection's weights name the same four texels.
vec2 lastTexelPlace(vec2 place)
{
    vec2 texelPlace = place * frame.previousViewport.xy - 0.5;
    vec2 center = round(texelPlace);
    return mix(texelPlace, center, lessThan(abs(texelPlace - center), vec2(StillPlaceTexels)));
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
    ivec2 first = ivec2(floor(lastTexelPlace(place)));
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
    vec2 texelPlace = lastTexelPlace(place);
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

// The share of a pixel's fresh light that the pixel sends (ARCHITECTURE.md, "Surface share"): all of it where the
// reprojection of its surface place holds a history, so the last draw showed this surface there. Elsewhere the part that
// one draw of the jitter's phases deserves, 1 / min(P, draws since the reset + 1): under the jitter a pixel can show a
// surface thinner than a pixel in one draw of P.
float surfaceShare(Reprojection surface)
{
    return holdsHistory(surface) ? 1.0 : 1.0 / frame.renderParams.w;
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
    if (holdsHistory(reprojectionOf(pixelPlace, surfacePassBits(bits))))
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
