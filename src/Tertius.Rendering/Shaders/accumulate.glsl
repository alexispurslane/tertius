// The accumulator of a temporal signal (docs/plans/temporal-renderer.md, section 8, "The model"): the images of one part of
// a signal (TemporalSignal.cs), its parameter set, and what both of its dispatches share. accumulate_temporal.comp
// reprojects and blends, and accumulate_spatial.comp fixes and clamps.
#ifndef TERTIUS_ACCUMULATE_GLSL
#define TERTIUS_ACCUMULATE_GLSL

#extension GL_GOOGLE_include_directive : require
#include "temporal.glsl"

layout(local_size_x = 8, local_size_y = 8) in;

// Set 3 of the accumulate layout, the signal set: one part's images and the surface test's outputs. Each image holds the
// render size, and every one stays in the General layout.
layout(set = 3, binding = 0, rgba16f) uniform readonly image2D sampleImage;   // the producer's sample: rgb the light
layout(set = 3, binding = 1, rgba16f) uniform image2D slowHistory;            // rgb the light, a the history length
layout(set = 3, binding = 2, rgba16f) uniform readonly image2D lastFast;      // the last frame's fast history: rgb the light
layout(set = 3, binding = 3, rgba16f) uniform image2D fastHistory;            // this frame's
layout(set = 3, binding = 4, rgba16f) uniform image2D scratch;                // the temporal dispatch's slow history
layout(set = 3, binding = 5, r8ui) uniform readonly uimage2D surfaceTest;     // the pass bits (signals.glsl)
layout(set = 3, binding = 6, rgba16f) uniform readonly image2D motionTarget;  // the G-buffer's motion target

// AccumulatePushConstants: the signal's parameter set (SignalParameters).
layout(push_constant, std430) uniform AccumulatePush
{
    float cap;           // the largest history length, in frames
    float fastCap;       // the fast history's
    float fixFrames;     // HistoryFixFrames: a history shorter than this and the cap takes its neighbors' mean
    float clampSigmas;   // the box's half width, in standard deviations
    float antiLagShare;  // the clamp's move at which the length falls to the fast cap, as a share of the mean's luminance
} parameters;

// Whether a pixel lies inside the images.
bool insideImages(ivec2 pixel)
{
    return all(greaterThanEqual(pixel, ivec2(0))) && all(lessThan(pixel, ivec2(frame.viewport.xy)));
}

#endif
