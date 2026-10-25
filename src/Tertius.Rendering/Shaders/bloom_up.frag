#version 460
#extension GL_GOOGLE_include_directive : require
#include "bloom.glsl"
#include "tent.glsl"

// An upsample from one bloom level into the larger one before it, which the pipeline's additive blend adds to. Level i
// then holds itself plus every smaller level.
void main()
{
    outColor = vec4(tent(source, linearClamp), 1.0);
}
