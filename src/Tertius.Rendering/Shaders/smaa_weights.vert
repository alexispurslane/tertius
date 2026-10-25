#version 460
#extension GL_GOOGLE_include_directive : require
#define SMAA_INCLUDE_PS 0
#include "smaa.glsl"

layout(location = 0) out vec2 texcoord;
layout(location = 1) out vec2 pixcoord;
layout(location = 2) out vec4 offset[3];

void main()
{
    vec2 ndc = fullscreenCorner();
    gl_Position = vec4(ndc, 0.0, 1.0);
    texcoord = ndc * 0.5 + 0.5;
    vec2 pixel;
    vec4 offsets[3];
    SMAABlendingWeightCalculationVS(texcoord, pixel, offsets);
    pixcoord = pixel;
    offset = offsets;
}
