#version 460
#extension GL_GOOGLE_include_directive : require
#define SMAA_INCLUDE_VS 0
#include "smaa.glsl"

// The tone-mapped image through its UNORM view, so the luma comes from the sRGB-encoded values, with point sampling.
layout(set = 0, binding = 0) uniform sampler2D encodedColor;

layout(location = 0) in vec2 texcoord;
layout(location = 1) in vec4 offset[3];

layout(location = 0) out vec2 edges;

void main()
{
    edges = SMAALumaEdgeDetectionPS(texcoord, offset, encodedColor);
}
