#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "gbuffer_inputs.glsl"
#include "srgb.glsl"

// Frame outlines over the tone-mapped image (OutlinePass.cs). A fragment behind geometry, by the G-buffer distance,
// draws at 30 % alpha; a fragment in front of geometry or against the sky draws at full alpha.

layout(location = 0) in vec3 inWorld;
layout(location = 1) flat in uint inColor;

layout(push_constant, std430) uniform OutlinePush
{
    uint encodeSrgb;   // 1 when the output format is UNORM and the shader must apply the sRGB curve itself
} outline;

layout(location = 0) out vec4 outColor;

const float BEHIND_ALPHA = 0.3;

void main()
{
    ivec2 pixel = clamp(ivec2(gl_FragCoord.xy * frame.outputViewport.zw * frame.viewport.xy - frame.renderParams.yz), ivec2(0), ivec2(frame.viewport.xy) - 1);
    float opaqueDistance = texelFetch(gbufferDistance, pixel, 0).r;
    bool behind = opaqueDistance > 0.0 && length(inWorld) > opaqueDistance;
    // The record holds the display color. An sRGB output encodes in hardware, so the shader gives it linear values.
    // On a UNORM output the shader encodes, so the hardware blends a dimmed ring on encoded values, not in linear light.
    vec3 color = srgbToLinear(unpackUnorm4x8(inColor).rgb);
    if (outline.encodeSrgb != 0u)
        color = linearToSrgb(color);
    outColor = vec4(color, behind ? BEHIND_ALPHA : 1.0);
}
