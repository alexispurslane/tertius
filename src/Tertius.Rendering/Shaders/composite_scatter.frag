#version 460
// The composite under SeparableSubsurface: the HDR image and the diffuse image (composite_pass.glsl).
#extension GL_GOOGLE_include_directive : require
#define SCATTER_OUTPUT
#include "composite_pass.glsl"
