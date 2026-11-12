// The accumulator of a temporal signal (docs/plans/temporal-renderer.md, section 8, "The model"): the images of one part of
// a signal (TemporalSignal.cs), its parameter set, and what both of its dispatches share. accumulate_temporal.comp
// reprojects and blends, and accumulate_spatial.comp fixes and clamps.
#ifndef TERTIUS_ACCUMULATE_GLSL
#define TERTIUS_ACCUMULATE_GLSL

#extension GL_GOOGLE_include_directive : require
#include "temporal.glsl"
#include "signals.glsl"
#include "specular_history.glsl"

layout(local_size_x = 8, local_size_y = 8) in;

// Set 3 of the accumulate layout, the signal set: one part's images and the surface test's outputs. Each image holds the
// render size, and every one stays in the General layout.
layout(set = 3, binding = 0, rgba16f) uniform readonly image2D sampleImage;   // the producer's sample: rgb the light
layout(set = 3, binding = 1, SPECULAR_HISTORY_FORMAT) uniform image2D slowHistory;
layout(set = 3, binding = 2, rgba16f) uniform readonly image2D lastFast;      // the last frame's fast history: rgb the light
layout(set = 3, binding = 3, rgba16f) uniform image2D fastHistory;            // this frame's
layout(set = 3, binding = 4, SPECULAR_HISTORY_FORMAT) uniform image2D scratch;
layout(set = 3, binding = 5, r8ui) uniform readonly uimage2D surfaceTest;     // the pass bits (signals.glsl)
layout(set = 3, binding = 6, rgba16f) uniform readonly image2D motionTarget;  // the G-buffer's motion target
layout(set = 3, binding = 7) uniform texture2D lastSurfaceHistory;            // the last frame's (SurfaceHistory)
layout(set = 3, binding = 8) uniform texture2D lastDeviceDepth;               // the last frame's, 0 for sky

#ifdef TERTIUS_ACCUMULATION_TRACE
// The accumulation trace (ticket tiunr-4b2g, TemporalSignal.AccumulationTrace), which only the capture tool and the tests
// read: RGBA32F layers of what each stage did to the specular history. The temporal dispatch writes layers 0 to 3 and 8, the
// spatial dispatch layers 4 to 7.
layout(set = 3, binding = 12, rgba32f) uniform writeonly image2DArray accumulationTrace;

// The flags in alpha of layer 0: bits 0 to 3 the taps that hold weight, in the order of surfaceTaps, and these.
const uint TracedReflectedWon = 1u << 4;
const uint TracedReflectedTried = 1u << 5;
const uint TracedSurfaceHolds = 1u << 6;
const uint TracedReflectedHolds = 1u << 7;
const uint TracedSample = 1u << 8;
const uint TracedSurfacePixel = 1u << 9;

void storeTrace(ivec2 pixel, int layer, vec4 value)
{
    imageStore(accumulationTrace, ivec3(pixel, layer), value);
}
#endif


// AccumulatePushConstants: the signal's parameter set (SignalParameters), and whether the frame resets.
layout(push_constant, std430) uniform AccumulatePush
{
    float cap;           // the largest history length of a rough pixel, in frames
    float smoothCap;     // of a smooth pixel
    float fastCap;       // the fast history's, or the cap where the cap is lower
    float fixFrames;     // HistoryFixFrames: a history shorter than this and the cap takes its neighbors' mean
    float clampSigmas;   // the box's half width, in standard deviations
    float antiLagShare;  // the clamp's move at which the length falls to the fast cap, as a share of the mean's luminance
    uint light;          // SignalLight: DiffuseLight or SpecularLight
    float reflectedPlaceBelow;   // the roughness below which the reflected place competes, with its distance test, 0 for none
    uint reset;          // 1 in a frame that resets every history
} parameters;

// SignalLight.
const uint DiffuseLight = 0u;
const uint SpecularLight = 1u;

// SignalParameters.SmoothRoughness and RoughRoughness: the roughness up to which a pixel takes the smooth cap, and from
// which it takes the rough one.
const float SmoothRoughness = 0.1;
const float RoughRoughness = 0.7;

// A pixel's floored roughness, by which its caps and the reflected place's eligibility go.
float roughnessAt(ivec2 pixel)
{
    return flooredRoughness(specularAt(pixel).g);
}

// The cap of a pixel of `roughness`: the smooth cap up to SmoothRoughness, linear to the rough cap at RoughRoughness.
float capAt(float roughness)
{
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    if (isinf(parameters.cap))
        return parameters.cap;
#endif
    float share = clamp((roughness - SmoothRoughness) / (RoughRoughness - SmoothRoughness), 0.0, 1.0);
    return mix(parameters.smoothCap, parameters.cap, share);
}

// The fast cap of a pixel whose cap is `cap`.
float fastCapOf(float cap)
{
    return min(parameters.fastCap, cap);
}

// Whether a pixel lies inside the images.
bool insideImages(ivec2 pixel)
{
    return all(greaterThanEqual(pixel, ivec2(0))) && all(lessThan(pixel, ivec2(frame.viewport.xy)));
}

#endif
