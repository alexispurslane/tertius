#version 460
#extension GL_GOOGLE_include_directive : require
#include "bloom.glsl"

// A downsample from one bloom level into the next with Jimenez's 13 bilinear taps.

vec4 tap(vec2 center, vec2 texel, vec2 offset)
{
    return textureLod(sampler2D(source, linearClamp), center + offset * texel, 0.0);
}

void main()
{
    vec2 texel = 1.0 / vec2(textureSize(sampler2D(source, linearClamp), 0));
    // The output pixel p has its center on the source texel corner 2p + 1, which is 2 gl_FragCoord.
    vec2 center = 2.0 * gl_FragCoord.xy * texel;
    outColor = vec4(jimenez13(
        tap(center, texel, vec2(-2, -2)), tap(center, texel, vec2(0, -2)), tap(center, texel, vec2(2, -2)),
        tap(center, texel, vec2(-1, -1)), tap(center, texel, vec2(1, -1)),
        tap(center, texel, vec2(-2, 0)), tap(center, texel, vec2(0, 0)), tap(center, texel, vec2(2, 0)),
        tap(center, texel, vec2(-1, 1)), tap(center, texel, vec2(1, 1)),
        tap(center, texel, vec2(-2, 2)), tap(center, texel, vec2(0, 2)), tap(center, texel, vec2(2, 2))).rgb, 1.0);
}
