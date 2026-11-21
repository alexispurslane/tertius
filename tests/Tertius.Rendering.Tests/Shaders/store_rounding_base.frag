#version 460
// A fragment path of StoreRoundingTests: pixel p writes the base of case p.x mod 36 of store_rounding.glsl into a
// 16-bit float color attachment.
#extension GL_GOOGLE_include_directive : require
#include "store_rounding.glsl"

layout(location = 0) out vec4 color;

void main()
{
    int c = int(gl_FragCoord.x) % RoundingCases;
    color = vec4(roundingBase(c), 0.0, 0.0, 1.0);
}
