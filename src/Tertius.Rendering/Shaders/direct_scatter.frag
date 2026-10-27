#version 460
// The direct pass under SeparableSubsurface: the direct image and the scatter image (direct_pass.glsl).
#extension GL_GOOGLE_include_directive : require
#define SCATTER_OUTPUT
#include "direct_pass.glsl"
