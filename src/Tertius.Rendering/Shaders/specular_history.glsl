#ifndef TERTIUS_SPECULAR_HISTORY_GLSL
#define TERTIUS_SPECULAR_HISTORY_GLSL

// The format of the slow history, and of a value of one channel at its precision, such as the own-sample mean
// (accumulate_blocks.glsl).
#ifdef TERTIUS_PRECISE_SPECULAR_HISTORY
#define SPECULAR_HISTORY_FORMAT rgba32f
#define SPECULAR_HISTORY_SCALAR_FORMAT r32f
#else
#define SPECULAR_HISTORY_FORMAT rgba16f
#define SPECULAR_HISTORY_SCALAR_FORMAT r16f
#endif

#endif
