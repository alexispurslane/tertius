#ifndef TERTIUS_SPECULAR_HISTORY_GLSL
#define TERTIUS_SPECULAR_HISTORY_GLSL

#include "half_store.glsl"
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
#define TERTIUS_SCENE_SPECULAR
#endif

// The format of the slow history.
#if defined(TERTIUS_PRECISE_SPECULAR_HISTORY) || defined(TERTIUS_SCENE_SPECULAR)
#define SPECULAR_HISTORY_FORMAT rgba32f
#else
#define SPECULAR_HISTORY_FORMAT rgba16f
#endif

// Glossy history and scratch retain the float32 value in scene units. Diffuse estimators round their
// pre-exposed light and length to nearest half before feedback (half_store.glsl).
vec4 slowHistoryValue(vec4 value)
{
#if defined(TERTIUS_PRECISE_SPECULAR_HISTORY) || defined(TERTIUS_SCENE_SPECULAR)
    return value;
#else
    return nearestHalf(value);
#endif
}

#endif
