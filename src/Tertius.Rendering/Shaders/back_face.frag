#version 460
#extension GL_GOOGLE_include_directive : require
#include "frame.glsl"
#include "draw.glsl"
#include "gbuffer_inputs.glsl"
#include "back_face.glsl"

// The back-face pass's solid runs: the fragment rule of back_face.glsl.
layout(location = 0) in vec3 inPosition;

void main()
{
    writeBackShare(inPosition);
}
