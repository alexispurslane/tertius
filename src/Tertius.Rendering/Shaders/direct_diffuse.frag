#version 460
// The direct pass when the targets hold the diffuse image: the direct image and the diffuse image (direct_pass.glsl).
#extension GL_GOOGLE_include_directive : require
#define DIFFUSE_OUTPUT
#include "direct_pass.glsl"
