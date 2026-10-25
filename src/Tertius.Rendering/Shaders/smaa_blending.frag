#version 460
#extension GL_GOOGLE_include_directive : require
#define SMAA_INCLUDE_VS 0
#include "smaa.glsl"
#include "srgb.glsl"

// The tone-mapped image through its sRGB view, so the bilinear blend mixes linear light.
layout(set = 0, binding = 4) uniform sampler2D linearColor;
layout(set = 0, binding = 5) uniform sampler2D weightImage;

layout(location = 0) in vec2 texcoord;
layout(location = 1) in vec4 offset;

layout(location = 0) out vec4 outColor;

void main()
{
    vec4 color = SMAANeighborhoodBlendingPS(texcoord, offset, linearColor, weightImage);
    if (smaa.encodeSrgb != 0u)
        color.rgb = linearToSrgb(color.rgb);
    outColor = color;
}
