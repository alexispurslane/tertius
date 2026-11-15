#ifndef TERTIUS_SPECULAR_HISTORY_GLSL
#define TERTIUS_SPECULAR_HISTORY_GLSL

#include "half_store.glsl"

// The format of the slow history.
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
#define SPECULAR_HISTORY_FORMAT rgba32f
#else
#define SPECULAR_HISTORY_FORMAT rgba16f
#endif

// The value that the accumulation stores in a signal's slow history or its scratch image, whose format
// SPECULAR_HISTORY_FORMAT gives: rgb the light, a the length. It is the nearest half float in the
// 16-bit floats of every history but the precise specular one, and the value itself in that history's 32-bit floats,
// which store it exactly. The length is a whole number until the anti-lag shortens it by the clamp's move, which gives
// it a fraction, so it takes the same rule as the light.
vec4 slowHistoryValue(vec4 value)
{
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
    return value;
#else
    return nearestHalf(value);
#endif
}

#endif
