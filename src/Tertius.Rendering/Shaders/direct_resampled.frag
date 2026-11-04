#version 460
// The direct pass under ResampledLights: the spatial reuse, the chosen light and the exact lights, and R1
// (direct_pass.glsl).
#extension GL_GOOGLE_include_directive : require
#define RESAMPLED
#include "direct_pass.glsl"
