#version 460
#extension GL_GOOGLE_include_directive : require
#define SMAA_INCLUDE_VS 0
#include "smaa.glsl"

layout(set = 0, binding = 1) uniform sampler2D edgeImage;
layout(set = 0, binding = 2) uniform sampler2D areaTable;
layout(set = 0, binding = 3) uniform sampler2D searchTable;

layout(location = 0) in vec2 texcoord;
layout(location = 1) in vec2 pixcoord;
layout(location = 2) in vec4 offset[3];

layout(location = 0) out vec4 weights;

void main()
{
    // SMAA 1x has one sample per pixel, so its subsample indices are zero.
    weights = SMAABlendingWeightCalculationPS(texcoord, pixcoord, offset, edgeImage, areaTable, searchTable, vec4(0.0));
}
