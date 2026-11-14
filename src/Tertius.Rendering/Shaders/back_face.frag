#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"

// THROWAWAY (ter-kh4g, stage 1): the fragment stage of the back-face cost probe. It writes the back face's distance
// to the eye in m, as gbuffer.frag writes the front face's. It keeps only a back face behind the pixel's front surface:
// the first run showed the back faces of the person's own body, 0.3 m from the eye, over the whole screen.
layout(location = 0) in vec3 inPosition;

layout(location = 0) out float outDistance;

// The share of the front surface's distance by which a back face must lie behind it.
const float BehindShare = 1e-4;

void main()
{
    float back = length(inPosition - draw.eye.xyz);
    float front = texelFetch(gbufferDistance, ivec2(gl_FragCoord.xy), 0).r;
    if (front <= 0.0 || back <= front * (1.0 + BehindShare))
        discard;
    // The back share: the front surface's distance over the back face's, in (0, 1). A pixel's back w is its center w
    // times this share, because both points lie on one view ray.
    outDistance = front / back;
}
