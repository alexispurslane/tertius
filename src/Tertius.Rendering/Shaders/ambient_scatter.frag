#version 460
// The ambient pass under SeparableSubsurface: the HDR image, and D's ambient part added to the scatter image
// (ambient_pass.glsl).
#extension GL_GOOGLE_include_directive : require
#define SCATTER_OUTPUT
#include "ambient_pass.glsl"
