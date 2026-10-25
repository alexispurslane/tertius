#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "sky.glsl"

// The background of an environment face (EnvironmentFacePass.cs): drawn first, over every texel, with no depth test. It
// writes the sky without the stars and an open-sky share of 1. The readers add the stars for each pixel
// (docs/plans/environment-map.md, "What a face draws").

// SkyFacePushConstants in GpuTypes.cs: the face's basis, forward.w the inverse of the face size.
layout(push_constant, std430) uniform SkyFacePush
{
    vec4 forward;
    vec4 right;
    vec4 up;
} face;

layout(location = 0) out vec4 outColor;   // RGBA16F: plain radiance, the open-sky share

void main()
{
    vec2 st = gl_FragCoord.xy * face.forward.w;
    vec3 direction = normalize(face.forward.xyz + (2.0 * st.x - 1.0) * face.right.xyz + (1.0 - 2.0 * st.y) * face.up.xyz);
    outColor = vec4(skyRadiance(direction), 1.0);
}
