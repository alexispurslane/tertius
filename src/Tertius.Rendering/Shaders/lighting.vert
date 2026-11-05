#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"

// The world-space view ray of this pixel, not normalized. It is affine in NDC, so linear interpolation is exact.
layout(location = 0) out vec3 outRay;

void main()
{
    // One triangle that covers the screen: (-1,-1), (3,-1), (-1,3).
    vec2 ndc = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2) * 2.0 - 1.0;
    // Vulkan NDC has +y down, so screen up is -ndc.y.
    vec2 rayNdc = ndc + 2.0 * frame.renderParams.yz * frame.viewport.zw;
    outRay = frame.cameraForward.xyz + rayNdc.x * frame.cameraRight.xyz - rayNdc.y * frame.cameraUp.xyz;
    gl_Position = vec4(ndc, 0.0, 1.0);
}
