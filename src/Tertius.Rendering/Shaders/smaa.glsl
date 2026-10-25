// SMAA 1x with the reference header (ThirdParty/Smaa/SMAA.hlsl) and the settings of docs/plans/anti-aliasing.md:
// the high preset, luma edge detection, no predication. A vertex stage defines SMAA_INCLUDE_PS 0 before it includes
// this file, and a fragment stage SMAA_INCLUDE_VS 0, because a vertex shader cannot hold the discard of the edge pass.
#ifndef TERTIUS_SMAA_GLSL
#define TERTIUS_SMAA_GLSL

layout(push_constant, std430) uniform SmaaPush
{
    vec4 metrics;     // 1 / width, 1 / height, width, height of the output, in pixels
    uint encodeSrgb;  // 1 when the output format is UNORM and the blending pass must apply the sRGB curve itself
} smaa;

#define SMAA_GLSL_4
#define SMAA_PRESET_HIGH
#define SMAA_RT_METRICS smaa.metrics
#include "SMAA.hlsl"

#if SMAA_INCLUDE_VS
// The corner of the full-screen triangle of fullscreen.vert, in normalized device coordinates. Its texture
// coordinate, ndc * 0.5 + 0.5, is (0, 0) at the top left, as the header assumes, because Vulkan's y points down.
vec2 fullscreenCorner()
{
    return vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2) * 2.0 - 1.0;
}
#endif

#endif
